namespace TransactionJournal.Application.Sync;

/// <summary>
/// Итог пополнения справочника инструментов: число фактически вставленных
/// спецификаций и перечень символов, чьи спецификации биржа отвергла отказом
/// «контракт недоступен для торговли» — факт запуска, а не состояние:
/// повторный запуск заново пробует запросить пропущенные спецификации.
/// Traceability: openspec:sync/bybit-history#scenario-unavailable-instrument-spec-skipped
/// </summary>
public sealed record InstrumentSyncResult
{
	/// <summary>Число фактически вставленных спецификаций.</summary>
	public required int InsertedCount { get; init; }

	/// <summary>
	/// Символы, чьи спецификации не получены из-за отказа биржи 110023, в стабильном
	/// порядке без повторов; пуст, когда все запрошенные спецификации получены.
	/// </summary>
	public required IReadOnlyList<string> UnresolvedSymbols { get; init; }
}
