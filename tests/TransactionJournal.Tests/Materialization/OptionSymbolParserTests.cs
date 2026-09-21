using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Materialization;

/// <summary>
/// Проверки разбора символа опциона Bybit формата {BASE}-{dMMMyy}-{strike}-{C|P}[-{QUOTE}]
/// инвариантной культурой на реальных символах BTC/ETH/XAUT из исследования API
/// и живой доски опционов.
/// </summary>
[TestClass]
public class OptionSymbolParserTests
{
	[TestMethod]
	[DataRow("BTC-27DEC24-2800-C", "BTC", 2024, 12, 27, 2800d, OptionType.Call)]
	[DataRow("BTC-30DEC22-18000-C", "BTC", 2022, 12, 30, 18000d, OptionType.Call)]
	[DataRow("ETH-3JAN23-1250-P", "ETH", 2023, 1, 3, 1250d, OptionType.Put)]
	[DataRow("ETH-26DEC22-1400-C", "ETH", 2022, 12, 26, 1400d, OptionType.Call)]
	[DataRow("BTC-2JAN26-100000-P", "BTC", 2026, 1, 2, 100000d, OptionType.Put)]
	// Реальные символы живой доски опционов Bybit несут хвостовой сегмент котируемой валюты.
	[DataRow("XAUT-30OCT26-4400-C-USDT", "XAUT", 2026, 10, 30, 4400d, OptionType.Call)]
	[DataRow("BTC-25JUN27-106000-P-USDT", "BTC", 2027, 6, 25, 106000d, OptionType.Put)]
	// Исторические USDC-опционы пишут ту же валюту в хвосте.
	[DataRow("BTC-27DEC24-2800-C-USDC", "BTC", 2024, 12, 27, 2800d, OptionType.Call)]
	[Description("Реальные символы опционов BTC/ETH/XAUT разбираются на базовый актив, дату экспирации, страйк и тип")]
	public void TryIfParsesRealBybitOptionSymbols(
		string symbol, string baseCoin, int year, int month, int day, double strike, OptionType type)
	{
		// Arrange: символы и их параметры взяты из исследования Bybit V5 API
		// и живого ответа instruments-info (категория option).
		// Требование: парсинг символа опциона идёт по формату {BASE}-{dMMMyy}-{strike}-{C|P}[-{QUOTE}]
		// инвариантной культурой, но канонические значения определяет справочник.
		// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
		// Traceability: doc:docs/research/bybit-api.md#4-форматы-символов

		// Act
		var parsed = OptionSymbolParser.TryParse(symbol, out var parts);

		// Assert: все четыре сегмента разобраны; день без ведущего нуля (3JAN23) поддержан.
		Assert.That(parsed, Is.True);
		Assert.That(parts, Is.Not.Null);
		Assert.That(parts!.BaseCoin, Is.EqualTo(baseCoin));
		Assert.That(parts.ExpiryDate, Is.EqualTo(new DateTime(year, month, day)));
		Assert.That(parts.Strike, Is.EqualTo(strike));
		Assert.That(parts.Type, Is.EqualTo(type));
	}

	[TestMethod]
	[DataRow("BTC-27Dec24-2800-C")]
	[DataRow("BTC-27dec24-2800-C")]
	[Description("Регистр букв месяца не мешает разбору: инвариантная культура сравнивает имена месяцев без учёта регистра")]
	public void TryIfMonthCaseDoesNotAffectParsing(string symbol)
	{
		// Arrange — Act: биржа пишет месяц заглавными, но разбор толерантен к регистру сегмента даты.
		var parsed = OptionSymbolParser.TryParse(symbol, out var parts);

		// Assert
		Assert.That(parsed, Is.True);
		Assert.That(parts!.ExpiryDate, Is.EqualTo(new DateTime(2024, 12, 27)));
	}

	[TestMethod]
	[Description("Двузначный год интерпретируется в XXI веке: 3JAN30 — это 2030 год, а не 1930")]
	public void TryIfTwoDigitYearMapsToTwentyFirstCentury()
	{
		// Arrange — Act: у экспираций опционов не бывает лет столетия 1900-х.
		var parsed = OptionSymbolParser.TryParse("BTC-3JAN30-100-C", out var parts);

		// Assert
		Assert.That(parsed, Is.True);
		Assert.That(parts!.ExpiryDate, Is.EqualTo(new DateTime(2030, 1, 3)));
	}

	[TestMethod]
	[DataRow("BTCUSDT")]
	[DataRow("BTC-2800-C")]
	[DataRow("BTC-27DEC24-2800")]
	// Хвостовой сегмент обязан быть котируемой валютой опционов Bybit: USDT или USDC.
	[DataRow("BTC-27DEC24-2800-C-EXTRA")]
	[DataRow("BTC-27DEC24-2800-C-USD")]
	[DataRow("BTC-3XZY26-2800-C")]
	[DataRow("BTC-27DEC24-28.00.0-C")]
	[DataRow("BTC-27DEC24-abc-C")]
	[DataRow("BTC-27DEC24-2800-X")]
	[DataRow("BTC--2800-C")]
	[DataRow("")]
	[DataRow(" ")]
	[Description("Некорректные символы опционов не разбираются, TryParse возвращает false без исключений")]
	public void TryIfTryParseReturnsFalseOnMalformedSymbol(string symbol)
	{
		// Arrange — Act: мягкий вариант разбора сигнализирует неудачу false, а не исключением.
		var parsed = OptionSymbolParser.TryParse(symbol, out var parts);

		// Assert
		Assert.That(parsed, Is.False);
		Assert.That(parts, Is.Null);
	}

	[TestMethod]
	[Description("TryParse для null-символа возвращает false без исключений")]
	public void TryIfTryParseReturnsFalseOnNullSymbol()
	{
		// Arrange — Act
		var parsed = OptionSymbolParser.TryParse(null, out var parts);

		// Assert
		Assert.That(parsed, Is.False);
		Assert.That(parts, Is.Null);
	}

	[TestMethod]
	[Description("Parse возвращает разобранные части для корректного символа")]
	public void TryIfParseReturnsPartsForValidSymbol()
	{
		// Arrange — Act: строгий вариант разбора для мест, где символ обязан быть опционом.
		var parts = OptionSymbolParser.Parse("ETH-3JAN23-1250-P");

		// Assert
		Assert.That(parts.BaseCoin, Is.EqualTo("ETH"));
		Assert.That(parts.ExpiryDate, Is.EqualTo(new DateTime(2023, 1, 3)));
		Assert.That(parts.Strike, Is.EqualTo(1250d));
		Assert.That(parts.Type, Is.EqualTo(OptionType.Put));
	}

	[TestMethod]
	[DataRow("BTCUSDT")]
	[DataRow("BTC-27DEC24-2800")]
	[DataRow("BTC-27DEC24-2800-X")]
	[DataRow("")]
	[Description("Parse выбрасывает FormatException для символа вне формата опциона")]
	[ExpectedException(typeof(FormatException))]
	public void ThrowOnParseMalformedOptionSymbol(string symbol)
	{
		// Arrange — Act — Assert: строгий вариант разбора сигнализирует FormatException.
		OptionSymbolParser.Parse(symbol);
	}
}
