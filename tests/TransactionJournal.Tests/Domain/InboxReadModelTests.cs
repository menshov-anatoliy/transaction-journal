using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain;

/// <summary>
/// Интеграционные проверки read-модели «Входящих» на стыке с сырым хранилищем
/// синхронизации: сделки выводятся из записей RawExecution материализатором
/// sync-слоя с атрибутами биржевой записи, привязанная сделка покидает список,
/// возврат из привязки возвращает её обратно.
/// </summary>
[TestClass]
public class InboxReadModelTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	// Каноническое время delivery инструмента опциона из справочника: 29DEC23 08:00 UTC.
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private InboxReadModel _readModel = null!;
	private TradeBindingService _bindingService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-inbox-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstrumentCatalog(db);
		}

		_readModel = new InboxReadModel(CreateOptions());
		_bindingService = new TradeBindingService(CreateOptions());
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
	[Description("«Входящие» выводят из RawExecution сделки с атрибутами материализатора sync")]
	public async Task TryIfInboxListsUnboundTradesWithMaterializerAttributes()
	{
		// Arrange: сырые записи исполнения — покупка опциона с комиссией USDC
		// и продажа линейного перпа с rebate — легли в хранилище синхронизацией.
		var optionExecMs = ExecMs(2023, 12, 28, 10, 0);
		var linearExecMs = ExecMs(2023, 12, 28, 10, 30);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawExecutions.Add(Raw("exec-opt-buy", "option", "BTC-29DEC23-45000-C", optionExecMs,
				ExecutionPayload("exec-opt-buy", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC",
					optionExecMs, isMaker: true)));
			db.RawExecutions.Add(Raw("exec-linear-sell", "linear", "BTCUSDT", linearExecMs,
				ExecutionPayload("exec-linear-sell", "BTCUSDT", "Sell", "42000", "0.01", "-0.0001", "BTC", linearExecMs)));
			await db.SaveChangesAsync();
		}

		// Act: читаем «Входящие».
		var inbox = await _readModel.ListAsync();

		// Assert: обе сделки без привязки на месте с атрибутами биржевой записи:
		// время, инструмент, знаковое количество по стороне, цена, комиссия со знаком
		// и её валюта в паритете USDC ≡ USDT; канонические атрибуты опциона —
		// из справочника инструментов.
		// Требование: сделки без привязки попадают во «Входящие» с атрибутами
		// материализатора синхронизации.
		// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
		// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
		Assert.That(inbox.Count, Is.EqualTo(2));

		var option = inbox.Single(trade => trade.ExecId == "exec-opt-buy");
		Assert.That(option.Category, Is.EqualTo("option"));
		Assert.That(option.Symbol, Is.EqualTo("BTC-29DEC23-45000-C"));
		Assert.That(option.ExecutedAt, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(optionExecMs)));
		Assert.That(option.Quantity, Is.EqualTo(0.0001m));
		Assert.That(option.Price, Is.EqualTo(45000m));
		Assert.That(option.Fee, Is.EqualTo(0.01m));
		Assert.That(option.FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(option.IsMaker, Is.True);
		Assert.That(option.Option, Is.Not.Null);
		Assert.That(option.Option!.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(option.Option.Strike, Is.EqualTo(45000m));
		Assert.That(option.Option.DeliveryTime, Is.EqualTo(OptionDelivery));

		var linear = inbox.Single(trade => trade.ExecId == "exec-linear-sell");
		Assert.That(linear.Category, Is.EqualTo("linear"));
		Assert.That(linear.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(linear.Quantity, Is.EqualTo(-0.01m));
		Assert.That(linear.Fee, Is.EqualTo(-0.0001m));
		Assert.That(linear.FeeCurrency, Is.EqualTo("BTC"));
		Assert.That(linear.Option, Is.Null);

		// Assert: порядок «Входящих» хронологический — сделка раньше по времени
		// стоит первой независимо от порядка вставки.
		Assert.That(inbox.Select(trade => trade.ExecId).ToArray(),
			Is.EqualTo(new[] { "exec-opt-buy", "exec-linear-sell" }));
	}

	[TestMethod]
	[Description("Привязанная сделка исчезает из «Входящих», возврат возвращает её обратно")]
	public async Task TryIfBoundTradeLeavesInboxAndReturnsAfterUnbind()
	{
		// Arrange: во «Входящих» две сделки; первую привязываем к конструкции.
		var constructionService = new ConstructionService(CreateOptions());
		var construction = await constructionService.CreateAsync("Стреддл BTC", 1000m);
		var firstMs = ExecMs(2023, 12, 28, 10, 0);
		var secondMs = ExecMs(2023, 12, 28, 11, 0);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawExecutions.Add(RawOption("exec-first", "Buy", firstMs));
			db.RawExecutions.Add(RawOption("exec-second", "Sell", secondMs));
			await db.SaveChangesAsync();
		}
		await _bindingService.BindAsync(construction.Id, "exec-first");

		// Act: читаем «Входящие» после привязки.
		var inboxAfterBind = await _readModel.ListAsync();

		// Assert: привязанная сделка исчезла из «Входящих», осталась только вторая.
		// Требование: привязка выводит сделку из «Входящих».
		// Traceability: openspec:domain/constructions#scenario-binding-removes-from-inbox
		Assert.That(inboxAfterBind.Select(trade => trade.ExecId).ToArray(),
			Is.EqualTo(new[] { "exec-second" }));

		// Act: возвращаем сделку во «Входящие» и читаем снова.
		await _bindingService.UnbindAsync("exec-first");
		var inboxAfterUnbind = await _readModel.ListAsync();

		// Assert: сделка вернулась во «Входящие» вместе со своей строкой данных.
		// Требование: возврат во «Входящие» делает сделку снова доступной для привязки.
		// Traceability: openspec:domain/constructions#scenario-return-to-inbox
		Assert.That(inboxAfterUnbind.Select(trade => trade.ExecId).ToArray(),
			Is.EqualTo(new[] { "exec-first", "exec-second" }));
	}

	[TestMethod]
	[Description("Пустое хранилище даёт пустые «Входящие»")]
	public async Task TryIfEmptyStorageGivesEmptyInbox()
	{
		// Act: читаем «Входящие» пустого журнала.
		var inbox = await _readModel.ListAsync();

		// Assert: записей нет — список пуст.
		Assert.That(inbox, Is.Empty);
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new InboxReadModel(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Справочник инструментов: опцион BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.</summary>
	private static void SeedInstrumentCatalog(JournalDbContext db)
	{
		var deliveryMs = OptionDelivery.ToUnixTimeMilliseconds();
		var optionPayload =
			$$"""{"symbol":"BTC-29DEC23-45000-C","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = "BTC-29DEC23-45000-C",
			Category = "option",
			PayloadJson = optionPayload,
			FetchedAt = FetchedAt,
		});
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = "BTCUSDT",
			Category = "linear",
			PayloadJson = linearPayload,
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	private static RawExecution RawOption(string execId, string side, long execTimeMs) => Raw(
		execId, "option", "BTC-29DEC23-45000-C", execTimeMs,
		ExecutionPayload(execId, "BTC-29DEC23-45000-C", side, "45000", "0.0001", "0.01", "USDC", execTimeMs));

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
		string? feeCurrency,
		long execTimeMs,
		bool isMaker = false)
	{
		var feeCurrencyJson = feeCurrency is null ? "null" : $"\"{feeCurrency}\"";
		var isMakerJson = isMaker ? "true" : "false";
		return $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":{{feeCurrencyJson}},"isMaker":{{isMakerJson}}}""";
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
