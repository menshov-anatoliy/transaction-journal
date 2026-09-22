namespace TransactionJournal.Materialization;

/// <summary>
/// Результат материализации сделок «Входящих»: сделки, выведенные из разрешимых
/// записей исполнения, и перечень неразрешённых символов — инструментов без
/// спецификации в справочнике, чьи записи отложены до появления спецификации.
/// Traceability: openspec:sync/bybit-history#requirement-unresolved-instrument-degradation
/// </summary>
public sealed record TradeMaterializationResult
{
	/// <summary>Сделки «Входящих», упорядоченные по времени исполнения, затем по execId.</summary>
	public required IReadOnlyList<MaterializedTrade> Trades { get; init; }

	/// <summary>
	/// Символы опционов, отсутствующие в справочнике инструментов, в стабильном
	/// порядке без повторов; записи по ним не материализованы.
	/// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbol-degrades-to-warning
	/// </summary>
	public required IReadOnlyList<string> UnresolvedSymbols { get; init; }
}
