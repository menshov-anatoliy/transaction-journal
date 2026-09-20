namespace TransactionJournal.Materialization;

/// <summary>
/// Результат материализации экспираций: закрывающие записи delivery/OTM по конструкциям
/// и предупреждения сверки с биржевым deliveryRpl. Обе коллекции производны от сырых
/// записей и текущих привязок сделок, поэтому повторная материализация детерминирована.
// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
/// </summary>
public sealed record ExpiryMaterializationResult
{
	/// <summary>Закрывающие записи экспираций, упорядоченные по моменту закрытия, символу и конструкции.</summary>
	public required IReadOnlyList<ExpiryClosingEntry> ClosingEntries { get; init; }

	/// <summary>Предупреждения сверки с deliveryRpl, упорядоченные по моменту доставки и символу.</summary>
	public required IReadOnlyList<ExpiryReconciliationWarning> Warnings { get; init; }
}
