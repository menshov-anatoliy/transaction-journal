using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Domain;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Интеграционные проверки use-case пересбора конструкций на SQLite-базе:
/// план сборки применяется одной транзакцией, счётчики соответствуют плану,
/// ручные данные стираются безвозвратно, сырьё и состояние синхронизации
/// остаются нетронутыми, повторный прогон воспроизводит результат.
/// </summary>
[TestClass]
public class ConstructionAssemblyServiceTests
{
	private const string EthCall1600 = "ETH-25SEP26-1600-C-USDT";
	private const string EthPut1600 = "ETH-25SEP26-1600-P-USDT";
	private const string EthCall2100Dec = "ETH-25DEC26-2100-C-USDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Момент «сейчас» проверок: сделки истории позади, экспирация доски ещё не наступила.</summary>
	private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-assembly-tests-{Guid.NewGuid():N}.db");
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();
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
	[Description("Пересбор возвращает счётчики плана и наполняет базу его конструкциями и привязками")]
	public async Task TryIfRebuildCountersAndStorageMatchAssemblerPlan()
	{
		// Arrange: стреддл, две закрывающие продажи, сделка робота внутри периода и вне его.
		SeedRawStorage();
		var service = CreateService();
		var expectedPlan = ExpectedPlan();

		// Act: выполняем пересбор.
		var result = await service.RebuildAsync();

		// Assert: счётчики совпадают с планом, состав базы — тоже.
		Assert.That(result.ConstructionsCount, Is.EqualTo(expectedPlan.Constructions.Count), "Счётчик конструкций совпадает с планом");
		Assert.That(result.BoundCount, Is.EqualTo(expectedPlan.Bindings.Count), "Счётчик привязок совпадает с планом");
		Assert.That(result.TradesInInbox, Is.EqualTo(expectedPlan.InboxCount), "Счётчик «Входящих» совпадает с планом");

		using (var db = new JournalDbContext(CreateOptions()))
		{
			var constructions = db.Constructions.OrderBy(construction => construction.Id).ToList();
			Assert.That(constructions, Has.Count.EqualTo(1), "План воспроизведён в базе одной конструкцией");
			Assert.That(constructions[0].Name, Is.EqualTo(expectedPlan.Constructions[0].Name), "Имя конструкции воспроизведено из плана");

			var bindings = db.TradeUserdata.ToDictionary(userdata => userdata.ExecId, userdata => userdata.ConstructionId);
			Assert.That(bindings, Has.Count.EqualTo(expectedPlan.Bindings.Count), "Число привязок в базе совпадает с планом");
			foreach (var pair in expectedPlan.Bindings)
			{
				Assert.That(bindings.TryGetValue(pair.Key, out var databaseId), Is.True, $"Сделка {pair.Key} привязана");
				Assert.That(databaseId, Is.EqualTo(constructions[(int)(pair.Value - 1)].Id), $"Привязка {pair.Key} указывает на конструкцию плана");
			}

			// Сделка робота e6 привязана: опционные ноги обнулены, но фьючерсный
			// остаток был жив — затухающая конструкция приняла сделку и закрылась
			// её счётом в момент обнуления последней позиции.
			// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-fading-construction-absorbs-robot-trades
			Assert.That(bindings.ContainsKey("e6"), Is.True, "Затухающая конструкция приняла сделку робота");
		}
	}

	[TestMethod]
	[Description("Повторная сборка над тем же сырьём воспроизводит состав конструкций и привязки")]
	// Состав конструкций, их атрибуты и привязки сделок совпадают с предыдущим
	// прогоном: алгоритм детерминирован, применение плана идемпотентно.
	// Traceability: openspec:domain/construction-assembly#scenario-rebuild-reproduces-result
	public async Task TryIfSecondRebuildReproducesFirstResult()
	{
		// Arrange: сырьё загружено, первый прогон зафиксировал состояние базы.
		SeedRawStorage();
		var service = CreateService();
		await service.RebuildAsync();
		var first = CaptureState();

		// Act: повторный прогон над тем же сырьём.
		await service.RebuildAsync();
		var second = CaptureState();

		// Assert: состояние базы идентично первому прогону.
		Assert.That(second.Constructions, Is.EqualTo(first.Constructions), "Состав и атрибуты конструкций воспроизводятся");
		Assert.That(second.Userdata, Is.EqualTo(first.Userdata), "Привязки сделок воспроизводятся");
	}

	[TestMethod]
	[Description("Пересбор стирает конструкции с ручными данными и строит их заново по правилам сборки")]
	// Все конструкции и ручные данные — привязки, комментарии, корректировки,
	// пометки закрытия — удаляются безвозвратно, конструкции строятся заново.
	// Traceability: openspec:domain/construction-assembly#scenario-rebuild-wipes-manual-data
	public async Task TryIfRebuildWipesManualData()
	{
		// Arrange: старая конструкция с капиталом, комментарием, корректировкой,
		// комментарием позиции, ручной пометкой закрытия и пользовательскими
		// данными сделок — привязанной и комментарием без привязки.
		SeedRawStorage();
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var old = new Construction
			{
				Name = "Старая конструкция",
				Status = ConstructionStatus.Archived,
				AllocatedCapitalUsdt = 100m,
				RiskValue = 10m,
				RiskUnit = TargetUnit.Usdt,
				ProfitValue = 30m,
				ProfitUnit = TargetUnit.Percent,
				Comment = "ручной комментарий конструкции",
			};
			db.Constructions.Add(old);
			db.PnLAdjustments.Add(new PnLAdjustment
			{
				Construction = old,
				Date = Now,
				Source = PnLAdjustmentSource.Manual,
				AmountUsdt = -5m,
				Comment = "ручная корректировка",
			});
			db.PositionComments.Add(new PositionComment
			{
				Construction = old,
				Symbol = "ETHUSDT",
				Text = "комментарий позиции",
			});
			db.ManualCloseMarks.Add(new ManualCloseMark
			{
				Construction = old,
				Symbol = "ETHUSDT",
				Price = 3000m,
				MarkedAt = Now,
			});
			db.TradeUserdata.Add(new TradeUserdata
			{
				ExecId = "e6",
				Construction = old,
				Comment = "комментарий сделки",
			});
			db.TradeUserdata.Add(new TradeUserdata
			{
				ExecId = "e-extra",
				Comment = "комментарий без привязки",
			});
			db.SaveChanges();
		}

		var service = CreateService();

		// Act: выполняем пересбор.
		await service.RebuildAsync();

		// Assert: в базе ровно собранные конструкции без ручных данных и привязки по плану.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var constructions = db.Constructions.ToList();
			Assert.That(constructions.Select(construction => construction.Name), Is.EqualTo(new[] { "ETH направленная PUT 25SEP26 1600" }), "Осталась только собранная конструкция с именем по живым ногам");
			var construction = constructions.Single();
			Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Closed), "Статус следует за всеми позициями: фьючерсный остаток обнулён сделкой e6");
			// Статус Closed теперь означает обнуление последней позиции — фьючерсного
			// остатка, переживший гибель опционного прикрытия: затухание закрылось
			// сделкой e6 в момент вывода остатка в ноль.
			// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-last-position-flat-closes-construction
			Assert.That(construction.AllocatedCapitalUsdt, Is.Null, "Выделенный капитал ручной конструкции стёрт");
			Assert.That(construction.RiskValue, Is.Null, "Риск ручной конструкции стёрт");
			Assert.That(construction.ProfitValue, Is.Null, "Профит ручной конструкции стёрт");
			Assert.That(construction.Comment, Is.Null, "Комментарий конструкции стёрт");

			Assert.That(db.PnLAdjustments, Is.Empty, "Внешние корректировки PnL стёрты");
			Assert.That(db.PositionComments, Is.Empty, "Комментарии позиций стёрты");
			Assert.That(db.ManualCloseMarks, Is.Empty, "Ручные пометки закрытия стёрты");

			var userdata = db.TradeUserdata.ToList();
			Assert.That(userdata.Select(row => row.ExecId), Is.EquivalentTo(new[] { "e1", "e2", "e3", "e4", "e5", "e6" }), "Остались только привязки плана");
			Assert.That(userdata.All(row => row.Comment is null), Is.True, "Комментарии сделок стёрты");
			Assert.That(userdata.All(row => row.ConstructionId == construction.Id), Is.True, "Все привязки указывают на собранную конструкцию");
		}
	}

	[TestMethod]
	[Description("Пересбор не изменяет сырьё, справочник инструментов и состояние синхронизации")]
	// Сырые записи, справочник инструментов и состояние синхронизации после
	// пересборки не изменены.
	// Traceability: openspec:domain/construction-assembly#scenario-rebuild-preserves-raw-storage
	public async Task TryIfRebuildPreservesRawStorage()
	{
		// Arrange: сырьё с delivery-записью датированного фьючерса и журналом синхронизации.
		SeedRawStorage();
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawDeliveries.Add(new RawDelivery
			{
				Symbol = "ETH-25SEP26",
				Category = "linear",
				DeliveryTimeMs = Ms(2026, 9, 25, 8, 0),
				PayloadJson = """{"symbol":"ETH-25SEP26","deliveryTime":"1758787200000"}""",
				FetchedAt = FetchedAt,
			});
			db.SyncRuns.Add(new SyncRun
			{
				StartedAt = Now,
				Mode = SyncRunMode.Backfill,
				Status = SyncRunStatus.Succeeded,
			});
			db.SyncStates.Add(new SyncState
			{
				Category = "linear",
				ExecWatermarkMs = Ms(2026, 8, 10, 10, 0),
			});
			db.SaveChanges();
		}

		var service = CreateService();
		var before = CaptureRawState();

		// Act: выполняем пересбор.
		await service.RebuildAsync();

		// Assert: сырьё, справочник и служебные записи синхронизации не изменились.
		var after = CaptureRawState();
		Assert.That(after.Executions, Is.EqualTo(before.Executions), "Сырые записи исполнения не изменены");
		Assert.That(after.Deliveries, Is.EqualTo(before.Deliveries), "Сырые delivery-записи не изменены");
		Assert.That(after.Instruments, Is.EqualTo(before.Instruments), "Справочник инструментов не изменён");
		Assert.That(after.SyncRuns, Is.EqualTo(before.SyncRuns), "Журнал синхронизаций не изменён");
		Assert.That(after.SyncStates, Is.EqualTo(before.SyncStates), "Состояние синхронизации не изменено");
	}

	[TestMethod]
	[Description("Фандинг и прочие не-Trade записи сырья не становятся сделками сборки")]
	// Сделкой сборки становится только исполнение биржевого типа Trade: фандинг
	// остаётся в сырье, но в план и привязки не попадает.
	// Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades
	public async Task TryIfFundingExecutionDoesNotEnterAssemblyPlan()
	{
		// Arrange: стандартное сырьё плюс фандинг-запись linear-инструмента.
		SeedRawStorage();
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawExecutions.Add(Raw("f1", "linear", "ETHUSDT", Ms(2026, 7, 13, 0, 0),
				"""{"symbol":"ETHUSDT","orderId":"","orderLinkId":"","side":"","execFee":"-0.5","execId":"f1","execPrice":"0","execQty":"0.5","execType":"Funding","execTime":"7000000000000","feeCurrency":"USDT","isMaker":false}"""));
			db.SaveChanges();
		}

		var service = CreateService();

		// Act: выполняем пересбор.
		var result = await service.RebuildAsync();

		// Assert: счётчики плана не изменились, фандинг не привязан; сделка e6
		// привязана пересбором к затухавшей конструкции, поэтому «Входящие» пусты.
		Assert.That(result.BoundCount, Is.EqualTo(6), "Фандинг не увеличил число привязок");
		Assert.That(result.TradesInInbox, Is.EqualTo(0), "«Входящие» пусты: сделка e6 привязана к затухавшей конструкции");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.TradeUserdata.Any(userdata => userdata.ExecId == "f1"), Is.False, "Фандинг не привязан к конструкции");
		}
	}

	[TestMethod]
	[Description("Пустое хранилище даёт нулевые счётчики без ошибок")]
	public async Task TryIfEmptyStorageGivesZeroCounters()
	{
		// Arrange: сырых записей нет — команда доступна и на пустом журнале.
		var service = CreateService();

		// Act: выполняем пересбор пустого хранилища.
		var result = await service.RebuildAsync();

		// Assert: итог согласованно пуст.
		Assert.That(result.ConstructionsCount, Is.EqualTo(0), "Конструкций не создано");
		Assert.That(result.BoundCount, Is.EqualTo(0), "Привязок не создано");
		Assert.That(result.TradesInInbox, Is.EqualTo(0), "Сделок во «Входящих» нет");
	}

	[TestMethod]
	[Description("Неудача резервной копии блокирует пересбор: снимок не читается, данные журнала не меняются")]
	// Копия создаётся до любой работы пересбора; когда копирование падает,
	// пересбор не запускается и сырьё остаётся нетронутым.
	// Traceability: openspec:domain/construction-assembly#scenario-rebuild-blocked-without-backup
	public async Task TryIfRebuildBlockedWithoutBackup()
	{
		// Arrange: в сырьё есть записи для пересбора, но копирование базы падает.
		SeedRawStorage();
		var backup = new StubJournalBackupService { Failure = new IOException("нет места на диске") };
		var snapshotStore = new CountingSnapshotStore();
		var service = new ConstructionAssemblyService(
			snapshotStore,
			backup,
			CreateOptions(),
			new FixedTimeProvider(Now));

		// Act: пересбор отклоняется исключением неудавшейся копии.
		try
		{
			await service.RebuildAsync();
			Assert.Fail("Ожидалось исключение неудавшейся резервной копии.");
		}
		catch (IOException failure)
		{
			Assert.That(failure.Message, Is.EqualTo("нет места на диске"), "Причина неудачи копии проходит наружу");
		}

		// Assert: копия запрашивалась с причиной «rebuild» ровно один раз и до
		// чтения снимка сырья — пересбор даже не начал работу, сырьё не изменено.
		Assert.That(backup.Reasons, Is.EqualTo(new[] { "rebuild" }), "Попытка копии с причиной rebuild предшествует пересбору");
		Assert.That(snapshotStore.LoadCalls, Is.EqualTo(0), "Снимок сырья не читается без успешной копии");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(await db.RawExecutions.CountAsync(), Is.EqualTo(6), "Сырые записи остались нетронутыми");
			Assert.That(await db.Constructions.CountAsync(), Is.EqualTo(0), "Доменные таблицы не вычищались");
		}
	}

	[TestMethod]
	[Description("Сборка из «Входящих» обрабатывает только непривязанные записи и переживает повторный запуск")]
	// Повторный запуск после синхронизации обрабатывает только новые «Входящие»:
	// прежние привязки и закрытая конструкция не изменяются, создаются только
	// новые конструкции и привязки.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-rerun-processes-only-inbox
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-incremental-inbox-assembly
	public async Task TryIfInboxAssemblyProcessesOnlyUnboundTrades()
	{
		// Arrange: пересбор собрал стреддл из e1..e5, затухание после гибели ног
		// закрыла сделка e6, обнулив фьючерсный остаток; затем синхронизация
		// принесла новые записи — докупку колла и сделку робота.
		SeedRawStorage();
		var backup = new StubJournalBackupService();
		var service = CreateService(backupService: backup);
		await service.RebuildAsync();

		// Пересбор сделал ровно одну копию с причиной «rebuild»; дальнейших копий
		// инкрементная сборка не добавляет — для «Входящих» защита не предусмотрена.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		Assert.That(backup.Reasons, Is.EqualTo(new[] { "rebuild" }), "Пересбор сделал одну копию с причиной rebuild");
		var backupsAfterRebuild = backup.Reasons.Count;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawExecutions.Add(Raw("e7", "option", EthCall1600, Ms(2026, 8, 15, 10, 0),
				ExecutionPayload("e7", EthCall1600, "Buy", "2400", "1", "0.01", "USDC", Ms(2026, 8, 15, 10, 0))));
			db.RawExecutions.Add(Raw("e8", "linear", "ETHUSDT", Ms(2026, 8, 16, 10, 0),
				ExecutionPayload("e8", "ETHUSDT", "Sell", "3100", "0.5", "-0.01", "USDT", Ms(2026, 8, 16, 10, 0))));
			db.SaveChanges();
		}

		// Act: собираем из «Входящих».
		var result = await service.AssembleInboxAsync();

		// Assert: создана одна новая конструкция, привязаны только новые записи,
		// закрытая конструкция и прежние привязки не изменены, «Входящие» пусты.
		Assert.That(result.ConstructionsCount, Is.EqualTo(1), "Из «Входящих» создана одна конструкция");
		Assert.That(result.BoundCount, Is.EqualTo(2), "Привязаны только новые записи");
		Assert.That(result.TradesInInbox, Is.EqualTo(0), "«Входящие» пусты: e6 привязан пересбором");

		using (var db = new JournalDbContext(CreateOptions()))
		{
			var constructions = db.Constructions.OrderBy(construction => construction.Id).ToList();
			Assert.That(constructions, Has.Count.EqualTo(2), "Существующая конструкция сохранена, новая добавлена");
			Assert.That(constructions[0].Name, Is.EqualTo("ETH направленная PUT 25SEP26 1600"), "Имя существующей конструкции не пересчитано без изменений ног");
			Assert.That(constructions[0].Status, Is.EqualTo(ConstructionStatus.Closed), "Статус существующей конструкции не изменён");
			Assert.That(constructions[1].Name, Is.EqualTo("ETH направленная CALL 25SEP26 1600"), "Новая конструкция названа по живой ноге");
			Assert.That(constructions[1].Status, Is.EqualTo(ConstructionStatus.Open), "Новая конструкция открыта");

			var constructionIdByExecId = db.TradeUserdata.ToDictionary(userdata => userdata.ExecId, userdata => userdata.ConstructionId);
			Assert.That(constructionIdByExecId["e7"], Is.EqualTo(constructions[1].Id), "Новая опционная сделка привязана к новой конструкции");
			Assert.That(constructionIdByExecId["e8"], Is.EqualTo(constructions[1].Id), "Сделка робота в периоде новой ноги привязана к ней");
			Assert.That(constructionIdByExecId["e1"], Is.EqualTo(constructions[0].Id), "Прежняя привязка не изменена");
			// Затухавшая конструкция закрыта сделкой e6, поэтому докупка колла e7
			// открыла новую конструкцию, а не усреднила затухание.
			// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-last-position-flat-closes-construction
			Assert.That(constructionIdByExecId["e6"], Is.EqualTo(constructions[0].Id), "Сделка e6 привязана к затухавшей конструкции");
		}

		// Повторный прогон без новых записей ничего не меняет: инкремент идемпотентен.
		var before = CaptureState();
		var rerun = await service.AssembleInboxAsync();
		Assert.That(rerun.ConstructionsCount, Is.EqualTo(0), "Повторный прогон не создаёт конструкций");
		Assert.That(rerun.BoundCount, Is.EqualTo(0), "Повторный прогон не создаёт привязок");
		Assert.That(rerun.TradesInInbox, Is.EqualTo(0), "Повторный прогон оставляет «Входящие» пустыми");
		var after = CaptureState();
		Assert.That(after.Constructions, Is.EqualTo(before.Constructions), "Повторный прогон не меняет конструкции");
		Assert.That(after.Userdata, Is.EqualTo(before.Userdata), "Повторный прогон не меняет привязки");

		// Инкрементная сборка выполняется без резервного копирования: копии делает
		// только полный пересбор, для «Входящих» защита не предусмотрена.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		Assert.That(backup.Reasons.Count, Is.EqualTo(backupsAfterRebuild), "Инкрементная сборка не создаёт резервных копий");
	}

	[TestMethod]
	[Description("Сборка из «Входящих» не изменяет ручные данные существующей конструкции")]
	// Капитал, риск и профит, комментарии, корректировка PnL и пометка закрытия
	// переживают инкрементную сборку; обновляются только производные имя и статус.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-manual-data-survives-incremental-assembly
	public async Task TryIfManualDataSurvivesInboxAssembly()
	{
		// Arrange: ручная конструкция с привязанной покупкой колла и полным
		// набором ручных данных; покупка пута того же страйка пришла во «Входящих».
		SeedRawStorage();
		long manualId;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var manual = new Construction
			{
				Name = "Ручная разгонка",
				AllocatedCapitalUsdt = 100m,
				RiskValue = 10m,
				RiskUnit = TargetUnit.Usdt,
				ProfitValue = 30m,
				ProfitUnit = TargetUnit.Percent,
				Comment = "ручной комментарий конструкции",
			};
			db.Constructions.Add(manual);
			db.PnLAdjustments.Add(new PnLAdjustment
			{
				Construction = manual,
				Date = Now,
				Source = PnLAdjustmentSource.Manual,
				AmountUsdt = -5m,
				Comment = "ручная корректировка",
			});
			db.PositionComments.Add(new PositionComment
			{
				Construction = manual,
				Symbol = "ETHUSDT",
				Text = "комментарий позиции",
			});
			db.ManualCloseMarks.Add(new ManualCloseMark
			{
				Construction = manual,
				Symbol = "ETHUSDT",
				Price = 3000m,
				MarkedAt = Now,
			});
			db.TradeUserdata.Add(new TradeUserdata
			{
				ExecId = "e1",
				Construction = manual,
				Comment = "комментарий сделки",
			});
			db.SaveChanges();
			manualId = manual.Id;
		}

		var service = CreateService();

		// Act: собираем из «Входящих» — пут присоединяется к живой конструкции.
		var result = await service.AssembleInboxAsync();

		// Assert: ручные данные нетронуты, привязка колла сохранена вместе с
		// комментарием, пут и сделки робота привязаны к той же конструкции.
		Assert.That(result.ConstructionsCount, Is.EqualTo(0), "Новые конструкции не созданы");
		Assert.That(result.BoundCount, Is.EqualTo(5), "Привязаны записи «Входящих» из периодов конструкции, включая сделку робота в затухании");
		Assert.That(result.TradesInInbox, Is.EqualTo(0), "«Входящие» пусты: e6 привязан в периоде затухания");

		using (var db = new JournalDbContext(CreateOptions()))
		{
			var manual = db.Constructions.Single(construction => construction.Id == manualId);
			Assert.That(manual.Name, Is.EqualTo("ETH направленная PUT 25SEP26 1600"), "Имя выводится из живых ног и после полного обнуления хранит последнее производное");
			Assert.That(manual.Status, Is.EqualTo(ConstructionStatus.Closed), "Статус следует за всеми позициями: затухание закрыто сделкой, обнулившей фьючерс");
			// Затухание (ноги обнулены, фьючерсный остаток жив) остаётся открытым до
			// сделки e6, выводящей остаток в ноль: закрытие происходит в момент e6.
			// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-last-position-flat-closes-construction
			Assert.That(manual.AllocatedCapitalUsdt, Is.EqualTo(100m), "Выделенный капитал не изменён");
			Assert.That(manual.RiskValue, Is.EqualTo(10m), "Значение риска не изменено");
			Assert.That(manual.RiskUnit, Is.EqualTo(TargetUnit.Usdt), "Единица риска не изменена");
			Assert.That(manual.ProfitValue, Is.EqualTo(30m), "Значение профита не изменено");
			Assert.That(manual.ProfitUnit, Is.EqualTo(TargetUnit.Percent), "Единица профита не изменена");
			Assert.That(manual.Comment, Is.EqualTo("ручной комментарий конструкции"), "Комментарий конструкции не изменён");
			Assert.That(manual.NameIsManual, Is.False, "Имя не было отредактировано вручную и пересчитано ассемблером");

			Assert.That(db.PnLAdjustments.Single().AmountUsdt, Is.EqualTo(-5m), "Корректировка PnL не изменена");
			Assert.That(db.PositionComments.Single().Text, Is.EqualTo("комментарий позиции"), "Комментарий позиции не изменён");
			Assert.That(db.ManualCloseMarks.Single().Price, Is.EqualTo(3000m), "Ручная пометка закрытия не изменена");

			var bindingByExecId = db.TradeUserdata.ToDictionary(userdata => userdata.ExecId);
			Assert.That(bindingByExecId["e1"].ConstructionId, Is.EqualTo(manualId), "Привязка колла сохранена");
			Assert.That(bindingByExecId["e1"].Comment, Is.EqualTo("комментарий сделки"), "Комментарий сделки не изменён");
			Assert.That(bindingByExecId["e2"].ConstructionId, Is.EqualTo(manualId), "Пут присоединён к той же конструкции");
			Assert.That(bindingByExecId["e6"].ConstructionId, Is.EqualTo(manualId), "Сделка e6 привязана в периоде затухания");
		}
	}

	[TestMethod]
	[Description("Инкремент пересчитывает статус существующей конструкции по фьючерсному остатку seed'а")]
	// Seed несёт фьючерсный остаток из read-модели позиций: ненулевой остаток
	// возвращает конструкцию в «открыта» после гибели прикрытия, а ручная
	// пометка, обнулившая остаток между прогонами, закрывает её снова.
	// Traceability: change:close-construction-on-all-positions/design#d3
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-cover-loss-with-open-futures-keeps-open
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-last-position-flat-closes-construction
	public async Task TryIfInboxAssemblyRecalculatesStatusBySeedFuturesResidual()
	{
		// Arrange: конструкция со связанным стреддлом (ноги обнулены продажами),
		// связанной покупкой фьючерса и статусом «закрыта» от прежнего правила —
		// обнуляющая фьючерс запись ещё не приходила.
		SeedFadingStorage();
		long constructionId;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var construction = new Construction
			{
				Name = "ETH направленная PUT 25SEP26 1600",
				Status = ConstructionStatus.Closed,
			};
			db.Constructions.Add(construction);
			foreach (var execId in new[] { "e1", "e2", "e3", "e4", "e5" })
			{
				db.TradeUserdata.Add(new TradeUserdata { ExecId = execId, Construction = construction });
			}

			db.SaveChanges();
			constructionId = construction.Id;
		}

		var service = CreateService();

		// Act: инкремент без новых записей пересчитывает производные атрибуты по seed'у.
		var result = await service.AssembleInboxAsync();

		// Assert: read-модель даёт ненулевой фьючерсный остаток — конструкция открыта.
		Assert.That(result.ConstructionsCount, Is.EqualTo(0), "Новые конструкции не созданы");
		Assert.That(result.BoundCount, Is.EqualTo(0), "«Входящие» пусты — привязки не созданы");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(
				db.Constructions.Single(construction => construction.Id == constructionId).Status,
				Is.EqualTo(ConstructionStatus.Open),
				"Ненулевой фьючерсный остаток возвращает статус «открыта»");
		}

		// Act: ручная пометка закрывает фьючерсную позицию между прогонами;
		// повторный инкремент пересчитывает остаток через read-модель.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.ManualCloseMarks.Add(new ManualCloseMark
			{
				ConstructionId = constructionId,
				Symbol = "ETHUSDT",
				Price = 3000m,
				MarkedAt = Now,
			});
			db.SaveChanges();
		}

		await service.AssembleInboxAsync();

		// Assert: обнулённый остаток закрывает конструкцию.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(
				db.Constructions.Single(construction => construction.Id == constructionId).Status,
				Is.EqualTo(ConstructionStatus.Closed),
				"Пометка, обнулившая фьючерсный остаток, закрывает конструкцию");
		}
	}

	[TestMethod]
	[Description("Ролл, разрезанный привязкой закрывающей ноги, открывает новую конструкцию")]
	// Закрывающая нога ролла привязана ранее и обнулила ногу владельца, поэтому
	// открывающая нога «Входящих» открывает новую конструкцию — принятое
	// расхождение инкремента с полным пересбором.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-split-roll-opens-new-construction
	public async Task TryIfSplitRollOpensNewConstruction()
	{
		// Arrange: первая сборка обрабатывает и открытие колла, и его закрытие.
		var service = CreateService();
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawInstruments.Add(OptionInstrument(EthCall1600, "Call"));
			db.RawExecutions.Add(Raw("r1", "option", EthCall1600, Ms(2026, 7, 1, 10, 0),
				ExecutionPayload("r1", EthCall1600, "Buy", "1500", "1", "0.01", "USDC", Ms(2026, 7, 1, 10, 0))));
			db.RawExecutions.Add(Raw("r2", "option", EthCall1600, Ms(2026, 7, 20, 10, 0),
				ExecutionPayload("r2", EthCall1600, "Sell", "2000", "1", "0.01", "USDC", Ms(2026, 7, 20, 10, 0))));
			db.SaveChanges();
		}

		var first = await service.AssembleInboxAsync();
		Assert.That(first.ConstructionsCount, Is.EqualTo(1), "Первая сборка создаёт конструкцию");
		Assert.That(first.BoundCount, Is.EqualTo(2), "Первая сборка привязывает обе ноги");
		Assert.That(first.TradesInInbox, Is.EqualTo(0), "Первая сборка опустошает «Входящие»");

		// Докупка колла дальней доски — открывающая нога ролла — приходит во «Входящих» позже.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawExecutions.Add(Raw("r3", "option", EthCall2100Dec, Ms(2026, 7, 20, 10, 5),
				ExecutionPayload("r3", EthCall2100Dec, "Buy", "300", "1", "0.01", "USDC", Ms(2026, 7, 20, 10, 5))));
			db.SaveChanges();
		}

		// Act: вторая сборка видит только открывающую ногу.
		var second = await service.AssembleInboxAsync();

		// Assert: открывающая нога открыла новую конструкцию, прежние привязки целы.
		Assert.That(second.ConstructionsCount, Is.EqualTo(1), "Открывающая нога ролла открывает новую конструкцию");
		Assert.That(second.BoundCount, Is.EqualTo(1), "Привязана только новая нога");
		Assert.That(second.TradesInInbox, Is.EqualTo(0), "«Входящие» опустошены");

		using (var db = new JournalDbContext(CreateOptions()))
		{
			var constructions = db.Constructions.OrderBy(construction => construction.Id).ToList();
			Assert.That(constructions, Has.Count.EqualTo(2), "Существующая конструкция сохранена, новая добавлена");
			Assert.That(constructions[0].Name, Is.EqualTo("ETH направленная CALL 25SEP26 1600"), "Имя закрытой конструкции сохранено");
			Assert.That(constructions[0].Status, Is.EqualTo(ConstructionStatus.Closed), "Конструкция закрыта обнулением ноги");
			Assert.That(constructions[1].Name, Is.EqualTo("ETH направленная CALL 25DEC26 2100"), "Новая конструкция названа по новой ноге");
			Assert.That(constructions[1].Status, Is.EqualTo(ConstructionStatus.Open), "Новая конструкция открыта");

			var constructionIdByExecId = db.TradeUserdata.ToDictionary(userdata => userdata.ExecId, userdata => userdata.ConstructionId);
			Assert.That(constructionIdByExecId["r1"], Is.EqualTo(constructions[0].Id), "Привязка открытия остаётся прежней");
			Assert.That(constructionIdByExecId["r2"], Is.EqualTo(constructions[0].Id), "Привязка закрытия остаётся прежней");
			Assert.That(constructionIdByExecId["r3"], Is.EqualTo(constructions[1].Id), "Открывающая нога привязана к новой конструкции");
		}
	}

	[TestMethod]
	[Description("Сборка из «Входящих» применяет полный delivery-таймлайн: экспирация между прогонами закрывает конструкцию")]
	// Delivery-события, включая выведенные из символов OTM-погашения, применяются
	// ко всему состоянию: наступившая между прогонами экспирация гасит ногу и
	// закрывает конструкцию без записей во «Входящих».
	// Traceability: change:refine-construction-assembly/design#d3
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-delivery-closes-construction
	public async Task TryIfInboxAssemblyAppliesDeliveryTimeline()
	{
		// Arrange: стреддл с живыми ногами; «сейчас» — до экспирации доски.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawInstruments.Add(OptionInstrument(EthCall1600, "Call"));
			db.RawInstruments.Add(OptionInstrument(EthPut1600, "Put"));
			db.RawExecutions.Add(Raw("d1", "option", EthCall1600, Ms(2026, 7, 10, 9, 0),
				ExecutionPayload("d1", EthCall1600, "Buy", "100", "1", "0.01", "USDC", Ms(2026, 7, 10, 9, 0))));
			db.RawExecutions.Add(Raw("d2", "option", EthPut1600, Ms(2026, 7, 10, 9, 10),
				ExecutionPayload("d2", EthPut1600, "Buy", "80", "1", "0.01", "USDC", Ms(2026, 7, 10, 9, 10))));
			db.SaveChanges();
		}

		var time = new MutableTimeProvider(Now);
		var service = CreateService(time);
		await service.AssembleInboxAsync();
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.Constructions.Single().Status, Is.EqualTo(ConstructionStatus.Open), "До экспирации конструкция открыта");
		}

		// Act: экспирация доски наступает между прогонами, новых записей нет.
		time.SetUtcNow(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
		var result = await service.AssembleInboxAsync();

		// Assert: ноги погашены, конструкция закрыта без новых привязок.
		Assert.That(result.ConstructionsCount, Is.EqualTo(0), "Новые конструкции не созданы");
		Assert.That(result.BoundCount, Is.EqualTo(0), "Новые привязки не созданы");
		Assert.That(result.TradesInInbox, Is.EqualTo(0), "«Входящие» пусты");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.Constructions.Single().Status, Is.EqualTo(ConstructionStatus.Closed), "Экспирация между прогонами закрыла конструкцию");
			Assert.That(db.TradeUserdata.Select(userdata => userdata.ExecId), Is.EquivalentTo(new[] { "d1", "d2" }), "Привязки сохранены");
		}
	}

	[TestMethod]
	[Description("Null-хранилище сырых записей отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawSnapshotStore()
	{
		// Arrange — Act — Assert
		new ConstructionAssemblyService(null!, new StubJournalBackupService(), CreateOptions());
	}

	[TestMethod]
	[Description("Null-сервис резервных копий отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullBackupService()
	{
		// Arrange — Act — Assert
		new ConstructionAssemblyService(new StubSnapshotStore(), null!, CreateOptions());
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullDbContextOptions()
	{
		// Arrange — Act — Assert
		new ConstructionAssemblyService(new StubSnapshotStore(), new StubJournalBackupService(), null!);
	}

	#region Помощники

	/// <summary>Команда пересбора над настоящим адаптером сырого хранилища, как в работе.</summary>
	private ConstructionAssemblyService CreateService(TimeProvider? timeProvider = null, StubJournalBackupService? backupService = null) => new(
		new JournalSyncStore(CreateOptions()),
		backupService ?? new StubJournalBackupService(),
		CreateOptions(),
		timeProvider ?? new FixedTimeProvider(Now));

	/// <summary>Создаёт опции контекста журнала над временной SQLite-базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Наполняет сырьё: справочник инструментов, стреддл, его закрытия и сделки робота.</summary>
	private void SeedRawStorage()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(OptionInstrument(EthCall1600, "Call"));
		db.RawInstruments.Add(OptionInstrument(EthPut1600, "Put"));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = "ETHUSDT",
			Category = "linear",
			PayloadJson = """{"symbol":"ETHUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});

		db.RawExecutions.Add(Raw("e1", "option", EthCall1600, Ms(2026, 7, 10, 9, 0),
			ExecutionPayload("e1", EthCall1600, "Buy", "100", "1", "0.01", "USDC", Ms(2026, 7, 10, 9, 0))));
		db.RawExecutions.Add(Raw("e2", "option", EthPut1600, Ms(2026, 7, 10, 9, 10),
			ExecutionPayload("e2", EthPut1600, "Buy", "80", "1", "0.01", "USDC", Ms(2026, 7, 10, 9, 10))));
		db.RawExecutions.Add(Raw("e3", "linear", "ETHUSDT", Ms(2026, 7, 12, 10, 0),
			ExecutionPayload("e3", "ETHUSDT", "Buy", "3000", "0.5", "-0.01", "USDT", Ms(2026, 7, 12, 10, 0))));
		db.RawExecutions.Add(Raw("e4", "option", EthCall1600, Ms(2026, 8, 3, 10, 0),
			ExecutionPayload("e4", EthCall1600, "Sell", "150", "1", "0.01", "USDC", Ms(2026, 8, 3, 10, 0))));
		db.RawExecutions.Add(Raw("e5", "option", EthPut1600, Ms(2026, 8, 4, 10, 0),
			ExecutionPayload("e5", EthPut1600, "Sell", "40", "1", "0.01", "USDC", Ms(2026, 8, 4, 10, 0))));
		db.RawExecutions.Add(Raw("e6", "linear", "ETHUSDT", Ms(2026, 8, 10, 10, 0),
			ExecutionPayload("e6", "ETHUSDT", "Sell", "3200", "0.5", "-0.01", "USDT", Ms(2026, 8, 10, 10, 0))));

		db.SaveChanges();
	}

	/// <summary>
	/// Наполняет сырьё затухающей конструкции: справочник инструментов, стреддл
	/// с продажами и покупка фьючерса — без обнуляющей фьючерсной сделки.
	/// </summary>
	private void SeedFadingStorage()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(OptionInstrument(EthCall1600, "Call"));
		db.RawInstruments.Add(OptionInstrument(EthPut1600, "Put"));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = "ETHUSDT",
			Category = "linear",
			PayloadJson = """{"symbol":"ETHUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});

		db.RawExecutions.Add(Raw("e1", "option", EthCall1600, Ms(2026, 7, 10, 9, 0),
			ExecutionPayload("e1", EthCall1600, "Buy", "100", "1", "0.01", "USDC", Ms(2026, 7, 10, 9, 0))));
		db.RawExecutions.Add(Raw("e2", "option", EthPut1600, Ms(2026, 7, 10, 9, 10),
			ExecutionPayload("e2", EthPut1600, "Buy", "80", "1", "0.01", "USDC", Ms(2026, 7, 10, 9, 10))));
		db.RawExecutions.Add(Raw("e3", "linear", "ETHUSDT", Ms(2026, 7, 12, 10, 0),
			ExecutionPayload("e3", "ETHUSDT", "Buy", "3000", "0.5", "-0.01", "USDT", Ms(2026, 7, 12, 10, 0))));
		db.RawExecutions.Add(Raw("e4", "option", EthCall1600, Ms(2026, 8, 3, 10, 0),
			ExecutionPayload("e4", EthCall1600, "Sell", "150", "1", "0.01", "USDC", Ms(2026, 8, 3, 10, 0))));
		db.RawExecutions.Add(Raw("e5", "option", EthPut1600, Ms(2026, 8, 4, 10, 0),
			ExecutionPayload("e5", EthPut1600, "Sell", "40", "1", "0.01", "USDC", Ms(2026, 8, 4, 10, 0))));

		db.SaveChanges();
	}

	/// <summary>Строит план эталонным алгоритмом над входом, соответствующим сырью проверки.</summary>
	private static AssemblyPlan ExpectedPlan()
	{
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Linear("e3", Ms(2026, 7, 12, 10, 0)),
			Option("e4", EthCall1600, Ms(2026, 8, 3, 10, 0), -1m),
			Option("e5", EthPut1600, Ms(2026, 8, 4, 10, 0), -1m),
			Linear("e6", Ms(2026, 8, 10, 10, 0)),
		};
		return new ConstructionAssembler().Assemble(executions, Array.Empty<AssemblyDelivery>());
	}

	private static AssemblyExecution Option(string execId, string symbol, long timeMs, decimal quantity) => new()
	{
		ExecId = execId,
		Category = "option",
		Symbol = symbol,
		ExecTimeMs = timeMs,
		SignedQuantity = quantity,
	};

	private static AssemblyExecution Linear(string execId, long timeMs) => new()
	{
		ExecId = execId,
		Category = "linear",
		Symbol = "ETHUSDT",
		ExecTimeMs = timeMs,
		SignedQuantity = 0.5m,
	};

	/// <summary>Снимок состояния доменных таблиц: конструкции и пользовательские данные сделок.</summary>
	private StateSnapshot CaptureState()
	{
		using var db = new JournalDbContext(CreateOptions());
		return new StateSnapshot(
			db.Constructions
				.OrderBy(construction => construction.Name)
				.Select(construction => new ConstructionRow(
					construction.Name,
					construction.Status,
					construction.AllocatedCapitalUsdt,
					construction.Comment))
				.ToList(),
			db.TradeUserdata
				.OrderBy(userdata => userdata.ExecId)
				.Select(userdata => new UserdataRow(
					userdata.ExecId,
					userdata.Construction!.Name,
					userdata.Comment))
				.ToList());
	}

	/// <summary>Снимок сырых и служебных таблиц для проверки неприкосновенности.</summary>
	private RawSnapshot CaptureRawState()
	{
		using var db = new JournalDbContext(CreateOptions());
		return new RawSnapshot(
			db.RawExecutions
				.OrderBy(execution => execution.ExecId)
				.Select(execution => new ExecutionRow(
					execution.Id,
					execution.ExecId,
					execution.Category,
					execution.Symbol,
					execution.ExecTimeMs,
					execution.PayloadJson,
					execution.FetchedAt))
				.ToList(),
			db.RawDeliveries
				.OrderBy(delivery => delivery.Symbol)
				.ThenBy(delivery => delivery.DeliveryTimeMs)
				.Select(delivery => new DeliveryRow(
					delivery.Id,
					delivery.Symbol,
					delivery.DeliveryTimeMs,
					delivery.Category,
					delivery.PayloadJson,
					delivery.FetchedAt))
				.ToList(),
			db.RawInstruments
				.OrderBy(instrument => instrument.Symbol)
				.Select(instrument => new InstrumentRow(
					instrument.Id,
					instrument.Symbol,
					instrument.Category,
					instrument.PayloadJson,
					instrument.FetchedAt))
				.ToList(),
			db.SyncRuns
				.OrderBy(run => run.Id)
				.Select(run => new SyncRunRow(
					run.Id,
					run.StartedAt,
					run.FinishedAt,
					run.Mode,
					run.Status,
					run.Error,
					run.NewExecutions,
					run.NewDeliveries,
					run.NewInstruments,
					run.WarningsJson))
				.ToList(),
			db.SyncStates
				.OrderBy(state => state.Category)
				.Select(state => new SyncStateRow(
					state.Id,
					state.Category,
					state.ExecWatermarkMs,
					state.DeliveryWatermarkMs,
					state.BackfillBoundaryMs,
					state.LastSuccessAt))
				.ToList());
	}

	private sealed record StateSnapshot(
		IReadOnlyList<ConstructionRow> Constructions,
		IReadOnlyList<UserdataRow> Userdata);

	private sealed record ConstructionRow(
		string Name,
		ConstructionStatus Status,
		decimal? AllocatedCapitalUsdt,
		string? Comment);

	private sealed record UserdataRow(
		string ExecId,
		string? ConstructionName,
		string? Comment);

	private sealed record RawSnapshot(
		IReadOnlyList<ExecutionRow> Executions,
		IReadOnlyList<DeliveryRow> Deliveries,
		IReadOnlyList<InstrumentRow> Instruments,
		IReadOnlyList<SyncRunRow> SyncRuns,
		IReadOnlyList<SyncStateRow> SyncStates);

	private sealed record ExecutionRow(
		long Id,
		string ExecId,
		string Category,
		string Symbol,
		long ExecTimeMs,
		string PayloadJson,
		DateTimeOffset FetchedAt);

	private sealed record DeliveryRow(
		long Id,
		string Symbol,
		long DeliveryTimeMs,
		string Category,
		string PayloadJson,
		DateTimeOffset FetchedAt);

	private sealed record InstrumentRow(
		long Id,
		string Symbol,
		string Category,
		string PayloadJson,
		DateTimeOffset FetchedAt);

	private sealed record SyncRunRow(
		long Id,
		DateTimeOffset StartedAt,
		DateTimeOffset? FinishedAt,
		SyncRunMode Mode,
		SyncRunStatus Status,
		string? Error,
		int NewExecutions,
		int NewDeliveries,
		int NewInstruments,
		string? WarningsJson);

	private sealed record SyncStateRow(
		long Id,
		string Category,
		long? ExecWatermarkMs,
		long? DeliveryWatermarkMs,
		long? BackfillBoundaryMs,
		DateTimeOffset? LastSuccessAt);

	/// <summary>Заглушка снимка сырья для проверки null-опций конструктора.</summary>
	private sealed class StubSnapshotStore : IJournalRawSnapshotStore
	{
		public Task<JournalRawSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(new JournalRawSnapshot
			{
				Instruments = [],
				Executions = [],
				Deliveries = [],
			});
	}

	/// <summary>
	/// Считающий источник снимка: пустой снимок и счётчик чтений — проверка
	/// блокировки следит, что пересбор без успешной копии сырьё не открывал.
	/// </summary>
	private sealed class CountingSnapshotStore : IJournalRawSnapshotStore
	{
		public int LoadCalls { get; private set; }

		public Task<JournalRawSnapshot> LoadAsync(CancellationToken cancellationToken = default)
		{
			LoadCalls++;
			return Task.FromResult(new JournalRawSnapshot
			{
				Instruments = [],
				Executions = [],
				Deliveries = [],
			});
		}
	}

	/// <summary>Спецификация опциона в справочнике с каноническим временем доставки 08:00 UTC.</summary>
	private static RawInstrument OptionInstrument(string symbol, string optionsType)
	{
		var deliveryMs = Ms(2026, 9, 25, 8, 0);
		return new RawInstrument
		{
			Symbol = symbol,
			Category = "option",
			PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""",
			FetchedAt = FetchedAt,
		};
	}

	private static RawExecution Raw(string execId, string category, string symbol, long execTimeMs, string payloadJson) => new()
	{
		ExecId = execId,
		Category = category,
		Symbol = symbol,
		ExecTimeMs = execTimeMs,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>Запись исполнения в форме ответа execution-list: числа биржа шлёт строками.</summary>
	private static string ExecutionPayload(
		string execId,
		string symbol,
		string side,
		string execPrice,
		string execQty,
		string execFee,
		string feeCurrency,
		long execTimeMs) =>
		$$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""";

	private static long Ms(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Поставщик фиксированного времени для детерминированных проверок.</summary>
	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}

	/// <summary>Поставщик управляемого времени: проверки двигают «сейчас» между прогонами.</summary>
	private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public void SetUtcNow(DateTimeOffset value) => utcNow = value;

		public override DateTimeOffset GetUtcNow() => utcNow;
	}

	#endregion
}
