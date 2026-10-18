using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Вычислитель метрик времени жизни открытых опционных ног и конструкции из
/// фактических биржевых данных. По каждой открытой ноге раскладывает марку на
/// внутреннюю стоимость (max(0, underlying − strike) у колла, зеркально у пута)
/// и временную (марка минус внутренняя) — на контракт и на чистый остаток,
/// считает дневной распад биржевой тетой на остаток и остаток полных суток до
/// экспирации floor((deliveryTime − now) / 24 ч) по UTC. Прирост распада —
/// фактическая разность тет с ближайшим снимком истории суточной давности,
/// приведённая к текущему остатку. Саммари конструкции сводит открытые
/// опционные ноги: минимум суток до экспирации, нетто-распад со знаком и
/// суммарную временную стоимость; нога без рыночных данных исключается из
/// слагаемых прочерком, не подменяя остальные. Открытая нога с остатком не
/// более четырнадцати полных суток порождает предупреждение об экспирации —
/// по одной записи на инструмент, чисто временное, без условия «в зоне
/// страйка»; фьючерсные ноги в метриках не участвуют. Недоступность тикеров —
/// деградация прочерком, а не ошибка: остаток суток вычисляется и без них.
/// Калькулятор чистый: хранилище не читает и марок не запрашивает — свежие
/// тикеры и история снимков передаются на вход, время берётся у
/// <see cref="TimeProvider"/>.
// Traceability: openspec:analytics/option-lifetime#requirement-leg-intrinsic-time-value
// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-and-days-to-expiry
// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-acceleration
// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
// Traceability: openspec:analytics/option-lifetime#requirement-expiry-warning-two-weeks
// Traceability: adr:docs/adr/0017-option-lifetime-metrics.md#considered-options
// Traceability: change:add-option-lifetime-metrics/design#d2
// Traceability: change:add-option-lifetime-metrics/design#d3
/// </summary>
public sealed class OptionLifetimeMetricsCalculator
{
	/// <summary>Порог предупреждения об экспирации: остаток полных суток не более четырнадцати.</summary>
	public const int ExpiryWarningDaysThreshold = 14;

	/// <summary>Минимальный возраст снимка истории для прироста распада — сутки.</summary>
	private static readonly TimeSpan SnapshotMinAge = TimeSpan.FromHours(24);

	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт вычислитель над поставщиком времени; по умолчанию системные часы.</summary>
	/// <param name="timeProvider">Поставщик времени для остатка суток и возраста снимков; по умолчанию системный.</param>
	public OptionLifetimeMetricsCalculator(TimeProvider? timeProvider = null)
	{
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <summary>
	/// Вычисляет метрики времени жизни открытых опционных ног, саммари конструкции
	/// и предупреждения об экспирации на переданных рыночных данных. Закрытые ноги
	/// (нулевой остаток) из расчёта исключаются; каждая открытая нога возвращает
	/// стоимости, распад, прирост и остаток суток, недоступная рыночная величина
	/// остаётся null. Саммари строится только по открытым опционным ногам: без них
	/// оно отсутствует. Предупреждение об экспирации возникает у открытой ноги
	/// с остатком суток не более порога, по одной записи на инструмент, и не
	/// зависит от проходов агента подсказок.
	/// </summary>
	/// <param name="legs">Открытые опционные ноги конструкции с разбором символа из справочника инструментов.</param>
	/// <param name="marks">Свежие снимки тикеров по символам: марка, индексная цена и тета; у отсутствующего символа рыночных величин не будет.</param>
	/// <param name="snapshotHistory">История рыночных снимков — материал прироста распада; проверяется давность каждого снимка.</param>
	/// <returns>Метрики ног, саммари конструкции и предупреждения об экспирации.</returns>
	/// <exception cref="ArgumentNullException">Ноги, снимки тикеров или история не заданы.</exception>
	public OptionLifetimeEvaluation Calculate(
		IEnumerable<OptionLegLifetimeInput> legs,
		IReadOnlyDictionary<string, InstrumentMarkSnapshot> marks,
		IEnumerable<InstrumentMarketSnapshot> snapshotHistory)
	{
		ArgumentNullException.ThrowIfNull(legs);
		ArgumentNullException.ThrowIfNull(marks);
		ArgumentNullException.ThrowIfNull(snapshotHistory);

		var now = _timeProvider.GetUtcNow();
		var historyBySymbol = snapshotHistory.ToLookup(snapshot => snapshot.Symbol, StringComparer.Ordinal);

		var legMetrics = new List<OptionLegLifetimeMetrics>();
		var warnings = new List<OptionExpiryWarning>();
		foreach (var leg in legs)
		{
			// Закрытая нога в метриках времени жизни не участвует: у неё нет ни
			// остатка для стоимостей и распада, ни предупреждения об экспирации.
			// Traceability: openspec:analytics/option-lifetime#requirement-leg-intrinsic-time-value
			// Traceability: openspec:analytics/option-lifetime#scenario-expiry-warning-open-legs-only
			if (leg.Residual == 0m)
			{
				continue;
			}

			var metrics = CalculateLeg(leg, marks, historyBySymbol[leg.Symbol], now);
			legMetrics.Add(metrics);

			// Предупреждение чисто временное: порог отсчитывается от остатка полных
			// суток, положение цены к страйку на срабатывание не влияет.
			// Traceability: openspec:analytics/option-lifetime#requirement-expiry-warning-two-weeks
			if (metrics.DaysToExpiry <= ExpiryWarningDaysThreshold)
			{
				warnings.Add(OptionExpiryWarning.Create(leg.Symbol, metrics.DaysToExpiry, metrics.DailyDecayUsdt));
			}
		}

		// По одной записи на инструмент: повторные ноги того же символа схлопываются
		// в первое предупреждение, порядок детерминированный — по символу.
		// Traceability: openspec:analytics/option-lifetime#scenario-expiry-warning-one-per-instrument
		var expiryWarnings = warnings
			.GroupBy(warning => warning.Symbol, StringComparer.Ordinal)
			.Select(group => group.First())
			.OrderBy(warning => warning.Symbol, StringComparer.Ordinal)
			.ToList();

		return new OptionLifetimeEvaluation
		{
			Legs = legMetrics,
			Summary = BuildSummary(legMetrics),
			ExpiryWarnings = expiryWarnings,
		};
	}

	#region Вспомогательные методы

	/// <summary>
	/// Вычисляет метрики одной открытой опционной ноги. Стоимости и распад
	/// существуют только при наличии свежего тикера: без индексной цены
	/// внутренняя и временная стоимости отсутствуют, без теты отсутствуют
	/// распад и его прирост; остаток суток от тикеров не зависит. Прирост
	/// распада сравнивает тету текущего тикера с тетой ближайшего снимка
	/// истории возрастом не менее суток — факт, без прогнозной модели.
	/// </summary>
	private OptionLegLifetimeMetrics CalculateLeg(
		OptionLegLifetimeInput leg,
		IReadOnlyDictionary<string, InstrumentMarkSnapshot> marks,
		IEnumerable<InstrumentMarketSnapshot> history,
		DateTimeOffset now)
	{
		marks.TryGetValue(leg.Symbol, out var mark);

		// Стоимости раскладываются только из пары «индексная цена — марка»:
		// внутренняя — превышение цены над страйком по типу опциона, временная —
		// остаток марки, обе далее масштабируются чистым остатком со знаком.
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-intrinsic-time-value
		// Traceability: openspec:analytics/option-lifetime#scenario-leg-value-scaled-by-residual
		decimal? intrinsicPerContract = null;
		decimal? timeValuePerContract = null;
		decimal? intrinsicUsdt = null;
		decimal? timeValueUsdt = null;
		if (mark != null && mark.UnderlyingPrice is { } underlyingPrice)
		{
			intrinsicPerContract = leg.Type == OptionType.Call
				? Math.Max(underlyingPrice - leg.Strike, 0m)
				: Math.Max(leg.Strike - underlyingPrice, 0m);
			timeValuePerContract = mark.MarkPrice - intrinsicPerContract.Value;
			intrinsicUsdt = intrinsicPerContract.Value * leg.Residual;
			timeValueUsdt = timeValuePerContract.Value * leg.Residual;
		}

		// Дневной распад — биржевая тета на контракт на чистый остаток: короткий
		// остаток меняет знак, распад продажи «положительный» по премии.
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-and-days-to-expiry
		decimal? dailyDecayUsdt = null;
		decimal? decayDayAgoUsdt = null;
		decimal? decayAccelerationUsdt = null;
		if (mark?.Theta is { } thetaNow)
		{
			dailyDecayUsdt = thetaNow * leg.Residual;

			// Прирост распада — фактическая разность тет со снимком суточной
			// давности, приведённая к текущему остатку: сделки внутри суток
			// не искажают сравнение. Молодая история даёт прочерк.
			// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-acceleration
			// Traceability: change:add-option-lifetime-metrics/design#d3
			var cutoff = now - SnapshotMinAge;
			var baselineTheta = history
				.Where(snapshot => snapshot.ReceivedAt <= cutoff)
				.OrderByDescending(snapshot => snapshot.ReceivedAt)
				.Select(snapshot => snapshot.Theta)
				.FirstOrDefault(theta => theta != null);
			if (baselineTheta is { } thetaBaseline)
			{
				decayDayAgoUsdt = thetaBaseline * leg.Residual;
				decayAccelerationUsdt = (thetaNow - thetaBaseline) * leg.Residual;
			}
		}

		// Остаток полных суток считается от deliveryTime по UTC и без рыночных
		// данных: сутки — календарная величина, а не рыночная.
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-and-days-to-expiry
		// Traceability: openspec:analytics/option-lifetime#scenario-leg-days-no-market-data
		var daysDecimal = Math.Floor(
			(leg.DeliveryTime.UtcDateTime - now.UtcDateTime).Ticks / (decimal)TimeSpan.TicksPerDay);

		return new OptionLegLifetimeMetrics
		{
			Symbol = leg.Symbol,
			DaysToExpiry = (int)daysDecimal,
			IntrinsicValueUsdtPerContract = intrinsicPerContract,
			TimeValueUsdtPerContract = timeValuePerContract,
			IntrinsicValueUsdt = intrinsicUsdt,
			TimeValueUsdt = timeValueUsdt,
			DailyDecayUsdt = dailyDecayUsdt,
			DecayDayAgoUsdt = decayDayAgoUsdt,
			DecayAccelerationUsdt = decayAccelerationUsdt,
		};
	}

	/// <summary>
	/// Сводит метрики открытых опционных ног в саммари конструкции: минимум
	/// суток, нетто-распад со знаком и суммарная временная стоимость. Нога без
	/// рыночной величины исключается из соответствующего слагаемого прочерком;
	/// пока ни одна нога величины не дала, слагаемое остаётся null. Без открытых
	/// опционных ног саммари отсутствует — фьючерсные остатки его не строят.
	/// </summary>
	private static ConstructionLifetimeSummary? BuildSummary(IReadOnlyList<OptionLegLifetimeMetrics> legs)
	{
		// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
		// Traceability: openspec:analytics/option-lifetime#scenario-summary-no-option-legs
		if (legs.Count == 0)
		{
			return null;
		}

		var decays = legs
			.Where(leg => leg.DailyDecayUsdt != null)
			.Select(leg => leg.DailyDecayUsdt.GetValueOrDefault())
			.ToList();
		var timeValues = legs
			.Where(leg => leg.TimeValueUsdt != null)
			.Select(leg => leg.TimeValueUsdt.GetValueOrDefault())
			.ToList();

		return new ConstructionLifetimeSummary
		{
			DaysToNearestExpiry = legs.Min(leg => leg.DaysToExpiry),
			NetDailyDecayUsdt = decays.Count == 0 ? null : decays.Sum(),
			TotalTimeValueUsdt = timeValues.Count == 0 ? null : timeValues.Sum(),
		};
	}

	#endregion
}
