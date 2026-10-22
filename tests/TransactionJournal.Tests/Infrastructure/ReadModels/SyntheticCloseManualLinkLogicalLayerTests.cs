using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.ReadModels;

/// <summary>
/// Проверки включения ручных синтетических связок в логический слой read-модели
/// деталей конструкции: действующие связки = распознанные − оспоренные + ручные.
/// Ручная связка показывает величину лока при отсутствии распознанных связок
/// и двигает распад остатка линейной ноги; инструментальные метрики — остатки,
/// реализованный результат, нереализованный PnL, итог — ручной связкой не
/// меняются, формула лока сохраняется.
/// </summary>
[TestClass]
public class SyntheticCloseManualLinkLogicalLayerTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";
	private const string XautPerp = "XAUTUSDT";

	/// <summary>Момент получения котировок стаба — фиксированный для детерминизма.</summary>
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private ConstructionService _constructions = null!;
	private TradeBindingService _binding = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке
		// со справочником доски 30OCT26 08:00 UTC и перпа XAUTUSDT.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-link-layer-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstruments(db);
		}

		_constructions = new ConstructionService(CreateOptions());
		_binding = new TradeBindingService(CreateOptions());
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
	[Description("Ручная связка показывает величину лока при отсутствии распознанных связок")]
	// Стреддл открыт биржевыми сделками, а связующая пара (перп и встречный
	// колл) внесена вручную: распознаватель по биржевым сделкам пары не находит,
	// ручная связка действует в логическом слое и показывает лок той же формулой.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-created
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-locked-value
	public async Task TryIfManualLinkAloneShowsLockedValueAndSplit()
	{
		// Arrange: стрэддл с биржевой пут-ногой и ручной парой «перп + колл».
		var construction = await _constructions.CreateAsync("XAUT ручная связка", 1000m);
		await AddExchangeTradeAsync("open-put", XautPut4400, "option", "Buy", "0.5", "139", "1.5", ExecMs(2026, 9, 30, 12, 0), "USDC");
		await AddExchangeTradeAsync("open-call", XautCall4400, "option", "Buy", "0.5", "12", "0.6", ExecMs(2026, 9, 30, 12, 5), "USDC");
		await AddExchangeTradeAsync("scalp-perp", XautPerp, "linear", "Buy", "0.04", "4200", "0.09", ExecMs(2026, 10, 15, 15, 0), "USDT");
		await _binding.BindBatchAsync(construction.Id, ["open-put", "open-call", "scalp-perp"]);
		var manualPerp = await AddManualTradeAsync(construction.Id, XautPerp, "Buy", 0.3m, 4141.91m, 0.55m, ExecMs(2026, 10, 15, 13, 0));
		var manualCall = await AddManualTradeAsync(construction.Id, XautCall4400, "Sell", 0.3m, 5.9m, 0.35m, ExecMs(2026, 10, 15, 13, 4));
		await AddManualLinkAsync(construction.Id, $"manual:{manualPerp.Id}", $"manual:{manualCall.Id}", 0.3m);
		var readModel = CreateReadModel();

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: единственная связка — ручная: лок той же формулой кейса,
		// признак спора ложный, строковые ключи сделок — ключи ручного сырья.
		Assert.That(data.SyntheticCloses, Has.Count.EqualTo(1));
		var link = data.SyntheticCloses!.Single();
		Assert.That(link.ClosedLegSymbol, Is.EqualTo(XautPut4400));
		Assert.That(link.Quantity, Is.EqualTo(0.3m));
		Assert.That(link.LockedValueUsdt, Is.EqualTo(35.697m));
		Assert.That(link.Disputed, Is.False);
		Assert.That(link.LinearExecId, Is.EqualTo($"manual:{manualPerp.Id}"));
		Assert.That(link.CounterOptionExecId, Is.EqualTo($"manual:{manualCall.Id}"));

		// Ручная связка двигает распад остатка линейной ноги: 0.34 распадается
		// на 0.30 синтетики и 0.04 прочего остатка.
		var perp = data.Positions.Single(row => row.Symbol == XautPerp);
		Assert.That(perp.Residual, Is.EqualTo(0.34m));
		Assert.That(perp.SyntheticResidual, Is.EqualTo(0.3m));
		Assert.That(perp.OtherResidual, Is.EqualTo(0.04m));
	}

	[TestMethod]
	[Description("Ручная связка не меняет инструментальные метрики позиции")]
	// Логический слой — только показ: остаток, средние цены, реализованный
	// результат, комиссии и нереализованный PnL совпадают с чтением до создания
	// ручной связки, меняется лишь распад остатка.
	// Traceability: openspec:domain/synthetic-close#scenario-instrumental-metrics-untouched
	public async Task TryIfManualLinkKeepsInstrumentalMetricsUnchanged()
	{
		// Arrange: конструкция с биржевой пут-ногой и ручной парой без связки.
		var construction = await _constructions.CreateAsync("XAUT инвариант", 1000m);
		await AddExchangeTradeAsync("open-put", XautPut4400, "option", "Buy", "0.5", "139", "1.5", ExecMs(2026, 9, 30, 12, 0), "USDC");
		await AddExchangeTradeAsync("open-call", XautCall4400, "option", "Buy", "0.5", "12", "0.6", ExecMs(2026, 9, 30, 12, 5), "USDC");
		await _binding.BindBatchAsync(construction.Id, ["open-put", "open-call"]);
		var manualPerp = await AddManualTradeAsync(construction.Id, XautPerp, "Buy", 0.3m, 4141.91m, 0.55m, ExecMs(2026, 10, 15, 13, 0));
		var manualCall = await AddManualTradeAsync(construction.Id, XautCall4400, "Sell", 0.3m, 5.9m, 0.35m, ExecMs(2026, 10, 15, 13, 4));
		var readModel = CreateReadModel();

		// Act: читаем до и после создания ручной связки.
		var before = await readModel.ReadAsync(construction.Id);
		await AddManualLinkAsync(construction.Id, $"manual:{manualPerp.Id}", $"manual:{manualCall.Id}", 0.3m);
		var after = await readModel.ReadAsync(construction.Id);

		// Assert: связка появилась в логическом слое.
		Assert.That(before.SyntheticCloses, Is.Empty);
		Assert.That(after.SyntheticCloses, Has.Count.EqualTo(1));

		// Инструментальные величины строк позиций совпадают запись за записью:
		// сверяются строки без презентационного распада остатка.
		var beforeBySymbol = before.Positions
			.Select(row => row with { SyntheticResidual = null, OtherResidual = null })
			.ToDictionary(row => row.Symbol, StringComparer.Ordinal);
		foreach (var afterRow in after.Positions)
		{
			var beforeRow = beforeBySymbol[afterRow.Symbol];
			Assert.That(afterRow with { SyntheticResidual = null, OtherResidual = null }, Is.EqualTo(beforeRow),
				$"инструментальная строка позиции {afterRow.Symbol}");
		}

		// Итог конструкции не изменился: ручная связка не вносит записей в потоки.
		Assert.That(after.Metrics.RealizedPnL, Is.EqualTo(before.Metrics.RealizedPnL));
		Assert.That(after.Metrics.UnrealizedPnL, Is.EqualTo(before.Metrics.UnrealizedPnL));
		Assert.That(after.Metrics.TotalPnL, Is.EqualTo(before.Metrics.TotalPnL));
		Assert.That(after.Metrics.ClosedAt, Is.EqualTo(before.Metrics.ClosedAt));
	}

	[TestMethod]
	[Description("Ручная связка суммируется с распознанной в логическом слое")]
	// Действующие связки = распознанные − оспоренные + ручные: две строки
	// показывают свои локи, распад остатка линейной ноги суммируется.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	public async Task TryIfManualLinkSumsWithRecognizedLink()
	{
		// Arrange: эталонный кейс XAUT с распознанной связкой 0.3 и ручной парой
		// «перп 0.2 @4100 + колл 0.2 @5» со связкой 0.2.
		var construction = await _constructions.CreateAsync("XAUT две связки", 1000m);
		await AddExchangeTradeAsync("open-put", XautPut4400, "option", "Buy", "0.5", "139", "1.5", ExecMs(2026, 9, 30, 12, 0), "USDC");
		await AddExchangeTradeAsync("open-call", XautCall4400, "option", "Buy", "0.5", "12", "0.6", ExecMs(2026, 9, 30, 12, 5), "USDC");
		await AddExchangeTradeAsync("case-perp", XautPerp, "linear", "Buy", "0.3", "4141.91", "0.55", ExecMs(2026, 10, 15, 13, 0), "USDT");
		await AddExchangeTradeAsync("case-call", XautCall4400, "option", "Sell", "0.3", "5.9", "0.35", ExecMs(2026, 10, 15, 13, 4), "USDT");
		await AddExchangeTradeAsync("scalp-perp", XautPerp, "linear", "Buy", "0.04", "4200", "0.09", ExecMs(2026, 10, 15, 15, 0), "USDT");
		await _binding.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp"]);
		var manualPerp = await AddManualTradeAsync(construction.Id, XautPerp, "Buy", 0.2m, 4100m, 0.1m, ExecMs(2026, 10, 15, 16, 0));
		var manualCall = await AddManualTradeAsync(construction.Id, XautCall4400, "Sell", 0.2m, 5m, 0.1m, ExecMs(2026, 10, 15, 16, 4));
		var manualLink = await AddManualLinkAsync(construction.Id, $"manual:{manualPerp.Id}", $"manual:{manualCall.Id}", 0.2m);
		var readModel = CreateReadModel();

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: две строки — распознанная 0.3 с локом 35.697 и ручная 0.2
		// с локом 32.4 по той же формуле: 0.2×(4400−4100) + 0.2×5 − 0.2×139
		// − (0.1 + 0.1 + 0.2×3) = 32.4.
		Assert.That(data.SyntheticCloses, Has.Count.EqualTo(2));
		var recognized = data.SyntheticCloses!.Single(row => row.LinearExecId == "case-perp");
		Assert.That(recognized.Quantity, Is.EqualTo(0.3m));
		Assert.That(recognized.LockedValueUsdt, Is.EqualTo(35.697m));
		var manual = data.SyntheticCloses!.Single(row => row.LinearExecId == $"manual:{manualPerp.Id}");
		Assert.That(manual.Quantity, Is.EqualTo(0.2m));
		Assert.That(manual.LockedValueUsdt, Is.EqualTo(32.4m));
		Assert.That(manual.Disputed, Is.False);

		// Идентификатор связки в строке снимка: ручная строка несёт ключ
		// удаления из хранилища ручных связок, распознанная остаётся null —
		// удаление адресуется строкой карточки без клиентских реестров.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		Assert.That(manual.ManualLinkId, Is.EqualTo(manualLink.Id));
		Assert.That(recognized.ManualLinkId, Is.Null);

		// Распад остатка перпа 0.54 суммирует обе действующие связки:
		// 0.5 синтетики и 0.04 прочего остатка.
		var perp = data.Positions.Single(row => row.Symbol == XautPerp);
		Assert.That(perp.Residual, Is.EqualTo(0.54m));
		Assert.That(perp.SyntheticResidual, Is.EqualTo(0.5m));
		Assert.That(perp.OtherResidual, Is.EqualTo(0.04m));
	}

	#region Помощники

	private ConstructionDetailReadModel CreateReadModel() => new(
		CreateOptions(),
		new JournalMetricsReadModel(CreateOptions(), new StubFreshQuoteSource(4300m, FetchedAt)),
		new PositionReadModel(CreateOptions()));

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Добавляет сырую запись исполнения; числа биржа шлёт строками.</summary>
	private async Task AddExchangeTradeAsync(
		string execId,
		string symbol,
		string category,
		string side,
		string execQty,
		string execPrice,
		string execFee,
		long execTimeMs,
		string feeCurrency)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = category,
			Symbol = symbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Вносит ручную сделку прямо в сырьё конструкции.</summary>
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

	/// <summary>Пишет ручную связку напрямую в хранилище ручных данных логического слоя.</summary>
	private async Task<SyntheticCloseManualLink> AddManualLinkAsync(long constructionId, string linearTradeId, string optionTradeId, decimal quantity)
	{
		using var db = new JournalDbContext(CreateOptions());
		var link = new SyntheticCloseManualLink
		{
			ConstructionId = constructionId,
			LinearTradeId = linearTradeId,
			OptionTradeId = optionTradeId,
			Quantity = quantity,
		};
		db.SyntheticCloseManualLinks.Add(link);
		await db.SaveChangesAsync();
		return link;
	}

	/// <summary>Справочник проверки: пут и колл доски 30OCT26 08:00 UTC и перп XAUTUSDT.</summary>
	private static void SeedInstruments(JournalDbContext db)
	{
		db.RawInstruments.Add(OptionInstrument(XautPut4400, "Put", ExecMs(2026, 10, 30, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(XautCall4400, "Call", ExecMs(2026, 10, 30, 8, 0)));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = XautPerp,
			Category = "linear",
			PayloadJson = """{"symbol":"XAUTUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"XAUT","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Спецификация опциона в справочнике с каноническим временем доставки 08:00 UTC.</summary>
	private static RawInstrument OptionInstrument(string symbol, string optionsType, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		Category = "option",
		PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"{{symbol.Split('-')[0]}}","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryTimeMs}}","deliveryFeeRate":"0.00015"}""",
		FetchedAt = FetchedAt,
	};

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>
	/// Заглушка источника свежих котировок: фиксированная котировка со временем получения.
	/// </summary>
	private sealed class StubFreshQuoteSource(decimal quotePrice, DateTimeOffset receivedAt) : IFreshInstrumentQuoteSource
	{
		public Task<InstrumentQuoteSnapshot?> GetFreshQuoteAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentQuoteSnapshot?>(new InstrumentQuoteSnapshot(symbol, quotePrice, receivedAt));
	}

	#endregion
}
