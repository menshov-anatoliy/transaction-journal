namespace TransactionJournal.Application.Analytics;

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
	private readonly RealRiskCalculator _realRiskCalculator = new();
	/// <summary>
	/// Вычисляет метрики конструкции из метрик её позиций и внешних корректировок
	/// PnL. Итог — сумма реализованного и нереализованного PnL позиций и
	/// корректировок; неоцененный открытый остаток обнуляет только нереализованную
	/// часть и зависящий от неё итог, остальные метрики возвращаются как есть.
	/// Стоимость конструкции — сумма стоимостей открытых позиций; без открытых
	/// остатков или при неоцененном открытом остатке она отсутствует, остальные
	/// метрики не меняются. Занято капитала, % — стоимость в процентах от текущего
	/// значения выделенного капитала. Проценты считаются от текущего значения
	/// выделенного капитала; незаданный или нулевой капитал не образует базы
	/// процентов — процентные величины возвращаются отсутствующими. Даты выводятся
	/// из записей: открытие — время первой сделки, закрытие — момент обнуления
	/// последней позиции; длительность открытой конструкции считается от первой
	/// сделки до переданного текущего момента. Реальный риск — наихудший результат
	/// открытых остатков на экспирации — выводится из структуры ног и средних цен
	/// открытых остатков и от текущих марок не зависит; в метрики он переносится
	/// вместе со статусом, различающим конечный, неограниченный и нерассчитанный
	/// риск, без изменения расчёта результата конструкции.
	/// </summary>
	/// <param name="constructionId">Конструкция, для которой вычисляются метрики.</param>
	/// <param name="allocatedCapitalUsdt">Текущий выделенный капитал конструкции в USDT — база процентов; null, когда капитал не задан.</param>
	/// <param name="positions">Метрики позиций конструкции.</param>
	/// <param name="adjustments">Внешние корректировки PnL конструкции.</param>
	/// <param name="now">Текущий момент — граница длительности открытой конструкции.</param>
	/// <returns>Метрики конструкции.</returns>
	/// <exception cref="ArgumentNullException">Позиции или корректировки не заданы.</exception>
	/// <exception cref="ArgumentException">Среди позиций есть позиция другой конструкции.</exception>
	public ConstructionMetrics Calculate(
		long constructionId,
		decimal? allocatedCapitalUsdt,
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

		// Стоимость конструкции — сумма стоимостей открытых позиций: закрытые
		// позиции стоимости не имеют, а неоцененный открытый остаток делает
		// стоимость недоступной целиком, не задевая остальные метрики.
		// Traceability: openspec:analytics/performance#scenario-construction-value-sums-positions
		// Traceability: openspec:analytics/performance#scenario-closed-position-has-no-mark-value
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-mark-value
		var openResiduals = positionList.Where(position => position.Residual != 0m).ToList();
		decimal? markValue = openResiduals.Count == 0 || openResiduals.Any(position => position.MarkValue == null)
			? null
			: openResiduals.Sum(position => position.MarkValue.GetValueOrDefault());

		// Реальный риск — наихудший результат открытых остатков на экспирации:
		// выводится из структуры ног и средних цен остатков, текущие марки на
		// метрику не влияют, поэтому сбой котировок её не задевает. В метрики
		// проходит состояние рядом с величиной: отсутствие числа перестаёт быть
		// двусмысленным — неограниченный хвост и неполные исходные данные
		// различимы потребителями, а P&L и прочие метрики не затронуты.
		// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
		// Traceability: openspec:analytics/performance#scenario-real-risk-marks-failure-independent
		var realRisk = _realRiskCalculator.CalculateResult(positionList);

		// Проценты — чистые функции текущих данных: базой служит текущее значение
		// выделенного капитала, поэтому правка капитала меняет только процентные
		// величины; незаданный или нулевой капитал базы не образует — проценты
		// остаются null при неизменных абсолютных величинах, датах и длительности.
		// Traceability: openspec:analytics/performance#scenario-percent-from-current-capital
		// Traceability: openspec:analytics/performance#scenario-no-capital-no-percent-metrics
		// Traceability: openspec:domain/constructions#requirement-allocated-capital
		decimal? Percent(decimal? value) => value == null || allocatedCapitalUsdt is null or 0m
			? null
			: value.Value / allocatedCapitalUsdt.Value * 100m;

		// Занято капитала, % — та же база, что и у процентных величин PnL, но
		// значением служит стоимость конструкции: знак стоимости сохраняется.
		// Traceability: openspec:analytics/performance#scenario-capital-usage-computed-when-capital-set
		// Traceability: openspec:analytics/performance#scenario-capital-usage-absent-without-capital
		var capitalUsagePercent = Percent(markValue);

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
			MarkValue = markValue,
			RealRiskUsdt = realRisk.Usdt,
			RealRiskStatus = realRisk.Status,
			RealizedPnLPercent = Percent(realizedPnL),
			UnrealizedPnLPercent = Percent(unrealizedPnL),
			AdjustmentsPnLPercent = Percent(adjustmentsPnL),
			TotalPnLPercent = Percent(totalPnL),
			CapitalUsagePercent = capitalUsagePercent,
			OpenedAt = openedAt,
			ClosedAt = closedAt,
			Duration = duration,
		};
	}
}
