namespace TransactionJournal.Analytics;

/// <summary>
/// Калькулятор метрик конструкции: сводит метрики позиций и внешние корректировки
/// PnL в итог конструкции, относит результат к текущему выделенному капиталу в
/// процентах и выводит даты с длительностью из записей позиций. Калькулятор
/// чистый: хранилище не читает, результатов не пишет, каждая метрика — функция
/// переданного набора позиций и корректировок, поэтому правка привязок сделок,
/// корректировок или капитала отражается очередным вызовом без следов прежнего
/// расчёта.
// Traceability: openspec:analytics/performance#requirement-construction-metrics
// Traceability: change:add-analytics/design#d4
// Traceability: change:add-analytics/design#d5
/// </summary>
public sealed class ConstructionMetricsCalculator
{
	/// <summary>
	/// Вычисляет метрики конструкции из метрик её позиций и внешних корректировок
	/// PnL. Итог — сумма реализованного и нереализованного PnL позиций и
	/// корректировок; неоцененный открытый остаток обнуляет только нереализованную
	/// часть и зависящий от неё итог, остальные метрики возвращаются как есть.
	/// Проценты считаются от текущего значения выделенного капитала; нулевой
	/// капитал не образует базы процентов. Даты выводятся из записей: открытие —
	/// время первой сделки, закрытие — момент обнуления последней позиции;
	/// длительность открытой конструкции считается от первой сделки до переданного
	/// текущего момента.
	/// </summary>
	/// <param name="constructionId">Конструкция, для которой вычисляются метрики.</param>
	/// <param name="allocatedCapitalUsdt">Текущий выделенный капитал конструкции в USDT — база процентов.</param>
	/// <param name="positions">Метрики позиций конструкции.</param>
	/// <param name="adjustments">Внешние корректировки PnL конструкции.</param>
	/// <param name="now">Текущий момент — граница длительности открытой конструкции.</param>
	/// <returns>Метрики конструкции.</returns>
	/// <exception cref="ArgumentNullException">Позиции или корректировки не заданы.</exception>
	/// <exception cref="ArgumentException">Среди позиций есть позиция другой конструкции.</exception>
	public ConstructionMetrics Calculate(
		long constructionId,
		decimal allocatedCapitalUsdt,
		IEnumerable<PositionMetrics> positions,
		IEnumerable<ConstructionPnLAdjustment> adjustments,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(positions);
		ArgumentNullException.ThrowIfNull(adjustments);

		var positionList = positions.ToList();

		// Позиции выводятся ключом «конструкция × инструмент»: чужая позиция в наборе
		// задвоила бы результат конструкции — это повреждение входных данных.
		if (positionList.Any(position => position.ConstructionId != constructionId))
		{
			throw new ArgumentException(
				"Все позиции должны принадлежать конструкции: в наборе есть позиция другой конструкции.",
				nameof(positions));
		}

		// Итог — чистая сумма: реализованный и нереализованный PnL позиций плюс
		// внешние корректировки без сделок.
		// Traceability: openspec:analytics/performance#scenario-construction-total-includes-adjustments
		var realizedPnL = positionList.Sum(position => position.RealizedPnL);
		var adjustmentsPnL = adjustments.Sum(adjustment => adjustment.AmountUsdt);

		// Нереализованная часть — сумма оценок позиций: неоцененный открытый остаток
		// обнуляет только её и зависящий от неё итог, реализованные метрики,
		// корректировки, даты и их проценты возвращаются без изменений.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
		var hasUnevaluatedResidual = positionList.Any(position => position.UnrealizedPnL == null);
		decimal? unrealizedPnL = hasUnevaluatedResidual
			? null
			: positionList.Sum(position => position.UnrealizedPnL.GetValueOrDefault());
		decimal? totalPnL = unrealizedPnL == null ? null : realizedPnL + unrealizedPnL.Value + adjustmentsPnL;

		// Проценты — чистые функции текущих данных: базой служит текущее значение
		// выделенного капитала, поэтому правка капитала меняет только процентные
		// величины; нулевой капитал базы не образует — проценты остаются null.
		// Traceability: openspec:analytics/performance#scenario-percent-from-current-capital
		// Traceability: openspec:domain/constructions#requirement-allocated-capital
		decimal? Percent(decimal? value) => value == null || allocatedCapitalUsdt == 0m
			? null
			: value.Value / allocatedCapitalUsdt * 100m;

		// Даты выводятся из записей позиций: открытие — время первой сделки (первая
		// запись самой ранней позиции), закрытие — момент обнуления последней
		// позиции. Конструкция открыта, пока открыта любая её позиция: даты закрытия
		// нет, а длительность тянется от первой сделки до текущего момента.
		// Traceability: openspec:analytics/performance#scenario-construction-dates-derived
		// Traceability: openspec:analytics/performance#scenario-open-construction-duration-to-now
		DateTimeOffset? openedAt = positionList.Count == 0 ? null : positionList.Min(position => position.OpenedAt);
		var hasOpenResidual = positionList.Any(position => position.Residual != 0m);
		DateTimeOffset? closedAt = hasOpenResidual || positionList.Count == 0
			? null
			: positionList.Max(position => position.ClosedAt);
		TimeSpan? duration = openedAt == null ? null : (closedAt ?? now) - openedAt.Value;

		return new ConstructionMetrics
		{
			ConstructionId = constructionId,
			AllocatedCapitalUsdt = allocatedCapitalUsdt,
			RealizedPnL = realizedPnL,
			UnrealizedPnL = unrealizedPnL,
			AdjustmentsPnL = adjustmentsPnL,
			TotalPnL = totalPnL,
			RealizedPnLPercent = Percent(realizedPnL),
			UnrealizedPnLPercent = Percent(unrealizedPnL),
			AdjustmentsPnLPercent = Percent(adjustmentsPnL),
			TotalPnLPercent = Percent(totalPnL),
			OpenedAt = openedAt,
			ClosedAt = closedAt,
			Duration = duration,
		};
	}
}
