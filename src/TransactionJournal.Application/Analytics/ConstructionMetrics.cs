namespace TransactionJournal.Application.Analytics;

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

	/// <summary>
	/// Текущий выделенный капитал конструкции в USDT — база процентных величин;
	/// null, когда капитал не задан: незаданный капитал базы процентов не образует.
	// Traceability: openspec:analytics/performance#scenario-no-capital-no-percent-metrics
	/// </summary>
	public required decimal? AllocatedCapitalUsdt { get; init; }

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

	/// <summary>
	/// Текущая стоимость конструкции в USDT — сумма стоимостей её открытых
	/// позиций; null без открытых остатков и пока хоть один открытый остаток
	/// не оценен марками: недоступная стоимость обнуляет только её и занятость
	/// капитала, остальные метрики не меняются.
	// Traceability: openspec:analytics/performance#requirement-mark-value-of-position-and-construction
	/// </summary>
	public decimal? MarkValue { get; init; }

	/// <summary>
	/// Реальный риск конструкции в USDT — наихудший результат её открытых
	/// остатков на экспирации: совместный минимум платежа ног в группе одной
	/// экспирации, группы разных экспираций суммируются консервативно. Без
	/// открытых остатков риск нулевой; null — когда худший случай неограничен
	/// (нетто-короткая позиция по коллам группы) или символ открытого остатка
	/// не разобран, — причина отсутствия числа различима статусом
	/// <see cref="RealRiskStatus"/>. Метрика выводится из структуры ног и
	/// средних цен открытых остатков и от текущих марок не зависит.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	// Traceability: change:fix-finresult-indicator-real-risk/design#d1
	/// </summary>
	public decimal? RealRiskUsdt { get; init; }

	/// <summary>
	/// Состояние реального риска: конечный риск (`finite`), неограниченный
	/// хвост убытка (`unbounded`) или неполные исходные данные (`unavailable`).
	/// Статус сопровождает величину без потери: число выдаётся только для
	/// `finite`, для остальных состояний <see cref="RealRiskUsdt"/> отсутствует.
	/// Незаданный статус консервативно читается как «не рассчитан» — по умолчанию
	/// он не может выдавать риск конечным, иначе потребители угадывали бы
	/// конечный риск там, где он не рассчитан.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
	// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	/// </summary>
	public RealRiskStatus RealRiskStatus { get; init; } = RealRiskStatus.Unavailable;

	/// <summary>
	/// Худший исход конструкции в USDT — знаковая величина: реализованный
	/// результат плюс неклэмпированный суммарный минимум платежа групп открытых
	/// остатков. При остаточном риске равен «реализованный минус реальный
	/// риск», при гарантированной прибыли остатков превышает реализованный
	/// результат; без открытых остатков равен реализованному результату.
	/// Число есть только при конечном риске, статус переиспользуется у
	/// <see cref="RealRiskStatus"/>; метрика выводится из структуры ног и
	/// средних цен остатков и от текущих марок не зависит.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	/// </summary>
	public decimal? WorstCaseUsdt { get; init; }

	/// <summary>
	/// Сводная величина «с учётом зафиксированного синтетикой» в USDT —
	/// «Реализованный результат» конструкции плюс сумма величин «зафиксировано
	/// синтетикой» действующих связок (оспоренные распознанные не входят, ручные
	/// входят наравне с распознанными). Величина выводится при чтении поверх уже
	/// готовых метрик и строк связок read-моделью деталей, от марок не зависит
	/// и существует только при наличии действующих связок: null — отсутствие,
	/// а не ноль. От <see cref="WorstCaseUsdt"/> отличается источником: худший
	/// исход — инструментальная оценка из средних цен остатков, сводная —
	/// логическая сумма из цен сделок связок; величины не сливаются.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#requirement-synthetic-close-adjusted-total
	/// </summary>
	public decimal? SyntheticAdjustedTotalUsdt { get; init; }

	/// <summary>
	/// Число предупреждений диагностики незакрытых рисков конструкции — счётчик
	/// бейджа «⚠ N» строки списка и превью. Вычисляется при чтении из текущих
	/// открытых остатков тем же вызовом вычислителя, что и сами предупреждения,
	/// в базе не хранится; без открытых остатков счётчик нулевой.
	// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
	/// </summary>
	public int RiskWarningsCount { get; init; }

	/// <summary>
	/// Предупреждения диагностики незакрытых рисков конструкции: инструмент,
	/// число незакрытых контрактов и причина. Источник один — тот же вызов
	/// вычислителя, что питает <see cref="RiskWarningsCount"/>, поэтому счётчик
	/// бейджа и перечень карточки не расходятся; набор не хранится и всегда
	/// соответствует текущим открытым остаткам, без них он пуст.
	// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
	// Traceability: openspec:analytics/risk-diagnostics#scenario-diagnostics-closed-construction-empty
	/// </summary>
	public IReadOnlyList<ConstructionRiskDiagnostic> RiskWarnings { get; init; } = [];

	/// <summary>
	/// Метрики времени жизни открытых опционных ног конструкции: ноги с
	/// внутренним/временным разложением и распадом, саммари конструкции и
	/// предупреждения об экспирации. null у конструкции без открытых
	/// опционных ног — саммари по фьючерсным остаткам не строится. Записи
	/// предупреждений из этого же вычисления слиты в <see cref="RiskWarnings"/>,
	/// поэтому счётчик бейджа и перечень карточки с ними согласованы.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	/// </summary>
	public OptionLifetimeEvaluation? Lifetime { get; init; }

	/// <summary>Реализованный PnL в процентах от текущего выделенного капитала; null при нулевом капитале.</summary>
	public required decimal? RealizedPnLPercent { get; init; }

	/// <summary>Нереализованный PnL в процентах от текущего выделенного капитала; null при нулевом капитале или недоступной оценке.</summary>
	public required decimal? UnrealizedPnLPercent { get; init; }

	/// <summary>Сумма корректировок в процентах от текущего выделенного капитала; null при нулевом капитале.</summary>
	public required decimal? AdjustmentsPnLPercent { get; init; }

	/// <summary>Итог в процентах от текущего выделенного капитала; null при нулевом капитале или недоступном итоге.</summary>
	public required decimal? TotalPnLPercent { get; init; }

	/// <summary>
	/// Занято капитала, % — стоимость конструкции в процентах от текущего
	/// выделенного капитала; null при незаданном или нулевом капитале и когда
	/// стоимость недоступна. Отрицательная стоимость даёт отрицательный процент.
	// Traceability: openspec:analytics/performance#requirement-capital-usage-from-value
	/// </summary>
	public decimal? CapitalUsagePercent { get; init; }

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
