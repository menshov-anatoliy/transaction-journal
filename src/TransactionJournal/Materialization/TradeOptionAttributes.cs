namespace TransactionJournal.Materialization;

/// <summary>
/// Канонические атрибуты опциона сделки, взятые из справочника инструментов
/// после сверки символа: строке символа журнал не доверяет, поэтому тип опциона,
/// базовый актив и время delivery приходят из спецификации биржи, а страйк —
/// из разбора символа, сверённого со справочником.
// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
public sealed record TradeOptionAttributes
{
	/// <summary>Канонический базовый актив из спецификации биржи, например BTC.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Канонический тип опциона из спецификации биржи.</summary>
	public required OptionType OptionsType { get; init; }

	/// <summary>Страйк из символа опциона.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Каноническое время delivery из спецификации биржи (08:00 UTC даты экспирации).</summary>
	public required DateTimeOffset DeliveryTime { get; init; }
}
