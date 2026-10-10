using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Калькулятор реального риска конструкции — наихудшего результата её открытых
/// остатков по цене базового актива. Ноги открытых остатков классифицируются
/// по символу: опционы группируются по паре «базовый актив × экспирация»,
/// линейные перпы прикрепляются к опционной группе самой ранней экспирации
/// своего базового актива, а без опционных групп той же базы образуют
/// собственную группу; внутри группы ищется совместный
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
		/// неограниченный хвост убытка по наклону платежа за последним узлом:
		/// отрицательная сумма коллов и прикреплённых линейных ног в опционной
		/// группе или короткая суммарная позиция чисто линейной группы дают
		/// платёж, убывающий без предела. Для конечного риска в опционной группе платеж
	/// f(S) = Σ ±(внутренняя стоимость(S, страйк, тип) − средняя цена остатка)
	/// линеен между узлами, поэтому минимум ищется по узлам S = 0 и страйкам
	/// группы; прикреплённые перпы добавляют в каждый узел прямой платёж
	/// qty × (S − средняя цена) без новых узлов излома. Перп без опционных
	/// групп той же базы платит ту же прямую и при неотрицательном наклоне
	/// достигает минимума в узле S = 0. Единицы — те
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
		var optionGroups = new Dictionary<(string BaseCoin, DateTime ExpiryDate), ExpiryGroup>();
		var linearLegs = new List<(LinearSymbolParts Parts, LinearLeg Leg)>();
		foreach (var position in openPositions)
		{
			if (position.AverageOpenPrice is null)
			{
				return RealRiskResult.Unavailable();
			}

			if (OptionSymbolParser.TryParse(position.Symbol, out var optionParts) && optionParts is not null)
			{
				var key = (optionParts.BaseCoin, optionParts.ExpiryDate.Date);
				if (optionGroups.TryGetValue(key, out var group) == false)
				{
					group = new ExpiryGroup();
					optionGroups.Add(key, group);
				}

				group.Options.Add(new Leg(position.Residual, optionParts.Strike, optionParts.Type, position.AverageOpenPrice.Value));
				continue;
			}

			if (LinearSymbolParser.TryParse(position.Symbol, out var linearParts) && linearParts is not null)
			{
				// Линейная нога собирается без немедленной привязки: её группа
				// определится только после полного прохода, когда известны все
				// опционные группы её базового актива.
				linearLegs.Add((linearParts, new LinearLeg(position.Residual, position.AverageOpenPrice.Value)));
				continue;
			}

			return RealRiskResult.Unavailable();
		}

		// Перп прикрепляется к опционной группе самой ранней экспирации своего
		// базового актива: совместный минимум опционов и перпа уточняет оценку
		// и никогда её не завышает, а связь наклона коллов и перпа сохраняется
		// для правила неограниченного хвоста. Без опционных групп той же базы
		// перп образует собственную группу с единственным узлом нулевой цены.
		// Traceability: openspec:analytics/performance#scenario-real-risk-linear-joins-earliest-expiry-group
		// Traceability: openspec:analytics/performance#scenario-real-risk-linear-only-construction
		var linearOnlyGroups = new Dictionary<string, List<LinearLeg>>();
		foreach (var (linearParts, linearLeg) in linearLegs)
		{
			var sameBaseGroups = optionGroups.Where(pair => pair.Key.BaseCoin == linearParts.BaseCoin).ToList();
			if (sameBaseGroups.Count > 0)
			{
				sameBaseGroups.MinBy(pair => pair.Key.ExpiryDate).Value.AttachedLinearLegs.Add(linearLeg);
				continue;
			}

			if (linearOnlyGroups.TryGetValue(linearParts.BaseCoin, out var perps) == false)
			{
				perps = new List<LinearLeg>();
				linearOnlyGroups.Add(linearParts.BaseCoin, perps);
			}

			perps.Add(linearLeg);
		}

		// Второй проход — неограниченный хвост групп по наклону платежа за
		// последним узлом: путы за последним узлом плоски, поэтому наклон
		// равен суммарному количеству коллов плюс суммарному количеству
		// прикреплённых линейных ног; для чисто линейной группы — только
		// количеству линейных ног. Отрицательный наклон даёт платёж,
		// убывающий без предела с ростом цены базового актива на +∞, поэтому
		// риск конструкции в целом не ограничен; хеджированный короткий перп
		// (накрытый длинными коллами) даёт неотрицательный наклон и конечный
		// риск.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
		// Traceability: openspec:analytics/performance#scenario-real-risk-naked-short-linear-is-unbounded
		// Traceability: openspec:analytics/performance#scenario-real-risk-linear-hedge-is-finite
		foreach (var group in optionGroups.Values)
		{
			var netCallQuantity = group.Options.Where(leg => leg.Type == OptionType.Call).Sum(leg => leg.Quantity);
			var attachedLinearQuantity = group.AttachedLinearLegs.Sum(perp => perp.Quantity);
			if (netCallQuantity + attachedLinearQuantity < 0m)
			{
				return RealRiskResult.Unbounded();
			}
		}

		foreach (var perps in linearOnlyGroups.Values)
		{
			if (perps.Sum(perp => perp.Quantity) < 0m)
			{
				return RealRiskResult.Unbounded();
			}
		}

		decimal worst = 0m;
		foreach (var group in optionGroups.Values)
		{
			// Платёж ломаной линеен между узлами, поэтому минимум на луче S ≥ 0
			// достигается в узле: S = 0 или один из страйков группы; хвост на +∞
			// при неотрицательном наклоне хуже последнего узла быть не может.
			var nodes = new List<decimal> { 0m };
			nodes.AddRange(group.Options.Select(leg => leg.Strike).Distinct());

			var groupMinimum = decimal.MaxValue;
			foreach (var node in nodes)
			{
				decimal payment = 0m;
				foreach (var leg in group.Options)
				{
					payment += leg.Quantity * (IntrinsicValue(node, leg) - leg.AverageOpenPrice);
				}

				// Прямой платёж перпа добавляется в те же узлы: линейная добавка
				// не создаёт новых узлов излома, минимум остаётся на узлах группы.
				foreach (var perp in group.AttachedLinearLegs)
				{
					payment += perp.Quantity * (node - perp.AverageOpenPrice);
				}

				groupMinimum = Math.Min(groupMinimum, payment);
			}

			// Группы суммируются консервативно: худшие исходы разных групп
			// складываются без предположения об их одновременности.
			worst += groupMinimum;
		}

		foreach (var perps in linearOnlyGroups.Values)
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

	/// <summary>Группа одной экспирации одного базового актива: опционные ноги и прикреплённые к группе линейные ноги.</summary>
	private sealed class ExpiryGroup
	{
		/// <summary>Опционные ноги группы; их страйки задают узлы излома платежа.</summary>
		public List<Leg> Options { get; } = new();

		/// <summary>Линейные ноги, прикреплённые к группе самой ранней экспирации своего базового актива.</summary>
		public List<LinearLeg> AttachedLinearLegs { get; } = new();
	}
}
