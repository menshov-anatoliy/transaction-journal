namespace TransactionJournal.Analytics;

/// <summary>
/// Метрики конструкции: агрегаты результата — сумма метрик её позиций и внешних
/// корректировок PnL, — процентные величины от текущего выделенного капитала и
/// производные временные характеристики. Метрики вычисляются при чтении из
/// текущего набора записей и хранением не живут: правка привязок, корректировок
/// или капитала отражается очередным чтением без следов прежнего расчёта.
// Traceability: openspec:analytics/performance#requirement-construction-metrics
// Traceability: change:add-analytics/design#d4
/// </summary>
public sealed record ConstructionMetrics
{
	/// <summary>Конструкция, к которой относятся метрики.</summary>
	public required long ConstructionId { get; init; }

	/// <summary>Текущий выделенный капитал конструкции в USDT — база процентных величин.</summary>
	public required decimal AllocatedCapitalUsdt { get; init; }

	/// <summary>Реализованный PnL — сумма реализованных PnL позиций конструкции.</summary>
	public required decimal RealizedPnL { get; init; }

	/// <summary>
	/// Нереализованный PnL — сумма нереализованных оценок позиций; null, пока
	/// хотя бы одна открытая позиция не оценена марками: недоступная оценка
	/// обнуляет только нереализованную часть, не затрагивая остальные метрики.
	// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
	/// </summary>
	public required decimal? UnrealizedPnL { get; init; }

	/// <summary>Сумма внешних корректировок PnL конструкции.</summary>
	public required decimal AdjustmentsPnL { get; init; }

	/// <summary>
	/// Итог конструкции — реализованный и нереализованный PnL позиций плюс внешние
	/// корректировки; null, пока нереализованная оценка недоступна.
	// Traceability: openspec:analytics/performance#scenario-construction-total-includes-adjustments
	/// </summary>
	public required decimal? TotalPnL { get; init; }

	/// <summary>Реализованный PnL в процентах от текущего выделенного капитала; null при нулевом капитале.</summary>
	public required decimal? RealizedPnLPercent { get; init; }

	/// <summary>Нереализованный PnL в процентах от текущего выделенного капитала; null при нулевом капитале или недоступной оценке.</summary>
	public required decimal? UnrealizedPnLPercent { get; init; }

	/// <summary>Сумма корректировок в процентах от текущего выделенного капитала; null при нулевом капитале.</summary>
	public required decimal? AdjustmentsPnLPercent { get; init; }

	/// <summary>Итог в процентах от текущего выделенного капитала; null при нулевом капитале или недоступном итоге.</summary>
	public required decimal? TotalPnLPercent { get; init; }

	/// <summary>Дата открытия — время первой сделки конструкции; null, пока сделок нет.</summary>
	public required DateTimeOffset? OpenedAt { get; init; }

	/// <summary>Дата закрытия — момент обнуления последней позиции; null, пока конструкция открыта или пока нет сделок.</summary>
	public required DateTimeOffset? ClosedAt { get; init; }

	/// <summary>
	/// Длительность конструкции: между открытием и закрытием, а для открытой —
	/// от первой сделки до текущего момента; null без сделок.
	// Traceability: openspec:analytics/performance#scenario-open-construction-duration-to-now
	/// </summary>
	public required TimeSpan? Duration { get; init; }
}
