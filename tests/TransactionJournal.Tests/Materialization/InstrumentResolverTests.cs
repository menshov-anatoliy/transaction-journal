using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Materialization;

/// <summary>
/// Проверки сверки символа опциона со справочником инструментов: разбор
/// {BASE}-{dMMMyy}-{strike}-{C|P} и сверка базового актива, типа опциона и даты
/// экспирации с канонической спецификацией биржи.
/// </summary>
[TestClass]
public class InstrumentResolverTests
{
	// Канонические времена delivery реальных экспираций BTC/ETH: 08:00 UTC.
	private const long Btc27Dec24DeliveryMs = 1735286400000L;
	private const long Eth3Jan23DeliveryMs = 1672732800000L;
	private const long Btc30Dec22DeliveryMs = 1672387200000L;

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
	[Description("Неизвестный справочнику инструмент останавливает сверку — его спецификацию нужно загрузить из биржи")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnUnknownSymbol()
	{
		// Arrange: справочник пуст — инструмент встретился в записях впервые.
		// Требование: неизвестный инструмент должен пополнять справочник до материализации.
		// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
		var resolver = Resolver();

		try
		{
			// Act
			resolver.ResolveOption("BTC-27DEC24-2800-C");
		}
		catch (InstrumentResolveException exception)
		{
			// Assert: причина — отсутствие в справочнике.
			Assert.That(exception.Reason, Is.EqualTo(InstrumentResolveFailureReason.UnknownSymbol));
			Assert.That(exception.Symbol, Is.EqualTo("BTC-27DEC24-2800-C"));
			throw;
		}
	}

	[TestMethod]
	[DataRow("BTCUSDT")]
	[DataRow("BTC-27DEC24-2800")]
	[DataRow("BTC-27DEC24-2800-X")]
	[Description("Символ вне формата опциона останавливает сверку с причиной MalformedOptionSymbol")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnMalformedOptionSymbol(string symbol)
	{
		// Arrange
		var resolver = Resolver(Raw(symbol == "BTCUSDT" ? symbol : "BTC-27DEC24-2800-C", "option", Payload("BTC", "Call", Btc27Dec24DeliveryMs)));

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
	[Description("Запись справочника категории linear не сворачивается с символом опциона")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnCategoryMismatch()
	{
		// Arrange: символ опциона есть в справочнике, но записан категорией linear.
		var resolver = Resolver(Raw("BTC-27DEC24-2800-C", "linear", Payload("BTC", "Call", Btc27Dec24DeliveryMs)));

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
	[Description("Базовый актив из символа расходится со справочником — сверка не проходит")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnBaseCoinMismatch()
	{
		// Arrange: биржа считает базовым активом ETH, а символ начинается с BTC.
		var resolver = Resolver(Raw("BTC-27DEC24-2800-C", "option", Payload("ETH", "Call", Btc27Dec24DeliveryMs)));

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
	[Description("Тип опциона из символа расходится со справочником — сверка не проходит")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnOptionsTypeMismatch()
	{
		// Arrange: последний сегмент символа — C (Call), справочник говорит Put.
		var resolver = Resolver(Raw("BTC-27DEC24-2800-C", "option", Payload("BTC", "Put", Btc27Dec24DeliveryMs)));

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
	[Description("Дата экспирации из символа расходится со временем delivery справочника — сверка не проходит")]
	[ExpectedException(typeof(InstrumentResolveException))]
	public void ThrowOnDeliveryTimeMismatch(long deliveryTimeMs, string caseDescription)
	{
		// Arrange: символ кодирует 27DEC24, а справочник — другую дату доставки.
		var resolver = Resolver(Raw("BTC-27DEC24-2800-C", "option", Payload("BTC", "Call", deliveryTimeMs)));

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
