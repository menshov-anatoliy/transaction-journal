namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Метрики времени жизни открытой опционной ноги: внутренняя и временная
/// стоимость на контракт и на чистый остаток, дневной распад, прирост распада
/// к снимку суточной давности и остаток полных суток до экспирации. Все
/// денежные величины — в USDT котируемой доски. Недоступная рыночная величина
/// равна null и контракты показывают её прочерком; остаток суток от рыночных
/// данных не зависит и вычисляется всегда.
// Traceability: openspec:analytics/option-lifetime#requirement-leg-intrinsic-time-value
// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-and-days-to-expiry
// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-acceleration
/// </summary>
public sealed record OptionLegLifetimeMetrics
{
	/// <summary>Символ опционного инструмента ноги.</summary>
	public required string Symbol { get; init; }

	/// <summary>Остаток полных суток до экспирации: floor((deliveryTime − now) / 24 ч) по UTC.</summary>
	public required int DaysToExpiry { get; init; }

	/// <summary>Внутренняя стоимость контракта: max(0, underlying − strike) у колла, зеркально у пута; null без индексной цены.</summary>
	public decimal? IntrinsicValueUsdtPerContract { get; init; }

	/// <summary>Временная стоимость контракта: марка минус внутренняя стоимость; null без индексной цены или марки.</summary>
	public decimal? TimeValueUsdtPerContract { get; init; }

	/// <summary>Внутренняя стоимость ноги — стоимость контракта на чистый остаток со знаком остатка.</summary>
	public decimal? IntrinsicValueUsdt { get; init; }

	/// <summary>Временная стоимость ноги — временная стоимость контракта на чистый остаток со знаком остатка.</summary>
	public decimal? TimeValueUsdt { get; init; }

	/// <summary>Дневной распад ноги: биржевая тета контракта на чистый остаток, USDT/день со знаком остатка; null без теты.</summary>
	public decimal? DailyDecayUsdt { get; init; }

	/// <summary>Распад ноги на момент снимка суточной давности, приведённый к текущему остатку; null без снимка или теты.</summary>
	public decimal? DecayDayAgoUsdt { get; init; }

	/// <summary>Прирост распада: (тета сейчас − тета снимка) × текущий остаток; null без снимка суточной давности.</summary>
	public decimal? DecayAccelerationUsdt { get; init; }
}
