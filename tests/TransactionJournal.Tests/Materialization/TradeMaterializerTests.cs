using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Materialization;

/// <summary>
/// Проверки материализатора сделок «Входящих»: вывод сделок из сырых записей
/// исполнения с атрибутами биржевой записи (знак количества по стороне, execPrice,
/// execFee со знаком и feeCurrency, USDC ≡ USDT по ADR-0001) и идемпотентность
/// проекции при повторных синках.
/// </summary>
[TestClass]
public class TradeMaterializerTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	// Каноническое время delivery инструмента из справочника: 29DEC23 08:00 UTC.
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Инкрементальная догрузка приводит во «Входящие» только новые сделки")]
	public void TryIfIncrementalSyncBringsOnlyNewTradesToInbox()
	{
		// Arrange: первый синк принёс две сделки опциона — покупку и продажу.
		// Требование: после синка во «Входящих» появляются новые сделки биржи
		// и только они, с атрибутами из биржевой записи исполнения.
		// Traceability: openspec:sync/bybit-history#scenario-new-trades-land-in-inbox
		var materializer = CreateMaterializer();
		var firstSyncExecutions = new[]
		{
			RawOption("exec-1", "Buy", ExecMs(2023, 12, 28, 10, 0)),
			RawOption("exec-2", "Sell", ExecMs(2023, 12, 28, 11, 0)),
		};
		var inboxAfterFirstSync = materializer.Materialize(firstSyncExecutions);
		Assert.That(inboxAfterFirstSync.Count, Is.EqualTo(2));

		// Act: инкрементальная догрузка принесла одну новую сделку — материализатор
		// работает по всему сырью хранилища (прежние записи плюс новая).
		var secondSyncExecutions = firstSyncExecutions
			.Append(RawOption("exec-3", "Buy", ExecMs(2023, 12, 28, 12, 0)))
			.ToArray();
		var inboxAfterSecondSync = materializer.Materialize(secondSyncExecutions);

		// Assert: во «Входящих» появились только новые сделки; прежние не изменились
		// и не задвоились.
		Assert.That(inboxAfterSecondSync.Count, Is.EqualTo(3));
		Assert.That(
			inboxAfterSecondSync.Select(trade => trade.ExecId).ToArray(),
			Is.EqualTo(new[] { "exec-1", "exec-2", "exec-3" }));
		Assert.That(inboxAfterSecondSync.Take(2).ToArray(), Is.EqualTo(inboxAfterFirstSync.ToArray()));
	}

	[TestMethod]
	[Description("Атрибуты сделки соответствуют биржевой записи исполнения; USDC-комиссия приведена к USDT")]
	public void TryIfTradeAttributesMatchExchangeRecord()
	{
		// Arrange: покупка опциона BTC-29DEC23-45000-C по 45000 количеством 0.0001
		// с комиссией 0.01 USDC — журнал учитывает USDC в паритете 1:1 к USDT.
		// Требование: атрибуты сделки «Входящих» соответствуют биржевой записи.
		// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
		// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
		var materializer = CreateMaterializer();
		var executedAt = new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero);
		var rawExecutions = new[]
		{
			Raw("exec-buy-1", "option", "BTC-29DEC23-45000-C", executedAt.ToUnixTimeMilliseconds(),
				ExecutionPayload("exec-buy-1", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC",
					executedAt.ToUnixTimeMilliseconds(), isMaker: true)),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions);

		// Assert: каждый атрибут повторяет биржевую запись; канонические атрибуты
		// опциона взяты из справочника инструментов.
		Assert.That(trades.Count, Is.EqualTo(1));
		var trade = trades[0];
		Assert.That(trade.ExecId, Is.EqualTo("exec-buy-1"));
		Assert.That(trade.Category, Is.EqualTo("option"));
		Assert.That(trade.Symbol, Is.EqualTo("BTC-29DEC23-45000-C"));
		Assert.That(trade.ExecutedAt, Is.EqualTo(executedAt));
		Assert.That(trade.Quantity, Is.EqualTo(0.0001m));
		Assert.That(trade.Price, Is.EqualTo(45000m));
		Assert.That(trade.Fee, Is.EqualTo(0.01m));
		Assert.That(trade.FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(trade.IsMaker, Is.True);
		Assert.That(trade.Option, Is.Not.Null);
		Assert.That(trade.Option!.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(trade.Option.OptionsType, Is.EqualTo(OptionType.Call));
		Assert.That(trade.Option.Strike, Is.EqualTo(45000m));
		Assert.That(trade.Option.DeliveryTime, Is.EqualTo(OptionDelivery));
	}

	[TestMethod]
	[Description("Продажа даёт отрицательное количество, знак комиссии rebate сохраняется")]
	public void TryIfSellSideGivesNegativeQuantityAndKeepsRebateSign()
	{
		// Arrange: продажа опциона с отрицательной комиссией (rebate) в BTC —
		// знак количества берётся по стороне Sell, знак комиссии остаётся знаком биржи,
		// а валюта вне паритета USDC/USDT не перекладывается.
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-sell-1", "Sell", ExecMs(2023, 12, 28, 11, 0), execFee: "-0.00000001", feeCurrency: "BTC"),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions);

		// Assert
		Assert.That(trades.Count, Is.EqualTo(1));
		Assert.That(trades[0].Quantity, Is.EqualTo(-0.0001m));
		Assert.That(trades[0].Fee, Is.EqualTo(-0.00000001m));
		Assert.That(trades[0].FeeCurrency, Is.EqualTo("BTC"));
	}

	[TestMethod]
	[Description("Повторная материализация того же сырья не меняет «Входящие»")]
	public void TryIfRepeatMaterializationKeepsInboxUnchanged()
	{
		// Arrange: повторный синк без новых записей отдаёт то же сырье хранилища.
		// Требование: повторная загрузка известных записей не создаёт дубликатов
		// доменных сущностей и не меняет прежние «Входящие».
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-1", "Buy", ExecMs(2023, 12, 28, 10, 0)),
			Raw("exec-2", "linear", "BTCUSDT", ExecMs(2023, 12, 28, 10, 30),
				ExecutionPayload("exec-2", "BTCUSDT", "Sell", "42000", "0.01", "0.0042", "USDT", ExecMs(2023, 12, 28, 10, 30))),
		};

		// Act: вторая материализация получает те же записи, включая идентичный дубликат.
		var firstPass = materializer.Materialize(rawExecutions);
		var secondPass = materializer.Materialize(rawExecutions.Append(rawExecutions[0]));

		// Assert: количество и состав сделок не изменились.
		Assert.That(secondPass.Count, Is.EqualTo(2));
		Assert.That(secondPass.ToArray(), Is.EqualTo(firstPass.ToArray()));
	}

	[TestMethod]
	[Description("Линейная сделка не получает атрибуты опциона")]
	public void TryIfLinearTradeHasNoOptionAttributes()
	{
		// Arrange: запись категории linear не сверяется со справочником как опцион.
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			Raw("exec-linear-1", "linear", "BTCUSDT", ExecMs(2023, 12, 28, 10, 30),
				ExecutionPayload("exec-linear-1", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT",
					ExecMs(2023, 12, 28, 10, 30))),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions);

		// Assert
		Assert.That(trades.Count, Is.EqualTo(1));
		Assert.That(trades[0].Category, Is.EqualTo("linear"));
		Assert.That(trades[0].Quantity, Is.EqualTo(0.01m));
		Assert.That(trades[0].Option, Is.Null);
	}

	[TestMethod]
	[Description("Сделки упорядочены по времени исполнения, при равенстве — по execId")]
	public void TryIfTradesOrderedByExecutionTimeThenExecId()
	{
		// Arrange: записи поданы в обратном порядке, две — с одинаковым временем.
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-3", "Buy", ExecMs(2023, 12, 28, 12, 0)),
			RawOption("exec-2b", "Sell", ExecMs(2023, 12, 28, 11, 0)),
			RawOption("exec-2a", "Buy", ExecMs(2023, 12, 28, 11, 0)),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions);

		// Assert: порядок результата не зависит от порядка входных записей.
		Assert.That(
			trades.Select(trade => trade.ExecId).ToArray(),
			Is.EqualTo(new[] { "exec-2a", "exec-2b", "exec-3" }));
	}

	[TestMethod]
	[Description("Null-коллекция сырых записей отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawExecutions()
	{
		// Arrange
		var materializer = CreateMaterializer();

		// Act — Assert
		materializer.Materialize(null!);
	}

	[TestMethod]
	[Description("Null-сверщик инструментов отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullInstrumentResolver()
	{
		// Arrange — Act — Assert
		new TradeMaterializer(null!);
	}

	[TestMethod]
	[Description("Некорректный JSON полезной нагрузки останавливает материализацию")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnMalformedPayloadJson()
	{
		// Arrange: сырое хранилище гарантий на форму JSON не даёт — повреждённая запись
		// должна останавливать материализацию с внятной ошибкой, а не пропускаться молча.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { Raw("exec-broken", "option", "BTC-29DEC23-45000-C", 0, "{ не json") };

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[DataRow("Long")]
	[DataRow("")]
	[Description("Неизвестная сторона исполнения отклоняется")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnUnknownSide(string side)
	{
		// Arrange: сторона исполнения вне пары Buy/Sell не даёт знака количества.
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-side", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-side", "BTC-29DEC23-45000-C", side, "45000", "0.0001", "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[DataRow("execPrice")]
	[DataRow("execQty")]
	[Description("Отсутствующая цена или количество исполнения отклоняется")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnMissingRequiredNumber(string missingField)
	{
		// Arrange: биржа кодирует отсутствующее число пустой строкой; сделка без
		// цены или количества не может попасть во «Входящие».
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var execPrice = missingField == "execPrice" ? "" : "45000";
		var execQty = missingField == "execQty" ? "" : "0.0001";
		var rawExecutions = new[]
		{
			Raw("exec-empty", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-empty", "BTC-29DEC23-45000-C", "Buy", execPrice, execQty, "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[Description("Расхождение execId строки хранилища и полезной нагрузки останавливает материализацию")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnExecIdMismatchBetweenRowAndPayload()
	{
		// Arrange: execId — ключ дедупликации; расхождение строки и payload
		// означает повреждение сырья.
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-row", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-payload", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[Description("Конфликтующие записи с одним execId останавливают материализацию")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnConflictingDuplicateExecId()
	{
		// Arrange: два разных payload с одним execId — источник записей с одним
		// идентификатором обязан быть единственным.
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-conflict", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-conflict", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC", execTimeMs)),
			Raw("exec-conflict", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-conflict", "BTC-29DEC23-45000-C", "Sell", "45000", "0.0001", "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[Description("Символ опциона вне справочника останавливает материализацию ошибкой сверки")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnUnknownOptionSymbol()
	{
		// Arrange: спека требует пополнять справочник до материализации сделок —
		// неизвестный символ должен останавливать проекцию явной ошибкой сверки.
		// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-eth", "option", "ETH-29DEC23-2000-C", execTimeMs,
				ExecutionPayload("exec-eth", "ETH-29DEC23-2000-C", "Buy", "200", "1", "0.02", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	#region Помощники

	private static TradeMaterializer CreateMaterializer() => new(new InstrumentResolver(CreateCatalog()));

	/// <summary>Справочник инструментов: опцион BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.</summary>
	private static InstrumentCatalog CreateCatalog()
	{
		var deliveryMs = OptionDelivery.ToUnixTimeMilliseconds();
		var optionPayload =
			$$"""{"symbol":"BTC-29DEC23-45000-C","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		return new InstrumentCatalog(new[]
		{
			new RawInstrument
			{
				Symbol = "BTC-29DEC23-45000-C",
				Category = "option",
				PayloadJson = optionPayload,
				FetchedAt = FetchedAt,
			},
			new RawInstrument
			{
				Symbol = "BTCUSDT",
				Category = "linear",
				PayloadJson = linearPayload,
				FetchedAt = FetchedAt,
			},
		});
	}

	private static RawExecution RawOption(
		string execId,
		string side,
		long execTimeMs,
		string execFee = "0.01",
		string feeCurrency = "USDC") => Raw(
		execId, "option", "BTC-29DEC23-45000-C", execTimeMs,
		ExecutionPayload(execId, "BTC-29DEC23-45000-C", side, "45000", "0.0001", execFee, feeCurrency, execTimeMs));

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
