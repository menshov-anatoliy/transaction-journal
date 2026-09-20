using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Materialization;

/// <summary>
/// Проверки материализатора закрывающих записей экспираций: ITM-деление аккаунтовой
/// delivery-записи пропорционально остаткам конструкций, автоматическое OTM-закрытие
/// по нулевой цене в deliveryTime при отсутствии биржевой записи и предупреждающая
/// сверка собственного расчёта с биржевым deliveryRpl без подмены расчёта.
/// </summary>
[TestClass]
public class ExpiryMaterializerTests
{
	private const string CallSymbol = "BTC-29DEC23-45000-C";
	private const string PutSymbol = "BTC-29DEC23-40000-P";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Каноническое время delivery справочника: 29DEC23 08:00 UTC.</summary>
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private static readonly long OptionDeliveryMs = OptionDelivery.ToUnixTimeMilliseconds();

	[TestMethod]
	[Description("ITM delivery делится между двумя конструкциями пропорционально остаткам")]
	public void TryIfItmDeliverySplitsAcrossConstructionsProportionally()
	{
		// Arrange: остатки колла в двух конструкциях — 0.0003 и 0.0001; аккаунтовая
		// delivery-запись на весь остаток 0.0004 с комиссией 0.0004 и расчётной ценой 46000
		// при страйке 45000 даёт внутреннюю стоимость 1000.
		// Требование: в каждой конструкции появляется закрывающая запись с долей аккаунтовой
		// записи, пропорциональной остатку, по эффективной цене внутренней стоимости.
		// Traceability: openspec:sync/bybit-history#scenario-itm-delivery-split-across-constructions
		// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-a1", "Buy", "0.0003", "100"),
			RawOption("exec-b1", "Buy", "0.0001", "100"),
		};
		var assignments = new Dictionary<string, string?>
		{
			["exec-a1"] = "con-a",
			["exec-b1"] = "con-b",
		};
		var rawDeliveries = new[]
		{
			Delivery(CallSymbol, OptionDeliveryMs, deliveryPrice: "46000", strike: "45000", fee: "0.0004", deliveryRpl: "0.36"),
		};

		// Act
		var result = materializer.Materialize(rawExecutions, rawDeliveries, assignments, AfterDelivery());

		// Assert: две записи — по доле каждой конструкции; количество обнуляет остаток,
		// цена равна внутренней стоимости, комиссия поделена теми же долями 3:1.
		Assert.That(result.ClosingEntries.Count, Is.EqualTo(2));
		var entryA = result.ClosingEntries[0];
		var entryB = result.ClosingEntries[1];
		Assert.That(entryA.ConstructionId, Is.EqualTo("con-a"));
		Assert.That(entryA.Kind, Is.EqualTo(ExpiryClosingKind.Delivery));
		Assert.That(entryA.Quantity, Is.EqualTo(-0.0003m));
		Assert.That(entryA.EffectivePrice, Is.EqualTo(1000m));
		Assert.That(entryA.Fee, Is.EqualTo(0.0003m));
		Assert.That(entryB.ConstructionId, Is.EqualTo("con-b"));
		Assert.That(entryB.Quantity, Is.EqualTo(-0.0001m));
		Assert.That(entryB.EffectivePrice, Is.EqualTo(1000m));
		Assert.That(entryB.Fee, Is.EqualTo(0.0001m));
		Assert.That(entryA.ClosedAt, Is.EqualTo(OptionDelivery));
		Assert.That(entryA.SourceKey, Is.EqualTo($"{CallSymbol}|{OptionDeliveryMs}"));
		Assert.That(entryB.SourceKey, Is.EqualTo($"{CallSymbol}|{OptionDeliveryMs}"));

		// deliveryRpl 0.36 совпал с собственным расчётом (-0.04 денежного потока сделок
		// плюс 0.4 внутренней стоимости) — предупреждений нет.
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("OTM-экспирация без биржевой записи закрывается автоматически по нулевой цене")]
	public void TryIfOtmExpiryAutoClosesAtZeroPriceInDeliveryTime()
	{
		// Arrange: пут без delivery-записи; остаток 0.0001 в конструкции и 0.0001
		// в непривязанных сделках «Входящих»; deliveryTime уже наступил.
		// Требование: система создаёт закрывающую запись по нулевой цене, обнуляющую
		// каждую позицию, в deliveryTime инструмента.
		// Traceability: openspec:sync/bybit-history#scenario-otm-expiry-auto-close
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-a1", "Buy", "0.0001", "20", PutSymbol),
			RawOption("exec-inbox-1", "Buy", "0.0001", "20", PutSymbol),
		};
		var assignments = new Dictionary<string, string?> { ["exec-a1"] = "con-a" };

		// Act: delivery-записей нет, момент материализации — после deliveryTime.
		var result = materializer.Materialize(rawExecutions, Array.Empty<RawDelivery>(), assignments, AfterDelivery());

		// Assert: по одной записи на каждый остаток — конструкцию и «Входящие»;
		// цена нулевая, количество обнуляет остаток, момент — deliveryTime справочника.
		Assert.That(result.ClosingEntries.Count, Is.EqualTo(2));
		Assert.That(result.Warnings, Is.Empty);
		var inboxEntry = result.ClosingEntries.Single(entry => entry.ConstructionId is null);
		Assert.That(inboxEntry.Symbol, Is.EqualTo(PutSymbol));
		Assert.That(inboxEntry.Kind, Is.EqualTo(ExpiryClosingKind.OtmExpiry));
		Assert.That(inboxEntry.Quantity, Is.EqualTo(-0.0001m));
		Assert.That(inboxEntry.EffectivePrice, Is.EqualTo(0m));
		Assert.That(inboxEntry.Fee, Is.EqualTo(0m));
		Assert.That(inboxEntry.ClosedAt, Is.EqualTo(OptionDelivery));
		Assert.That(inboxEntry.SourceKey, Is.EqualTo($"{PutSymbol}|{OptionDeliveryMs}"));
		var constructionEntry = result.ClosingEntries.Single(entry => entry.ConstructionId == "con-a");
		Assert.That(constructionEntry.Quantity, Is.EqualTo(-0.0001m));
		Assert.That(constructionEntry.EffectivePrice, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("OTM-закрытие не выводится до наступления deliveryTime инструмента")]
	public void TryIfOtmExpiryNotEmittedBeforeDeliveryTime()
	{
		// Arrange: тот же ненулевой остаток, но момент материализации — за минуту
		// до deliveryTime: экспирация ещё не наступила, позиция остаётся открытой.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "20", PutSymbol) };
		var beforeDelivery = new DateTimeOffset(2023, 12, 29, 7, 59, 0, TimeSpan.Zero);

		// Act
		var result = materializer.Materialize(rawExecutions, Array.Empty<RawDelivery>(), null, beforeDelivery);

		// Assert
		Assert.That(result.ClosingEntries, Is.Empty);
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Расхождение deliveryRpl с собственным расчётом даёт предупреждение, не подменяя расчёт")]
	public void TryIfDeliveryRplMismatchWarnsWithoutOverridingOwnResult()
	{
		// Arrange: покупка 0.0001 по 100 и delivery с внутренней стоимостью 1000 —
		// собственный результат 0.09, а биржа отчиталась 0.05.
		// Требование: система показывает предупреждение о расхождении, не блокирует
		// синхронизацию и не переписывает собственный расчёт.
		// Traceability: openspec:sync/bybit-history#scenario-delivery-reconciliation-warning
		// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "100") };
		var assignments = new Dictionary<string, string?> { ["exec-a1"] = "con-a" };
		var rawDeliveries = new[]
		{
			Delivery(CallSymbol, OptionDeliveryMs, deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.05"),
		};

		// Act: материализация завершается обычным результатом — предупреждение
		// не превращается в ошибку.
		var result = materializer.Materialize(rawExecutions, rawDeliveries, assignments, AfterDelivery());

		// Assert: предупреждение содержит обе величины и расхождение; закрывающая
		// запись сохраняет собственный расчёт — цену внутренней стоимости.
		Assert.That(result.Warnings.Count, Is.EqualTo(1));
		var warning = result.Warnings[0];
		Assert.That(warning.Symbol, Is.EqualTo(CallSymbol));
		Assert.That(warning.DeliveryTime, Is.EqualTo(OptionDelivery));
		Assert.That(warning.DeliveryRpl, Is.EqualTo(0.05m));
		Assert.That(warning.OwnResult, Is.EqualTo(0.09m));
		Assert.That(warning.Difference, Is.EqualTo(-0.04m));
		Assert.That(warning.SourceKey, Is.EqualTo($"{CallSymbol}|{OptionDeliveryMs}"));
		Assert.That(result.ClosingEntries.Count, Is.EqualTo(1));
		Assert.That(result.ClosingEntries[0].EffectivePrice, Is.EqualTo(1000m));
		Assert.That(result.ClosingEntries[0].Quantity, Is.EqualTo(-0.0001m));
	}

	[TestMethod]
	[Description("Перепривязка сделки пересчитывает пропорции деления delivery автоматически")]
	public void TryIfRebindingTradesRecomputesDeliverySplit()
	{
		// Arrange: остатки 0.0003 и 0.0001 в двух конструкциях; после переноса сделки
		// во вторую конструкцию остатки становятся 0.0002 и 0.0002, и аккаунтовая
		// delivery обязана поделиться пополам — записи производны и пересчитываются
		// при чтении из текущих привязок.
		// Traceability: change:add-bybit-sync/design#d3
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-a1", "Buy", "0.0002", "100"),
			RawOption("exec-b1", "Buy", "0.0001", "100"),
			RawOption("exec-move-1", "Buy", "0.0001", "100"),
		};
		var rawDeliveries = new[]
		{
			Delivery(CallSymbol, OptionDeliveryMs, deliveryPrice: "46000", strike: "45000", fee: "0.0004", deliveryRpl: "0.32"),
		};

		// Act: сначала сделка в первой конструкции, затем перенесена во вторую.
		var beforeRebind = materializer.Materialize(
			rawExecutions,
			rawDeliveries,
			new Dictionary<string, string?> { ["exec-a1"] = "con-a", ["exec-b1"] = "con-b", ["exec-move-1"] = "con-a" },
			AfterDelivery());
		var afterRebind = materializer.Materialize(
			rawExecutions,
			rawDeliveries,
			new Dictionary<string, string?> { ["exec-a1"] = "con-a", ["exec-b1"] = "con-b", ["exec-move-1"] = "con-b" },
			AfterDelivery());

		// Assert: деление аккаунтовой записи следует за остатками — из 3:1 стало 1:1.
		Assert.That(beforeRebind.ClosingEntries.Single(entry => entry.ConstructionId == "con-a").Fee, Is.EqualTo(0.0003m));
		Assert.That(afterRebind.ClosingEntries.Single(entry => entry.ConstructionId == "con-a").Quantity, Is.EqualTo(-0.0002m));
		Assert.That(afterRebind.ClosingEntries.Single(entry => entry.ConstructionId == "con-a").Fee, Is.EqualTo(0.0002m));
		Assert.That(afterRebind.ClosingEntries.Single(entry => entry.ConstructionId == "con-b").Fee, Is.EqualTo(0.0002m));
	}

	[TestMethod]
	[Description("Линейные delivery-записи и линейные инструменты не порождают закрывающих записей")]
	public void TryIfLinearDeliveriesAndLinearTradesProduceNoClosingEntries()
	{
		// Arrange: правила ADR-0002 определены для опционов — delivery датированного
		// фьючерса остаётся в сырье, а OTM-детектор не трогает линейные инструменты
		// без атрибутов опциона.
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			Raw("exec-linear-1", "linear", "BTCUSDT", ExecMs(2023, 12, 28, 10, 30),
				ExecutionPayload("exec-linear-1", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT", ExecMs(2023, 12, 28, 10, 30))),
		};
		var rawDeliveries = new[]
		{
			Delivery("BTCUSDT-26MAR26", OptionDeliveryMs, deliveryPrice: "46000", strike: "0", fee: "0", deliveryRpl: "1", category: "linear"),
		};

		// Act
		var result = materializer.Materialize(rawExecutions, rawDeliveries, null, AfterDelivery());

		// Assert
		Assert.That(result.ClosingEntries, Is.Empty);
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Идентичная повторная delivery-строка не меняет результат материализации")]
	public void TryIfDuplicateIdenticalDeliveryRowKeepsSingleClosingSet()
	{
		// Arrange: хранилище может отдать одну и ту же запись дважды — проекция
		// обязана остаться идемпотентной по ключу symbol + deliveryTime.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "100") };
		var delivery = Delivery(CallSymbol, OptionDeliveryMs, deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.09");
		var assignments = new Dictionary<string, string?> { ["exec-a1"] = "con-a" };

		// Act
		var result = materializer.Materialize(rawExecutions, new[] { delivery, delivery }, assignments, AfterDelivery());

		// Assert: одна запись, предупреждений нет — дубликат пропущен.
		Assert.That(result.ClosingEntries.Count, Is.EqualTo(1));
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Null-коллекции сырых записей отклоняются")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawDeliveries()
	{
		// Arrange
		var materializer = CreateMaterializer();

		// Act — Assert
		materializer.Materialize(Array.Empty<RawExecution>(), null!, null, AfterDelivery());
	}

	[TestMethod]
	[Description("Null-материализатор сделок отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullTradeMaterializer()
	{
		// Arrange — Act — Assert
		new ExpiryMaterializer(null!, new InstrumentResolver(CreateCatalog()));
	}

	[TestMethod]
	[Description("Null-сверщик инструментов отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullInstrumentResolver()
	{
		// Arrange — Act — Assert
		new ExpiryMaterializer(new TradeMaterializer(new InstrumentResolver(CreateCatalog())), null!);
	}

	[TestMethod]
	[Description("Отрицательный допуск сверки отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnNegativeReconciliationTolerance()
	{
		// Arrange — Act — Assert
		new ExpiryMaterializer(
			new TradeMaterializer(new InstrumentResolver(CreateCatalog())),
			new InstrumentResolver(CreateCatalog()),
			reconciliationTolerance: -0.01m);
	}

	[TestMethod]
	[Description("Некорректный JSON delivery-записи останавливает материализацию")]
	[ExpectedException(typeof(ExpiryMaterializationException))]
	public void ThrowOnMalformedDeliveryPayload()
	{
		// Arrange: повреждённое сырьё должно останавливать разбор с внятной ошибкой,
		// а не пропускаться молча.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "100") };
		var broken = RowWithPayload(CallSymbol, OptionDeliveryMs, "option", "{ не json");

		// Act — Assert
		materializer.Materialize(rawExecutions, new[] { broken }, null, AfterDelivery());
	}

	[TestMethod]
	[Description("Расхождение ключей строки хранилища и полезной нагрузки останавливает материализацию")]
	[ExpectedException(typeof(ExpiryMaterializationException))]
	public void ThrowOnDeliveryKeyMismatchBetweenRowAndPayload()
	{
		// Arrange: ключ symbol|deliveryTime — основа идемпотентности проекции;
		// расхождение строки и payload означает повреждение сырья.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "100") };
		var mismatched = RowWithPayload(
			CallSymbol, OptionDeliveryMs, "option",
			DeliveryPayload(PutSymbol, OptionDeliveryMs, "46000", "45000", "0", "0.09"));

		// Act — Assert
		materializer.Materialize(rawExecutions, new[] { mismatched }, null, AfterDelivery());
	}

	[TestMethod]
	[Description("Конфликтующие delivery-записи с одним ключом останавливают материализацию")]
	[ExpectedException(typeof(ExpiryMaterializationException))]
	public void ThrowOnConflictingDuplicateDeliveryKey()
	{
		// Arrange: две записи с одним ключом, но разным содержимым — источник записей
		// с одним идентификатором обязан быть единственным.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "100") };
		var first = Delivery(CallSymbol, OptionDeliveryMs, deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.09");
		var second = Delivery(CallSymbol, OptionDeliveryMs, deliveryPrice: "47000", strike: "45000", fee: "0", deliveryRpl: "0.1");

		// Act — Assert
		materializer.Materialize(rawExecutions, new[] { first, second }, null, AfterDelivery());
	}

	[TestMethod]
	[DataRow("deliveryPrice")]
	[DataRow("strike")]
	[Description("Отсутствующая расчётная цена или страйк delivery-записи останавливает материализацию")]
	[ExpectedException(typeof(ExpiryMaterializationException))]
	public void ThrowOnMissingDeliveryNumber(string missingField)
	{
		// Arrange: внутренняя стоимость не считается без расчётной цены экспирации
		// и страйка — биржа кодирует отсутствующее число пустой строкой.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "100") };
		var deliveryPrice = missingField == "deliveryPrice" ? "" : "46000";
		var strike = missingField == "strike" ? "" : "45000";
		var incomplete = Delivery(CallSymbol, OptionDeliveryMs, deliveryPrice, strike, fee: "0", deliveryRpl: "0.09");

		// Act — Assert
		materializer.Materialize(rawExecutions, new[] { incomplete }, null, AfterDelivery());
	}

	[TestMethod]
	[Description("Расхождение даты доставки со справочником инструментов останавливает материализацию")]
	[ExpectedException(typeof(ExpiryMaterializationException))]
	public void ThrowOnDeliveryDateMismatchWithCatalog()
	{
		// Arrange: канонический deliveryTime хранит справочник; запись с датой
		// доставки другого дня — повреждение или подмена инструмента.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { RawOption("exec-a1", "Buy", "0.0001", "100") };
		var wrongDayMs = new DateTimeOffset(2023, 12, 28, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
		var wrongDay = Delivery(CallSymbol, wrongDayMs, deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.09");

		// Act — Assert
		materializer.Materialize(rawExecutions, new[] { wrongDay }, null, AfterDelivery());
	}

	[TestMethod]
	[Description("Символ delivery-записи вне справочника останавливает материализацию ошибкой сверки")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnUnknownDeliverySymbol()
	{
		// Arrange: справочник — канонический источник свойств опциона; символ без
		// спецификации не допускается до закрывающих записей.
		var materializer = CreateMaterializer();
		var unknown = Delivery("ETH-29DEC23-2000-C", OptionDeliveryMs, deliveryPrice: "2400", strike: "2000", fee: "0", deliveryRpl: "0.4");

		// Act — Assert
		materializer.Materialize(Array.Empty<RawExecution>(), new[] { unknown }, null, AfterDelivery());
	}

	#region Помощники

	private static ExpiryMaterializer CreateMaterializer()
	{
		var resolver = new InstrumentResolver(CreateCatalog());
		return new ExpiryMaterializer(new TradeMaterializer(resolver), resolver);
	}

	/// <summary>Момент материализации после deliveryTime: экспирация уже наступила.</summary>
	private static DateTimeOffset AfterDelivery() => new(2023, 12, 29, 12, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Справочник инструментов: колл и пут BTC с delivery 29DEC23 08:00 UTC
	/// и линейный перп BTCUSDT.
	/// </summary>
	private static InstrumentCatalog CreateCatalog()
	{
		var callPayload =
			$$"""{"symbol":"{{CallSymbol}}","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{OptionDeliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var putPayload =
			$$"""{"symbol":"{{PutSymbol}}","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Put","deliveryTime":"{{OptionDeliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		return new InstrumentCatalog(new[]
		{
			new RawInstrument { Symbol = CallSymbol, Category = "option", PayloadJson = callPayload, FetchedAt = FetchedAt },
			new RawInstrument { Symbol = PutSymbol, Category = "option", PayloadJson = putPayload, FetchedAt = FetchedAt },
			new RawInstrument { Symbol = "BTCUSDT", Category = "linear", PayloadJson = linearPayload, FetchedAt = FetchedAt },
		});
	}

	private static RawExecution RawOption(
		string execId,
		string side,
		string execQty,
		string execPrice,
		string symbol = CallSymbol) => Raw(
		execId, "option", symbol, ExecMs(2023, 12, 28, 10, 0),
		ExecutionPayload(execId, symbol, side, execPrice, execQty, "0", "USDC", ExecMs(2023, 12, 28, 10, 0)));

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

	private static RawDelivery Delivery(
		string symbol,
		long deliveryTimeMs,
		string deliveryPrice,
		string strike,
		string fee,
		string deliveryRpl,
		string category = "option") => RowWithPayload(
		symbol, deliveryTimeMs, category,
		DeliveryPayload(symbol, deliveryTimeMs, deliveryPrice, strike, fee, deliveryRpl));

	private static RawDelivery RowWithPayload(string symbol, long deliveryTimeMs, string category, string payloadJson) => new()
	{
		Symbol = symbol,
		DeliveryTimeMs = deliveryTimeMs,
		Category = category,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>Delivery-запись в форме ответа delivery-record: числа биржа шлёт строками.</summary>
	private static string DeliveryPayload(
		string symbol,
		long deliveryTimeMs,
		string deliveryPrice,
		string strike,
		string fee,
		string deliveryRpl,
		string side = "Buy",
		string position = "0.0001",
		string entryPrice = "100") =>
		$$"""{"symbol":"{{symbol}}","side":"{{side}}","deliveryTime":"{{deliveryTimeMs}}","entryPrice":"{{entryPrice}}","deliveryPrice":"{{deliveryPrice}}","strike":"{{strike}}","fee":"{{fee}}","position":"{{position}}","deliveryRpl":"{{deliveryRpl}}"}""";

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
