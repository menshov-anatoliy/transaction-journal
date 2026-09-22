using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Sync;
using TransactionJournal.Tests.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки оркестратора синхронизации истории исполнения на реальном SQLite-хранилище
/// и фиктивном шлюзе биржи: один запуск SyncRun на все категории, пакетная запись сырых
/// записей по окнам и главный сценарий задачи — повторный прогон после обрыва на середине
/// продолжает с места остановки и не создаёт дублей.
/// </summary>
[TestClass]
public class ExecutionHistorySyncTests
{
	// Виртуальные часы фиксированы на 2026-01-01: старт ManualTimeProvider совпадает с этой датой.
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly long NowMs = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
	private static readonly long DayMs = 86_400_000L;
	private static readonly long WeekMs = ExecutionWindowPass.MaxWindowMs;

	private string _databasePath = null!;
	private JournalSyncStore _store = null!;
	private ScriptedGateway _gateway = null!;
	private ExecutionHistorySync _orchestrator = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-history-sync-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		// Полный стек на реальном хранилище: проверка известных execId, состояние категорий,
		// пакетная запись и журнал запусков работают над одной физической базой.
		_store = new JournalSyncStore(CreateOptions(), new ManualTimeProvider());
		_gateway = new ScriptedGateway();
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _store), _store, new FakeOptionBaseCoinSource("BTC"), new ManualTimeProvider(), _store);
		_orchestrator = new ExecutionHistorySync(engine, _store, _store);
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база и соседние WAL/SHM-файлы удаляются после каждой проверки.
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
	[Description("Повторный прогон после обрыва на середине продолжает с места остановки и не создаёт дублей")]
	public async Task TryIfInterruptedRunResumesFromStopWithoutDuplicates()
	{
		// Arrange (прогон 1): backfill читает два окна с записями, третье окно падает ошибкой
		// биржи — имитация обрыва на середине запуска.
		// Требование: прерванная синхронизация оставляет журнал в согласованном состоянии
		// и допускает продолжение повторным запуском без дублей.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		_gateway.Enqueue(Page(Execution("exec-1", NowMs - DayMs)));
		_gateway.Enqueue(Page(Execution("exec-2", NowMs - 8 * DayMs)));
		_gateway.EnqueueError(new BybitApiException(10001, "обрыв связи с биржей"));

		// Act (прогон 1): ошибка биржи проходит наружу, запуск закрывается ошибкой.
			var interruption = Assert.ThrowsAsync<BybitApiException>(
			() => _orchestrator.RunAsync(categories: new[] { "linear" }));
			Assert.That(interruption!.Message, Does.Contain("retCode=10001"));

		// Assert (прогон 1): уже прочитанные пачки сохранены — записи двух окон в хранилище.
		var savedAfterInterruption = LoadRawExecutions();
		Assert.That(savedAfterInterruption.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-1", "exec-2" }));

		// Assert (прогон 1): запуск закрыт ошибкой с прогрессом двух сохранённых пачек;
		// состояние категории не зафиксировано — водяного знака нет.
		var failedRun = LoadRuns().Single();
		Assert.That(failedRun.Status, Is.EqualTo(SyncRunStatus.Failed));
		Assert.That(failedRun.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(failedRun.NewExecutions, Is.EqualTo(2));
		Assert.That(failedRun.FinishedAt, Is.EqualTo(Now));
		Assert.That(failedRun.Error, Does.Contain("retCode=10001"));
		Assert.That(LoadState("linear"), Is.Null);

		// Arrange (прогон 2): биржа снова отдаёт те же два окна (повторное чтение с места
		// остановки), третье окно пустое, пол глубины трёх недель завершает перебор.
		_gateway.Enqueue(Page(Execution("exec-1", NowMs - DayMs)));
		_gateway.Enqueue(Page(Execution("exec-2", NowMs - 8 * DayMs)));
		_gateway.Enqueue(Page());

		// Act (прогон 2): повторный запуск завершается успешно.
		var result = await _orchestrator.RunAsync(
			categories: new[] { "linear" },
			options: new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 3 * WeekMs });

		// Assert (прогон 2): сохранённые записи распознаны известными и не задублированы —
		// в хранилище по-прежнему ровно две записи.
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		var savedAfterResume = LoadRawExecutions();
		Assert.That(savedAfterResume.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-1", "exec-2" }));

		// Assert (прогон 2): новый запуск успешен, счётчик новых записей нулевой —
		// прогресс честно отражает отсутствие нового сырья.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.Zero);
		Assert.That(result.Categories["linear"].NewExecutionsPersisted, Is.Zero);
		Assert.That(result.Categories["linear"].HistoryExhausted, Is.True);

		// Assert (прогон 2): в журнале два запуска — неудачный и успешный; успех без ошибки.
		var runs = LoadRuns();
		Assert.That(runs, Has.Count.EqualTo(2));
		Assert.That(runs.Count(run => run.Status == SyncRunStatus.Failed), Is.EqualTo(1));
		var succeededRun = runs.Single(run => run.Status == SyncRunStatus.Succeeded);
		Assert.That(succeededRun.Error, Is.Null);
		Assert.That(succeededRun.FinishedAt, Is.EqualTo(Now));

		// Assert (прогон 2): состояние категории зафиксировано успешным проходом —
		// водяной знак на момент запуска, граница backfill на самой ранней записи.
		var state = LoadState("linear");
		Assert.That(state!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(state.BackfillBoundaryMs, Is.EqualTo(NowMs - 8 * DayMs));
	}

	[TestMethod]
	[Description("Успешный первый запуск пишет сырые пачки, ведёт счётчик и фиксирует состояние")]
	public async Task TryIfSuccessfulFirstRunWritesBatchesAndCounts()
	{
		// Arrange: первый запуск — backfill глубиной двух окон; первое окно приносит две
		// записи, второе пустое, на полу глубины перебор исчерпан.
		// Требование: первичный backfill сохраняет все полученные записи исполнения
		// и по завершении фиксирует водяной знак успешной синхронизации.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		_gateway.Enqueue(Page(Execution("exec-1", NowMs - DayMs), Execution("exec-2", NowMs - 2 * DayMs)));
		_gateway.Enqueue(Page());

		// Act
		var result = await _orchestrator.RunAsync(
			categories: new[] { "linear" },
			options: new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 2 * WeekMs });

		// Assert: обе записи сохранены в хранилище с сырым JSON и отметкой загрузки.
		var saved = LoadRawExecutions();
		Assert.That(saved.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-1", "exec-2" }));
		Assert.That(saved.All(execution => execution.PayloadJson.Contains("exec-")), Is.True);
		Assert.That(saved.All(execution => execution.FetchedAt == Now), Is.True);

		// Assert: запуск успешен, счётчик новых записей равен двум, состояние зафиксировано.
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.EqualTo(2));
		Assert.That(result.Categories["linear"].NewExecutionsPersisted, Is.EqualTo(2));
		Assert.That(LoadState("linear")!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(LoadState("linear")!.BackfillBoundaryMs, Is.EqualTo(NowMs - 2 * DayMs));
	}

	[TestMethod]
	[Description("Категории одного запуска делят общую строку SyncRun и сумму счётчиков")]
	public async Task TryIfBothCategoriesShareSingleRunWithCommonCounters()
	{
		// Arrange: обе категории синхронизируются впервые, по одной записи и пустому окну
		// каждая; глубина backfill ограничена двумя окнами. Опционная категория проходится
		// областью единственного актива заглушки источника.
		// Требование: backfill выполняется по каждой торговой категории отдельным проходом,
		// а запуск синхронизации у общей кнопки один.
		// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 2 * WeekMs };
		_gateway.Enqueue(Page(Execution("exec-linear", NowMs - DayMs)));
		_gateway.Enqueue(Page());
		_gateway.Enqueue(Page(Execution("exec-option", NowMs - 3 * DayMs)));
		_gateway.Enqueue(Page());

		// Act: запуск по категориям по умолчанию — linear и option.
		var result = await _orchestrator.RunAsync(options: options);

		// Assert: каждая категория запрашивалась своим проходом.
		Assert.That(_gateway.Queries.Select(query => query.Category),
			Is.EqualTo(new[] { "linear", "linear", "option", "option" }));

		// Assert: строка запуска одна на обе категории, счётчик суммирует новые записи.
		Assert.That(LoadRuns(), Has.Count.EqualTo(1));
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.EqualTo(2));
		Assert.That(LoadRawExecutions().Select(execution => execution.Category).OrderBy(category => category),
			Is.EqualTo(new[] { "linear", "option" }));

		// Assert: состояние зафиксировано по каждой категории независимо.
		Assert.That(LoadState("linear")!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(LoadState("option")!.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Пустой список категорий отклоняется до открытия запуска")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnEmptyCategoriesCollection()
	{
		// Arrange — Act: запуск без категорий бессмысленен и не должен оставлять строку SyncRun.
		try
		{
			await _orchestrator.RunAsync(categories: Array.Empty<string>());
		}
		catch (ArgumentException)
		{
			// Assert: журнал запусков и хранилище не тронуты.
			Assert.That(LoadRuns(), Is.Empty);
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Повторяющиеся категории отклоняются до открытия запуска")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnDuplicateCategories()
	{
		// Arrange — Act: повтор категории прочитал бы одну историю дважды в одном запуске.
		try
		{
			await _orchestrator.RunAsync(categories: new[] { "linear", "linear" });
		}
		catch (ArgumentException)
		{
			// Assert: журнал запусков и хранилище не тронуты.
			Assert.That(LoadRuns(), Is.Empty);
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[DataRow(false, false)]
	[Description("Нулевая зависимость конструктора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(bool nullCategorySync, bool nullStateStore = true, bool nullRunJournal = true)
	{
		// Arrange — Act: движок категории, хранилище состояния и журнал запусков обязательны.
		if (nullCategorySync)
		{
			new ExecutionHistorySync(null!, _store, _store);
		}
		else if (nullStateStore)
		{
			new ExecutionHistorySync(CreateEngine(), null!, _store);
		}
		else
		{
			new ExecutionHistorySync(CreateEngine(), _store, null!);
		}
	}

	#region Помощники

	private ExecutionCategorySync CreateEngine() =>
		new(new ExecutionWindowPass(_gateway, _store), _store, new FakeOptionBaseCoinSource("BTC"), new ManualTimeProvider(), _store);

	private static BybitExecution Execution(string execId, long execTimeMs) => new()
	{
		Symbol = "BTCUSDT",
		ExecId = execId,
		Side = "Buy",
		ExecTimeMs = execTimeMs,
	};

	private static BybitPagedResponse<BybitExecution> Page(params BybitExecution[] executions) => new()
	{
		List = executions,
	};

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private List<RawExecution> LoadRawExecutions()
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.RawExecutions.OrderBy(execution => execution.ExecId).ToList();
	}

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

	#region Фиктивные зависимости

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
	/// Фиктивный шлюз биржи: раздаёт заготовленные страницы и ошибки по порядку и помнит
	/// все запросы движка. При исчерпании сценария отвечает пустой страницей без курсора.
	/// </summary>
	private sealed class ScriptedGateway : IBybitHistoryGateway
	{
		private readonly Queue<object> _responses = new();

		/// <summary>Все запросы движка в порядке отправления.</summary>
		public List<BybitExecutionListQuery> Queries { get; } = [];

		public void Enqueue(BybitPagedResponse<BybitExecution> page)
		{
			_responses.Enqueue(page);
		}

		public void EnqueueError(BybitApiException error)
		{
			_responses.Enqueue(error);
		}

		public Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
			BybitExecutionListQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);
			if (_responses.Count == 0)
			{
				return Task.FromResult(new BybitPagedResponse<BybitExecution>());
			}

			var response = _responses.Dequeue();
			return response is BybitApiException error
				? Task.FromException<BybitPagedResponse<BybitExecution>>(error)
				: Task.FromResult((BybitPagedResponse<BybitExecution>)response);
		}

		public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
			BybitDeliveryRecordQuery query,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Оркестратор истории исполнения не запрашивает delivery-записи.");
		}
	}

	#endregion
}
