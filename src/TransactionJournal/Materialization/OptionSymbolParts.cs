namespace TransactionJournal.Materialization;

/// <summary>
/// Разобранные из символа опциона Bybit части: базовый актив, дата экспирации,
/// страйк и тип. Разобранные части — только кандидат: канонические свойства опциона
/// журнал берёт из справочника инструментов, а не из строки символа.
// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
public sealed record OptionSymbolParts
{
	/// <summary>Базовый актив из первого сегмента символа, например BTC или ETH.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Дата экспирации из сегмента dMMMyy; только дата, без времени доставки.</summary>
	public required DateTime ExpiryDate { get; init; }

	/// <summary>Страйк из символа в инвариантном десятичном формате.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Тип опциона из последнего сегмента: C — Call, P — Put.</summary>
	public required OptionType Type { get; init; }
}
