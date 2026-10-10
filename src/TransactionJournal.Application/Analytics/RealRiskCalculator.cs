using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Калькулятор реального риска конструкции — наихудшего результата её открытых
/// остатков по цене базового актива. Ноги открытых остатков классифицируются
/// по символу: опционы группируются по паре «базовый актив × экспирация»,
/// линейные перпы — по базовому активу; внутри группы ищется совместный
/// минимум суммарного платежа ног, группы суммируются консервативно.
/// Результат типизирован: конечный, неограниченный
/// и нерассчитанный риск различаются состояниями, число выдаётся только для
/// конечного риска. Калькулятор чистый: хранилище не читает и марок не
/// запрашивает — метрика выводится из структуры ног (тип, страйк,
/// экспирация, базовый актив, знаковый остаток) и средней цены открытого
/// остатка, поэтому сбой котировок на неё не влияет.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: change:show-unbounded-finresult-risk/design#d1
// Traceability: change:show-unbounded-finresult-risk/design#d4
/// </summary>
public sealed class RealRiskCalculator
{
	/// <summary>
	/// Вычисляет реальный риск конструкции из метрик её позиций в виде
	/// типизированного результата. Сначала каждая нога открытых остатков
	/// классифицируется разбором символа — опцион или вечный линейный
	/// фьючерс — и проверяется полнота исходных данных: неразобранный символ
	/// или отсутствующая средняя цена дают состояние «не рассчитан» до любой
	/// классификации хвостов групп. Затем все группы проверяются на
	/// неограниченный хвост убытка: нетто-короткую позицию по коллам в
	/// опционных группах и отрицательный суммарный наклон прямых платежей
	/// в линейных группах. Для конечного риска в опционной группе платеж
	/// f(S) = Σ ±(внутренняя стоимость(S, страйк, тип) − средняя цена остатка)
	/// линеен между узлами, поэтому минимум ищется по узлам S = 0 и страйкам
	/// группы; линейная группа платит qty × (S − средняя цена) и при
	/// неотрицательном наклоне достигает минимума в узле S = 0. Единицы — те
	/// же, что у стоимости по маркам: «цена × количество», без множителя
	/// контракта. Группы суммируются консервативно: реальный риск равен сумме
	/// минимумов групп с обратным знаком и не опускается ниже нуля. Без
	/// открытых остатков риск конечен и равен нулю.
	/// </summary>
	/// <param name="positions">Метрики позиций конструкции.</param>
	/// <returns>Типизированный результат: состояние риска и число только для конечного риска.</returns>
	/// <exception cref="ArgumentNullException">Позиции не заданы.</exception>
	public RealRiskResult CalculateResult(IEnumerable<PositionMetrics> positions)
	{
		ArgumentNullException.ThrowIfNull(positions);

		var openPositions = positions.Where(position => position.Residual != 0m).ToList();

		// Без открытых остатков худшего исхода на экспирацию больше нет: риск
		// конечен и равен нулю.
		// Traceability: openspec:analytics/performance#scenario-real-risk-zero-without-open-residuals
		if (openPositions.Count == 0)
		{
			return RealRiskResult.Finite(0m);
		}

		// Первый проход — классификация ног открытых остатков и полнота исходных
		// данных: символ разбирается либо как опцион, либо как вечный линейный
		// фьючерс; неразобранный символ не даёт ни базового актива, ни страйка,
		// а без средней цены остатка не определён платёж ноги, — совместный
		// минимум группы не определён, и выводы о хвостах групп преждевременны,
		// поэтому неполные данные дают «не рассчитан» раньше классификации
		// хвостов групп.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
		// Traceability: openspec:analytics/performance#scenario-real-risk-missing-open-price-unavailable
		var optionGroups = new Dictionary<(string BaseCoin, DateTime ExpiryDate), List<Leg>>();
		var linearGroups = new Dictionary<string, List<LinearLeg>>();
		foreach (var position in openPositions)
		{
			if (position.AverageOpenPrice is null)
			{
				return RealRiskResult.Unavailable();
			}

			if (OptionSymbolParser.TryParse(position.Symbol, out var optionParts) && optionParts is not null)
			{
				var key = (optionParts.BaseCoin, optionParts.ExpiryDate.Date);
				if (optionGroups.TryGetValue(key, out var legs) == false)
				{
					legs = new List<Leg>();
					optionGroups.Add(key, legs);
				}

				legs.Add(new Leg(position.Residual, optionParts.Strike, optionParts.Type, position.AverageOpenPrice.Value));
				continue;
			}

			if (LinearSymbolParser.TryParse(position.Symbol, out var linearParts) && linearParts is not null)
			{
				// Линейная нога группируется по базовому активу; пока прикрепление
				// к опционным группам не реализовано, перпы одной базы образуют
				// собственную группу с узлом нулевой цены.
				// Traceability: openspec:analytics/performance#scenario-real-risk-linear-only-construction
				if (linearGroups.TryGetValue(linearParts.BaseCoin, out var perps) == false)
				{
					perps = new List<LinearLeg>();
					linearGroups.Add(linearParts.BaseCoin, perps);
				}

				perps.Add(new LinearLeg(position.Residual, position.AverageOpenPrice.Value));
				continue;
			}

			return RealRiskResult.Unavailable();
		}

		// Второй проход — неограниченный хвост групп: суммарно короткая позиция
		// по коллам в опционной группе или отрицательный суммарный наклон
		// линейных ног дают платёж, убывающий без предела с ростом цены
		// базового актива на +∞, поэтому риск конструкции в целом не ограничен.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
		// Traceability: openspec:analytics/performance#scenario-real-risk-naked-short-linear-is-unbounded
		foreach (var legs in optionGroups.Values)
		{
			var netCallQuantity = legs.Where(leg => leg.Type == OptionType.Call).Sum(leg => leg.Quantity);
			if (netCallQuantity < 0m)
			{
				return RealRiskResult.Unbounded();
			}
		}

		foreach (var perps in linearGroups.Values)
		{
			if (perps.Sum(perp => perp.Quantity) < 0m)
			{
				return RealRiskResult.Unbounded();
			}
		}

		decimal worst = 0m;
		foreach (var legs in optionGroups.Values)
		{
			// Платёж ломаной линеен между узлами, поэтому минимум на луче S ≥ 0
			// достигается в узле: S = 0 или один из страйков группы; хвост на +∞
			// при неотрицательном наклоне хуже последнего узла быть не может.
			var nodes = new List<decimal> { 0m };
			nodes.AddRange(legs.Select(leg => leg.Strike).Distinct());

			var groupMinimum = decimal.MaxValue;
			foreach (var node in nodes)
			{
				decimal payment = 0m;
				foreach (var leg in legs)
				{
					payment += leg.Quantity * (IntrinsicValue(node, leg) - leg.AverageOpenPrice);
				}

				groupMinimum = Math.Min(groupMinimum, payment);
			}

			// Группы суммируются консервативно: худшие исходы разных групп
			// складываются без предположения об их одновременности.
			worst += groupMinimum;
		}

		foreach (var perps in linearGroups.Values)
		{
			// Платёж линейной ноги — прямая без изломов; при неотрицательном
			// наклоне (проверено вторым проходом) минимум группы достигается
			// в узле нулевой цены.
			decimal payment = 0m;
			foreach (var perp in perps)
			{
				payment += perp.Quantity * (0m - perp.AverageOpenPrice);
			}

			worst += payment;
		}

		// Реальный риск — худший платёж с обратным знаком; платёж, неотрицательный
		// во всех узлах, риска не несёт, поэтому величина не уходит ниже нуля.
		return RealRiskResult.Finite(Math.Max(0m, -worst));
	}

	#region Вспомогательные методы

	/// <summary>Внутренняя стоимость опциона в узле: колл — превышение узла над страйком, пут — страйка над узлом.</summary>
	private static decimal IntrinsicValue(decimal spot, Leg leg) => leg.Type == OptionType.Call
		? Math.Max(spot - leg.Strike, 0m)
		: Math.Max(leg.Strike - spot, 0m);

	#endregion

	/// <summary>Опционная нога группы: знаковое количество, страйк, тип опциона и средняя цена открытого остатка.</summary>
	private readonly record struct Leg(decimal Quantity, decimal Strike, OptionType Type, decimal AverageOpenPrice);

	/// <summary>Линейная нога группы: знаковое количество и средняя цена открытого остатка.</summary>
	private readonly record struct LinearLeg(decimal Quantity, decimal AverageOpenPrice);
}
