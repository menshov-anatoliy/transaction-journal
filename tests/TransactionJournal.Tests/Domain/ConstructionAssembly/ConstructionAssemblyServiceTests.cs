using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
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

			// Сделка робота вне периодов осталась во «Входящих» — строки привязки нет.
			Assert.That(bindings.ContainsKey("e6"), Is.False, "Непривязанная сделка остаётся во «Входящих»");
		}
	}

	[TestMethod]
	[Description("Повторная сборка над тем же сырьём воспроизводит состав конструкций и привязки")]
	// Состав конструкций, их атрибуты и привязки сделок совпадают с предыдущим
	// прогоном: алгоритм детерминирован, применение плана идемпотентно.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-reproduces-result
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
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-wipes-manual-data
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
			Assert.That(constructions.Select(construction => construction.Name), Is.EqualTo(new[] { "ETH стреддл 25SEP26 1600" }), "Осталась только собранная конструкция");
			var construction = constructions.Single();
			Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Open), "Собранная конструкция открывается со статусом «открыта»");
			Assert.That(construction.AllocatedCapitalUsdt, Is.Null, "Выделенный капитал ручной конструкции стёрт");
			Assert.That(construction.RiskValue, Is.Null, "Риск ручной конструкции стёрт");
			Assert.That(construction.ProfitValue, Is.Null, "Профит ручной конструкции стёрт");
			Assert.That(construction.Comment, Is.Null, "Комментарий конструкции стёрт");

			Assert.That(db.PnLAdjustments, Is.Empty, "Внешние корректировки PnL стёрты");
			Assert.That(db.PositionComments, Is.Empty, "Комментарии позиций стёрты");
			Assert.That(db.ManualCloseMarks, Is.Empty, "Ручные пометки закрытия стёрты");

			var userdata = db.TradeUserdata.ToList();
			Assert.That(userdata.Select(row => row.ExecId), Is.EquivalentTo(new[] { "e1", "e2", "e3", "e4", "e5" }), "Остались только привязки плана");
			Assert.That(userdata.All(row => row.Comment is null), Is.True, "Комментарии сделок стёрты");
			Assert.That(userdata.All(row => row.ConstructionId == construction.Id), Is.True, "Все привязки указывают на собранную конструкцию");
		}
	}

	[TestMethod]
	[Description("Пересбор не изменяет сырьё, справочник инструментов и состояние синхронизации")]
	// Сырые записи, справочник инструментов и состояние синхронизации после
	// пересборки не изменены.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-preserves-raw-storage
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
	[Description("Null-хранилище сырых записей отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawSnapshotStore()
	{
		// Arrange — Act — Assert
		new ConstructionAssemblyService(null!, CreateOptions());
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullDbContextOptions()
	{
		// Arrange — Act — Assert
		new ConstructionAssemblyService(new StubSnapshotStore(), null!);
	}

	#region Помощники

	/// <summary>Команда пересбора над настоящим адаптером сырого хранилища, как в работе.</summary>
	private ConstructionAssemblyService CreateService() => new(
		new JournalSyncStore(CreateOptions()),
		CreateOptions(),
		new FixedTimeProvider(Now));

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

	#endregion
}
