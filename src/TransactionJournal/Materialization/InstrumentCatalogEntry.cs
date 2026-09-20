namespace TransactionJournal.Materialization;

/// <summary>
/// Запись справочника инструментов журнала, построенного из сырых спецификаций
/// эндпоинта instruments-info: канонические категория, базовый актив, тип опциона
/// и время delivery. Каноническим значениям журнал доверяет больше, чем разбору
/// строки символа.
// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
public sealed record InstrumentCatalogEntry
{
	/// <summary>Символ инструмента — ключ справочника, например BTC-27DEC24-2800-C или BTCUSDT.</summary>
	public required string Symbol { get; init; }

	/// <summary>Торговая категория инструмента: linear или option.</summary>
	public required string Category { get; init; }

	/// <summary>Канонический базовый актив из спецификации биржи; null, если биржа не указала.</summary>
	public string? BaseCoin { get; init; }

	/// <summary>Канонический тип опциона из спецификации; null у линейных инструментов.</summary>
	public OptionType? OptionsType { get; init; }

	/// <summary>
	/// Каноническое время delivery из спецификации (у опционов — 08:00 UTC даты экспирации);
	/// null, если доставки нет (бессрочный контракт, биржа передала 0).
	/// </summary>
	public DateTimeOffset? DeliveryTime { get; init; }
}
