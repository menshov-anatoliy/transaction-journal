namespace TransactionJournal.Hints.Ports;

/// <summary>
/// Часы окружения агента: единый источник отметки времени прохода. Все записи
/// подсказок и решения движка штампуются этой отметкой — as-of прохода, а не
/// моментом обращения к хранилищу, чтобы история подсказок описывала момент
/// генерации детерминированно.
// Traceability: openspec:hints/engine-pass#requirement-engine-self-describing-record
/// </summary>
public interface IClock
{
	/// <summary>Текущий момент времени в UTC.</summary>
	DateTimeOffset UtcNow { get; }
}
