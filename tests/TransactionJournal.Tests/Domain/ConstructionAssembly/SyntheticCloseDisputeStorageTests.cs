using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.Chats;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using TransactionJournal.Chats.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки жизненного цикла записей оспаривания в сборке конструкций: спор
/// переживает инкрементную сборку «Входящих», полный пересбор стирает спор
/// вместе с содержимым конструкций, после чего связка возвращается
/// автоматическому распознаванию.
/// </summary>
[TestClass]
public class SyntheticCloseDisputeStorageTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";
	private const string XautPerp = "XAUTUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 13, 30, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private string _chatsDatabasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-dispute-storage-tests-{Guid.NewGuid():N}.db");
		_chatsDatabasePath = Path.Combine(Path.GetTempPath(), $"journal-dispute-chats-tests-{Guid.NewGuid():N}.db");
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база и соседние WAL/SHM-файлы удаляются после каждой проверки;
		// вместе с базой журнала убирается и файл единой базы чатов.
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			foreach (var candidate in new[] { _databasePath, _chatsDatabasePath })
			{
				var file = candidate + suffix;
				if (File.Exists(file))
				{
					File.Delete(file);
				}
			}
		}
	}

	[TestMethod]
	[Description("Инкрементная сборка сохраняет запись оспаривания конструкции")]
	// Спор — ручные данные конструкции: сборка из «Входящих» привязывает новые
	// сделки, но не пересоздает конструкции и не трогает ручные записи.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-survives-incremental-assembly
	public async Task TryIfDisputeSurvivesIncrementalAssembly()
	{
		// Arrange: пересбор разворачивает конструкцию стреддла, связка оспаривается.
		SeedSyntheticFlow();
		await CreateService().RebuildAsync();
		var disputeService = new SyntheticCloseDisputeService(CreateOptions());
		var constructionId = GetConstructionId();
		await disputeService.DisputeAsync(constructionId, "hedge-perp", "hedge-call");
		SyntheticCloseDispute disputedRecord;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			disputedRecord = db.SyntheticCloseDisputes.Single();
		}

		var disputedAt = disputedRecord.DisputedAt;

		// Act: покупка 0.1 колла приходит во «Входящих» и собирается инкрементно.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawExecutions.Add(Raw("inbox-call", "option", XautCall4400, Ms(2026, 10, 16, 12, 0),
				ExecutionPayload("inbox-call", XautCall4400, "Buy", "6.0", "0.1", "0.01", "USDC", Ms(2026, 10, 16, 12, 0))));
			db.SaveChanges();
		}

		var result = await CreateService().AssembleInboxAsync();

		// Assert: инкремент привязал новую сделку, но спор остался прежним —
		// тот же ключ и тот же момент оспаривания.
		Assert.That(result.BoundCount, Is.EqualTo(1), "Сделка «Входящих» привязана к конструкции");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var dispute = db.SyntheticCloseDisputes.Single();
			Assert.That(dispute.ConstructionId, Is.EqualTo(constructionId), "Спор остался в исходной конструкции");
			Assert.That(dispute.LinearExecId, Is.EqualTo("hedge-perp"));
			Assert.That(dispute.CounterOptionExecId, Is.EqualTo("hedge-call"));
			Assert.That(dispute.DisputedAt, Is.EqualTo(disputedAt), "Момент оспаривания не изменён");
		}
	}

	[TestMethod]
	[Description("Пересбор стирает спор, и связка возвращается автоматическому распознаванию")]
	// Спор — данные конструкции: пересбор вычищает записи оспаривания вместе с
	// конструкциями, поэтому после пересбора фильтр над пустым списком споров
	// снова пропускает связку в логический слой — без ручного возврата.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-rebuild-wipes-dispute
	public async Task TryIfRebuildWipesDisputeAndLinkReturnsAutomatically()
	{
		// Arrange: пересбор разворачивает конструкцию, связка распознаётся и оспаривается —
		// оспоренный логический слой её не видит.
		SeedSyntheticFlow();
		await CreateService().RebuildAsync();
		var constructionId = GetConstructionId();
		var disputeService = new SyntheticCloseDisputeService(CreateOptions());
		await disputeService.DisputeAsync(constructionId, "hedge-perp", "hedge-call");

		// Assert: оспоренная связка скрыта — логический слой на спорной связке пуст.
		List<SyntheticCloseDispute> disputes;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			disputes = db.SyntheticCloseDisputes.ToList();
		}

		Assert.That(SyntheticCloseDisputeFilter.ExcludeDisputed(RecognizedLinks(), disputes),
			Is.Empty, "Спорная связка исключена из логического слоя");

		// Act: полный пересбор с резервной копией.
		await CreateService().RebuildAsync();

		// Assert: споров больше нет, конструкции пересозданы, распознавание снова
		// видит связку — пустой список споров пропускает её в логический слой.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.SyntheticCloseDisputes, Is.Empty, "Пересбор стёр записи оспаривания");
			Assert.That(db.Constructions.Any(construction => construction.Id == constructionId), Is.False, "Конструкция пересоздана");
		}

		Assert.That(SyntheticCloseDisputeFilter.ExcludeDisputed(RecognizedLinks(), []),
			Has.Count.EqualTo(1), "Связка вернулась в логический слой автоматически");
	}

	#region Помощники

	private ConstructionAssemblyService CreateService() => new(
		new JournalSyncStore(CreateOptions()),
		new StubJournalBackupService(),
		CreateOptions(),
		new ChatStore(_chatsDatabasePath),
		new FixedTimeProvider(Now));

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private long GetConstructionId()
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.Constructions.Single().Id;
	}

	/// <summary>Связки распознанного прогона над составом записей конструкции.</summary>
	private static IReadOnlyList<SyntheticCloseLink> RecognizedLinks()
	{
		var executions = new List<AssemblyExecution>
		{
			Option("open-call", XautCall4400, Ms(2026, 9, 30, 12, 0), 0.5m),
			Option("open-put", XautPut4400, Ms(2026, 9, 30, 12, 5), 0.5m),
			Linear("hedge-perp", XautPerp, Ms(2026, 10, 15, 13, 0), 0.3m),
			Option("hedge-call", XautCall4400, Ms(2026, 10, 15, 13, 4), -0.3m),
		};
		return SyntheticCloseRecognizer.Recognize(executions);
	}

	/// <summary>Наполняет сырьё: справочник XAUT-инструментов, стреддл 0.5/0.5 и связка 15.10.</summary>
	private void SeedSyntheticFlow()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(OptionInstrument(XautCall4400, "Call"));
		db.RawInstruments.Add(OptionInstrument(XautPut4400, "Put"));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = XautPerp,
			Category = "linear",
			PayloadJson = """{"symbol":"XAUTUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"XAUT","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});

		db.RawExecutions.Add(Raw("open-call", "option", XautCall4400, Ms(2026, 9, 30, 12, 0),
			ExecutionPayload("open-call", XautCall4400, "Buy", "9.5", "0.5", "0.01", "USDC", Ms(2026, 9, 30, 12, 0))));
		db.RawExecutions.Add(Raw("open-put", "option", XautPut4400, Ms(2026, 9, 30, 12, 5),
			ExecutionPayload("open-put", XautPut4400, "Buy", "5.2", "0.5", "0.01", "USDC", Ms(2026, 9, 30, 12, 5))));
		db.RawExecutions.Add(Raw("hedge-perp", "linear", XautPerp, Ms(2026, 10, 15, 13, 0),
			ExecutionPayload("hedge-perp", XautPerp, "Buy", "4141.91", "0.3", "-0.01", "USDT", Ms(2026, 10, 15, 13, 0))));
		db.RawExecutions.Add(Raw("hedge-call", "option", XautCall4400, Ms(2026, 10, 15, 13, 4),
			ExecutionPayload("hedge-call", XautCall4400, "Sell", "5.9", "0.3", "0.01", "USDC", Ms(2026, 10, 15, 13, 4))));

		db.SaveChanges();
	}

	/// <summary>Спецификация опциона в справочнике с каноническим временем доставки.</summary>
	private static RawInstrument OptionInstrument(string symbol, string optionsType)
	{
		var deliveryMs = Ms(2026, 10, 30, 8, 0);
		return new RawInstrument
		{
			Symbol = symbol,
			Category = "option",
			PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"XAUT","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""",
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

	/// <summary>Опционное исполнение с знаковым количеством.</summary>
	private static AssemblyExecution Option(string execId, string symbol, long timeMs, decimal quantity) => new()
	{
		ExecId = execId,
		Category = "option",
		Symbol = symbol,
		ExecTimeMs = timeMs,
		SignedQuantity = quantity,
	};

	/// <summary>Линейное исполнение — сделка по вечному фьючерсу.</summary>
	private static AssemblyExecution Linear(string execId, string symbol, long timeMs, decimal quantity) => new()
	{
		ExecId = execId,
		Category = "linear",
		Symbol = symbol,
		ExecTimeMs = timeMs,
		SignedQuantity = quantity,
	};

	#endregion
}
