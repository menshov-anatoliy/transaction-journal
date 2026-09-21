namespace TransactionJournal.Materialization;

/// <summary>
/// Сверяет символы опционов со справочником инструментов: разбирает символ
/// {BASE}-{dMMMyy}-{strike}-{C|P}[-{QUOTE}], находит спецификацию в справочнике и проверяет,
/// что базовый актив, тип опциона и дата экспирации совпадают с каноническими
/// значениями биржи. Канонические свойства результата берутся из справочника,
/// а не из строки символа.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: change:add-bybit-sync/design#d7
/// </summary>
public sealed class InstrumentResolver
{
	/// <summary>Категория опционов Bybit — единственная, для которой имеет смысл сверка опциона.</summary>
	public const string OptionsCategory = "option";

	private readonly InstrumentCatalog _catalog;

	/// <summary>Создаёт сверщик над готовым справочником инструментов.</summary>
	/// <param name="catalog">Справочник инструментов, построенный из сырых записей.</param>
	/// <exception cref="ArgumentNullException">Справочник не задан.</exception>
	public InstrumentResolver(InstrumentCatalog catalog)
	{
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
	}

	/// <summary>
	/// Разбирает символ опциона и сверяет его со справочником: несовпадение базового
	/// актива, типа опциона или даты экспирации с канонической спецификацией биржи — ошибка.
	/// </summary>
	/// <param name="symbol">Символ опциона, например BTC-27DEC24-2800-C или XAUT-30OCT26-4400-C-USDT.</param>
	/// <exception cref="ArgumentException">Символ не задан.</exception>
	/// <exception cref="InstrumentResolveException">Символ не разобран, неизвестен справочнику или расходится с ним.</exception>
	public ResolvedOptionInstrument ResolveOption(string symbol)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		if (OptionSymbolParser.TryParse(symbol, out var parts) == false)
		{
			throw new InstrumentResolveException(
				InstrumentResolveFailureReason.MalformedOptionSymbol,
				symbol,
				$"Символ «{symbol}» не соответствует формату опциона Bybit {{BASE}}-{{dMMMyy}}-{{strike}}-{{C|P}}[-{{QUOTE}}].");
		}

		if (_catalog.TryGet(symbol, out var entry) == false)
		{
			// Неизвестный инструмент должен пополнять справочник загрузкой спецификации
			// из биржи до материализации сделок — сама сверка здесь останавливается.
			// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
			throw new InstrumentResolveException(
				InstrumentResolveFailureReason.UnknownSymbol,
				symbol,
				$"Инструмент «{symbol}» отсутствует в справочнике; загрузите его спецификацию из биржи.");
		}

		if (string.Equals(entry.Category, OptionsCategory, StringComparison.Ordinal) == false)
		{
			throw new InstrumentResolveException(
				InstrumentResolveFailureReason.CategoryMismatch,
				symbol,
				$"Инструмент «{symbol}» в справочнике принадлежит категории «{entry.Category}», а не option.");
		}

		if (entry.BaseCoin is null || string.Equals(entry.BaseCoin, parts!.BaseCoin, StringComparison.Ordinal) == false)
		{
			throw new InstrumentResolveException(
				InstrumentResolveFailureReason.BaseCoinMismatch,
				symbol,
				$"Базовый актив символа «{symbol}» ({parts!.BaseCoin}) расходится со справочником ({entry.BaseCoin ?? "не указан"}).");
		}

		if (entry.OptionsType is null || entry.OptionsType.Value != parts.Type)
		{
			throw new InstrumentResolveException(
				InstrumentResolveFailureReason.OptionsTypeMismatch,
				symbol,
				$"Тип опциона символа «{symbol}» ({parts.Type}) расходится со справочником ({entry.OptionsType?.ToString() ?? "не указан"}).");
		}

		// Символ кодирует только дату экспирации, справочник — полное время delivery
		// (08:00 UTC), поэтому сверяются именно даты, без времени суток.
		if (entry.DeliveryTime is null || entry.DeliveryTime.Value.UtcDateTime.Date != parts.ExpiryDate)
		{
			throw new InstrumentResolveException(
				InstrumentResolveFailureReason.DeliveryTimeMismatch,
				symbol,
				$"Дата экспирации символа «{symbol}» ({parts.ExpiryDate:yyyy-MM-dd}) расходится со временем delivery справочника ({entry.DeliveryTime?.UtcDateTime.ToString("yyyy-MM-dd") ?? "не указано"}).");
		}

		// Канонические значения берутся из справочника: строке символа журнал не доверяет.
		// Traceability: change:add-bybit-sync/design#d7
		return new ResolvedOptionInstrument
		{
			Symbol = symbol,
			Category = entry.Category,
			BaseCoin = entry.BaseCoin,
			OptionsType = entry.OptionsType.Value,
			Strike = parts.Strike,
			DeliveryTime = entry.DeliveryTime.Value,
		};
	}
}
