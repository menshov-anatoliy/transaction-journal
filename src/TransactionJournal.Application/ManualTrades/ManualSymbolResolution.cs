namespace TransactionJournal.Application.ManualTrades;

/// <summary>
/// Итог разрешения символа ручной сделки: категория, по которой символ адресуется
/// справочнику и бирже, и признак свежего пополнения — спецификация символа
/// запрошена у биржи и сохранена в сыром справочнике этим вызовом.
/// Traceability: change:manual-journal-management/specs/sync/bybit-history/spec#scenario-manual-symbol-populates-catalog
/// </summary>
public sealed record ManualSymbolResolution
{
	/// <summary>Разрешённый символ инструмента.</summary>
	public required string Symbol { get; init; }

	/// <summary>Торговая категория символа: option или linear.</summary>
	public required string Category { get; init; }

	/// <summary>
	/// Признак пополнения справочника этим вызовом: true — спецификация запрошена
	/// у биржи и сохранена; false — символ уже был известен справочнику, сеть не вызывалась.
	/// </summary>
	public required bool RegisteredNow { get; init; }
}
