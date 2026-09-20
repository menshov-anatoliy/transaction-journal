using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Синхронизация delivery-истории одной торговой категории: по водяному знаку состояния
/// выбирает режим — без отметки об успешном delivery-синке выполняется первичный backfill
/// 30-дневными окнами назад до исчерпания данных биржи, иначе инкрементальная догрузка
/// от водяного знака с перекрытием назад. Дедуп по ключу symbol + deliveryTime отсекает
/// записи пересекающихся окон, поэтому повторные прогоны не создают дублей. Успешный
/// проход фиксирует водяной знак delivery-категории, не трогая поля прохода исполнений.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
/// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
/// Traceability: change:add-bybit-sync/design#d4
/// </summary>
public sealed class DeliveryCategorySync
{
	private readonly DeliveryWindowPass _windowPass;
	private readonly IExecutionSyncStateStore _stateStore;
	private readonly TimeProvider _timeProvider;
	private readonly IRawDeliveryBatchWriter? _rawWriter;

	/// <summary>Создаёт движок над проходом окна и хранилищем состояния категории.</summary>
	/// <param name="windowPass">Проход одного 30-дневного окна с курсорной пагинацией.</param>
	/// <param name="stateStore">Хранилище состояния синхронизации категории.</param>
	/// <param name="timeProvider">Поставщик времени; по умолчанию системные часы.</param>
	/// <param name="rawWriter">Писатель сырых delivery-записей пачками по окнам; null — записи не сохраняются, движок только собирает их в памяти.</param>
	/// <exception cref="ArgumentNullException">Проход или хранилище не заданы.</exception>
	public DeliveryCategorySync(
		DeliveryWindowPass windowPass,
		IExecutionSyncStateStore stateStore,
		TimeProvider? timeProvider = null,
		IRawDeliveryBatchWriter? rawWriter = null)
	{
		_windowPass = windowPass ?? throw new ArgumentNullException(nameof(windowPass));
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
	/// <param name="options">Параметры синхронизации: размер страницы и перекрытие инкремента.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых delivery-записей продвигается пачками по окнам; null — прогресс запуска не ведётся.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Категория не задана либо запуск прогресса передан без писателя сырых записей.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Перекрытие инкрементального окна отрицательно.</exception>
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

		if (progressRun is not null && _rawWriter is null)
		{
			throw new ArgumentException(
				"Ведение прогресса запуска требует писателя сырых delivery-записей; передайте его в конструктор движка.",
				nameof(progressRun));
		}

		var state = await _stateStore.FindAsync(category, cancellationToken).ConfigureAwait(false);
		var startedAt = _timeProvider.GetUtcNow();
		var startedAtMs = startedAt.ToUnixTimeMilliseconds();

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

		if (mode == SyncRunMode.Backfill)
		{
			// Backfill идёт окнами назад от момента запуска до первого пустого окна:
			// пустое окно при пролистывании назад означает, что глубже delivery-данных
			// биржа не отдаёт.
			// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
			var windowEndMs = startedAtMs;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var windowStartMs = windowEndMs - DeliveryWindowPass.MaxWindowMs;
				var passResult = await RunWindowAsync(category, windowStartMs, windowEndMs, options, cancellationToken).ConfigureAwait(false);
				windowsProcessed++;
				newDeliveries.AddRange(passResult.NewDeliveries);
				newDeliveriesPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

				if (passResult.AllDeliveries.Count == 0)
				{
					historyExhausted = true;
					break;
				}

				windowEndMs = windowStartMs;
			}
		}
		else
		{
			// Инкрементальная догрузка идёт окнами назад от момента запуска до водяного
			// знака минус перекрытие: перечитывается только хвост delivery-истории.
			// Перекрывающаяся часть и записи границы отсекаются дедупом по
			// symbol + deliveryTime — дубли не создаются.
			// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
			var targetStartMs = watermarkMs.GetValueOrDefault() - options.IncrementalOverlapMs;
			var windowEndMs = startedAtMs;
			while (windowEndMs > targetStartMs)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var windowStartMs = Math.Max(windowEndMs - DeliveryWindowPass.MaxWindowMs, targetStartMs);
				var passResult = await RunWindowAsync(category, windowStartMs, windowEndMs, options, cancellationToken).ConfigureAwait(false);
				windowsProcessed++;
				newDeliveries.AddRange(passResult.NewDeliveries);
				newDeliveriesPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

				windowEndMs = windowStartMs;
			}
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

	#endregion
}
