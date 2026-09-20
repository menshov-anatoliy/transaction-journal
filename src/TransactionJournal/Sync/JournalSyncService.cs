using TransactionJournal.Data;
using TransactionJournal.Materialization;

namespace TransactionJournal.Sync;

/// <summary>
/// Оркестратор полной ручной синхронизации журнала под одной строкой SyncRun: определяет
/// режим по водяным знакам обеих историй всех категорий, обходит по каждой категории
/// историю исполнения и delivery-записи, пополняет справочник неизвестных инструментов
/// и после закрытия запуска перестраивает доменные проекции из сырых записей. Прерванный
/// запуск оставляет журнал согласованным: сохранённые пачки остаются в хранилище,
/// состояние категорий не фиксируется, повторное нажатие кнопки продолжает с места
/// остановки без дублей.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
/// Traceability: change:add-bybit-sync/design#d2
/// </summary>
public sealed class JournalSyncService : IJournalSyncService
{
	private readonly ExecutionCategorySync _executionSync;
	private readonly DeliveryCategorySync _deliverySync;
	private readonly InstrumentReferenceSync _instrumentSync;
	private readonly IExecutionSyncStateStore _stateStore;
	private readonly ISyncRunJournal _runJournal;
	private readonly IJournalRawSnapshotStore _rawSnapshotStore;
	private readonly JournalMaterializer _materializer;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт оркестратор над движками категорий, хранилищами и материализатором.</summary>
	/// <param name="executionSync">Движок синхронизации истории исполнения одной категории.</param>
	/// <param name="deliverySync">Движок синхронизации delivery-истории одной категории.</param>
	/// <param name="instrumentSync">Пополнитель справочника неизвестных инструментов.</param>
	/// <param name="stateStore">Хранилище состояния категорий для выбора режима запуска.</param>
	/// <param name="runJournal">Журнал запусков синхронизации.</param>
	/// <param name="rawSnapshotStore">Источник полного снимка сырых записей для проекции.</param>
	/// <param name="materializer">Переразборщик доменных представлений из сырых записей.</param>
	/// <param name="timeProvider">Поставщик времени; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Какая-либо зависимость не задана.</exception>
	public JournalSyncService(
		ExecutionCategorySync executionSync,
		DeliveryCategorySync deliverySync,
		InstrumentReferenceSync instrumentSync,
		IExecutionSyncStateStore stateStore,
		ISyncRunJournal runJournal,
		IJournalRawSnapshotStore rawSnapshotStore,
		JournalMaterializer materializer,
		TimeProvider? timeProvider = null)
	{
		_executionSync = executionSync ?? throw new ArgumentNullException(nameof(executionSync));
		_deliverySync = deliverySync ?? throw new ArgumentNullException(nameof(deliverySync));
		_instrumentSync = instrumentSync ?? throw new ArgumentNullException(nameof(instrumentSync));
		_stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
		_runJournal = runJournal ?? throw new ArgumentNullException(nameof(runJournal));
		_rawSnapshotStore = rawSnapshotStore ?? throw new ArgumentNullException(nameof(rawSnapshotStore));
		_materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc cref="IJournalSyncService.SyncAsync" />
	public async Task<JournalSyncResult> SyncAsync(CancellationToken cancellationToken = default)
	{
		// Режим запуска определяется до обхода: строка SyncRun открывается в начале и должна
		// знать режим. Backfill выбирается, пока хотя бы одна категория не отметила успешный
		// синк обеих историй — запуск дочитывает недостающую историю целиком.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		var mode = SyncRunMode.Incremental;
		foreach (var category in ExecutionHistorySync.DefaultCategories)
		{
			var state = await _stateStore.FindAsync(category, cancellationToken).ConfigureAwait(false);
			if (state?.ExecWatermarkMs is null || state?.DeliveryWatermarkMs is null)
			{
				mode = SyncRunMode.Backfill;
				break;
			}
		}

		// Строка запуска открывается до первого запроса биржи: даже мгновенный обрыв
		// оставляет след запуска со статусом и текстом ошибки.
		var run = await _runJournal.StartAsync(mode, cancellationToken).ConfigureAwait(false);

		var executionResults = new Dictionary<string, ExecutionCategorySyncResult>(StringComparer.Ordinal);
		var deliveryResults = new Dictionary<string, DeliveryCategorySyncResult>(StringComparer.Ordinal);
		try
		{
			foreach (var category in ExecutionHistorySync.DefaultCategories)
			{
				// Категория проходится целиком: сначала история исполнения, затем delivery-записи —
				// оба прохода делят общую строку запуска и её счётчики.
				executionResults[category] = await _executionSync
					.RunAsync(category, progressRun: run, cancellationToken: cancellationToken)
					.ConfigureAwait(false);
				deliveryResults[category] = await _deliverySync
					.RunAsync(category, progressRun: run, cancellationToken: cancellationToken)
					.ConfigureAwait(false);
			}

			// Справочник пополняется символами новых записей запуска до закрытия запуска:
			// материализация сделок опциона требует канонической спецификации биржи.
			// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
			var seenInstruments = CollectSeenInstruments(executionResults, deliveryResults);
			await _instrumentSync.SyncAsync(seenInstruments, run, cancellationToken).ConfigureAwait(false);

			await _runJournal.MarkSucceededAsync(run, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// Ошибка закрывает запуск со статусом Failed и текстом причины; состояние категорий
			// остаётся без изменений, поэтому повторный запуск продолжит с места остановки.
			// Пометка выполняется без токена отмены: отмена тоже должна оставлять след запуска.
			await _runJournal.MarkFailedAsync(run, ex.Message, CancellationToken.None).ConfigureAwait(false);
			throw;
		}

		// Проекция перестраивается после закрытия запуска из локального сырья без сетевых
		// запросов: сбой разбора не отменяет синхронизацию — сырые записи уже сохранены,
		// а причина сбоя показывается пользователю текстом.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		JournalMaterializationResult? projection = null;
		string? projectionError = null;
		try
		{
			var snapshot = await _rawSnapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
			projection = _materializer.Materialize(
				snapshot.Instruments,
				snapshot.Executions,
				snapshot.Deliveries,
				tradeAssignments: null,
				asOf: _timeProvider.GetUtcNow());
		}
		catch (Exception ex)
		{
			projectionError = ex.Message;
		}

		return new JournalSyncResult
		{
			Mode = mode,
			Run = run,
			Executions = executionResults,
			Deliveries = deliveryResults,
			Projection = projection,
			ProjectionError = projectionError,
		};
	}

	#region Вспомогательные методы

	/// <summary>
	/// Собирает пары категория-символ из новых записей запуска: исполнения и delivery-записи
	/// дают полный набор инструментов, требующих спецификации в справочнике.
	/// </summary>
	private static IReadOnlyCollection<(string Category, string Symbol)> CollectSeenInstruments(
		IReadOnlyDictionary<string, ExecutionCategorySyncResult> executionResults,
		IReadOnlyDictionary<string, DeliveryCategorySyncResult> deliveryResults)
	{
		var instruments = new List<(string Category, string Symbol)>();
		foreach (var (category, result) in executionResults)
		{
			instruments.AddRange(result.NewExecutions.Select(execution => (category, execution.Symbol)));
		}

		foreach (var (category, result) in deliveryResults)
		{
			instruments.AddRange(result.NewDeliveries.Select(delivery => (category, delivery.Symbol)));
		}

		return instruments;
	}

	#endregion
}
