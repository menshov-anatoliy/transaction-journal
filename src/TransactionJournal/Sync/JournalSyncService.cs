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
	private readonly ExecutionCategorySyncOptions _executionOptions;
	private readonly DeliveryCategorySyncOptions _deliveryOptions;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт оркестратор над движками категорий, хранилищами и материализатором.</summary>
	/// <param name="executionSync">Движок синхронизации истории исполнения одной категории.</param>
	/// <param name="deliverySync">Движок синхронизации delivery-истории одной категории.</param>
	/// <param name="instrumentSync">Пополнитель справочника неизвестных инструментов.</param>
	/// <param name="stateStore">Хранилище состояния категорий для выбора режима запуска.</param>
	/// <param name="runJournal">Журнал запусков синхронизации.</param>
	/// <param name="rawSnapshotStore">Источник полного снимка сырых записей для проекции.</param>
	/// <param name="materializer">Переразборщик доменных представлений из сырых записей.</param>
	/// <param name="executionOptions">Опции движка исполнения с глубиной backfill из конфигурации приложения.</param>
	/// <param name="deliveryOptions">Опции delivery-движка с глубиной backfill из конфигурации приложения.</param>
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
		ExecutionCategorySyncOptions executionOptions,
		DeliveryCategorySyncOptions deliveryOptions,
		TimeProvider? timeProvider = null)
	{
		_executionSync = executionSync ?? throw new ArgumentNullException(nameof(executionSync));
		_deliverySync = deliverySync ?? throw new ArgumentNullException(nameof(deliverySync));
		_instrumentSync = instrumentSync ?? throw new ArgumentNullException(nameof(instrumentSync));
		_stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
		_runJournal = runJournal ?? throw new ArgumentNullException(nameof(runJournal));
		_rawSnapshotStore = rawSnapshotStore ?? throw new ArgumentNullException(nameof(rawSnapshotStore));
		_materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
		_executionOptions = executionOptions ?? throw new ArgumentNullException(nameof(executionOptions));
		_deliveryOptions = deliveryOptions ?? throw new ArgumentNullException(nameof(deliveryOptions));
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
		InstrumentSyncResult? instrumentSync = null;
		try
		{
			foreach (var category in ExecutionHistorySync.DefaultCategories)
			{
				// Категория проходится целиком: сначала история исполнения, затем delivery-записи —
					// оба прохода делят общую строку запуска и её счётчики. Опции движков задают
					// глубину backfill, прочитанную из конфигурации при старте приложения.
					executionResults[category] = await _executionSync
						.RunAsync(category, _executionOptions, progressRun: run, cancellationToken: cancellationToken)
						.ConfigureAwait(false);
					deliveryResults[category] = await _deliverySync
						.RunAsync(category, _deliveryOptions, progressRun: run, cancellationToken: cancellationToken)
						.ConfigureAwait(false);
			}

			// Справочник пополняется символами новых записей запуска до закрытия запуска:
			// материализация сделок опциона требует канонической спецификации биржи.
			// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
			var seenInstruments = CollectSeenInstruments(executionResults, deliveryResults);
			instrumentSync = await _instrumentSync.SyncAsync(seenInstruments, run, cancellationToken).ConfigureAwait(false);

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
		IReadOnlyList<string> uncoveredBaseCoins = [];
		try
		{
			var snapshot = await _rawSnapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
			projection = _materializer.Materialize(
				snapshot.Instruments,
				snapshot.Executions,
				snapshot.Deliveries,
				tradeAssignments: null,
				asOf: _timeProvider.GetUtcNow());

			// Непокрытые активы доски сравниваются по полному снимку, а не только по новым
			// записям запуска: дыра покрытия существует и когда записи пришли прежними
			// запусками, а область актива не обходилась ни разу.
			// Traceability: openspec:sync/bybit-history#scenario-uncovered-base-coin-reported
			uncoveredBaseCoins = ComputeUncoveredBaseCoins(snapshot, executionResults);
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
			// Отказ 110023 на запросе спецификации не прерывает запуск: перечень
			// неполученных спецификаций проходит в итог запуска для показа пользователю.
			// Traceability: openspec:sync/bybit-history#scenario-unavailable-instrument-spec-skipped
			UnresolvedInstruments = instrumentSync?.UnresolvedSymbols ?? [],
			UncoveredBaseCoins = uncoveredBaseCoins,
		};
	}

	#region Вспомогательные методы

	/// <summary>Категория опционной доски: её записи делятся на базовые активы по префиксу символа.</summary>
	private const string OptionCategory = "option";

	/// <summary>
	/// Вычисляет непокрытые базовые активы опционной доски: собирает активы символов
	/// option-записей полного снимка сырья (исполнений и delivery-записей) и вычитает
	/// области, фактически пройденные запуском. Актив извлекается из префикса символа
	/// до первого дефиса и нормализуется как в <see cref="OptionBaseCoinSource"/> —
	/// trim и верхний регистр. Расчёт advisory: перечень отсортирован, повторы сняты.
	/// Traceability: openspec:sync/bybit-history#requirement-uncovered-base-coin-visibility
	/// </summary>
	private static IReadOnlyList<string> ComputeUncoveredBaseCoins(
		JournalRawSnapshot snapshot,
		IReadOnlyDictionary<string, ExecutionCategorySyncResult> executionResults)
	{
		// Пройденные области берутся из итога option-движка исполнения: только он
		// делит обход доски на области по базовым активам.
		var passedBaseCoins = new HashSet<string>(
			executionResults.GetValueOrDefault(OptionCategory)?.PassedBaseCoins ?? [],
			StringComparer.Ordinal);

		// Сортированное множество дедуплицирует повторяющиеся активы и даёт
		// стабильный порядок перечня между запусками.
		var recordedBaseCoins = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var execution in snapshot.Executions)
		{
			if (string.Equals(execution.Category, OptionCategory, StringComparison.Ordinal))
			{
				recordedBaseCoins.Add(ExtractBaseCoin(execution.Symbol));
			}
		}

		foreach (var delivery in snapshot.Deliveries)
		{
			if (string.Equals(delivery.Category, OptionCategory, StringComparison.Ordinal))
			{
				recordedBaseCoins.Add(ExtractBaseCoin(delivery.Symbol));
			}
		}

		return recordedBaseCoins.Where(coin => passedBaseCoins.Contains(coin) == false).ToList();
	}

	/// <summary>Извлекает базовый актив из символа: префикс до первого дефиса, trim и верхний регистр.</summary>
	private static string ExtractBaseCoin(string symbol)
	{
		var separatorIndex = symbol.IndexOf('-');
		var prefix = separatorIndex < 0 ? symbol : symbol[..separatorIndex];
		return prefix.Trim().ToUpperInvariant();
	}

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
