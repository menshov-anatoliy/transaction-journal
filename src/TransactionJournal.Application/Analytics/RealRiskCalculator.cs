using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Калькулятор реального риска конструкции — наихудшего результата её открытых
/// остатков на экспирации. Открытые остатки группируются по паре «базовый
/// актив × экспирация» из разбора символа ноги; внутри группы ищется совместный
/// минимум суммарного платежа ног по узлам-страйкам, группы разных экспираций
/// суммируются консервативно. Результат типизирован: конечный, неограниченный
/// и нерассчитанный риск различаются состояниями, число выдаётся только для
/// конечного риска. Калькулятор чистый: хранилище не читает и марок не
/// запрашивает — метрика выводится из структуры ног (тип, страйк,
/// экспирация, знаковый остаток) и средней цены открытого остатка, поэтому сбой
/// котировок на неё не влияет.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: change:show-unbounded-finresult-risk/design#d1
// Traceability: change:show-unbounded-finresult-risk/design#d4
/// </summary>
public sealed class RealRiskCalculator
{
	/// <summary>
	/// Вычисляет реальный риск конструкции из метрик её позиций в виде
	/// типизированного результата. Сначала проверяется полнота исходных данных
	/// всех открытых остатков — разбор символа и средняя цена: при смешанном
	/// наборе неполные данные дают состояние «не рассчитан» до любой
	/// классификации хвостов групп. Затем все группы проверяются на
	/// нетто-короткую позицию по коллам — она даёт неограниченный хвост убытка.
	/// Для конечного риска в группе платеж
	/// f(S) = Σ ±(внутренняя стоимость(S, страйк, тип) − средняя цена остатка)
	/// линеен между узлами, поэтому минимум ищется по узлам S = 0 и страйкам
	/// группы. Единицы — те же, что у стоимости по маркам:
	/// «цена × количество», без множителя контракта. Группы суммируются
	/// консервативно: реальный риск равен сумме минимумов групп с обратным
	/// знаком и не опускается ниже нуля. Без открытых остатков риск конечен и
	/// равен нулю.
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

		// Первый проход — полнота исходных данных всех открытых остатков:
		// неразобранный символ не даёт ни базового актива, ни экспирации, ни
		// страйка, а без средней цены остатка не определён платеж ноги, —
		// совместный минимум группы не определён, и выводы о хвостах групп
		// преждевременны, поэтому данные проверяются раньше классификации
		// хвостов групп.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
		// Traceability: openspec:analytics/performance#scenario-real-risk-missing-open-price-unavailable
		var groups = new Dictionary<(string BaseCoin, DateTime ExpiryDate), List<Leg>>();
		foreach (var position in openPositions)
		{
			if (OptionSymbolParser.TryParse(position.Symbol, out var parts) == false
				|| parts is null
				|| position.AverageOpenPrice is null)
			{
				return RealRiskResult.Unavailable();
			}

			var key = (parts.BaseCoin, parts.ExpiryDate.Date);
			if (groups.TryGetValue(key, out var legs) == false)
			{
				legs = new List<Leg>();
				groups.Add(key, legs);
			}

			legs.Add(new Leg(position.Residual, parts.Strike, parts.Type, position.AverageOpenPrice.Value));
		}

		// Второй проход — нетто-короткий хвост групп: суммарно короткая позиция
		// по коллам даёт платёж, убывающий без предела с ростом цены базового
		// актива на +∞, поэтому риск конструкции в целом не ограничен.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
		foreach (var legs in groups.Values)
		{
			var netCallQuantity = legs.Where(leg => leg.Type == OptionType.Call).Sum(leg => leg.Quantity);
			if (netCallQuantity < 0m)
			{
				return RealRiskResult.Unbounded();
			}
		}

		decimal worst = 0m;
		foreach (var legs in groups.Values)
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

			// Группы суммируются консервативно: худшие исходы разных экспираций
			// складываются без предположения об их одновременности.
			worst += groupMinimum;
		}

		// Реальный риск — худший платёж с обратным знаком; платёж, неотрицательный
		// во всех узлах, риска не несёт, поэтому величина не уходит ниже нуля.
		return RealRiskResult.Finite(Math.Max(0m, -worst));
	}

	/// <summary>
	/// Переходная обёртка совместимости: отдаёт числовую величину конечного
	/// риска или null, когда риск не ограничен либо не рассчитан. Существует,
	/// пока потребители результата не переведены на типизированный контракт;
	/// различие причин отсутствия числа при этом не теряется — оно
	/// восстанавливается вызовом <see cref="CalculateResult"/>.
	/// </summary>
	/// <param name="positions">Метрики позиций конструкции.</param>
	/// <returns>Реальный риск в USDT или null, когда величина не определена.</returns>
	/// <exception cref="ArgumentNullException">Позиции не заданы.</exception>
	public decimal? Calculate(IEnumerable<PositionMetrics> positions)
	{
		var result = CalculateResult(positions);

		return result.Status == RealRiskStatus.Finite ? result.Usdt : null;
	}

	#region Вспомогательные методы

	/// <summary>Внутренняя стоимость опциона в узле: колл — превышение узла над страйком, пут — страйка над узлом.</summary>
	private static decimal IntrinsicValue(decimal spot, Leg leg) => leg.Type == OptionType.Call
		? Math.Max(spot - leg.Strike, 0m)
		: Math.Max(leg.Strike - spot, 0m);

	#endregion

	/// <summary>Нога группы: знаковое количество, страйк, тип опциона и средняя цена открытого остатка.</summary>
	private readonly record struct Leg(decimal Quantity, decimal Strike, OptionType Type, decimal AverageOpenPrice);
}
