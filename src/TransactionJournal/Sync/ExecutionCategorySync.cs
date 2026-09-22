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
/// ещё и достигнутую границу доступной истории.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
/// Traceability: change:add-bybit-sync/design#d4
/// </summary>
public sealed class ExecutionCategorySync
{
	/// <summary>Категория опционной доски: её история делится на области по базовым активам.</summary>
	private const string OptionCategory = "option";

	private readonly ExecutionWindowPass _windowPass;
	private readonly IExecutionSyncStateStore _stateStore;
	private readonly IOptionBaseCoinSource _optionBaseCoins;
	private readonly TimeProvider _timeProvider;
		private readonly IRawExecutionBatchWriter? _rawWriter;

		/// <summary>Создаёт движок над проходом окна, хранилищем состояния и источником активов доски.</summary>
		/// <param name="windowPass">Проход одного 7-дневного окна с курсорной пагинацией.</param>
		/// <param name="stateStore">Хранилище состояния синхронизации категории.</param>
		/// <param name="optionBaseCoins">Источник базовых активов опционной доски для областей прохода.</param>
		/// <param name="timeProvider">Поставщик времени; по умолчанию системные часы.</param>
		/// <param name="rawWriter">Писатель сырых записей пачками по окнам; null — записи не сохраняются, движок только собирает их в памяти.</param>
		/// <exception cref="ArgumentNullException">Проход, хранилище или источник активов не заданы.</exception>
		public ExecutionCategorySync(
			ExecutionWindowPass windowPass,
			IExecutionSyncStateStore stateStore,
			IOptionBaseCoinSource optionBaseCoins,
			TimeProvider? timeProvider = null,
			IRawExecutionBatchWriter? rawWriter = null)
		{
			_windowPass = windowPass ?? throw new ArgumentNullException(nameof(windowPass));
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

		if (mode == SyncRunMode.Backfill)
		{
			// Пол глубины: окна листаются назад от момента запуска до этой границы,
			// последнее окно усекается до пола. Пустые окна проход не завершают —
			// перерыв в торговле не означает отсутствие более старой истории, а
			// зафиксированная ранее граница глубину перебора больше не ограничивает:
			// вычисленная по неполному набору активов, она не должна резать историю других.
			// Traceability: openspec:sync/bybit-history#scenario-trading-gap-does-not-truncate-history
			// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
			var floorMs = startedAtMs - options.MaxBackfillDepthMs;
			foreach (var scopeBaseCoin in scopes)
			{
				var windowEndMs = startedAtMs;
				while (windowEndMs > floorMs)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var windowStartMs = Math.Max(windowEndMs - ExecutionWindowPass.MaxWindowMs, floorMs);
					var passResult = await RunWindowAsync(category, scopeBaseCoin, windowStartMs, windowEndMs, options, earlyStopOnKnownPage: false, cancellationToken).ConfigureAwait(false);
					windowsProcessed++;
					newExecutions.AddRange(passResult.NewExecutions);
					allSeenExecutions.AddRange(passResult.AllExecutions);
					newExecutionsPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

					windowEndMs = windowStartMs;
				}
			}

			// Каждая область листана до пола глубины: перебор истории категории исчерпан,
			// можно переходить к следующей категории.
			// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
			historyExhausted = true;
		}
		else
		{
			// Инкрементальная догрузка идёт окнами назад от момента запуска до водяного знака
			// минус перекрытие: перечитывается только хвост истории, а не вся она. Ранняя
			// остановка на целиком известной странице завершает проход области — старше лежат
			// только уже сохранённые записи, остальные области продолжают обход.
			// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
			var targetStartMs = watermarkMs.GetValueOrDefault() - options.IncrementalOverlapMs;
			foreach (var scopeBaseCoin in scopes)
			{
				var windowEndMs = startedAtMs;
				while (windowEndMs > targetStartMs)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var windowStartMs = Math.Max(windowEndMs - ExecutionWindowPass.MaxWindowMs, targetStartMs);
					var passResult = await RunWindowAsync(category, scopeBaseCoin, windowStartMs, windowEndMs, options, earlyStopOnKnownPage: true, cancellationToken).ConfigureAwait(false);
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
			}
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

	#endregion
}
