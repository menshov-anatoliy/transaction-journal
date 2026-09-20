namespace TransactionJournal.Domain;

/// <summary>
/// Результат чтения позиций: снапшоты остатков по «конструкция × инструмент»,
/// единый поток закрывающих записей и предупреждения об избыточных записях.
/// Всё содержимое производно — пересчитывается из сделок, записей синхронизации
/// и пользовательских пометок при каждом чтении.
// Traceability: openspec:domain/constructions#requirement-position-derived-residual
/// </summary>
public sealed record PositionReadResult
{
	/// <summary>Позиции, упорядоченные по конструкции и инструменту.</summary>
	public required IReadOnlyList<PositionSnapshot> Positions { get; init; }

	/// <summary>Единый поток применённых закрывающих записей в хронологическом порядке.</summary>
	public required IReadOnlyList<PositionClosingEntry> ClosingEntries { get; init; }

	/// <summary>Предупреждения об избыточных закрывающих записях, упорядоченные по времени.</summary>
	public required IReadOnlyList<RedundantClosingEntryWarning> Warnings { get; init; }
}
