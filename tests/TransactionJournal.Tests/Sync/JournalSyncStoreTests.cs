using System.Text.Json;
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
/// Проверки EF-адаптера сырого хранилища на временной SQLite-базе: идемпотентная
/// пакетная вставка записей исполнения с пропуском известных execId, хранение сырого
/// JSON целиком, продвижение счётчика запуска, состояние категорий и журнал запусков.
/// </summary>
[TestClass]
public class JournalSyncStoreTests
{
	// Виртуальные часы фиксированы на 2026-01-01: отметки загрузки и запусков детерминированы.
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private JournalSyncStore _store = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-store-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_store = new JournalSyncStore(CreateOptions(), new ManualTimeProvider());
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
	[Description("Пакетная вставка записывает только неизвестные execId и пропускает известные")]
	public async Task TryIfWriteAsyncInsertsOnlyUnknownExecutionsAndSkipsKnownOnes()
	{
		// Arrange: первая пачка целиком новая, вторая содержит уже сохранённый идентификатор.
		// Требование: повторная загрузка известных записей не создаёт дубликатов — вставка
		// идемпотентна по биржевому идентификатору execId.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		await _store.WriteAsync("linear", [Execution("exec-a"), Execution("exec-b")]);

		// Act: вторая пачка повторяет exec-b и приносит новую exec-c.
		var secondResult = await _store.WriteAsync("linear", [Execution("exec-b"), Execution("exec-c")]);

		// Assert: вставлена только новая запись, повтор признан известным и пропущен.
		Assert.That(secondResult.InsertedExecIds, Is.EqualTo(new[] { "exec-c" }));
		Assert.That(secondResult.SkippedKnownCount, Is.EqualTo(1));

		// Assert: в хранилище ровно три записи — дублей нет.
		var storedIds = LoadRawExecutions().Select(execution => execution.ExecId).ToList();
		Assert.That(storedIds, Is.EqualTo(new[] { "exec-a", "exec-b", "exec-c" }));
	}

	[TestMethod]
	[Description("Пакетная вставка сохраняет запись исполнения целиком в сыром виде с отметкой загрузки")]
	public async Task TryIfWriteAsyncStoresWholeRecordAsRawJsonWithFetchTime()
	{
		// Arrange: запись с типовыми полями биржи — цена, комиссия, валюта, сторона, мейкерство.
		// Требование: каждая полученная биржевая запись сохраняется в необработанном виде
		// с идентификатором источника и временем загрузки — сырьё доступно для переразбора.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		// Traceability: openspec:sync/bybit-history#scenario-raw-records-persisted-for-reparse
		var execution = new BybitExecution
		{
			Symbol = "BTC-27DEC24-2800-C",
			OrderId = "order-1",
			Side = "Sell",
			ExecFee = 0.0021m,
			ExecId = "exec-raw-1",
			ExecPrice = 42000.5m,
			ExecQty = 0.15m,
			ExecType = "Trade",
			ExecTimeMs = 1735296000000,
			FeeCurrency = "USDT",
			IsMaker = true,
		};

		// Act
		await _store.WriteAsync("option", [execution]);

		// Assert: строка сырья несёт идентификатор источника, категорию, инструмент,
		// время исполнения для проходов окнами и отметку загрузки с виртуальных часов.
		var row = LoadRawExecutions().Single();
		Assert.That(row.ExecId, Is.EqualTo("exec-raw-1"));
		Assert.That(row.Category, Is.EqualTo("option"));
		Assert.That(row.Symbol, Is.EqualTo("BTC-27DEC24-2800-C"));
		Assert.That(row.ExecTimeMs, Is.EqualTo(1735296000000));
		Assert.That(row.FetchedAt, Is.EqualTo(Now));

		// Assert: сырой JSON разбирается обратно в запись биржи без потери полей.
		var roundTripped = JsonSerializer.Deserialize<BybitExecution>(row.PayloadJson);
		Assert.That(roundTripped!.ExecId, Is.EqualTo("exec-raw-1"));
		Assert.That(roundTripped.Symbol, Is.EqualTo("BTC-27DEC24-2800-C"));
		Assert.That(roundTripped.Side, Is.EqualTo("Sell"));
		Assert.That(roundTripped.ExecPrice, Is.EqualTo(42000.5m));
		Assert.That(roundTripped.ExecFee, Is.EqualTo(0.0021m));
		Assert.That(roundTripped.FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(roundTripped.IsMaker, Is.True);
		Assert.That(roundTripped.ExecTimeMs, Is.EqualTo(1735296000000));
	}

	[TestMethod]
	[Description("Пакетная вставка не удваивает одинаковый execId внутри одной пачки")]
	public async Task TryIfWriteAsyncDeduplicatesSameExecIdInsideSingleBatch()
	{
		// Arrange: пачка дважды содержит один идентификатор — защита от нестандартной выдачи биржи.
		var batch = new[] { Execution("exec-dup"), Execution("exec-dup") };

		// Act
		var result = await _store.WriteAsync("linear", batch);

		// Assert: вставлена одна строка, вторая признана повтором внутри пачки.
		Assert.That(result.InsertedExecIds, Is.EqualTo(new[] { "exec-dup" }));
		Assert.That(LoadRawExecutions(), Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("Пакетная вставка продвигает счётчик новых записей запуска на размер вставки")]
	public async Task TryIfWriteAsyncAdvancesProgressRunCounterWithEachBatch()
	{
		// Arrange: запуск открыт в журнале, счётчик новых записей пока нулевой.
		// Требование: прогресс запуска пишется в SyncRun поэтапно — каждая пачка
		// продвигает счётчик сохранённых записей.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		var run = await _store.StartAsync(SyncRunMode.Backfill);

		// Act: две пачки по одной записи.
		await _store.WriteAsync("linear", [Execution("exec-1")], run);
		await _store.WriteAsync("linear", [Execution("exec-2")], run);

		// Assert: строка запуска в базе и дескриптор в памяти несут согласованный счётчик.
		Assert.That(LoadRun(run.Id)!.NewExecutions, Is.EqualTo(2));
		Assert.That(run.NewExecutions, Is.EqualTo(2));
	}

	[TestMethod]
	[Description("Пустая пачка не меняет хранилище и не создаёт ошибок")]
	public async Task TryIfWriteAsyncWithEmptyBatchInsertsNothing()
	{
		// Act: пустое окно — частый случай на границах истории.
		var result = await _store.WriteAsync("linear", Array.Empty<BybitExecution>());

		// Assert: нулевой итог и пустое хранилище.
		Assert.That(result.InsertedCount, Is.Zero);
		Assert.That(result.SkippedKnownCount, Is.Zero);
		Assert.That(LoadRawExecutions(), Is.Empty);
	}

	[TestMethod]
	[Description("Проверка известных execId возвращает только сохранённые идентификаторы")]
	public async Task TryIfFindKnownAsyncReturnsOnlyStoredExecIds()
	{
		// Arrange: в хранилище две записи.
		await _store.WriteAsync("linear", [Execution("exec-a"), Execution("exec-b")]);

		// Act: пачка содержит один известный и один новый идентификатор.
		var known = await _store.FindKnownAsync(new[] { "exec-a", "exec-unknown" });

		// Assert: известным признан только сохранённый идентификатор.
		Assert.That(known, Is.EqualTo(new HashSet<string> { "exec-a" }));
	}

	[TestMethod]
	[Description("Состояние категории сохраняется и читается обратно целиком")]
	public async Task TryIfStateFindSaveRoundTripsCategoryState()
	{
		// Arrange: у категории ещё нет состояния — успешного синка не было.
		Assert.That(await _store.FindAsync("option"), Is.Null);

		// Act: повторное сохранение той же категории обновляет строку, а не плодит дубли.
		await _store.SaveAsync(new SyncState { Category = "option", ExecWatermarkMs = 1000, BackfillBoundaryMs = 500 });
		await _store.SaveAsync(new SyncState { Category = "option", ExecWatermarkMs = 2000, BackfillBoundaryMs = 500, LastSuccessAt = Now });

		// Assert: чтение возвращает последние водяной знак и границу; строка состояния одна.
		var state = await _store.FindAsync("option");
		Assert.That(state!.ExecWatermarkMs, Is.EqualTo(2000));
		Assert.That(state.BackfillBoundaryMs, Is.EqualTo(500));
		Assert.That(state.LastSuccessAt, Is.EqualTo(Now));
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.SyncStates.Count(), Is.EqualTo(1));
		}
	}

	[TestMethod]
	[Description("Сброс состояния удаляет строку только своей категории: сырые записи и запуски остаются")]
	public async Task TryIfResetRemovesOnlyOwnCategoryState()
	{
		// Arrange: обе категории зафиксировали успешный синк, в хранилище лежат сырые
		// записи обеих категорий и строка запуска синхронизации.
		// Требование: сброс состояния категории удаляет водяные знаки только этой
		// категории; сырые записи, доменные сущности и журнал запусков не затрагиваются.
		// Traceability: openspec:sync/bybit-history#requirement-manual-category-state-reset
		// Traceability: openspec:sync/bybit-history#scenario-reset-keeps-journal-data
		await _store.SaveAsync(new SyncState
		{
			Category = "option",
			ExecWatermarkMs = 2000,
			DeliveryWatermarkMs = 1500,
			BackfillBoundaryMs = 500,
			LastSuccessAt = Now,
		});
		await _store.SaveAsync(new SyncState { Category = "linear", ExecWatermarkMs = 3000, DeliveryWatermarkMs = 2500 });
		await _store.WriteAsync("option", [Execution("exec-opt")]);
		await _store.WriteAsync("linear", [Execution("exec-lin")]);
		var run = await _store.StartAsync(SyncRunMode.Incremental);

		// Act
		await _store.ResetAsync("option");

		// Assert: состояние option удалено — следующий запуск категории выполнит backfill;
		// состояние linear цело.
		Assert.That(await _store.FindAsync("option"), Is.Null);
		var linearState = await _store.FindAsync("linear");
		Assert.That(linearState, Is.Not.Null);
		Assert.That(linearState!.ExecWatermarkMs, Is.EqualTo(3000));
		Assert.That(linearState.DeliveryWatermarkMs, Is.EqualTo(2500));

		// Assert: сырые записи обеих категорий и строка запуска остались в журнале.
		var storedIds = LoadRawExecutions().Select(execution => execution.ExecId).ToList();
		Assert.That(storedIds, Is.EqualTo(new[] { "exec-lin", "exec-opt" }));
		Assert.That(LoadRun(run.Id), Is.Not.Null);
	}

	[TestMethod]
	[Description("Журнал запусков открывает запуск бегущим и закрывает успехом или ошибкой")]
	public async Task TryIfRunJournalClosesRunWithSuccessOrFailure()
	{
		// Arrange: два запуска — один завершится успехом, второй ошибкой.
		// Требование: прерванная синхронизация оставляет журнал согласованным — запуск
		// закрывается со статусом и текстом ошибки, повторный запуск продолжает без дублей.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		var succeededRun = await _store.StartAsync(SyncRunMode.Backfill);
		var failedRun = await _store.StartAsync(SyncRunMode.Incremental);

		// Act
		await _store.MarkSucceededAsync(succeededRun);
		await _store.MarkFailedAsync(failedRun, "ошибка биржи");

		// Assert: первый запуск закрыт успехом с моментом завершения и без ошибки.
		var succeededRow = LoadRun(succeededRun.Id)!;
		Assert.That(succeededRow.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(succeededRow.FinishedAt, Is.EqualTo(Now));
		Assert.That(succeededRow.Error, Is.Null);
		Assert.That(succeededRun.Status, Is.EqualTo(SyncRunStatus.Succeeded));

		// Assert: второй запуск закрыт ошибкой с текстом причины.
		var failedRow = LoadRun(failedRun.Id)!;
		Assert.That(failedRow.Status, Is.EqualTo(SyncRunStatus.Failed));
		Assert.That(failedRow.FinishedAt, Is.EqualTo(Now));
		Assert.That(failedRow.Error, Is.EqualTo("ошибка биржи"));
		Assert.That(failedRun.Status, Is.EqualTo(SyncRunStatus.Failed));
	}

	[TestMethod]
	[Description("Пакетная вставка delivery записывает только неизвестные ключи и пропускает известные")]
	public async Task TryIfDeliveryWriteAsyncInsertsOnlyUnknownKeysAndSkipsKnownOnes()
	{
		// Arrange: первая пачка целиком новая.
		// Требование: повторная загрузка известных delivery-записей не создаёт дубликатов —
		// вставка идемпотентна по паре symbol + deliveryTime.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		await _store.WriteAsync("option", [Delivery("BTC-2JAN26-100000-C", 1735296000000), Delivery("ETH-2JAN26-4000-P", 1735296000000)]);

		// Act: вторая пачка повторяет первый ключ тем же символом, но другим временем
		// доставки (новая запись), и приносит повтор уже сохранённого ключа ETH.
		var secondResult = await _store.WriteAsync("option", [Delivery("BTC-2JAN26-100000-C", 1735382400000), Delivery("ETH-2JAN26-4000-P", 1735296000000)]);

		// Assert: вставлена только новая пара symbol + deliveryTime, повтор признан известным и пропущен.
		Assert.That(secondResult.InsertedKeys,
			Is.EqualTo(new[] { new DeliveryRecordKey("BTC-2JAN26-100000-C", 1735382400000) }));
		Assert.That(secondResult.SkippedKnownCount, Is.EqualTo(1));

		// Assert: в хранилище ровно три записи — дублей нет.
		Assert.That(LoadRawDeliveries(), Has.Count.EqualTo(3));
	}

	[TestMethod]
	[Description("Пакетная вставка delivery сохраняет запись целиком в сыром виде с отметкой загрузки")]
	public async Task TryIfDeliveryWriteAsyncStoresWholeRecordAsRawJsonWithFetchTime()
	{
		// Arrange: запись с типовыми полями биржи — сторона, позиция, цены, страйк, комиссия, PnL.
		// Требование: каждая полученная биржевая запись сохраняется в необработанном виде
		// с идентификатором источника и временем загрузки — сырьё доступно для переразбора.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		// Traceability: openspec:sync/bybit-history#scenario-raw-records-persisted-for-reparse
		var delivery = new BybitDeliveryRecord
		{
			Symbol = "BTC-27DEC24-2800-C",
			DeliveryTimeMs = 1735296000000,
			Side = "Sell",
			Position = 0.15m,
			EntryPrice = 42000.5m,
			DeliveryPrice = 41000m,
			Strike = 28000m,
			Fee = 0.0021m,
			DeliveryRpl = 195.0m,
		};

		// Act
		await _store.WriteAsync("option", [delivery]);

		// Assert: строка сырья несёт ключ источника, категорию и отметку загрузки с виртуальных часов.
		var row = LoadRawDeliveries().Single();
		Assert.That(row.Symbol, Is.EqualTo("BTC-27DEC24-2800-C"));
		Assert.That(row.DeliveryTimeMs, Is.EqualTo(1735296000000));
		Assert.That(row.Category, Is.EqualTo("option"));
		Assert.That(row.FetchedAt, Is.EqualTo(Now));

		// Assert: сырой JSON разбирается обратно в запись биржи без потери полей.
		var roundTripped = JsonSerializer.Deserialize<BybitDeliveryRecord>(row.PayloadJson);
		Assert.That(roundTripped!.Symbol, Is.EqualTo("BTC-27DEC24-2800-C"));
		Assert.That(roundTripped.DeliveryTimeMs, Is.EqualTo(1735296000000));
		Assert.That(roundTripped.Side, Is.EqualTo("Sell"));
		Assert.That(roundTripped.Position, Is.EqualTo(0.15m));
		Assert.That(roundTripped.EntryPrice, Is.EqualTo(42000.5m));
		Assert.That(roundTripped.DeliveryPrice, Is.EqualTo(41000m));
		Assert.That(roundTripped.Strike, Is.EqualTo(28000m));
		Assert.That(roundTripped.Fee, Is.EqualTo(0.0021m));
		Assert.That(roundTripped.DeliveryRpl, Is.EqualTo(195.0m));
	}

	[TestMethod]
	[Description("Пакетная вставка delivery не удваивает одинаковый ключ внутри одной пачки")]
	public async Task TryIfDeliveryWriteAsyncDeduplicatesSameKeyInsideSingleBatch()
	{
		// Arrange: пачка дважды содержит одну пару symbol + deliveryTime.
		var batch = new[]
		{
			Delivery("BTC-2JAN26-100000-C", 1735296000000),
			Delivery("BTC-2JAN26-100000-C", 1735296000000),
		};

		// Act
		var result = await _store.WriteAsync("option", batch);

		// Assert: вставлена одна строка, вторая признана повтором внутри пачки.
		Assert.That(result.InsertedCount, Is.EqualTo(1));
		Assert.That(LoadRawDeliveries(), Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("Пакетная вставка delivery продвигает счётчик новых записей запуска на размер вставки")]
	public async Task TryIfDeliveryWriteAsyncAdvancesProgressRunCounterWithEachBatch()
	{
		// Arrange: запуск открыт в журнале, счётчик delivery-записей пока нулевой.
		// Требование: прогресс запуска пишется в SyncRun поэтапно — каждая пачка
		// продвигает счётчик сохранённых delivery-записей.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		var run = await _store.StartAsync(SyncRunMode.Backfill);

		// Act: две пачки по одной записи.
		await _store.WriteAsync("option", [Delivery("BTC-2JAN26-100000-C", 1735296000000)], run);
		await _store.WriteAsync("option", [Delivery("ETH-2JAN26-4000-P", 1735296000000)], run);

		// Assert: строка запуска в базе и дескриптор в памяти несут согласованный счётчик.
		Assert.That(LoadRun(run.Id)!.NewDeliveries, Is.EqualTo(2));
		Assert.That(run.NewDeliveries, Is.EqualTo(2));
	}

	[TestMethod]
	[Description("Пустая пачка delivery не меняет хранилище и не создаёт ошибок")]
	public async Task TryIfDeliveryWriteAsyncWithEmptyBatchInsertsNothing()
	{
		// Act: пустое окно — частый случай на границах delivery-истории.
		var result = await _store.WriteAsync("option", Array.Empty<BybitDeliveryRecord>());

		// Assert: нулевой итог и пустое хранилище.
		Assert.That(result.InsertedCount, Is.Zero);
		Assert.That(result.SkippedKnownCount, Is.Zero);
		Assert.That(LoadRawDeliveries(), Is.Empty);
	}

	[TestMethod]
	[Description("Проверка известных delivery-ключей возвращает только сохранённые пары")]
	public async Task TryIfDeliveryFindKnownAsyncReturnsOnlyStoredKeys()
	{
		// Arrange: в хранилище две записи с разным временем доставки одного символа.
		await _store.WriteAsync("option", [Delivery("BTC-2JAN26-100000-C", 1735296000000)]);

		// Act: пачка содержит известную пару, тот же символ с другим временем и новый символ.
		var known = await _store.FindKnownAsync(new[]
		{
			new DeliveryRecordKey("BTC-2JAN26-100000-C", 1735296000000),
			new DeliveryRecordKey("BTC-2JAN26-100000-C", 1735382400000),
			new DeliveryRecordKey("ETH-2JAN26-4000-P", 1735296000000),
		});

		// Assert: известной признана только сохранённая пара symbol + deliveryTime.
		Assert.That(known, Is.EqualTo(new HashSet<DeliveryRecordKey> { new("BTC-2JAN26-100000-C", 1735296000000) }));
	}

	[TestMethod]
	[DataRow(" ")]
	[Description("Пакетная вставка delivery отклоняет пустую категорию")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnDeliveryWriteAsyncNullOrWhiteSpaceCategory(string category)
	{
		// Arrange — Act: категория — обязательный атрибут сырой delivery-записи.
		await _store.WriteAsync(category, [Delivery("BTC-2JAN26-100000-C", 1735296000000)]);
	}

	[TestMethod]
	[Description("Пакетная вставка delivery отклоняет null-категорию")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnDeliveryWriteAsyncNullCategory()
	{
		// Arrange — Act: null-категория отвергается отдельной веткой проверки.
		await _store.WriteAsync(null!, [Delivery("BTC-2JAN26-100000-C", 1735296000000)]);
	}

	[TestMethod]
	[Description("Пакетная вставка delivery отклоняет null-пачку записей")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnDeliveryWriteAsyncNullDeliveries()
	{
		// Arrange — Act: записи пачки обязательны.
		await _store.WriteAsync("option", (IReadOnlyCollection<BybitDeliveryRecord>)null!);
	}

	[TestMethod]
	[Description("Проверка известных delivery-ключей отклоняет null-пачку")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnDeliveryFindKnownAsyncNullKeys()
	{
		// Arrange — Act: пачка ключей обязательна.
		await _store.FindKnownAsync((IReadOnlyCollection<DeliveryRecordKey>)null!);
	}

	[TestMethod]
	[DataRow(" ")]
	[Description("Пакетная вставка отклоняет пустую категорию")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnWriteAsyncNullOrWhiteSpaceCategory(string category)
	{
		// Arrange — Act: категория — обязательный атрибут сырой записи.
		await _store.WriteAsync(category, [Execution("exec-a")]);
	}

	[TestMethod]
	[Description("Пакетная вставка отклоняет null-категорию")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnWriteAsyncNullCategory()
	{
		// Arrange — Act: null-категория отвергается отдельной веткой проверки.
		await _store.WriteAsync(null!, [Execution("exec-a")]);
	}

	[TestMethod]
	[Description("Пакетная вставка отклоняет null-пачку записей")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnWriteAsyncNullExecutions()
	{
		// Arrange — Act: записи пачки обязательны.
		await _store.WriteAsync("linear", (IReadOnlyCollection<BybitExecution>)null!);
	}

	[TestMethod]
	[Description("Закрытие успеха отклоняет запуск, которого нет в журнале")]
	[ExpectedException(typeof(InvalidOperationException))]
	public async Task ThrowOnMarkSucceededAsyncUnknownRun()
	{
		// Arrange: дескриптор запуска не проходит через StartAsync — строки в журнале нет.
		var orphanRun = new SyncRun { Id = 404, StartedAt = Now, Mode = SyncRunMode.Backfill, Status = SyncRunStatus.Running };

		// Act
		await _store.MarkSucceededAsync(orphanRun);
	}

	[TestMethod]
	[DataRow("")]
	[Description("Закрытие ошибки отклоняет пустой текст ошибки")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnMarkFailedAsyncEmptyError(string error)
	{
		// Arrange: запуск открыт корректно.
		var run = await _store.StartAsync(SyncRunMode.Backfill);

		// Act: текст ошибки обязателен для диагностики прерывания.
		await _store.MarkFailedAsync(run, error);
	}

	[TestMethod]
	[Description("Закрытие ошибки отклоняет null-текст ошибки")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnMarkFailedAsyncNullError()
	{
		// Arrange: запуск открыт корректно.
		var run = await _store.StartAsync(SyncRunMode.Backfill);

		// Act: null-текст отвергается отдельной веткой проверки.
		await _store.MarkFailedAsync(run, null!);
	}

	[TestMethod]
	[Description("Адаптер отклоняет null-опции контекста")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorOptions()
	{
		// Arrange — Act: опции контекста обязательны.
		new JournalSyncStore(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private List<RawExecution> LoadRawExecutions()
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.RawExecutions.OrderBy(execution => execution.ExecId).ToList();
	}

	private List<RawDelivery> LoadRawDeliveries()
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.RawDeliveries
			.OrderBy(delivery => delivery.Symbol)
			.ThenBy(delivery => delivery.DeliveryTimeMs)
			.ToList();
	}

	private SyncRun? LoadRun(long runId)
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.SyncRuns.Find(runId);
	}

	private static BybitExecution Execution(string execId) => new()
	{
		Symbol = "BTCUSDT",
		ExecId = execId,
		Side = "Buy",
		ExecTimeMs = 1735296000000,
	};

	private static BybitDeliveryRecord Delivery(string symbol, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		DeliveryTimeMs = deliveryTimeMs,
		Side = "Sell",
		Position = 0.1m,
		Strike = 100000m,
	};

	#endregion
}
