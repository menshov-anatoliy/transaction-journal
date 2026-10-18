using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Infrastructure.Chats;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки жизненного цикла ручных синтетических связок в сборке конструкций:
/// инкрементная сборка «Входящих» сохраняет ручную связку — строка остаётся
/// прежней и продолжает действовать в логическом слое наравне с распознанными,
/// полный пересбор стирает связку вместе с содержимым конструкций, симметрично
/// оспариваниям, — сырьё ручных сделок переживает пересбор, распознанная пара
/// возвращается распознаванию.
/// Требование: ручная связка переживает инкрементную сборку и стирается
/// полным пересбором.
/// Traceability: openspec:domain/synthetic-close#scenario-manual-link-wiped-by-rebuild
/// </summary>
[TestClass]
public class SyntheticCloseManualLinkStorageTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";
	private const string XautPerp = "XAUTUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Момент «сейчас» сборки: стреддл жив, экспирация доски впереди.</summary>
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 13, 30, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private string _chatsDatabasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-link-storage-tests-{Guid.NewGuid():N}.db");
		_chatsDatabasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-link-chats-tests-{Guid.NewGuid():N}.db");
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
	[Description("Инкрементная сборка сохраняет ручную связку конструкции в хранилище и логическом слое")]
	// Ручная связка — ручные данные логического слоя конструкции: сборка из
	// «Входящих» привязывает новые сделки, но конструкции не пересоздаёт и
	// ручные записи не трогает — строка связки остаётся прежней и продолжает
	// действовать в логическом слое наравне с распознанной.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-wiped-by-rebuild
	public async Task TryIfIncrementalAssemblyPreservesManualLink()
	{
		// Arrange: пересбор разворачивает конструкцию стреддла с биржевой парой,
		// ручная пара «перп + встречный колл» соединена ручной связкой.
		SeedSyntheticFlow();
		await CreateService().RebuildAsync();
		var constructionId = GetConstructionId();
		var manualPerp = await AddManualTradeAsync(constructionId, XautPerp, "Buy", 0.2m, 4100m, 0.1m, Ms(2026, 10, 15, 16, 0));
		var manualCall = await AddManualTradeAsync(constructionId, XautCall4400, "Sell", 0.2m, 5m, 0.1m, Ms(2026, 10, 15, 16, 4));
		var created = await CreateManualLinkAsync(constructionId, $"manual:{manualPerp.Id}", $"manual:{manualCall.Id}", 0.2m);

		// Act: покупка 0.1 колла приходит во «Входящих» и собирается инкрементно.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawExecutions.Add(Raw("inbox-call", "option", XautCall4400, Ms(2026, 10, 16, 12, 0),
				ExecutionPayload("inbox-call", XautCall4400, "Buy", "6.0", "0.1", "0.01", "USDC", Ms(2026, 10, 16, 12, 0))));
			db.SaveChanges();
		}

		var result = await CreateService().AssembleInboxAsync();

		// Assert: инкремент привязал новую сделку, связка осталась прежней строкой —
		// тот же ключ, та же конструкция, те же сделки и то же количество.
		Assert.That(result.BoundCount, Is.EqualTo(1), "Сделка «Входящих» привязана к конструкции");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var link = db.SyntheticCloseManualLinks.Single();
			Assert.That(link.Id, Is.EqualTo(created.Id), "Инкремент не пересоздал строку ручной связки");
			Assert.That(link.ConstructionId, Is.EqualTo(constructionId), "Связка осталась в исходной конструкции");
			Assert.That(link.LinearTradeId, Is.EqualTo($"manual:{manualPerp.Id}"), "Линейная сделка связки не изменена");
			Assert.That(link.OptionTradeId, Is.EqualTo($"manual:{manualCall.Id}"), "Встречный опцион связки не изменён");
			Assert.That(link.Quantity, Is.EqualTo(0.2m), "Количество связки не изменено");
		}

		// Логический слой читается как прежде: действующие связки — распознанная
		// биржевая пара и ручная связка с её количеством.
		var data = await CreateDetailReader().ReadAsync(constructionId);
		Assert.That(data.SyntheticCloses, Has.Count.EqualTo(2), "Инкремент сохранил действующие связки логического слоя");
		var manual = data.SyntheticCloses!.Single(row => row.LinearExecId == $"manual:{manualPerp.Id}");
		Assert.That(manual.Quantity, Is.EqualTo(0.2m), "Ручная связка продолжает действовать в логическом слое");
	}

	[TestMethod]
	[Description("Полный пересбор стирает ручную связку конструкции, сохраняя её сырьё")]
	// Связка принадлежит конструкции и умирает вместе с ней, симметрично
	// оспариваниям: пересбор вычищает ручные данные логического слоя — в
	// логическом слое новой конструкции остаётся только распознанная связка,
	// а сырьё ручных сделок переживает пересбор как материал повторной сборки.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-wiped-by-rebuild
	public async Task TryIfFullRebuildWipesManualLink()
	{
		// Arrange: конструкция стреддла с биржевой парой, ручная пара соединена
		// ручной связкой — до пересбора действуют обе связки.
		SeedSyntheticFlow();
		await CreateService().RebuildAsync();
		var constructionId = GetConstructionId();
		var manualPerp = await AddManualTradeAsync(constructionId, XautPerp, "Buy", 0.2m, 4100m, 0.1m, Ms(2026, 10, 15, 16, 0));
		var manualCall = await AddManualTradeAsync(constructionId, XautCall4400, "Sell", 0.2m, 5m, 0.1m, Ms(2026, 10, 15, 16, 4));
		await CreateManualLinkAsync(constructionId, $"manual:{manualPerp.Id}", $"manual:{manualCall.Id}", 0.2m);
		var before = await CreateDetailReader().ReadAsync(constructionId);
		Assert.That(before.SyntheticCloses, Has.Count.EqualTo(2), "До пересбора действует распознанная и ручная связки");

		// Act: полный пересбор с резервной копией.
		await CreateService().RebuildAsync();

		// Assert: строка ручной связки стёрта вместе с конструкцией, сырьё ручных
		// сделок пережило пересбор, конструкция пересоздана с новым ключом.
		long newConstructionId;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.SyntheticCloseManualLinks, Is.Empty, "Пересбор стёр ручные связки");
			Assert.That(db.Constructions.Any(candidate => candidate.Id == constructionId), Is.False, "Конструкция пересоздана");
			Assert.That(await db.ManualTrades.CountAsync(), Is.EqualTo(2), "Сырьё ручных сделок переживает пересбор");
			newConstructionId = db.Constructions.Single().Id;
		}

		// Логический слой новой конструкции: распознанная пара вернулась
		// распознаванию, ручной связки больше нет — она умерла вместе с конструкцией.
		var after = await CreateDetailReader().ReadAsync(newConstructionId);
		Assert.That(after.SyntheticCloses, Has.Count.EqualTo(1), "После пересбора действует только распознанная связка");
		Assert.That(after.SyntheticCloses!.Single().LinearExecId, Is.EqualTo("hedge-perp"), "Оставшаяся связка — распознанная пара");
	}

	#region Помощники

	/// <summary>Команда пересбора над настоящим адаптером сырого хранилища и единым хранилищем чатов, как в работе.</summary>
	private ConstructionAssemblyService CreateService() => new(
		new JournalSyncStore(CreateOptions()),
		new StubJournalBackupService(),
		CreateOptions(),
		new ChatStore(_chatsDatabasePath),
		new FixedTimeProvider(Now));

	/// <summary>Читающая модель деталей конструкции с заглушкой марок: логический слой читается как на экране.</summary>
	private ConstructionDetailReadModel CreateDetailReader() => new(
		CreateOptions(),
		new JournalMetricsReadModel(CreateOptions(), new StubFreshMarkSource(4300m, FetchedAt)),
		new PositionReadModel(CreateOptions()));

	/// <summary>Создаёт опции контекста журнала над временной SQLite-базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private long GetConstructionId()
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.Constructions.Single().Id;
	}

	/// <summary>Создаёт ручную связку сервисом ручных связок — тем же путём, что и пользователь.</summary>
	private async Task<SyntheticCloseManualLink> CreateManualLinkAsync(
		long constructionId,
		string linearTradeId,
		string optionTradeId,
		decimal quantity) =>
		await new SyntheticCloseManualLinkService(CreateOptions())
			.CreateAsync(constructionId, linearTradeId, optionTradeId, quantity);

	/// <summary>Сеет ручную сделку в сырье конструкции и возвращает сохранённую строку.</summary>
	private async Task<ManualTrade> AddManualTradeAsync(
		long constructionId,
		string symbol,
		string side,
		decimal quantity,
		decimal price,
		decimal feeUsdt,
		long executedAtMs)
	{
		using var db = new JournalDbContext(CreateOptions());
		var trade = new ManualTrade
		{
			ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(executedAtMs),
			Symbol = symbol,
			Side = side,
			Quantity = quantity,
			Price = price,
			FeeUsdt = feeUsdt,
			ConstructionId = constructionId,
			CreatedAt = FetchedAt,
			UpdatedAt = FetchedAt,
		};
		db.ManualTrades.Add(trade);
		await db.SaveChangesAsync();
		return trade;
	}

	/// <summary>Наполняет сырьё: справочник XAUT-инструментов, стреддл 0.5/0.5 и биржевая связка 15.10.</summary>
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

	/// <summary>Спецификация опциона в справочнике с каноническим временем доставки 08:00 UTC.</summary>
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

	/// <summary>Поставщик фиксированного времени для детерминированных проверок.</summary>
	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}

	/// <summary>
	/// Заглушка источника свежих марок: фиксированная марка со временем получения.
	/// </summary>
	private sealed class StubFreshMarkSource(decimal markPrice, DateTimeOffset receivedAt) : IFreshInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentMarkSnapshot?>(new InstrumentMarkSnapshot(symbol, markPrice, receivedAt));
	}

	#endregion
}
