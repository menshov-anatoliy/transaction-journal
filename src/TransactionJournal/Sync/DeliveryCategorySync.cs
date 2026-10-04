using TransactionJournal.Domain.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;

using TransactionJournal.Domain.Sync;
namespace TransactionJournal.Sync;

/// <summary>
/// Синхронизация delivery-истории одной торговой категории: по водяному знаку состояния
/// выбирает режим — без отметки об успешном delivery-синке выполняется первичный backfill
/// 30-дневными окнами назад до пола глубины, иначе инкрементальная догрузка
/// от водяного знака с перекрытием назад. Дедуп по ключу symbol + deliveryTime отсекает
/// записи пересекающихся окон, поэтому повторные прогоны не создают дублей. Успешный
/// проход фиксирует водяной знак delivery-категории, не трогая поля прохода исполнений.
/// Пол перебора зажимается границей хранения истории биржи по серверному времени,
/// а пограничный отказ биржи завершает перебор исчерпанием, не срывая запуск.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
/// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
/// Traceability: openspec:sync/bybit-history#requirement-history-boundary-guard
/// Traceability: change:add-bybit-sync/design#d4
/// </summary>
public sealed class DeliveryCategorySync
{
	private readonly DeliveryWindowPass _windowPass;
	private readonly IBybitHistoryGateway _gateway;
	private readonly IExecutionSyncStateStore _stateStore;
	private readonly TimeProvider _timeProvider;
	private readonly IRawDeliveryBatchWriter? _rawWriter;

	/// <summary>Создаёт движок над проходом окна, шлюзом биржи и хранилищем состояния категории.</summary>
	/// <param name="windowPass">Проход одного 30-дневного окна с курсорной пагинацией.</param>
	/// <param name="gateway">Шлюз истории биржи: источник серверного времени для расчёта границы хранения.</param>
	/// <param name="stateStore">Хранилище состояния синхронизации категории.</param>
	/// <param name="timeProvider">Поставщик времени; по умолчанию системные часы.</param>
	/// <param name="rawWriter">Писатель сырых delivery-записей пачками по окнам; null — записи не сохраняются, движок только собирает их в памяти.</param>
	/// <exception cref="ArgumentNullException">Проход, шлюз или хранилище не заданы.</exception>
	public DeliveryCategorySync(
		DeliveryWindowPass windowPass,
		IBybitHistoryGateway gateway,
		IExecutionSyncStateStore stateStore,
		TimeProvider? timeProvider = null,
		IRawDeliveryBatchWriter? rawWriter = null)
	{
		_windowPass = windowPass ?? throw new ArgumentNullException(nameof(windowPass));
		_gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
		_stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
		_timeProvider = timeProvider ?? TimeProvider.System;
		_rawWriter = rawWriter;
	}

	/// <summary>
	/// Выполняет синхронизацию delivery-категории: определяет режим по водяному знаку
	/// состояния, обходит 30-дневные окна выбранным режимом и фиксирует состояние
	/// успешного прохода.
	/// </summary>
	/// <param name="category">Торговая категория: option или linear.</param>
	/// <param name="options">Параметры синхронизации: размер страницы, перекрытие инкремента и глубина backfill.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых delivery-записей продвигается пачками по окнам; null — прогресс запуска не ведётся.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Категория не задана либо запуск прогресса передан без писателя сырых записей.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Перекрытие инкрементального окна отрицательно или глубина backfill неположительна.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов; состояние категории не меняется.</exception>
	public async Task<DeliveryCategorySyncResult> RunAsync(
		string category,
		DeliveryCategorySyncOptions? options = null,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		options ??= new DeliveryCategorySyncOptions();
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
				"Ведение прогресса запуска требует писателя сырых delivery-записей; передайте его в конструктор движка.",
				nameof(progressRun));
		}

		var state = await _stateStore.FindAsync(category, cancellationToken).ConfigureAwait(false);
		var startedAt = _timeProvider.GetUtcNow();
		var startedAtMs = startedAt.ToUnixTimeMilliseconds();

		// Серверное время биржи запрашивается один на запуск: по нему считается граница
		// хранения истории, чтобы рассинхрон локальных часов не сдвигал пол перебора за
		// разрешённую зону. Отказ эндпоинта времени не роняет запуск — fallback на
		// локальное время, а запас границы покрывает остаточный дрейф.
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

		// Режим определяется по delivery-водяному знаку: его отсутствие означает, что
		// экспирации категории ещё не синхронизировались — выполняется первичный backfill
		// всей доступной delivery-истории. Отметка исполнения не влияет на выбор режима:
		// проходы исполнения и доставки независимы.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		var watermarkMs = state?.DeliveryWatermarkMs;
		var mode = watermarkMs is null ? SyncRunMode.Backfill : SyncRunMode.Incremental;

		var newDeliveries = new List<BybitDeliveryRecord>();
		var newDeliveriesPersisted = 0;
		var windowsProcessed = 0;
		var historyExhausted = false;
		var boundaryExhausted = false;
		var skippedAreas = new List<string>();

		if (mode == SyncRunMode.Backfill)
		{
			// Пол глубины: окна листаются назад от момента запуска до этой границы,
			// последнее окно усекается до пола. Пустое окно проход не завершает —
			// перерыв в delivery-торговле не означает отсутствие более старых экспираций.
			// Пол зажимается clamp-ом к границе хранения биржи: конфигурация глубже неё
			// и смещённые локальные часы не отправляют запросы в запретную зону.
			// Traceability: openspec:sync/bybit-history#scenario-trading-gap-does-not-truncate-history
			// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
			// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
			var floorMs = BybitHistoryBoundary.ClampFloorMs(startedAtMs - options.MaxBackfillDepthMs, serverNowMs);
			var windowEndMs = startedAtMs;
			while (windowEndMs > floorMs && boundaryExhausted == false)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var windowStartMs = Math.Max(windowEndMs - DeliveryWindowPass.MaxWindowMs, floorMs);
					var guardResult = await RunWindowWithBoundaryGuardAsync(category, windowStartMs, windowEndMs, allowedEarliestMs, options, cancellationToken).ConfigureAwait(false);
					if (guardResult.Outcome == WindowGuardOutcome.BoundaryExhausted)
				{
					// Повторный пограничный отказ: доступная delivery-история исчерпана —
					// оставшиеся окна не запрашиваются.
					// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
					boundaryExhausted = true;
					break;
				}

					if (guardResult.Outcome == WindowGuardOutcome.AreaUnavailable)
					{
						// Единственная область delivery-прохода недоступна бирже: факт пропуска
						// фиксируется меткой, обход её окон прекращается — проход категории
						// завершён; водяной знак фиксируется штатно в конце запуска.
						// Traceability: openspec:sync/bybit-history#scenario-unavailable-single-area-ends-walk
						skippedAreas.Add(category);
						break;
					}

					var passResult = guardResult.PassResult!;

					windowsProcessed++;
				newDeliveries.AddRange(passResult.NewDeliveries);
				newDeliveriesPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

				windowEndMs = windowStartMs;
			}

			// Пол глубины достигнут либо перебор остановлен границей хранения биржи:
			// перебор delivery-истории категории исчерпан.
			// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
			historyExhausted = true;
		}
		else
		{
			// Инкрементальная догрузка идёт окнами назад от момента запуска до водяного
			// знака минус перекрытие: перечитывается только хвост delivery-истории.
			// Перекрывающаяся часть и записи границы отсекаются дедупом по
			// symbol + deliveryTime — дубли не создаются. Нижняя граница зажимается
			// к границе хранения биржи: долгий перерыв между запусками не отправляет
			// запросы в запретную зону.
			// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
			// Traceability: openspec:sync/bybit-history#scenario-incremental-boundary-clamped
			var targetStartMs = BybitHistoryBoundary.ClampFloorMs(watermarkMs.GetValueOrDefault() - options.IncrementalOverlapMs, serverNowMs);
			var windowEndMs = startedAtMs;
			while (windowEndMs > targetStartMs && boundaryExhausted == false)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var windowStartMs = Math.Max(windowEndMs - DeliveryWindowPass.MaxWindowMs, targetStartMs);
					var guardResult = await RunWindowWithBoundaryGuardAsync(category, windowStartMs, windowEndMs, allowedEarliestMs, options, cancellationToken).ConfigureAwait(false);
					if (guardResult.Outcome == WindowGuardOutcome.BoundaryExhausted)
				{
					// Повторный пограничный отказ: хвост delivery-истории за границей
					// недоступен — оставшиеся окна не запрашиваются.
					// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
					boundaryExhausted = true;
					break;
				}

					if (guardResult.Outcome == WindowGuardOutcome.AreaUnavailable)
					{
						// Единственная область delivery-прохода недоступна бирже: факт пропуска
						// фиксируется меткой, обход её окон прекращается — проход категории
						// завершён; водяной знак фиксируется штатно в конце запуска.
						// Traceability: openspec:sync/bybit-history#scenario-unavailable-single-area-ends-walk
						skippedAreas.Add(category);
						break;
					}

					var passResult = guardResult.PassResult!;

					windowsProcessed++;
				newDeliveries.AddRange(passResult.NewDeliveries);
				newDeliveriesPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

				windowEndMs = windowStartMs;
			}

			// Пограничный отказ завершает инкрементальную догрузку исчерпанием истории:
			// хвост за границей хранения биржа не отдаёт.
			// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
			historyExhausted = boundaryExhausted;
		}

		// Фиксация фактов успешного прохода. Водяной знак монотонен: покрывает всю
		// delivery-историю до момента запуска и не откатывается назад даже при сдвиге
		// локальных часов. Поля прохода исполнений (ExecWatermarkMs, BackfillBoundaryMs)
		// не меняются: движок сохраняет загруженное состояние категории как есть.
		state ??= new SyncState { Category = category };
		var fixedWatermarkMs = Math.Max(state.DeliveryWatermarkMs ?? long.MinValue, startedAtMs);
		state.DeliveryWatermarkMs = fixedWatermarkMs;
		state.LastSuccessAt = startedAt;
		await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);

		return new DeliveryCategorySyncResult
		{
			Mode = mode,
			NewDeliveries = newDeliveries,
			WindowsProcessed = windowsProcessed,
			HistoryExhausted = historyExhausted,
			DeliveryWatermarkMs = fixedWatermarkMs,
			NewDeliveriesPersisted = newDeliveriesPersisted,
			SkippedAreas = skippedAreas,
		};
	}

	#region Вспомогательные методы

	/// <summary>
	/// Сохраняет пачку новых delivery-записей одного окна в хранилище сырых записей
	/// и возвращает число фактически вставленных строк. Пачка пишется сразу после
	/// прохождения окна, поэтому обрыв на следующем окне не теряет уже прочитанные
	/// записи, а повторный прогон продолжает с места остановки без дублей.
	/// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
	/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
	/// </summary>
	private async Task<int> PersistWindowBatchAsync(
		string category,
		DeliveryWindowPassResult passResult,
		SyncRun? progressRun,
		CancellationToken cancellationToken)
	{
		// Пустая пачка не адресуется писателю: пустое окно не создаёт вызовов хранилища.
		if (_rawWriter is null || passResult.NewDeliveries.Count == 0)
		{
			return 0;
		}

		var writeResult = await _rawWriter
			.WriteAsync(category, passResult.NewDeliveries, progressRun, cancellationToken)
			.ConfigureAwait(false);
		return writeResult.InsertedCount;
	}

	private async Task<DeliveryWindowPassResult> RunWindowAsync(
		string category,
		long windowStartMs,
		long windowEndMs,
		DeliveryCategorySyncOptions options,
		CancellationToken cancellationToken)
	{
		var window = new DeliveryWindow
		{
			Category = category,
			StartMs = windowStartMs,
			EndMs = windowEndMs,
		};
		var passOptions = new DeliveryWindowPassOptions
		{
			PageSize = options.PageSize,
		};
		return await _windowPass.RunAsync(window, passOptions, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Проходит одно окно с защитным контуром перебора delivery-истории и возвращает исход
	/// прохода: успешный результат, пограничное исчерпание перебора либо недоступность
	/// области. Пограничный отказ биржи повторяет перебор диапазона один раз с началом,
	/// зажатым до границы, а повторный отказ означает исчерпание доступной истории. Отказ
	/// биржи «контракт недоступен для торговли» не ретраится и не зажимается — область
	/// считается недоступной, её окна больше не запрашиваются; прочие ошибки биржи
	/// пробрасываются как раньше.
	/// Traceability: openspec:sync/bybit-history#requirement-history-boundary-guard
	/// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
	/// </summary>
	private async Task<WindowGuardResult<DeliveryWindowPassResult>> RunWindowWithBoundaryGuardAsync(
		string category,
		long windowStartMs,
		long windowEndMs,
		long allowedEarliestMs,
		DeliveryCategorySyncOptions options,
		CancellationToken cancellationToken)
	{
		try
		{
			DeliveryWindowPassResult? passResult;
			try
			{
				passResult = await RunWindowAsync(category, windowStartMs, windowEndMs, options, cancellationToken).ConfigureAwait(false);
			}
			catch (BybitApiException error) when (BybitApiException.IsHistoryBoundaryError(error))
			{
				// Окно отклонено за глубину хранения: записи в разрешённой зоне ещё можно
				// прочитать — диапазон повторяется один раз с началом на границе. Диапазон
				// идёт под-окнами ширины прохода: зажатый диапазон бывает шире тридцати дней.
				// Повторный пограничный отказ внутри повтора означает, что запаса не
				// хватило — доступная история исчерпана.
				// Traceability: openspec:sync/bybit-history#scenario-boundary-window-retried-clamped
				// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
				var clampedResult = await RunClampedRangeAsync(category, windowEndMs, allowedEarliestMs, options, cancellationToken).ConfigureAwait(false);
				return clampedResult is null
					? WindowGuardResult<DeliveryWindowPassResult>.BoundaryExhausted()
					: WindowGuardResult<DeliveryWindowPassResult>.Passed(clampedResult);
			}

			return WindowGuardResult<DeliveryWindowPassResult>.Passed(passResult);
		}
		catch (BybitApiException error) when (BybitApiException.IsContractUnavailableError(error))
		{
			// Отказ «контракт недоступен для торговли» детерминированный: повтор запроса
			// и зажатие начала окно ответа не меняют. Третий исход защитного контура —
			// область недоступна: её окна больше не запрашиваются, ранее прочитанные
			// страницы области остаются сохранёнными.
			// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
			return WindowGuardResult<DeliveryWindowPassResult>.AreaUnavailable();
		}
	}

	/// <summary>
	/// Повторяет перебор зажатого диапазона от границы хранения до конца отказавшего
	/// окна под-окнами ширины прохода. Любой пограничный отказ внутри повтора означает,
	/// что запаса не хватило: возвращается null — доступная история исчерпана.
	/// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
	/// </summary>
	private async Task<DeliveryWindowPassResult?> RunClampedRangeAsync(
		string category,
		long rangeEndMs,
		long allowedEarliestMs,
		DeliveryCategorySyncOptions options,
		CancellationToken cancellationToken)
	{
		var allDeliveries = new List<BybitDeliveryRecord>();
		var newDeliveries = new List<BybitDeliveryRecord>();
		var pagesFetched = 0;
		var rangeEnd = rangeEndMs;
		while (rangeEnd > allowedEarliestMs)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var rangeStart = Math.Max(rangeEnd - DeliveryWindowPass.MaxWindowMs, allowedEarliestMs);
			DeliveryWindowPassResult subResult;
			try
			{
				subResult = await RunWindowAsync(category, rangeStart, rangeEnd, options, cancellationToken).ConfigureAwait(false);
			}
			catch (BybitApiException error) when (BybitApiException.IsHistoryBoundaryError(error))
			{
				// Повторный пограничный отказ: граница ближе, чем рассчитано, —
				// доступная история исчерпана.
				// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
				return null;
			}

			allDeliveries.AddRange(subResult.AllDeliveries);
			newDeliveries.AddRange(subResult.NewDeliveries);
			pagesFetched += subResult.PagesFetched;
			rangeEnd = rangeStart;
		}

		return new DeliveryWindowPassResult
		{
			AllDeliveries = allDeliveries,
			NewDeliveries = newDeliveries,
			PagesFetched = pagesFetched,
		};
	}

	#endregion
}
