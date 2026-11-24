namespace TransactionJournal.Application.Analytics;

/// <summary>Состояние реального риска конструкции.</summary>
public enum RealRiskStatus
{
	/// <summary>
	/// Риск конечен: доступен итог конструкции, и величина известна —
	/// минусовая часть итога, включая ноль при неотрицательном итоге.
	// Минусовая часть итога вместо худшего исхода на экспирацию —
	// решение зафиксировано ADR-0025 и issue-спекой реального риска.
	// Traceability: adr:docs/adr/0025-real-risk-negative-part-of-current-result.md#consequences
	/// </summary>
	Finite,

	/// <summary>
	/// Риск не рассчитан: итог конструкции недоступен — сбой котировок или
	/// неоценённый открытый остаток обнулили нереализованную часть; число
	/// не выдаётся, частичный расчёт по доступной части запрещён.
	// Недоступность целиком вместо частичного расчёта — метрика следует
	// доступности итога конструкции (ADR-0025).
	// Traceability: adr:docs/adr/0025-real-risk-negative-part-of-current-result.md#consequences
	/// </summary>
	Unavailable,
}
