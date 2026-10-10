using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.Materialization;

/// <summary>
/// Проверки разбора символа вечного линейного фьючерса Bybit формата {BASE}{QUOTE}
/// с котируемой валютой USDT/USDC на реальных символах живой доски и на формах,
/// которые линейным перпом не являются.
/// </summary>
[TestClass]
public class LinearSymbolParserTests
{
	[TestMethod]
	[DataRow("XAUTUSDT", "XAUT", "USDT")]
	[DataRow("ETHUSDC", "ETH", "USDC")]
	[DataRow("BTCUSDT", "BTC", "USDT")]
	[Description("Символы вечных линейных фьючерсов разбираются на базовый актив и котируемую валюту")]
	// Линейная нога распознаётся по форме символа {BASE}{QUOTE}: без этого хедж-перп
	// деградировал бы в unavailable вместе со всей конструкцией реального риска.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	public void TryIfParsesPerpetualLinearSymbols(string symbol, string expectedBaseCoin, string expectedQuoteCoin)
	{
		// Arrange: XAUTUSDT — символ хеджа живого кейса «XAUT стреддл 30OCT26 4400»,
		// ETHUSDC — историческая USDC-доска.
		// Act
		var parsed = LinearSymbolParser.TryParse(symbol, out var parts);

		// Assert: база и валюта выделены в исходном регистре.
		Assert.That(parsed, Is.True);
		Assert.That(parts, Is.Not.Null);
		Assert.That(parts!.BaseCoin, Is.EqualTo(expectedBaseCoin));
		Assert.That(parts.QuoteCoin, Is.EqualTo(expectedQuoteCoin));
	}

	[TestMethod]
	[DataRow("BtcUSDT", "Btc", "USDT")]
	[Description("Разбор сохраняет регистр и значения base/quote без нормализации")]
	// Значения возвращаются ровно как в строке символа: парсер не канонизирует регистр.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	public void TryIfParsePreservesSymbolCase(string symbol, string expectedBaseCoin, string expectedQuoteCoin)
	{
		// Arrange — Act
		var parsed = LinearSymbolParser.TryParse(symbol, out var parts);

		// Assert: регистр базы сохранён, валюта — точное совпадение хвоста.
		Assert.That(parsed, Is.True);
		Assert.That(parts!.BaseCoin, Is.EqualTo(expectedBaseCoin));
		Assert.That(parts.QuoteCoin, Is.EqualTo(expectedQuoteCoin));
	}

	[TestMethod]
	[DataRow("XAUT-30OCT26-4400-C-USDT")]
	[DataRow("BTC-27DEC24-2800-C")]
	[Description("Опционные символы не распознаются как линейный перп")]
	// Опцион обязан оставаться вне линейной категории: иначе калькулятор примет его
	// за перп и исказит классификацию ног.
	// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
	public void TryIfOptionSymbolsAreNotRecognizedAsLinear(string symbol)
	{
		// Arrange — Act
		var parsed = LinearSymbolParser.TryParse(symbol, out var parts);

		// Assert
		Assert.That(parsed, Is.False);
		Assert.That(parts, Is.Null);
	}

	[TestMethod]
	[DataRow("BTCUSDT-27DEC24")]
	[DataRow("BTCUSDT-250926")]
	[Description("Датированные формы {BASE}{QUOTE}-{экспирация} не распознаются как вечный перп")]
	// Датированные линейные фьючерсы — отдельное будущее изменение: сейчас они
	// вне формата и не должны попадать в линейные ноги.
	// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
	public void TryIfDatedFutureFormsAreNotRecognizedAsLinear(string symbol)
	{
		// Arrange — Act
		var parsed = LinearSymbolParser.TryParse(symbol, out var parts);

		// Assert
		Assert.That(parsed, Is.False);
		Assert.That(parts, Is.Null);
	}

	[TestMethod]
	[DataRow("BTCUSD")]
	[DataRow("BTCUSDTT")]
	[DataRow("USDT")]
	[DataRow("USDC")]
	[DataRow("btcusdt")]
	[DataRow("BTC USDT")]
	[DataRow("BTC-USDT")]
	[DataRow("")]
	[DataRow(" ")]
	[Description("Мусорные и граничные символы вне формата перпа не разбираются, TryParse возвращает false без исключений")]
	// Неразобранный символ сохраняет прежний статус unavailable: мягкий разбор
	// сигнализирует неудачу false, а не исключением.
	// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
	public void TryIfTryParseReturnsFalseOnMalformedLinearSymbol(string symbol)
	{
		// Arrange — Act
		var parsed = LinearSymbolParser.TryParse(symbol, out var parts);

		// Assert
		Assert.That(parsed, Is.False);
		Assert.That(parts, Is.Null);
	}

	[TestMethod]
	[Description("TryParse для null-символа возвращает false без исключений")]
	public void TryIfTryParseReturnsFalseOnNullSymbol()
	{
		// Arrange — Act
		var parsed = LinearSymbolParser.TryParse(null, out var parts);

		// Assert
		Assert.That(parsed, Is.False);
		Assert.That(parts, Is.Null);
	}

	[TestMethod]
	[Description("Parse возвращает разобранные части для корректного символа")]
	public void TryIfParseReturnsPartsForValidLinearSymbol()
	{
		// Arrange — Act: строгий вариант разбора для мест, где символ обязан быть перпом.
		var parts = LinearSymbolParser.Parse("ETHUSDC");

		// Assert
		Assert.That(parts.BaseCoin, Is.EqualTo("ETH"));
		Assert.That(parts.QuoteCoin, Is.EqualTo("USDC"));
	}

	[TestMethod]
	[DataRow("BTCUSD")]
	[DataRow("XAUT-30OCT26-4400-C-USDT")]
	[DataRow("")]
	[Description("Parse выбрасывает FormatException для символа вне формата вечного перпа")]
	[ExpectedException(typeof(FormatException))]
	public void ThrowOnParseMalformedLinearSymbol(string symbol)
	{
		// Arrange — Act — Assert: строгий вариант разбора сигнализирует FormatException.
		LinearSymbolParser.Parse(symbol);
	}
}
