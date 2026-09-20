namespace TransactionJournal.Materialization;

/// <summary>
/// Полный результат переразбора доменных представлений журнала из сырых записей:
/// сделки «Входящих», закрывающие записи экспираций и предупреждения сверки.
/// Все части выводятся только из локального сырья, поэтому пересборка после изменения
/// правила разбора заменяет проекцию целиком и не оставляет следов прежних правил.
// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public sealed record JournalMaterializationResult
{
	/// <summary>Сделки «Входящих», упорядоченные по времени исполнения, затем по execId.</summary>
	public required IReadOnlyList<MaterializedTrade> InboxTrades { get; init; }

	/// <summary>Закрывающие записи экспираций по конструкциям и «Входящим», производны от привязок сделок.</summary>
	public required IReadOnlyList<ExpiryClosingEntry> ExpiryClosingEntries { get; init; }

	/// <summary>Предупреждения сверки собственного расчёта с биржевым deliveryRpl.</summary>
	public required IReadOnlyList<ExpiryReconciliationWarning> ReconciliationWarnings { get; init; }
}
