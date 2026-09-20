using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки движка FIFO результата позиции: встречные записи закрывают старейшие
/// открытые части по хронологии, закрывающая запись delivery входит в расчёт по
/// внутренней стоимости, комиссии исполнений уменьшают результат с учётом паритета
/// USDC, funding не входит в результат вовсе, а биржевой deliveryRpl остаётся
/// предупреждающей сверкой, не подменяя собственный расчёт. Негативные проверки
/// отклоняют пустые входные коллекции, пустые ключи источников и неизвестный вид записи.
/// </summary>
[TestClass]
public class PositionFifoEngineTests
{
	private const string CallSymbol = "BTC-29DEC23-45000-C";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Каноническое время delivery справочника: 29DEC23 08:00 UTC.</summary>
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private static readonly long OptionDeliveryMs = OptionDelivery.ToUnixTimeMilliseconds();

	private PositionFifoEngine _engine = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Движок чистый — каждая проверка получает свежий экземпляр без состояния.
		_engine = new PositionFifoEngine();
	}

	[TestMethod]
	[Description("Встречные записи закрывают старейшие открытые части в порядке хронологии")]
	public void TryIfOppositeEntriesCloseOldestOpenPartsChronologically()
	{
		// Arrange: две покупки и частичная продажа; записи переданы не по порядку,
		// чтобы хронологию выстраивал сам движок.
		// Требование: каждая встречная запись закрывает старейшие ещё не закрытые
		// части (FIFO), непокрытыми остаются только последние части.
		// Traceability: openspec:analytics/performance#scenario-fifo-matches-chronologically
		var entries = new[]
		{
			Trade(30, "exec-sell-1", -1.5m, 115m),
			Trade(0, "exec-buy-1", 1m, 100m),
			Trade(10, "exec-buy-2", 1m, 120m),
		};

		// Act
		var result = _engine.Match(entries);

		// Assert: продажа закрыла слой 1@100 целиком (+15) и половину слоя 1@120 (−2.5);
		// непокрытым остался остаток 0.5 по цене последнего слоя.
		Assert.That(result.RealizedPnL, Is.EqualTo(12.5m));
		Assert.That(result.Residual, Is.EqualTo(0.5m));
		Assert.That(result.AverageOpenPrice, Is.EqualTo(120m));
		Assert.That(result.AccumulatedFees, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Короткие слои закрываются выкупом в том же FIFO-порядке")]
	public void TryIfShortLayersCloseByBuyBackChronologically()
	{
		// Arrange: две продажи открывают короткие слои, выкуп закрывает старейший
		// целиком и часть второго; знак PnL переворачивается направлением слоя.
		// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
		var entries = new[]
		{
			Trade(0, "exec-sell-1", -0.2m, 50m),
			Trade(10, "exec-sell-2", -0.3m, 60m),
			Trade(20, "exec-buy-1", 0.4m, 55m),
		};

		// Act
		var result = _engine.Match(entries);

		// Assert: выкуп 0.4 закрыл 0.2@50 с убытком (50−55)·0.2 = −1 и 0.2@60
		// с прибылью (60−55)·0.2 = +1; остаток короткий 0.1 по 60.
		Assert.That(result.RealizedPnL, Is.EqualTo(0m));
		Assert.That(result.Residual, Is.EqualTo(-0.1m));
		Assert.That(result.AverageOpenPrice, Is.EqualTo(60m));
	}

	[TestMethod]
	[Description("Закрывающая запись delivery входит в расчёт по внутренней стоимости наравне со встречной сделкой")]
	public void TryIfDeliveryClosingEntersFifoAtIntrinsicValue()
	{
		// Arrange: длинная позиция опциона 0.0001 по цене 100; delivery-запись
		// закрывает её эффективной ценой внутренней стоимости 1000.
		// Требование: закрывающая запись delivery учитывается в реализованном PnL
		// наравне со встречной сделкой по той же цене.
		// Traceability: openspec:analytics/performance#scenario-delivery-closing-enters-fifo
		// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
		var entries = new[]
		{
			Trade(0, "exec-buy-1", 0.0001m, 100m),
			Closing(30, $"{CallSymbol}|{OptionDeliveryMs}", PositionFifoEntryKind.ExpiryClosing, -0.0001m, 1000m),
		};

		// Act
		var result = _engine.Match(entries);

		// Assert: (1000 − 100) · 0.0001 = 0.09; позиция закрыта записью — остаток
		// нулевой, средней цены у закрытой позиции нет.
		Assert.That(result.RealizedPnL, Is.EqualTo(0.09m));
		Assert.That(result.Residual, Is.EqualTo(0m));
		Assert.That(result.AverageOpenPrice, Is.Null);
	}

	[TestMethod]
	[DataRow("0.02", "0.01", "0.03", "9.97")]
	[DataRow("-0.005", "0.01", "0.005", "9.995")]
	[Description("Комиссии записей исполнения уменьшают результат; rebate повышает его")]
	public void TryIfExecutionFeesReduceResult(string buyFeeText, string sellFeeText, string feesText, string pnlText)
	{
		// Arrange: покупка 1@100 и продажа 1@110 дают 10 ценового потока; комиссии
		// обеих записей суммируются и уменьшают реализованный PnL, отрицательная
		// комиссия (rebate) повышает его.
		// Traceability: openspec:analytics/performance#scenario-fees-reduce-result
		var buyFee = Parse(buyFeeText);
		var sellFee = Parse(sellFeeText);
		var entries = new[]
		{
			Trade(0, "exec-buy-1", 1m, 100m, buyFee),
			Trade(10, "exec-sell-1", -1m, 110m, sellFee),
		};

		// Act
		var result = _engine.Match(entries);

		// Assert: накопленные комиссии — сумма со знаком, реализованный PnL —
		// результат встречных частей минус комиссии.
		Assert.That(result.AccumulatedFees, Is.EqualTo(Parse(feesText)));
		Assert.That(result.RealizedPnL, Is.EqualTo(Parse(pnlText)));
		Assert.That(result.Residual, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Комиссия в USDC входит в результат по паритету 1:1 к USDT")]
	public void TryIfUsdcFeesEnterResultAtParity()
	{
		// Arrange: сделки материализуются из биржевых записей исполнения, где
		// комиссия опционных сделок номинирована в USDC; паритет 1:1 не меняет
		// величину комиссии, приводя её к валюте журнала USDT.
		// Traceability: openspec:analytics/performance#scenario-fees-reduce-result
		// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
		var resolver = new InstrumentResolver(CreateCatalog());
		var materializer = new TradeMaterializer(resolver);
		var rawExecutions = new[]
		{
			Raw("exec-buy-1", "Buy", "1", "100", "0.02"),
			Raw("exec-sell-1", "Sell", "1", "110", "0.01"),
		};

		// Act: сделки материализуются и превращаются в записи потока позиции «как есть».
		var trades = materializer.Materialize(rawExecutions);
		var entries = trades.Select(trade => new PositionFifoEntry
		{
			At = trade.ExecutedAt,
			Kind = PositionFifoEntryKind.Trade,
			SourceKey = trade.ExecId,
			Quantity = trade.Quantity,
			Price = trade.Price,
			Fee = trade.Fee,
		}).ToList();
		var result = _engine.Match(entries);

		// Assert: комиссии USDC 0.02 и 0.01 вошли в результат как 0.03 USDT,
		// уменьшив 10 ценового потока до 9.97; валюта приведена паритетом.
		Assert.That(trades.All(trade => trade.FeeCurrency == "USDT"), Is.True);
		Assert.That(result.AccumulatedFees, Is.EqualTo(0.03m));
		Assert.That(result.RealizedPnL, Is.EqualTo(9.97m));
	}

	[TestMethod]
	[Description("Funding-начисления биржи не входят в результат конструкции")]
	public void TryIfFundingPaymentsDoNotEnterResult()
	{
		// Arrange: фьючерсная позиция — покупка 0.1 по 50000 и продажа 0.1
		// по 51000 с комиссиями 0.1; биржевые funding-начисления существуют только
		// на бирже: у потока записей позиции нет вида для funding, граница проходит
		// на входе движка, а не фильтром внутри.
		// Traceability: openspec:analytics/performance#scenario-funding-excluded
		// Traceability: change:add-analytics/design#d6
		var entries = new[]
		{
			Trade(0, "exec-buy-1", 0.1m, 50000m, 0.1m),
			Trade(10, "exec-sell-1", -0.1m, 51000m, 0.1m),
		};

		// Act
		var result = _engine.Match(entries);

		// Assert: результат равен ценовом потоку минус комиссии — 100 − 0.2;
		// funding-платежи не изменили ни реализованный PnL, ни остаток.
		Assert.That(result.RealizedPnL, Is.EqualTo(99.8m));
		Assert.That(result.Residual, Is.EqualTo(0m));
		Assert.That(result.AccumulatedFees, Is.EqualTo(0.2m));
	}

	[TestMethod]
	[Description("Расхождение deliveryRpl с собственным расчётом остаётся предупреждением и не подменяет расчёт")]
	public void TryIfDeliveryRplReconciliationWarnsWithoutReplacingOwnResult()
	{
		// Arrange: покупка 0.0001 опциона по 100 и delivery с внутренней стоимостью
		// 1000 — собственный результат 0.09, а биржа отчиталась deliveryRpl 0.05.
		// Требование: deliveryRpl используется только предупреждающей сверкой;
		// собственный расчёт FIFO считается из собственных записей и не переписывается.
		// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
		// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
		var resolver = new InstrumentResolver(CreateCatalog());
		var tradeMaterializer = new TradeMaterializer(resolver);
		var expiryMaterializer = new ExpiryMaterializer(tradeMaterializer, resolver);
		var rawExecutions = new[] { Raw("exec-buy-1", "Buy", "0.0001", "100", "0") };
		var rawDeliveries = new[]
		{
			Delivery(deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.05"),
		};
		var assignments = new Dictionary<string, string?> { ["exec-buy-1"] = "con-a" };

		// Act: материализатор даёт сделки и закрывающие записи с предупреждением
		// сверки; движок считает результат по собственным записям.
		var trades = tradeMaterializer.Materialize(rawExecutions);
		var expiry = expiryMaterializer.Materialize(rawExecutions, rawDeliveries, assignments, AfterDelivery());
		var entries = trades.Select(trade => new PositionFifoEntry
		{
			At = trade.ExecutedAt,
			Kind = PositionFifoEntryKind.Trade,
			SourceKey = trade.ExecId,
			Quantity = trade.Quantity,
			Price = trade.Price,
			Fee = trade.Fee,
		}).ToList();
		entries.AddRange(expiry.ClosingEntries.Select(entry => new PositionFifoEntry
		{
			At = entry.ClosedAt,
			Kind = PositionFifoEntryKind.ExpiryClosing,
			SourceKey = entry.SourceKey,
			Quantity = entry.Quantity,
			Price = entry.EffectivePrice,
			Fee = entry.Fee,
		}));
		var result = _engine.Match(entries);

		// Assert: предупреждение сверки зафиксировало расхождение −0.04, но результат
		// движка — собственные 0.09 из внутренней стоимости, а не биржевые 0.05.
		Assert.That(expiry.Warnings.Count, Is.EqualTo(1));
		Assert.That(expiry.Warnings[0].DeliveryRpl, Is.EqualTo(0.05m));
		Assert.That(expiry.Warnings[0].OwnResult, Is.EqualTo(0.09m));
		Assert.That(expiry.Warnings[0].Difference, Is.EqualTo(-0.04m));
		Assert.That(result.RealizedPnL, Is.EqualTo(0.09m));
		Assert.That(result.Residual, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Ручная пометка закрывает остаток ценой пользователя")]
	public void TryIfManualMarkClosesResidualAtUserPrice()
	{
		// Arrange: длинный остаток 0.5@120 закрыт ручной пометкой по цене 90;
		// пометка — полноценная закрывающая запись потока с ценой пользователя.
		// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
		// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
		var entries = new[]
		{
			Trade(0, "exec-buy-1", 0.5m, 120m),
			Closing(20, "manual:1", PositionFifoEntryKind.ManualMark, -0.5m, 90m),
		};

		// Act
		var result = _engine.Match(entries);

		// Assert: (90 − 120) · 0.5 = −15; позиция закрыта пометкой целиком.
		Assert.That(result.RealizedPnL, Is.EqualTo(-15m));
		Assert.That(result.Residual, Is.EqualTo(0m));
		Assert.That(result.AverageOpenPrice, Is.Null);
	}

	[TestMethod]
	[Description("Null-коллекция записей отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullEntries()
	{
		// Arrange — Act — Assert
		_engine.Match(null!);
	}

	[TestMethod]
	[Description("Пустой ключ источника записи отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnEmptySourceKey()
	{
		// Arrange: ключ источника — основа устойчивой хронологии, пустой ключ
		// означает повреждение записи потока.
		var entries = new[] { Trade(0, " ", 1m, 100m) };

		// Act — Assert
		_engine.Match(entries);
	}

	[TestMethod]
	[Description("Неизвестный вид записи отклоняется")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnUnknownEntryKind()
	{
		// Arrange: вид записи задаёт ранг хронологии; неизвестный вид не может
		// быть упорядочен и останавливает сопоставление. Вторая запись заставляет
		// сортировку вычислять ключи порядка — одиночный элемент сортировка не сортирует.
		var entries = new[]
		{
			new PositionFifoEntry
			{
				At = At(0),
				Kind = (PositionFifoEntryKind)99,
				SourceKey = "exec-unknown-1",
				Quantity = 1m,
				Price = 100m,
			},
			Trade(10, "exec-buy-2", 1m, 120m),
		};

		// Act — Assert
		_engine.Match(entries);
	}

	#region Помощники

	/// <summary>Базовый момент потока: 1 января 2026 года 10:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

	private static DateTimeOffset At(int minutes) => BaseAt.AddMinutes(minutes);

	/// <summary>Строит запись сделки потока позиции.</summary>
	private static PositionFifoEntry Trade(int minutes, string execId, decimal quantity, decimal price, decimal fee = 0m) => new()
	{
		At = At(minutes),
		Kind = PositionFifoEntryKind.Trade,
		SourceKey = execId,
		Quantity = quantity,
		Price = price,
		Fee = fee,
	};

	/// <summary>Строит закрывающую запись потока позиции.</summary>
	private static PositionFifoEntry Closing(
		int minutes,
		string sourceKey,
		PositionFifoEntryKind kind,
		decimal quantity,
		decimal price,
		decimal fee = 0m) => new()
	{
		At = At(minutes),
		Kind = kind,
		SourceKey = sourceKey,
		Quantity = quantity,
		Price = price,
		Fee = fee,
	};

	/// <summary>Разбирает десятичную строку DataRow в десятичное число инвариантной культурой.</summary>
	private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

	/// <summary>Момент материализации после deliveryTime: экспирация уже наступила.</summary>
	private static DateTimeOffset AfterDelivery() => new(2023, 12, 29, 12, 0, 0, TimeSpan.Zero);

	/// <summary>Справочник инструментов: колл BTC с delivery 29DEC23 08:00 UTC.</summary>
	private static InstrumentCatalog CreateCatalog()
	{
		var callPayload =
			$$"""{"symbol":"{{CallSymbol}}","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{OptionDeliveryMs}}","deliveryFeeRate":"0.00015"}""";
		return new InstrumentCatalog(new[]
		{
			new RawInstrument { Symbol = CallSymbol, Category = "option", PayloadJson = callPayload, FetchedAt = FetchedAt },
		});
	}

	/// <summary>Строит сырую запись исполнения опциона с комиссией USDC.</summary>
	private static RawExecution Raw(string execId, string side, string execQty, string execPrice, string execFee)
	{
		var executedAtMs = ExecMs(2023, 12, 28, 10, 0);
		var payload =
			$$"""{"symbol":"{{CallSymbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{executedAtMs}}","feeCurrency":"USDC","isMaker":false}""";
		return new RawExecution
		{
			ExecId = execId,
			Category = "option",
			Symbol = CallSymbol,
			ExecTimeMs = executedAtMs,
			PayloadJson = payload,
			FetchedAt = FetchedAt,
		};
	}

	/// <summary>Строит сырую delivery-запись колла с расчётной ценой экспирации 46000.</summary>
	private static RawDelivery Delivery(string deliveryPrice, string strike, string fee, string deliveryRpl)
	{
		var payload =
			$$"""{"symbol":"{{CallSymbol}}","side":"Buy","deliveryTime":"{{OptionDeliveryMs}}","entryPrice":"100","deliveryPrice":"{{deliveryPrice}}","strike":"{{strike}}","fee":"{{fee}}","position":"0.0001","deliveryRpl":"{{deliveryRpl}}"}""";
		return new RawDelivery
		{
			Symbol = CallSymbol,
			DeliveryTimeMs = OptionDeliveryMs,
			Category = "option",
			PayloadJson = payload,
			FetchedAt = FetchedAt,
		};
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
