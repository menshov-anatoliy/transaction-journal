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
/// `unavailable` — недостаток исходных данных, а не бесконечность.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: change:show-unbounded-finresult-risk/design#d1
/// </summary>
public sealed record RealRiskResult
{
	private RealRiskResult(RealRiskStatus status, decimal? usdt)
	{
		Status = status;
		Usdt = usdt;
	}

	/// <summary>Состояние риска: конечный, неограниченный или нерассчитанный.</summary>
	public RealRiskStatus Status { get; }

	/// <summary>Реальный риск в USDT; число есть только при конечном риске, в остальных состояниях null.</summary>
	public decimal? Usdt { get; }

	/// <summary>Конечный риск с числовой величиной.</summary>
	public static RealRiskResult Finite(decimal usdt) => new(RealRiskStatus.Finite, usdt);

	/// <summary>Неограниченный риск: числовая величина не определена.</summary>
	public static RealRiskResult Unbounded() => new(RealRiskStatus.Unbounded, null);

	/// <summary>Риск не рассчитан из-за неполных исходных данных: числовая величина не определена.</summary>
	public static RealRiskResult Unavailable() => new(RealRiskStatus.Unavailable, null);
}
