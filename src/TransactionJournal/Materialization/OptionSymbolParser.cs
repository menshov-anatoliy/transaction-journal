using System.Globalization;

namespace TransactionJournal.Materialization;

/// <summary>
/// Разбор символа опциона Bybit формата {BASE}-{dMMMyy}-{strike}-{C|P} инвариантной
/// культурой: день без ведущего нуля, три английские буквы месяца, две цифры года,
/// страйк инвариантным десятичным разделителем. Канонические свойства опциона
/// (тип, базовый актив, deliveryTime) определяет справочник инструментов, поэтому
/// строка символа здесь только разбирается, а сверкой занимается InstrumentResolver.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: change:add-bybit-sync/design#d7
/// Traceability: doc:docs/research/bybit-api.md#4-форматы-символов
/// </summary>
public static class OptionSymbolParser
{
	private static readonly CultureInfo SymbolCulture = CreateSymbolCulture();

	/// <summary>
	/// Разбирает символ опциона; для строк вне формата возвращает false без исключений.
	/// </summary>
	/// <param name="symbol">Символ опциона, например BTC-27DEC24-2800-C.</param>
	/// <param name="parts">Разобранные части символа или null при неудаче.</param>
	public static bool TryParse(string? symbol, out OptionSymbolParts? parts)
	{
		parts = null;
		if (string.IsNullOrWhiteSpace(symbol))
		{
			return false;
		}

		var segments = symbol.Split('-');
		if (segments.Length != 4)
		{
			return false;
		}

		var baseCoin = segments[0];
		var dateSegment = segments[1];
		var strikeSegment = segments[2];
		var typeSegment = segments[3];

		// Биржа пишет месяц заглавными буквами (27DEC24); инвариантная культура сравнивает
		// имена месяцев без учёта регистра, поэтому формат dMMMyy подходит и для верхнего регистра.
		if (baseCoin.Length == 0
			|| DateTime.TryParseExact(dateSegment, "dMMMyy", SymbolCulture, DateTimeStyles.None, out var expiryDate) == false
			|| decimal.TryParse(strikeSegment, NumberStyles.Number, CultureInfo.InvariantCulture, out var strike) == false)
		{
			return false;
		}

		// Последний сегмент — ровно одна буква: C или P.
		var type = typeSegment switch
		{
			"C" => OptionType.Call,
			"P" => OptionType.Put,
			_ => (OptionType?)null,
		};
		if (type is null)
		{
			return false;
		}

		parts = new OptionSymbolParts
		{
			BaseCoin = baseCoin,
			ExpiryDate = expiryDate,
			Strike = strike,
			Type = type.Value,
		};
		return true;
	}

	/// <summary>
	/// Разбирает символ опциона или выбрасывает исключение формата — строгий вариант
	/// для мест, где символ обязан быть опционом.
	/// </summary>
	/// <param name="symbol">Символ опциона, например BTC-27DEC24-2800-C.</param>
	/// <exception cref="FormatException">Символ не соответствует формату символа опциона Bybit.</exception>
	public static OptionSymbolParts Parse(string symbol)
	{
		if (TryParse(symbol, out var parts) == false)
		{
			throw new FormatException(
				$"Символ «{symbol}» не соответствует формату символа опциона Bybit {{BASE}}-{{dMMMyy}}-{{strike}}-{{C|P}}.");
		}

		return parts!;
	}

	#region Вспомогательные методы

	/// <summary>
	/// Клон инвариантной культуры с расширенным окном двузначных лет: по умолчанию .NET
	/// относит годы от 30 к столетию 1900-х, а экспирации опционов всегда лежат в 2000-х.
	/// </summary>
	private static CultureInfo CreateSymbolCulture()
	{
		var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
		culture.Calendar.TwoDigitYearMax = 2099;
		return culture;
	}

	#endregion
}
