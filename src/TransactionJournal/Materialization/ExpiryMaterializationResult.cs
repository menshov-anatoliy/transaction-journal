namespace TransactionJournal.Materialization;

/// <summary>
/// Результат материализации экспираций: закрывающие записи delivery/OTM по конструкциям,
/// предупреждения сверки с биржевым deliveryRpl и перечень неразрешённых символов
/// delivery-записей без спецификации в справочнике. Обе коллекции записей производны
/// от сырых записей и текущих привязок сделок, поэтому повторная материализация
/// детерминирована.
// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
/// </summary>
public sealed record ExpiryMaterializationResult
{
	/// <summary>Закрывающие записи экспираций, упорядоченные по моменту закрытия, символу и конструкции.</summary>
	public required IReadOnlyList<ExpiryClosingEntry> ClosingEntries { get; init; }

	/// <summary>Предупреждения сверки с deliveryRpl, упорядоченные по моменту доставки и символу.</summary>
	public required IReadOnlyList<ExpiryReconciliationWarning> Warnings { get; init; }

	/// <summary>
	/// Символы опционов delivery-записей, отсутствующие в справочнике инструментов,
	/// в стабильном порядке без повторов: закрывающие записи и OTM-автозакрытие
	/// по ним не строятся до появления спецификации.
	/// Traceability: openspec:sync/bybit-history#requirement-unresolved-instrument-degradation
	/// </summary>
	public IReadOnlyList<string> UnresolvedSymbols { get; init; } = [];
}
