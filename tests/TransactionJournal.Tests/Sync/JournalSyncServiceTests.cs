using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;
using TransactionJournal.Tests.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки оркестратора кнопки «Синхронизировать» на реальном SQLite-хранилище
/// и фиктивной бирже: один запуск SyncRun на исполнения, delivery-записи и пополнение
/// справочника, счётчики новых записей, перестроенная проекция с предупреждениями
/// сверки, повторный запуск в режиме инкремента без дублей и реакция на ошибки биржи.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// </summary>
[TestClass]
public class JournalSyncServiceTests
{
	private const string OptionSymbol = "BTC-15DEC25-45000-C";

	// Виртуальные часы фиксированы на 2026-01-01: окна backfill отсчитываются от этой даты.
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly long NowMs = Now.ToUnixTimeMilliseconds();
	private static readonly long DayMs = 86_400_000L;
	private static readonly long WeekMs = ExecutionWindowPass.MaxWindowMs;
	private static readonly long MonthMs = DeliveryWindowPass.MaxWindowMs;

	/// <summary>Каноническое время delivery опциона: 15DEC25 08:00 UTC.</summary>
	private static readonly long OptionDeliveryMs =
		new DateTimeOffset(2025, 12, 15, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	private string _databasePath = null!;
	private JournalSyncStore _store = null!;
	private ScriptedExchangeGateway _gateway = null!;
	private JournalSyncService _service = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-sync-service-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_store = new JournalSyncStore(CreateOptions(), new ManualTimeProvider());
		_gateway = new ScriptedExchangeGateway();

		// Полный стек оркестратора: движки категорий с писателями сырых записей,
		// пополнитель справочника и материализатор над одним хранилищем. Список активов
		// опционной доски отдаёт заглушка: один базовый актив, без обращения к справочнику.
		var executionEngine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _store),
			_store,
			new FakeOptionBaseCoinSource("BTC"),
			new ManualTimeProvider(),
			_store);
		var deliveryEngine = new DeliveryCategorySync(
			new DeliveryWindowPass(_gateway, _store), _store, new ManualTimeProvider(), _store);
		_service = new JournalSyncService(
			executionEngine,
			deliveryEngine,
			new InstrumentReferenceSync(_gateway, _store),
			_store,
			_store,
			_store,
			new JournalMaterializer(),
			new ManualTimeProvider());
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

	[TestMethod]
	[Description("Первый запуск backfill проходит обе категории, пополняет справочник и возвращает проекцию с предупреждением сверки")]
	public async Task TryIfFirstRunBackfillsAllCategoriesAndBuildsProjection()
	{
		// Arrange: биржа отдаёт по категориям линейную сделку перпа и покупку колла,
		// delivery-запись экспирации колла и спецификации обоих инструментов. Окна старее
		// первого шлюз отвечает пустыми страницами без расходования сценария.
		// Требование: по единственной команде система сама выполняет первичный backfill
		// всей доступной истории и экспираций, новые записи попадают во «Входящие»,
		// а расхождение с deliveryRpl показывается предупреждением.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		// Traceability: openspec:sync/bybit-history#scenario-delivery-reconciliation-warning
		_gateway.EnqueueExecution(ExecutionPage(LinearExecution()));
		_gateway.EnqueueExecution(ExecutionPage(OptionExecution()));
		_gateway.EnqueueDelivery(DeliveryPage());
		_gateway.EnqueueDelivery(DeliveryPage(OptionDelivery()));
		_gateway.EnqueueDelivery(DeliveryPage());
		_gateway.AddInstrument(LinearInstrument());
		_gateway.AddInstrument(OptionInstrument());

		// Act
		var result = await _service.SyncAsync();

		// Assert: режим первого запуска — backfill, запуск успешен, счётчики запуска
		// собирают новые записи обеих категорий и справочника.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.EqualTo(2));
		Assert.That(result.Run.NewDeliveries, Is.EqualTo(1));
		Assert.That(result.Run.NewInstruments, Is.EqualTo(2));

		// Обе категории прошли проходом исполнения и доставки под общей строкой запуска.
		Assert.That(result.Executions.Keys, Is.EqualTo(new[] { "linear", "option" }));
		Assert.That(result.Deliveries.Keys, Is.EqualTo(new[] { "linear", "option" }));
		Assert.That(result.Executions["linear"].NewExecutionsPersisted, Is.EqualTo(1));
		Assert.That(result.Executions["option"].NewExecutionsPersisted, Is.EqualTo(1));

		// Проекция перестроена из сырья: обе сделки во «Входящих», закрывающая запись
		// экспирации выведена, сверка с deliveryRpl дала предупреждение.
		Assert.That(result.ProjectionError, Is.Null);
		Assert.That(result.Projection, Is.Not.Null);
		Assert.That(result.Projection!.InboxTrades.Count, Is.EqualTo(2));
		Assert.That(result.Projection.ExpiryClosingEntries.Count, Is.EqualTo(1));
		Assert.That(result.Projection.ReconciliationWarnings.Count, Is.EqualTo(1));
		Assert.That(result.Projection.ReconciliationWarnings[0].Difference, Is.EqualTo(2.23m).Within(0.000001m));

		// Состояние категорий зафиксировано успешным проходом: водяные знаки обеих историй.
		foreach (var category in new[] { "linear", "option" })
		{
			var state = LoadState(category);
			Assert.That(state!.ExecWatermarkMs, Is.EqualTo(NowMs), category);
			Assert.That(state.DeliveryWatermarkMs, Is.EqualTo(NowMs), category);
		}

		// Строка запуска в журнале одна на все проходы.
		var runs = LoadRuns();
		Assert.That(runs, Has.Count.EqualTo(1));
		Assert.That(runs[0].Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(runs[0].Status, Is.EqualTo(SyncRunStatus.Succeeded));
	}

	[TestMethod]
	[Description("Повторный запуск идёт инкрементом без новых записей, дублей и перечитывания справочника")]
	public async Task TryIfSecondRunIncrementsWithoutDuplicates()
	{
		// Arrange (прогон 1): первичный backfill тех же данных, что в первом сценарии.
		// Требование: повторный запуск догружает только отсутствующие записи и не
		// перечитывает всю историю.
		// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		await RunFirstBackfillAsync();
		var instrumentQueriesAfterFirst = _gateway.InstrumentQueries.Count;

		// Act (прогон 2): биржа не отдаёт новых записей — сценарий шлюза исчерпан,
		// фиктивный шлюз отвечает пустыми страницами.
		var result = await _service.SyncAsync();

		// Assert: режим инкремента, запуск успешен, счётчики нулевые.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Incremental));
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.Zero);
		Assert.That(result.Run.NewDeliveries, Is.Zero);
		Assert.That(result.Run.NewInstruments, Is.Zero);

		// Хранилище не задвоено: записи исполнения, доставки и справочник прежнего объёма.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.RawExecutions.Count(), Is.EqualTo(2));
			Assert.That(db.RawDeliveries.Count(), Is.EqualTo(1));
			Assert.That(db.RawInstruments.Count(), Is.EqualTo(2));
			Assert.That(db.SyncRuns.Count(), Is.EqualTo(2));
		}

		// Инкремент не перечитывает справочник: новых запросов спецификаций нет.
		Assert.That(_gateway.InstrumentQueries.Count, Is.EqualTo(instrumentQueriesAfterFirst));

		// Проекция прежняя: сделки «Входящих» и предупреждение сверки на месте.
		Assert.That(result.Projection!.InboxTrades.Count, Is.EqualTo(2));
		Assert.That(result.Projection.ReconciliationWarnings.Count, Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Сброс состояния option возвращает категорию к первичному backfill без дублей известных записей")]
	public async Task TryIfOptionStateResetForcesBackfillWithoutDuplicates()
	{
		// Arrange (прогон 1): первичный backfill обеих категорий.
		// Требование: сброс состояния категории удаляет водяные знаки — следующий запуск
		// выполняет первичный backfill категории; сырые записи и домен не затронуты,
		// идемпотентность по execId исключает дубликаты уже известной истории.
		// Traceability: openspec:sync/bybit-history#requirement-manual-category-state-reset
		// Traceability: openspec:sync/bybit-history#scenario-reset-forces-backfill
		// Traceability: openspec:sync/bybit-history#scenario-reset-keeps-journal-data
		await RunFirstBackfillAsync();

		// Act: сброс состояния option удаляет только его строку, данные журнала целы.
		await _store.ResetAsync("option");
		Assert.That(LoadState("option"), Is.Null);
		Assert.That(LoadState("linear"), Is.Not.Null);

		// Arrange (прогон 2): биржа снова отдаёт известную опционную сделку и одну новую
		// по активу ETH; инкремент linear получает пустую страницу. Список активов доски
		// от заглушки по-прежнему один — BTC, ETH-сделка приходит первым же окном.
		_gateway.EnqueueExecution(ExecutionPage());
		_gateway.EnqueueExecution(ExecutionPage(OptionExecution(), OptionExecution("ETH-15DEC25-45000-P")));
		_gateway.AddInstrument(new BybitInstrumentInfo
		{
			Symbol = "ETH-15DEC25-45000-P",
			Status = "Trading",
			BaseCoin = "ETH",
			QuoteCoin = "USD",
			SettleCoin = "USDC",
			OptionsType = "Put",
			DeliveryTimeMs = OptionDeliveryMs,
		});

		// Act (прогон 2)
		var result = await _service.SyncAsync();

		// Assert: запуск вернулся к backfill: у option нет водяного знака исполнения
		// и доставки, linear продолжил инкрементом.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Executions["option"].Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(result.Deliveries["option"].Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(result.Executions["linear"].Mode, Is.EqualTo(SyncRunMode.Incremental));

		// Assert: из повторно отданной страницы вставлена только новая сделка — известный
		// exec-opt пропущен, дублей нет; объём журнала вырос ровно на одну запись.
		Assert.That(result.Run.NewExecutions, Is.EqualTo(1));
		Assert.That(result.Run.NewDeliveries, Is.Zero);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var execIds = db.RawExecutions.Select(execution => execution.ExecId).ToList();
			Assert.That(execIds, Has.Count.EqualTo(3));
			Assert.That(execIds.Count(execId => execId == "exec-opt"), Is.EqualTo(1));
			Assert.That(db.RawDeliveries.Count(), Is.EqualTo(1));
		}

		// Assert: проекция перестроена с новой сделкой во «Входящих», состояние option
		// восстановлено успешным backfill-проходом.
		Assert.That(result.ProjectionError, Is.Null);
		Assert.That(result.Projection!.InboxTrades.Count, Is.EqualTo(3));
		var optionState = LoadState("option");
		Assert.That(optionState, Is.Not.Null);
		Assert.That(optionState!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(optionState.DeliveryWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Ошибка биржи закрывает запуск со статусом Failed и не фиксирует состояние категорий")]
	public void ThrowOnExchangeErrorClosesRunFailedWithoutStateFixation()
	{
		// Arrange: первый запрос истории исполнения обрывается ошибкой биржи.
		// Требование: прерванная синхронизация оставляет журнал в согласованном
		// состоянии и допускает продолжение повторным запуском.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		_gateway.EnqueueExecutionError(new BybitApiException(10001, "обрыв связи с биржей"));

		// Act — Assert: ошибка проходит наружу вызывающей стороне (странице).
		var interruption = Assert.ThrowsAsync<BybitApiException>(() => _service.SyncAsync());
		Assert.That(interruption!.Message, Does.Contain("retCode=10001"));

		// Запуск закрыт ошибкой с текстом причины; состояние категорий не зафиксировано —
		// повторное нажатие кнопки продолжит с места остановки.
		var runs = LoadRuns();
		Assert.That(runs, Has.Count.EqualTo(1));
		Assert.That(runs[0].Status, Is.EqualTo(SyncRunStatus.Failed));
		Assert.That(runs[0].Error, Does.Contain("retCode=10001"));
		Assert.That(LoadState("linear"), Is.Null);
		Assert.That(LoadState("option"), Is.Null);
	}

	[TestMethod]
	[Description("Сбой материализации не рушит запуск: сырые записи сохранены, причина показывается текстом")]
	public async Task TryIfProjectionFailureKeepsSuccessfulRunWithErrorText()
	{
		// Arrange: первое окно исполнения linear пусто, опционное окно отдаёт сделку
		// пута, чью спецификацию не знает и эндпоинт справочника — символ остаётся без
		// канонических данных, материализация останавливается на сверке со справочником.
		// Требование: сырые записи сохраняются целиком и достаточны для переразбора,
		// поэтому сбой проекции не отменяет синхронизацию.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		_gateway.EnqueueExecution(ExecutionPage());
		_gateway.EnqueueExecution(ExecutionPage(OptionExecution("BTC-15DEC25-45000-P")));
		_gateway.EnqueueDelivery(DeliveryPage());
		_gateway.EnqueueDelivery(DeliveryPage());

		// Act
		var result = await _service.SyncAsync();

		// Assert: запуск успешен и запись сохранена, проекция не построена — причина
		// передана текстом для показа на странице.
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.EqualTo(1));
		Assert.That(result.Projection, Is.Null);
		Assert.That(result.ProjectionError, Is.Not.Null);
		Assert.That(result.ProjectionError, Does.Contain("BTC-15DEC25-45000-P"));

		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.RawExecutions.Count(), Is.EqualTo(1));
	}

	#region Помощники

	/// <summary>Прогон первичного backfill тех же данных, что в первом сценарии.</summary>
	private async Task RunFirstBackfillAsync()
	{
		// Порядок заготовок повторяет порядок «свежих» окон оркестратора: первое окно
		// исполнения linear, затем первое окно option; окна глубже пола первой недели
		// шлюз отвечает пустыми страницами без расходования сценария. Delivery-страницы
		// раздаются по порядку: linear завершается на пустом окне, option — на записи
		// и пустом окне.
		_gateway.EnqueueExecution(ExecutionPage(LinearExecution()));
		_gateway.EnqueueExecution(ExecutionPage(OptionExecution()));
		_gateway.EnqueueDelivery(DeliveryPage());
		_gateway.EnqueueDelivery(DeliveryPage(OptionDelivery()));
		_gateway.EnqueueDelivery(DeliveryPage());
		_gateway.AddInstrument(LinearInstrument());
		_gateway.AddInstrument(OptionInstrument());

		var first = await _service.SyncAsync();
		Assert.That(first.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
	}

	private static BybitExecution LinearExecution() => new()
	{
		Symbol = "BTCUSDT",
		ExecId = "exec-lin",
		Side = "Buy",
		ExecPrice = 42000m,
		ExecQty = 0.01m,
		ExecFee = 0.0042m,
		FeeCurrency = "USDT",
		ExecTimeMs = NowMs - DayMs,
	};

	private static BybitExecution OptionExecution(string symbol = OptionSymbol) => new()
	{
		Symbol = symbol,
		ExecId = symbol == OptionSymbol ? "exec-opt" : "exec-opt-put",
		Side = "Buy",
		ExecPrice = 100m,
		ExecQty = 0.0003m,
		ExecFee = 0.0002m,
		FeeCurrency = "USDC",
		ExecTimeMs = NowMs - 4 * DayMs,
	};

	/// <summary>ITM delivery-запись купленного колла: расчётная цена 46000 при страйке 45000.</summary>
	private static BybitDeliveryRecord OptionDelivery() => new()
	{
		Symbol = OptionSymbol,
		DeliveryTimeMs = OptionDeliveryMs,
		Side = "Buy",
		Position = 0.0003m,
		EntryPrice = 100m,
		DeliveryPrice = 46000m,
		Strike = 45000m,
		Fee = 0.0003m,
		DeliveryRpl = 2.5m,
	};

	private static BybitInstrumentInfo LinearInstrument() => new()
	{
		Symbol = "BTCUSDT",
		ContractType = "LinearPerpetual",
		Status = "Trading",
		BaseCoin = "BTC",
		QuoteCoin = "USDT",
		SettleCoin = "USDT",
		DeliveryTimeMs = 0L,
	};

	private static BybitInstrumentInfo OptionInstrument() => new()
	{
		Symbol = OptionSymbol,
		Status = "Trading",
		BaseCoin = "BTC",
		QuoteCoin = "USD",
		SettleCoin = "USDC",
		OptionsType = "Call",
		DeliveryTimeMs = OptionDeliveryMs,
	};

	private static BybitPagedResponse<BybitExecution> ExecutionPage(params BybitExecution[] executions) => new()
	{
		List = executions,
	};

	private static BybitPagedResponse<BybitDeliveryRecord> DeliveryPage(params BybitDeliveryRecord[] deliveries) => new()
	{
		List = deliveries,
	};

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private List<SyncRun> LoadRuns()
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.SyncRuns.OrderBy(run => run.Id).ToList();
	}

	private SyncState? LoadState(string category)
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.SyncStates.FirstOrDefault(state => state.Category == category);
	}

	#endregion

	#region Фиктивная биржа

	/// <summary>
	/// Фиктивный источник базовых активов опционной доски: возвращает заготовленный
	/// список активов без обращений к бирже.
	/// </summary>
	private sealed class FakeOptionBaseCoinSource : IOptionBaseCoinSource
	{
		private readonly IReadOnlyList<string> _baseCoins;

		public FakeOptionBaseCoinSource(params string[] baseCoins)
		{
			_baseCoins = baseCoins;
		}

		public Task<IReadOnlyList<string>> GetBaseCoinsAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(_baseCoins);
	}

	/// <summary>
	/// Фиктивная биржа: раздаёт заготовленные страницы истории исполнения, delivery-записей
	/// и спецификаций инструментов по порядку и помнит все запросы. При исчерпании
	/// сценария отвечает пустой страницей без курсора, а спецификации — фильтром по символу.
	/// </summary>
	private sealed class ScriptedExchangeGateway : IBybitHistoryGateway, IBybitInstrumentSource
	{
		private readonly Queue<object> _executionResponses = new();
		private readonly Queue<object> _deliveryResponses = new();
		private readonly Dictionary<string, BybitInstrumentInfo> _instruments = new(StringComparer.Ordinal);

		/// <summary>Все запросы истории исполнения в порядке отправления.</summary>
		public List<BybitExecutionListQuery> ExecutionQueries { get; } = [];

		/// <summary>Все запросы delivery-записей в порядке отправления.</summary>
		public List<BybitDeliveryRecordQuery> DeliveryQueries { get; } = [];

		/// <summary>Все запросы спецификаций инструментов в порядке отправления.</summary>
		public List<BybitInstrumentInfoQuery> InstrumentQueries { get; } = [];

		public void EnqueueExecution(BybitPagedResponse<BybitExecution> page)
		{
			_executionResponses.Enqueue(page);
		}

		public void EnqueueExecutionError(BybitApiException error)
		{
			_executionResponses.Enqueue(error);
		}

		public void EnqueueDelivery(BybitPagedResponse<BybitDeliveryRecord> page)
		{
			_deliveryResponses.Enqueue(page);
		}

		public void AddInstrument(BybitInstrumentInfo instrument)
		{
			_instruments[instrument.Symbol] = instrument;
		}

		public Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
			BybitExecutionListQuery query,
			CancellationToken cancellationToken = default)
		{
			ExecutionQueries.Add(query);
			// Окно старее первого отвечает пустой страницей без расходования сценария:
			// backfill листает окна до пола глубины, и заготовленных страниц на каждое
			// окно перебора не существует — содержательны только самые свежие окна.
			if (query.StartTimeMs < NowMs - WeekMs)
			{
				return Task.FromResult(new BybitPagedResponse<BybitExecution>());
			}

			return Task.FromResult(DequeuePage<BybitExecution>(_executionResponses));
		}

		public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
			BybitDeliveryRecordQuery query,
			CancellationToken cancellationToken = default)
		{
			DeliveryQueries.Add(query);
			// Окно старее первого отвечает пустой страницей без расходования сценария:
			// backfill листает окна до пола глубины, и заготовленных страниц на каждое
			// окно перебора не существует — содержательны только самые свежие окна.
			if (query.StartTimeMs < NowMs - MonthMs)
			{
				return Task.FromResult(new BybitPagedResponse<BybitDeliveryRecord>());
			}

			return Task.FromResult(DequeuePage<BybitDeliveryRecord>(_deliveryResponses));
		}

		public Task<BybitPagedResponse<BybitInstrumentInfo>> GetInstrumentInfoAsync(
			BybitInstrumentInfoQuery query,
			CancellationToken cancellationToken = default)
		{
			InstrumentQueries.Add(query);
			IReadOnlyList<BybitInstrumentInfo> list = query.Symbol is not null && _instruments.TryGetValue(query.Symbol, out var instrument)
				? [instrument]
				: [];
			return Task.FromResult(new BybitPagedResponse<BybitInstrumentInfo> { List = list });
		}

		private static BybitPagedResponse<TItem> DequeuePage<TItem>(Queue<object> responses)
		{
			if (responses.Count == 0)
			{
				return new BybitPagedResponse<TItem>();
			}

			var response = responses.Dequeue();
			return response is BybitApiException error
				? throw error
				: (BybitPagedResponse<TItem>)response;
		}
	}

	#endregion
}
