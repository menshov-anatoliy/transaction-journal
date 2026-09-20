namespace TransactionJournal.Analytics;

/// <summary>
/// Результат чтения метрик журнала: метрики всех конструкций и их позиций,
/// вычисленные из текущих данных, — и журнал-уровневый итог. Итог равен сумме
/// итогов конструкций и становится null, когда сбой марок обнуляет итог хотя
/// бы одной из них: неполный итог помечается, а не подменяется частичной
/// суммой. Всё содержимое производно — пересчитывается при каждом чтении,
/// хранением не живёт.
// Traceability: openspec:analytics/performance#requirement-analytics-computed-on-read
/// </summary>
public sealed record JournalMetrics
{
	/// <summary>Метрики конструкций, упорядоченные по идентификатору.</summary>
	public required IReadOnlyList<ConstructionMetrics> Constructions { get; init; }

	/// <summary>Метрики позиций всех конструкций, упорядоченные по конструкции и инструменту.</summary>
	public required IReadOnlyList<PositionMetrics> Positions { get; init; }

	/// <summary>
	/// Итог по журналу — сумма итогов конструкций; null, пока сбой марок оставил
	/// без нереализованной оценки хотя бы одну конструкцию (итог неполный).
	// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
	/// </summary>
	public required decimal? TotalPnL { get; init; }

	/// <summary>
	/// Отметка времени марок оценки — старейшая из использованных марок; null при
	/// сбое марок или когда открытым остаткам марки были не нужны.
	// Traceability: openspec:analytics/performance#scenario-open-residual-valued-at-request
	/// </summary>
	public required DateTimeOffset? MarksAsOf { get; init; }

	/// <summary>
	/// Признак сбоя марок: хотя бы один открытый остаток остался без оценки;
	/// деградирует только нереализованная часть и зависящий от неё итог.
	// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
	/// </summary>
	public required bool HasMarkFailure { get; init; }
}
