namespace TransactionJournal.Materialization;

/// <summary>
/// Инструмент опциона со сверенными свойствами: страйк и дата экспирации взяты из
/// разбора символа, а канонические категория, базовый актив, тип опциона и время
/// delivery — из справочника инструментов, потому что строке символа журнал не доверяет.
// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
public sealed record ResolvedOptionInstrument
{
	/// <summary>Символ опциона, например BTC-27DEC24-2800-C.</summary>
	public required string Symbol { get; init; }

	/// <summary>Торговая категория инструмента: option.</summary>
	public required string Category { get; init; }

	/// <summary>Канонический базовый актив из справочника биржи.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Канонический тип опциона из справочника биржи.</summary>
	public required OptionType OptionsType { get; init; }

	/// <summary>Страйк из символа опциона.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Каноническое время delivery из справочника биржи (08:00 UTC даты экспирации).</summary>
	public required DateTimeOffset DeliveryTime { get; init; }
}
