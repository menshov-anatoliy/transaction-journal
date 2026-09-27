using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Materialization;

/// <summary>
/// Проверки сверки символа опциона со справочником инструментов: разбор
/// {BASE}-{dMMMyy}-{strike}-{C|P}[-{QUOTE}] и сверка базового актива, типа опциона
/// и даты экспирации с канонической спецификацией биржи.
/// </summary>
[TestClass]
public class InstrumentResolverTests
{
	// Канонические времена delivery реальных экспираций BTC/ETH/XAUT: 08:00 UTC.
	private const long Btc27Dec24DeliveryMs = 1735286400000L;
	private const long Eth3Jan23DeliveryMs = 1672732800000L;
	private const long Btc30Dec22DeliveryMs = 1672387200000L;
	private const long Xaut30Oct26DeliveryMs = 1793347200000L;

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[DataRow("BTC-27DEC24-2800-C", "BTC", "Call", Btc27Dec24DeliveryMs, 2800d, 2024, 12, 27)]
	[DataRow("ETH-3JAN23-1250-P", "ETH", "Put", Eth3Jan23DeliveryMs, 1250d, 2023, 1, 3)]
	[DataRow("BTC-30DEC22-18000-C", "BTC", "Call", Btc30Dec22DeliveryMs, 18000d, 2022, 12, 30)]
	[Description("Реальные символы BTC/ETH сверяются со справочником; канонические значения берутся из спецификации, а не из строки")]
	public void TryIfRealSymbolsResolveAgainstCatalog(
		string symbol, string baseCoin, string optionsType, long deliveryTimeMs, double strike, int year, int month, int day)
	{
		// Arrange: справочник содержит спецификацию инструмента из instruments-info.
		// Требование: парсинг символа опциона сверяется со справочником, а не доверяет
		// строке символа; канонические optionsType/deliveryTime берутся из справочника.
		// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
		// Traceability: change:add-bybit-sync/design#d7
		var resolver = Resolver(Raw(symbol, "option", Payload(baseCoin, optionsType, deliveryTimeMs)));

		// Act
		var resolved = resolver.ResolveOption(symbol);

		// Assert: канонические свойства — из справочника (deliveryTime 08:00 UTC,
		// а не только дата из символа); страйк — из разбора символа.
		Assert.That(resolved.Symbol, Is.EqualTo(symbol));
		Assert.That(resolved.Category, Is.EqualTo("option"));
		Assert.That(resolved.BaseCoin, Is.EqualTo(baseCoin));
		Assert.That(resolved.OptionsType, Is.EqualTo(optionsType == "Call" ? OptionType.Call : OptionType.Put));
		Assert.That(resolved.Strike, Is.EqualTo(strike));
		Assert.That(resolved.DeliveryTime, Is.EqualTo(new DateTimeOffset(year, month, day, 8, 0, 0, TimeSpan.Zero)));
	}

	[TestMethod]
	[Description("Символ с хвостовой котируемой валютой (реальная доска опционов Bybit) сверяется со справочником")]
	public void TryIfSymbolWithQuoteCoinSuffixResolvesAgainstCatalog()
	{
		// Arrange: символ XAUT-30OCT26-4400-C-USDT пришёл из живого ответа
		// execution-list категории option и есть в справочнике инструментов.
		// Требование: парсинг символа сверяется со справочником, а не доверяет строке.
		// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
		var resolver = Resolver(Raw("XAUT-30OCT26-4400-C-USDT", "option", Payload("XAUT", "Call", Xaut30Oct26DeliveryMs)));

		// Act
		var resolved = resolver.ResolveOption("XAUT-30OCT26-4400-C-USDT");

		// Assert: символ разобран вместе с суффиксом USDT, канонические значения — из справочника.
		Assert.That(resolved.Symbol, Is.EqualTo("XAUT-30OCT26-4400-C-USDT"));
		Assert.That(resolved.BaseCoin, Is.EqualTo("XAUT"));
		Assert.That(resolved.OptionsType, Is.EqualTo(OptionType.Call));
		Assert.That(resolved.Strike, Is.EqualTo(4400d));
		Assert.That(resolved.DeliveryTime, Is.EqualTo(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero)));
	}

	[TestMethod]
	[Description("Неизвестный справочнику инструмент с недоставленной доской останавливает сверку — его спецификацию нужно загрузить из биржи")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnUnknownSymbol()
	{
		// Arrange: справочник пуст — инструмент встретился в записях впервые, а часы
		// стоят до доставки доски 27DEC24: живому инструменту спецификация обязана
		// попасть в справочник до материализации сделок.
		// Требование: неизвестный инструмент должен пополнять справочник до материализации.
		// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
		// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbol-degrades-to-warning
		var resolver = Resolver(
			new FixedTimeProvider(new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero)));

		try
		{
			// Act
			resolver.ResolveOption("BTC-27DEC24-2800-C");
		}
		catch (InstrumentResolveException exception)
		{
			// Assert: причина — отсутствие в справочнике, а не разрешение из символа.
			Assert.That(exception.Reason, Is.EqualTo(InstrumentResolveFailureReason.UnknownSymbol));
			Assert.That(exception.Symbol, Is.EqualTo("BTC-27DEC24-2800-C"));
			throw;
		}
	}

	[TestMethod]
	[DataRow(0, "текущий момент совпал со временем доставки доски")]
	[DataRow(24, "доска доставлена сутки назад")]
	[Description("Делистинговый опцион с доставленной доской разрешается из частей символа, когда справочник молчит")]
	public void TryIfDelistedOptionResolvesFromSymbol(int hoursAfterDelivery, string caseDescription)
	{
		// Arrange: справочник не содержит спецификации ETH-27DEC24-3100-C — биржа
		// отвечает отказом «контракт недоступен» и никогда её не отдаст; часы стоят
		// на момент доставки доски 27DEC24 08:00 UTC или позже.
		// Требование: истёкшая доска разрешается из символа, чтобы записи делистингового
		// инструмента материализовались без пополнения справочника.
		// Traceability: openspec:sync/bybit-history#scenario-delisted-option-resolves-from-symbol
		var deliveryTime = new DateTimeOffset(2024, 12, 27, 8, 0, 0, TimeSpan.Zero);
		var resolver = Resolver(new FixedTimeProvider(deliveryTime.AddHours(hoursAfterDelivery)));

		// Act
		var resolved = resolver.ResolveOption("ETH-27DEC24-3100-C");

		// Assert: спецификация выведена из символа — базовый актив, тип и страйк из
		// разбора, категория option, delivery — 08:00 UTC даты доски.
		Assert.That(resolved.Symbol, Is.EqualTo("ETH-27DEC24-3100-C"), caseDescription);
		Assert.That(resolved.Category, Is.EqualTo("option"), caseDescription);
		Assert.That(resolved.BaseCoin, Is.EqualTo("ETH"), caseDescription);
		Assert.That(resolved.OptionsType, Is.EqualTo(OptionType.Call), caseDescription);
		Assert.That(resolved.Strike, Is.EqualTo(3100d), caseDescription);
		Assert.That(resolved.DeliveryTime, Is.EqualTo(deliveryTime), caseDescription);
	}

	[TestMethod]
	[Description("Fallback делистинговой доски действует и без явных часов — по системному времени читающей стороны")]
	public void TryIfDelistedOptionResolvesWithSystemClockByDefault()
	{
		// Arrange: справочник пуст, часы не заданы — конструктор по умолчанию берёт
		// системное время; доска BTC-30DEC22 давно доставлена, поэтому существующие
		// места вызова резолвера получают разрешение из символа без изменений.
		// Требование: разрешение выполняется при чтении read-моделью, без пересборки.
		// Traceability: openspec:sync/bybit-history#scenario-delisted-option-resolves-from-symbol
		var resolver = Resolver();

		// Act
		var resolved = resolver.ResolveOption("BTC-30DEC22-18000-C");

		// Assert: инструмент разрешён из символа с delivery 08:00 UTC даты доски.
		Assert.That(resolved.Symbol, Is.EqualTo("BTC-30DEC22-18000-C"));
		Assert.That(resolved.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(resolved.OptionsType, Is.EqualTo(OptionType.Call));
		Assert.That(resolved.Strike, Is.EqualTo(18000d));
		Assert.That(resolved.DeliveryTime, Is.EqualTo(new DateTimeOffset(2022, 12, 30, 8, 0, 0, TimeSpan.Zero)));
	}

	[TestMethod]
	[DataRow("BTCUSDT")]
	[DataRow("BTC-27DEC24-2800")]
	[DataRow("BTC-27DEC24-2800-X")]
	[Description("Символ вне формата опциона останавливает сверку с причиной MalformedOptionSymbol даже при доставленной доске")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnMalformedOptionSymbol(string symbol)
	{
		// Arrange: часы стоят после доставки доски — неразбираемый символ не
		// разрешается из себя и fallback'ом: fallback опирается на разбор символа.
		// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
		var resolver = Resolver(
			DeliveredBoardClock(),
			Raw(symbol == "BTCUSDT" ? symbol : "BTC-27DEC24-2800-C", "option", Payload("BTC", "Call", Btc27Dec24DeliveryMs)));

		try
		{
			// Act
			resolver.ResolveOption(symbol);
		}
		catch (InstrumentResolveException exception)
		{
			// Assert
			Assert.That(exception.Reason, Is.EqualTo(InstrumentResolveFailureReason.MalformedOptionSymbol));
			throw;
		}
	}

	[TestMethod]
	[Description("Запись справочника категории linear не сворачивается с символом опциона даже при доставленной доске")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnCategoryMismatch()
	{
		// Arrange: символ опциона есть в справочнике, но записан категорией linear;
		// часы стоят после доставки доски — fallback применяется только к промаху
		// справочника и не смягчает расхождение с имеющейся спецификацией.
		// Traceability: openspec:sync/bybit-history#scenario-instrument-mismatch-still-fails
		var resolver = Resolver(
			DeliveredBoardClock(),
			Raw("BTC-27DEC24-2800-C", "linear", Payload("BTC", "Call", Btc27Dec24DeliveryMs)));

		try
		{
			// Act
			resolver.ResolveOption("BTC-27DEC24-2800-C");
		}
		catch (InstrumentResolveException exception)
		{
			// Assert
			Assert.That(exception.Reason, Is.EqualTo(InstrumentResolveFailureReason.CategoryMismatch));
			throw;
		}
	}

	[TestMethod]
	[Description("Базовый актив из символа расходится со справочником — сверка не проходит даже при доставленной доске")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnBaseCoinMismatch()
	{
		// Arrange: биржа считает базовым активом ETH, а символ начинается с BTC;
		// часы стоят после доставки доски — расхождение со справочником остаётся
		// ошибкой сверки, а не разрешается fallback'ом.
		// Traceability: openspec:sync/bybit-history#scenario-instrument-mismatch-still-fails
		var resolver = Resolver(
			DeliveredBoardClock(),
			Raw("BTC-27DEC24-2800-C", "option", Payload("ETH", "Call", Btc27Dec24DeliveryMs)));

		try
		{
			// Act
			resolver.ResolveOption("BTC-27DEC24-2800-C");
		}
		catch (InstrumentResolveException exception)
		{
			// Assert
			Assert.That(exception.Reason, Is.EqualTo(InstrumentResolveFailureReason.BaseCoinMismatch));
			throw;
		}
	}

	[TestMethod]
	[Description("Тип опциона из символа расходится со справочником — сверка не проходит даже при доставленной доске")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnOptionsTypeMismatch()
	{
		// Arrange: последний сегмент символа — C (Call), справочник говорит Put;
		// часы стоят после доставки доски — расхождение не смягчается fallback'ом.
		// Traceability: openspec:sync/bybit-history#scenario-instrument-mismatch-still-fails
		var resolver = Resolver(
			DeliveredBoardClock(),
			Raw("BTC-27DEC24-2800-C", "option", Payload("BTC", "Put", Btc27Dec24DeliveryMs)));

		try
		{
			// Act
			resolver.ResolveOption("BTC-27DEC24-2800-C");
		}
		catch (InstrumentResolveException exception)
		{
			// Assert
			Assert.That(exception.Reason, Is.EqualTo(InstrumentResolveFailureReason.OptionsTypeMismatch));
			throw;
		}
	}

	[TestMethod]
	[DataRow(Btc27Dec24DeliveryMs + 86_400_000L, "сдвиг на сутки вперёд")]
	[DataRow(Btc27Dec24DeliveryMs - 86_400_000L, "сдвиг на сутки назад")]
	[DataRow(0L, "отсутствие доставки у записи")]
	[Description("Дата экспирации из символа расходится со временем delivery справочника — сверка не проходит даже при доставленной доске")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnDeliveryTimeMismatch(long deliveryTimeMs, string caseDescription)
	{
		// Arrange: символ кодирует 27DEC24, а справочник — другую дату доставки;
		// часы стоят после доставки доски — расхождение со справочником остаётся
		// ошибкой сверки, fallback делистинговых досок на него не действует.
		// Traceability: openspec:sync/bybit-history#scenario-instrument-mismatch-still-fails
		var resolver = Resolver(
			DeliveredBoardClock(),
			Raw("BTC-27DEC24-2800-C", "option", Payload("BTC", "Call", deliveryTimeMs)));

		try
		{
			// Act
			resolver.ResolveOption("BTC-27DEC24-2800-C");
		}
		catch (InstrumentResolveException exception)
		{
			// Assert: сверяются именно даты, а не полное время суток.
			Assert.That(exception.Reason, Is.EqualTo(InstrumentResolveFailureReason.DeliveryTimeMismatch), caseDescription);
			throw;
		}
	}

	[TestMethod]
	[DataRow("")]
	[DataRow(" ")]
	[Description("Пустой символ отклоняется сверкой")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnEmptySymbol(string symbol)
	{
		// Arrange
		var resolver = Resolver();

		// Act — Assert
		resolver.ResolveOption(symbol);
	}

	[TestMethod]
	[Description("Null-справочник отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullCatalog()
	{
		// Arrange — Act — Assert
		new InstrumentResolver(null!);
	}

	#region Помощники

	private static InstrumentResolver Resolver(params RawInstrument[] rawInstruments)
	{
		return new InstrumentResolver(new InstrumentCatalog(rawInstruments));
	}

	private static InstrumentResolver Resolver(TimeProvider timeProvider, params RawInstrument[] rawInstruments)
	{
		return new InstrumentResolver(new InstrumentCatalog(rawInstruments), timeProvider);
	}

	/// <summary>Часы после доставки фикстурных досок (декабрь 2024 и ранее): доски уже доставлены.</summary>
	private static FixedTimeProvider DeliveredBoardClock() => new(FetchedAt);

	private static RawInstrument Raw(string symbol, string category, string payloadJson) => new()
	{
		Symbol = symbol,
		Category = category,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>Спецификация опциона в форме ответа instruments-info: числа биржа шлёт строками.</summary>
	private static string Payload(string baseCoin, string optionsType, long deliveryTimeMs) =>
		$$"""{"symbol":"BTC-27DEC24-2800-C","baseCoin":"{{baseCoin}}","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryTimeMs}}"}""";

	#endregion
}
