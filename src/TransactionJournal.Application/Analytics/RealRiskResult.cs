namespace TransactionJournal.Application.Analytics;

/// <summary>Состояние реального риска конструкции.</summary>
public enum RealRiskStatus
{
	/// <summary>Риск конечен: числовая величина известна, включая ноль без открытых остатков.</summary>
	Finite,

	/// <summary>Риск не ограничен: нетто-короткая позиция по коллам даёт неограниченный хвост убытка.</summary>
	Unbounded,

	/// <summary>Риск не рассчитан: исходные данные открытых остатков неполны.</summary>
	Unavailable,
}

/// <summary>
/// Типизированный результат расчёта реального риска: состояние конечного,
/// неограниченного или нерассчитанного риска и числовая величина только для
/// конечного риска. Отсутствие числа перестаёт быть двусмысленным: состояние
/// `unbounded` означает финансовую бесконечность худшего случая, состояние
/// `unavailable` — недостаток исходных данных, а не бесконечность. Рядом с
/// клэмпированным риском результат несёт неклэмпированный худший платёж
/// открытых остатков — общий источник и риска, и гарантированной прибыли
/// остатков, входящей в худший исход конструкции.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
// Traceability: change:show-unbounded-finresult-risk/design#d1
/// </summary>
public sealed record RealRiskResult
{
	private RealRiskResult(RealRiskStatus status, decimal? usdt, decimal? worstPayoffUsdt)
	{
		Status = status;
		Usdt = usdt;
		WorstPayoffUsdt = worstPayoffUsdt;
	}

	/// <summary>Состояние риска: конечный, неограниченный или нерассчитанный.</summary>
	public RealRiskStatus Status { get; }

	/// <summary>Реальный риск в USDT; число есть только при конечном риске, в остальных состояниях null.</summary>
	public decimal? Usdt { get; }

	/// <summary>
	/// Худший платёж открытых остатков до клэмпа нулём — тот же суммарный
	/// минимум платежа групп, из которого выводится реальный риск. Число есть
	/// только при конечном риске; без открытых остатков платёж нулевой,
	/// а в состояниях `unbounded` и `unavailable` величина отсутствует вместе
	/// с риском. Положительный минимум означает гарантированную прибыль
	/// открытых остатков и участника худшего исхода конструкции.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	/// </summary>
	public decimal? WorstPayoffUsdt { get; }

	/// <summary>
	/// Конечный риск с числовой величиной и неклэмпированным худшим платежом
	/// остатков: риск клэмпится нулём, платёж сохраняет знак.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	/// </summary>
	public static RealRiskResult Finite(decimal usdt, decimal worstPayoffUsdt) => new(RealRiskStatus.Finite, usdt, worstPayoffUsdt);

	/// <summary>Неограниченный риск: числовая величина не определена.</summary>
	public static RealRiskResult Unbounded() => new(RealRiskStatus.Unbounded, null, null);

	/// <summary>Риск не рассчитан из-за неполных исходных данных: числовая величина не определена.</summary>
	public static RealRiskResult Unavailable() => new(RealRiskStatus.Unavailable, null, null);
}
