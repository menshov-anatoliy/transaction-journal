using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Синхронизация истории исполнения одной торговой категории: по водяному знаку
/// состояния выбирает режим — без отметки об успешном синке выполняется первичный
/// backfill окнами назад до пола глубины, иначе инкрементальная догрузка от водяного
/// знака с перекрытием назад. Категория option проходится областью на каждый базовый
/// актив опционной доски, остальные категории — одной областью без фильтра. Успешный
/// проход всех областей фиксирует в состоянии категории водяной знак, а backfill —
/// ещё и достигнутую границу доступной истории. Пол перебора зажимается границей
/// хранения истории биржи по серверному времени, а пограничный отказ биржи завершает
/// перебор исчерпанием, не срывая запуск.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
/// Traceability: openspec:sync/bybit-history#requirement-history-boundary-guard
/// Traceability: change:add-bybit-sync/design#d4
/// </summary>
public sealed class ExecutionCategorySync
{
	/// <summary>Категория опционной доски: её история делится на области по базовым активам.</summary>
	private const string OptionCategory = "option";

	private readonly ExecutionWindowPass _windowPass;
	private readonly IBybitHistoryGateway _gateway;
	private readonly IExecutionSyncStateStore _stateStore;
	private readonly IOptionBaseCoinSource _optionBaseCoins;
	private readonly TimeProvider _timeProvider;
		private readonly IRawExecutionBatchWriter? _rawWriter;

		/// <summary>Создаёт движок над проходом окна, шлюзом биржи, хранилищем состояния и источником активов доски.</summary>
		/// <param name="windowPass">Проход одного 7-дневного окна с курсорной пагинацией.</param>
		/// <param name="gateway">Шлюз истории биржи: источник серверного времени для расчёта границы хранения.</param>
		/// <param name="stateStore">Хранилище состояния синхронизации категории.</param>
		/// <param name="optionBaseCoins">Источник базовых активов опционной доски для областей прохода.</param>
		/// <param name="timeProvider">Поставщик времени; по умолчанию системные часы.</param>
		/// <param name="rawWriter">Писатель сырых записей пачками по окнам; null — записи не сохраняются, движок только собирает их в памяти.</param>
		/// <exception cref="ArgumentNullException">Проход, шлюз, хранилище или источник активов не заданы.</exception>
		public ExecutionCategorySync(
			ExecutionWindowPass windowPass,
			IBybitHistoryGateway gateway,
			IExecutionSyncStateStore stateStore,
			IOptionBaseCoinSource optionBaseCoins,
			TimeProvider? timeProvider = null,
			IRawExecutionBatchWriter? rawWriter = null)
		{
			_windowPass = windowPass ?? throw new ArgumentNullException(nameof(windowPass));
			_gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
			_stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
			_optionBaseCoins = optionBaseCoins ?? throw new ArgumentNullException(nameof(optionBaseCoins));
			_timeProvider = timeProvider ?? TimeProvider.System;
			_rawWriter = rawWriter;
		}

	/// <summary>
	/// Выполняет синхронизацию категории: определяет режим по водяному знаку состояния,
	/// строит области прохода по базовым активам, обходит их 7-дневными окнами выбранным
	/// режимом и после успешного прохода всех областей фиксирует состояние.
	/// </summary>
	/// <param name="category">Торговая категория: linear или option.</param>
	/// <param name="options">Параметры синхронизации: размер страницы, перекрытие инкремента и глубина backfill.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых записей продвигается пачками по окнам; null — прогресс запуска не ведётся.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Категория не задана либо запуск прогресса передан без писателя сырых записей.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Перекрытие инкрементального окна отрицательно или глубина backfill неположительна.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов; состояние категории не меняется.</exception>
	public async Task<ExecutionCategorySyncResult> RunAsync(
		string category,
		ExecutionCategorySyncOptions? options = null,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		options ??= new ExecutionCategorySyncOptions();
		if (options.IncrementalOverlapMs < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options), options.IncrementalOverlapMs, "Перекрытие инкрементального окна не может быть отрицательным.");
		}

		if (options.MaxBackfillDepthMs <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options), options.MaxBackfillDepthMs, "Глубина backfill должна быть положительной.");
		}

		if (progressRun is not null && _rawWriter is null)
		{
			throw new ArgumentException(
				"Ведение прогресса запуска требует писателя сырых записей; передайте его в конструктор движка.",
				nameof(progressRun));
		}

		var state = await _stateStore.FindAsync(category, cancellationToken).ConfigureAwait(false);
		var startedAt = _timeProvider.GetUtcNow();
		var startedAtMs = startedAt.ToUnixTimeMilliseconds();

		// Серверное время биржи запрашивается один на запуск: по нему считается граница
		// хранения истории, чтобы рассинхрон локальных часов не сдвигал пол перебора за
		// разрешённую зону. Отказ эндпоинта времени не роняет запуск — fallback на
		// локальное время, а запас границы в одно execution-окно покрывает остаточный дрейф.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		long serverNowMs;
		try
		{
			serverNowMs = await _gateway.GetServerTimeMsAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (BybitApiException)
		{
			serverNowMs = startedAtMs;
		}

		// Самая ранняя запрашиваемая дата этого запуска: опорная точка clamp-а пола
		// и зажатия начала ретрая пограничного окна.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		var allowedEarliestMs = BybitHistoryBoundary.GetAllowedEarliestMs(serverNowMs);

		// Режим определяется по водяному знаку: отсутствие отметки об успешном синке
		// означает первый запуск категории — выполняется первичный backfill всей доступной истории.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		var watermarkMs = state?.ExecWatermarkMs;
		var mode = watermarkMs is null ? SyncRunMode.Backfill : SyncRunMode.Incremental;

		// Области прохода строятся до обхода: option делится по активам доски, определённым
		// при этом запуске, остальные категории читаются целиком.
		var scopes = await ResolvePassScopesAsync(category, cancellationToken).ConfigureAwait(false);

		var newExecutions = new List<BybitExecution>();
		var allSeenExecutions = new List<BybitExecution>();
		var newExecutionsPersisted = 0;
		var windowsProcessed = 0;
		var historyExhausted = false;
		var earlyStopped = false;
		var boundaryExhausted = false;
		var skippedAreas = new List<string>();

		if (mode == SyncRunMode.Backfill)
		{
			// Пол глубины: окна листаются назад от момента запуска до этой границы,
			// последнее окно усекается до пола. Пустые окна проход не завершают —
			// перерыв в торговле не означает отсутствие более старой истории, а
			// зафиксированная ранее граница глубину перебора больше не ограничивает:
			// вычисленная по неполному набору активов, она не должна резать историю других.
			// Пол зажимается clamp-ом к границе хранения биржи: конфигурация глубже неё
			// и смещённые локальные часы не отправляют запросы в запретную зону.
			// Traceability: openspec:sync/bybit-history#scenario-trading-gap-does-not-truncate-history
			// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
			// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
			var floorMs = BybitHistoryBoundary.ClampFloorMs(startedAtMs - options.MaxBackfillDepthMs, serverNowMs);
			foreach (var scopeBaseCoin in scopes)
			{
				var windowEndMs = startedAtMs;
				while (windowEndMs > floorMs && boundaryExhausted == false)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var windowStartMs = Math.Max(windowEndMs - ExecutionWindowPass.MaxWindowMs, floorMs);
						var guardResult = await RunWindowWithBoundaryGuardAsync(category, scopeBaseCoin, windowStartMs, windowEndMs, allowedEarliestMs, options, earlyStopOnKnownPage: false, cancellationToken).ConfigureAwait(false);
						if (guardResult.Outcome == WindowGuardOutcome.BoundaryExhausted)
					{
						// Повторный пограничный отказ: доступная история исчерпана — оставшиеся
						// окна области и остальные области категории не запрашиваются.
						// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
						boundaryExhausted = true;
						break;
					}

						if (guardResult.Outcome == WindowGuardOutcome.AreaUnavailable)
						{
							// Область недоступна бирже: факт пропуска фиксируется меткой, обход
							// её окон прекращается, перебор продолжается со следующими областями;
							// водяной знак фиксируется штатно в конце запуска.
							// Traceability: openspec:sync/bybit-history#scenario-unavailable-option-area-skipped
							// Traceability: openspec:sync/bybit-history#scenario-unavailable-single-area-ends-walk
							skippedAreas.Add(BuildSkippedAreaLabel(category, scopeBaseCoin));
							break;
						}

						var passResult = guardResult.PassResult!;

						windowsProcessed++;
					newExecutions.AddRange(passResult.NewExecutions);
					allSeenExecutions.AddRange(passResult.AllExecutions);
					newExecutionsPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

					windowEndMs = windowStartMs;
				}

				if (boundaryExhausted)
				{
					break;
				}
			}

			// Каждая область листана до пола глубины либо до границы хранения биржи:
			// перебор истории категории исчерпан, можно переходить к следующей категории.
			// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
			historyExhausted = true;
		}
		else
		{
			// Инкрементальная догрузка идёт окнами назад от момента запуска до водяного знака
			// минус перекрытие: перечитывается только хвост истории, а не вся она. Ранняя
			// остановка на целиком известной странице завершает проход области — старше лежат
			// только уже сохранённые записи, остальные области продолжают обход. Нижняя
			// граница зажимается к границе хранения биржи: долгий перерыв между запусками
			// не отправляет запросы в запретную зону.
			// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
			// Traceability: openspec:sync/bybit-history#scenario-incremental-boundary-clamped
			var targetStartMs = BybitHistoryBoundary.ClampFloorMs(watermarkMs.GetValueOrDefault() - options.IncrementalOverlapMs, serverNowMs);
			foreach (var scopeBaseCoin in scopes)
			{
				var windowEndMs = startedAtMs;
				while (windowEndMs > targetStartMs && boundaryExhausted == false)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var windowStartMs = Math.Max(windowEndMs - ExecutionWindowPass.MaxWindowMs, targetStartMs);
						var guardResult = await RunWindowWithBoundaryGuardAsync(category, scopeBaseCoin, windowStartMs, windowEndMs, allowedEarliestMs, options, earlyStopOnKnownPage: true, cancellationToken).ConfigureAwait(false);
						if (guardResult.Outcome == WindowGuardOutcome.BoundaryExhausted)
					{
						// Повторный пограничный отказ: хвост истории за границей недоступен —
						// оставшиеся окна и области категории не запрашиваются.
						// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
						boundaryExhausted = true;
						break;
					}

						if (guardResult.Outcome == WindowGuardOutcome.AreaUnavailable)
						{
							// Область недоступна бирже: факт пропуска фиксируется меткой, обход
							// её окон прекращается, перебор продолжается со следующими областями;
							// водяной знак фиксируется штатно в конце запуска.
							// Traceability: openspec:sync/bybit-history#scenario-unavailable-option-area-skipped
							// Traceability: openspec:sync/bybit-history#scenario-unavailable-single-area-ends-walk
							skippedAreas.Add(BuildSkippedAreaLabel(category, scopeBaseCoin));
							break;
						}

						var passResult = guardResult.PassResult!;

						windowsProcessed++;
					newExecutions.AddRange(passResult.NewExecutions);
					allSeenExecutions.AddRange(passResult.AllExecutions);
					newExecutionsPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

					if (passResult.EarlyStopped)
					{
						earlyStopped = true;
						break;
					}

					windowEndMs = windowStartMs;
				}

				if (boundaryExhausted)
				{
					break;
				}
			}

			// Пограничный отказ завершает инкрементальную догрузку исчерпанием истории:
			// хвост за границей хранения биржа не отдаёт.
			// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
			historyExhausted = boundaryExhausted;
		}

		// Фиксация фактов успешного прохода всех областей. Водяной знак монотонен: покрывает
		// всю историю до момента запуска и не откатывается назад даже при сдвиге локальных часов.
		state ??= new SyncState { Category = category };
		var fixedWatermarkMs = Math.Max(state.ExecWatermarkMs ?? long.MinValue, startedAtMs);
		state.ExecWatermarkMs = fixedWatermarkMs;

		// Граница backfill — самая ранняя достигнутая запись: факт о фактической глубине
		// истории биржи. Глубину последующих проходов она больше не ограничивает —
		// граница, вычисленная по неполному набору активов, не должна резать историю других.
		// Traceability: openspec:sync/bybit-history#scenario-backfill-depth-boundary
		if (mode == SyncRunMode.Backfill && allSeenExecutions.Count > 0)
		{
			var earliestSeenMs = allSeenExecutions.Min(execution => execution.ExecTimeMs);
			state.BackfillBoundaryMs = state.BackfillBoundaryMs is { } fixedBoundary
				? Math.Min(fixedBoundary, earliestSeenMs)
				: earliestSeenMs;
		}

		state.LastSuccessAt = startedAt;
		await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);

		return new ExecutionCategorySyncResult
		{
			Mode = mode,
			NewExecutions = newExecutions,
			WindowsProcessed = windowsProcessed,
			HistoryExhausted = historyExhausted,
			EarlyStopped = earlyStopped,
			BackfillBoundaryMs = state.BackfillBoundaryMs,
			ExecWatermarkMs = fixedWatermarkMs,
			NewExecutionsPersisted = newExecutionsPersisted,
			SkippedAreas = skippedAreas,
		};
	}

	#region Вспомогательные методы

	/// <summary>
	/// Строит области прохода категории. Для option каждая область — один базовый актив
	/// доски: без явного фильтра биржа отдаёт записи только одного актива по умолчанию.
	/// Остальные категории отдают все символы разом и читаются одной областью без фильтра.
	/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
	/// </summary>
	private async Task<IReadOnlyList<string?>> ResolvePassScopesAsync(string category, CancellationToken cancellationToken)
	{
		if (string.Equals(category, OptionCategory, StringComparison.Ordinal) == false)
		{
			return [null];
		}

		var baseCoins = await _optionBaseCoins.GetBaseCoinsAsync(cancellationToken).ConfigureAwait(false);
		return baseCoins.Cast<string?>().ToList();
	}

	/// <summary>
	/// Строит метку пропущенной области: для области опционной доски — категория плюс
	/// базовый актив, для безфильтровой области — только категория. Метка возвращается
	/// в результате запуска и показывается пользователю на странице синхронизации.
	/// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
	/// </summary>
	private static string BuildSkippedAreaLabel(string category, string? baseCoin)
	{
		return baseCoin is null ? category : $"{category}:{baseCoin}";
	}

	/// <summary>
	/// Сохраняет пачку новых записей одного окна в хранилище сырых записей и возвращает
	/// число фактически вставленных строк. Пачка пишется сразу после прохождения окна,
	/// поэтому обрыв на следующем окне не теряет уже прочитанные записи, а повторный
	/// прогон продолжает с места остановки без дублей.
	/// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
	/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
	/// </summary>
	private async Task<int> PersistWindowBatchAsync(
		string category,
		ExecutionWindowPassResult passResult,
		SyncRun? progressRun,
		CancellationToken cancellationToken)
	{
		// Пустая пачка не адресуется писателю: пустое окно не создаёт вызовов хранилища.
		if (_rawWriter is null || passResult.NewExecutions.Count == 0)
		{
			return 0;
		}

		var writeResult = await _rawWriter
			.WriteAsync(category, passResult.NewExecutions, progressRun, cancellationToken)
			.ConfigureAwait(false);
		return writeResult.InsertedCount;
	}

	private async Task<ExecutionWindowPassResult> RunWindowAsync(
		string category,
		string? baseCoin,
		long windowStartMs,
		long windowEndMs,
		ExecutionCategorySyncOptions options,
		bool earlyStopOnKnownPage,
		CancellationToken cancellationToken)
	{
		var window = new ExecutionWindow
		{
			Category = category,
			BaseCoin = baseCoin,
			StartMs = windowStartMs,
			EndMs = windowEndMs,
		};
		var passOptions = new ExecutionWindowPassOptions
		{
			PageSize = options.PageSize,
			EarlyStopOnKnownPage = earlyStopOnKnownPage,
		};
		return await _windowPass.RunAsync(window, passOptions, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Проходит одно окно с защитным контуром перебора истории и возвращает исход прохода:
	/// успешный результат, пограничное исчерпание перебора либо недоступность области.
	/// Пограничный отказ биржи повторяет перебор диапазона один раз с началом, зажатым до
	/// границы, а повторный отказ означает исчерпание доступной истории. Отказ биржи
	/// «контракт недоступен для торговли» не ретраится и не зажимается — область считается
	/// недоступной, её окна больше не запрашиваются; прочие ошибки биржи пробрасываются
	/// как раньше.
	/// Traceability: openspec:sync/bybit-history#requirement-history-boundary-guard
	/// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
	/// </summary>
	private async Task<WindowGuardResult<ExecutionWindowPassResult>> RunWindowWithBoundaryGuardAsync(
		string category,
		string? baseCoin,
		long windowStartMs,
		long windowEndMs,
		long allowedEarliestMs,
		ExecutionCategorySyncOptions options,
		bool earlyStopOnKnownPage,
		CancellationToken cancellationToken)
	{
		try
		{
			ExecutionWindowPassResult? passResult;
			try
			{
				passResult = await RunWindowAsync(category, baseCoin, windowStartMs, windowEndMs, options, earlyStopOnKnownPage, cancellationToken).ConfigureAwait(false);
			}
			catch (BybitApiException error) when (BybitApiException.IsHistoryBoundaryError(error))
			{
				// Окно отклонено за глубину хранения: записи в разрешённой зоне ещё можно
				// прочитать — диапазон повторяется один раз с началом на границе. Диапазон
				// идёт под-окнами ширины прохода: зажатый диапазон бывает шире семи дней.
				// Повторный пограничный отказ внутри повтора означает, что запаса не
				// хватило — доступная история исчерпана.
				// Traceability: openspec:sync/bybit-history#scenario-boundary-window-retried-clamped
				// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
				var clampedResult = await RunClampedRangeAsync(category, baseCoin, windowEndMs, allowedEarliestMs, options, earlyStopOnKnownPage, cancellationToken).ConfigureAwait(false);
				return clampedResult is null
					? WindowGuardResult<ExecutionWindowPassResult>.BoundaryExhausted()
					: WindowGuardResult<ExecutionWindowPassResult>.Passed(clampedResult);
			}

			return WindowGuardResult<ExecutionWindowPassResult>.Passed(passResult);
		}
		catch (BybitApiException error) when (BybitApiException.IsContractUnavailableError(error))
		{
			// Отказ «контракт недоступен для торговли» детерминированный: повтор запроса
			// и зажатие начала окно ответа не меняют. Третий исход защитного контура —
			// область недоступна: её окна больше не запрашиваются, ранее прочитанные
			// страницы области остаются сохранёнными.
			// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
			return WindowGuardResult<ExecutionWindowPassResult>.AreaUnavailable();
		}
	}

	/// <summary>
	/// Повторяет перебор зажатого диапазона от границы хранения до конца отказавшего
	/// окна под-окнами ширины прохода. Любой пограничный отказ внутри повтора означает,
	/// что запаса не хватило: возвращается null — доступная история исчерпана.
	/// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
	/// </summary>
	private async Task<ExecutionWindowPassResult?> RunClampedRangeAsync(
		string category,
		string? baseCoin,
		long rangeEndMs,
		long allowedEarliestMs,
		ExecutionCategorySyncOptions options,
		bool earlyStopOnKnownPage,
		CancellationToken cancellationToken)
	{
		var allExecutions = new List<BybitExecution>();
		var newExecutions = new List<BybitExecution>();
		var pagesFetched = 0;
		var earlyStopped = false;
		var rangeEnd = rangeEndMs;
		while (rangeEnd > allowedEarliestMs)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var rangeStart = Math.Max(rangeEnd - ExecutionWindowPass.MaxWindowMs, allowedEarliestMs);
			ExecutionWindowPassResult subResult;
			try
			{
				subResult = await RunWindowAsync(category, baseCoin, rangeStart, rangeEnd, options, earlyStopOnKnownPage, cancellationToken).ConfigureAwait(false);
			}
			catch (BybitApiException error) when (BybitApiException.IsHistoryBoundaryError(error))
			{
				// Повторный пограничный отказ: граница ближе, чем рассчитано, —
				// доступная история исчерпана.
				// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
				return null;
			}

			allExecutions.AddRange(subResult.AllExecutions);
			newExecutions.AddRange(subResult.NewExecutions);
			pagesFetched += subResult.PagesFetched;

			// Целиком известное под-окно останавливает повтор: глубже лежат только
			// уже сохранённые записи, как и в обычном инкрементальном проходе.
			if (subResult.EarlyStopped)
			{
				earlyStopped = true;
				break;
			}

			rangeEnd = rangeStart;
		}

		return new ExecutionWindowPassResult
		{
			AllExecutions = allExecutions,
			NewExecutions = newExecutions,
			PagesFetched = pagesFetched,
			EarlyStopped = earlyStopped,
		};
	}

	#endregion
}
