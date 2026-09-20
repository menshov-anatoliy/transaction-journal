namespace TransactionJournal.Analytics;

/// <summary>
/// Результат оценки нереализованного PnL по маркам на момент запроса: метрики
/// позиций с подставленными маркой и нереализованной оценкой открытых остатков,
/// отметка времени марок и признак сбоя провайдера. Оценка живёт одно чтение —
/// это производные величины, хранением они не живут и пересчитываются при
/// каждом запросе.
// Traceability: openspec:analytics/performance#requirement-unrealized-pnl-current-marks
/// </summary>
public sealed record UnrealizedPnlEvaluation
{
	/// <summary>Метрики позиций после оценки: открытые остатки несут марку и нереализованный PnL либо null при сбое марок.</summary>
	public required IReadOnlyList<PositionMetrics> Positions { get; init; }

	/// <summary>
	/// Отметка времени марок (marks_as_of) — момент получения марок оценки:
	/// старейшая из марок, по которым оценена нереализованная часть. null при
	/// сбое марок или когда открытым остаткам марки были не нужны.
	// Traceability: openspec:analytics/performance#scenario-open-residual-valued-at-request
	/// </summary>
	public required DateTimeOffset? MarksAsOf { get; init; }

	/// <summary>
	/// Признак сбоя марок: хотя бы один открытый остаток остался без оценки.
	/// Нереализованная часть и отметка времени марок деградируют в null только
	/// вместе с этим признаком, реализованные метрики не затрагиваются.
	// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
	/// </summary>
	public required bool HasMarkFailure { get; init; }
}
