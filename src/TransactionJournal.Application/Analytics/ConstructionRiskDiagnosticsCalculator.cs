using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Калькулятор диагностики незакрытых рисков конструкции. Ноги открытых
/// остатков классифицируются разбором символа и группируются по базовому
/// активу через все экспирации: паритет USDT/USDC группу не разрывает, а
/// правило реального риска (прикрепление линейной ноги к группе ранней
/// экспирации) к диагностике не применяется. По каждому базовому активу
/// считаются наклоны суммарного платежа ног на нижнем и верхнем хвостах оси
/// цены; покрытие распределяется детерминированно — встречные длинные опционы
/// в первую очередь закрывают фьючерсную часть (торговое правило владельца:
/// фьючерсы конструкции всегда закрыты по рискам опционами), остаток наклона
/// коротких опционов закрывается встречными длинными опционами и фьючерсами.
/// Незакрытый остаток наклона публикуется предупреждением с инструментом,
/// числом незакрытых контрактов и причиной; неразборчивый символ даёт
/// отдельное предупреждение о непроверенном покрытии, не маскируя остальные
/// проверки. Калькулятор чистый: хранилище не читает и котировок не запрашивает —
/// диагностика выводится только из структуры ног (тип опциона, базовый актив,
/// знаковый остаток), поэтому сбой котировок на неё не влияет.
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-coverage-by-underlying
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-slope-attribution
/// </summary>
public sealed class ConstructionRiskDiagnosticsCalculator
{
	/// <summary>
	/// Вычисляет предупреждения о незакрытых рисках конструкции из метрик её
	/// позиций. Ненулевые остатки классифицируются разбором символа — опцион
	/// или вечный линейный фьючерс; неразборчивый символ даёт собственное
	/// предупреждение, не прерывая диагноз остальных ног. По каждому базовому
	/// активу на нижнем хвосте (цена к нулю) закрываются наклоны длинных
	/// фьючерсов и коротких путов, на верхнем (цена к бесконечности) — наклоны
	/// коротких фьючерсов и коротких коллов; частичное покрытие выражается
	/// числом незакрытых контрактов («1 из 2»), а длинный опцион без встречной
	/// ноги не предупреждается: его убыток ограничен премией. Без открытых
	/// остатков набор предупреждений пуст.
	/// </summary>
	/// <param name="positions">Метрики позиций конструкции.</param>
	/// <returns>Предупреждения о незакрытых рисках; порядок детерминирован.</returns>
	/// <exception cref="ArgumentNullException">Позиции не заданы.</exception>
	public IReadOnlyList<ConstructionRiskDiagnostic> Calculate(IEnumerable<PositionMetrics> positions)
	{
		ArgumentNullException.ThrowIfNull(positions);

		var openPositions = positions.Where(position => position.Residual != 0m).ToList();

		// Без открытых остатков незакрытых рисков нет: закрытая и архивная
		// конструкции имеют пустой набор предупреждений, а чисто фьючерсная
		// конструкция ниже диагностируется на общих основаниях.
		// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
		if (openPositions.Count == 0)
		{
			return Array.Empty<ConstructionRiskDiagnostic>();
		}

		var warnings = new List<ConstructionRiskDiagnostic>();
		var groups = new Dictionary<string, UnderlyingGroup>(StringComparer.Ordinal);
		foreach (var position in openPositions)
		{
			// Символы разбираются теми же парсерами, что и в реальном риске:
			// формат символа один, различие слоёв — только в правиле группировки.
			if (OptionSymbolParser.TryParse(position.Symbol, out var optionParts) && optionParts is not null)
			{
				// Опцион попадает в группу своего базового актива без экспирации
				// и котируемой валюты: покрывает фьючерсы через все экспирации,
				// паритет USDT/USDC группу не разрывает.
				GetGroup(groups, optionParts.BaseCoin).AddOption(position.Symbol, optionParts.Type, position.Residual);
				continue;
			}

			if (LinearSymbolParser.TryParse(position.Symbol, out var linearParts) && linearParts is not null)
			{
				// Линейная нога остаётся в группе своего базового актива,
				// а не прикрепляется к ранней экспирации, как в реальном риске.
				GetGroup(groups, linearParts.BaseCoin).AddFuture(position.Symbol, position.Residual);
				continue;
			}

			// Неразборчивый символ открытого остатка предупреждается как
			// непроверенное покрытие и выпадает из диагноза, не маскируя
			// проверки остальных ног.
			// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-unparseable-symbol
			warnings.Add(ConstructionRiskDiagnostic.UnverifiedCoverage(position.Symbol));
		}

		foreach (var group in groups.Values)
		{
			DiagnoseTail(group, isBottomTail: true, warnings);
			DiagnoseTail(group, isBottomTail: false, warnings);
		}

		return warnings;
	}

	#region Вспомогательные методы

	/// <summary>
	/// Диагностирует один хвост одного базового актива. На нижнем хвосте (цена
	/// к нулю) теряют длинные фьючерсы и короткие путы, закрывают их длинные
	/// путы и короткие фьючерсы; на верхнем хвосте (цена к бесконечности)
	/// теряют короткие фьючерсы и короткие коллы, закрывают их длинные коллы
	/// и длинные фьючерсы. Встречные длинные опционы сначала закрывают
	/// фьючерсную часть, их остаток — короткие опционы; затем встречные
	/// фьючерсы закрывают остаток фьючерсной части, их остаток — короткие
	/// опционы. Каждый незакрытый остаток наклона даёт предупреждение своего
	/// инструмента с числом незакрытых контрактов и причиной.
	/// </summary>
	private static void DiagnoseTail(UnderlyingGroup group, bool isBottomTail, List<ConstructionRiskDiagnostic> warnings)
	{
		// Незакрытый риск копится по инструментам: фьючерс теряет на хвосте
		// своим знаком, короткий опцион — только своего типа хвоста (пут снизу,
		// колл сверху); длинный опцион на чужом хвосте плоски и потому
		// не предупреждается — его убыток ограничен премией.
		// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-slope-attribution
		var riskyFutures = group.OrderedFutures()
			.Where(leg => isBottomTail ? leg.Quantity > 0m : leg.Quantity < 0m)
			.Select(leg => new RiskyLeg(leg.Symbol, leg.Quantity))
			.ToList();
		var riskyShortOptions = group.OrderedOptions()
			.Where(leg => leg.Quantity < 0m && (isBottomTail ? leg.Type == OptionType.Put : leg.Type == OptionType.Call))
			.Select(leg => new RiskyLeg(leg.Symbol, leg.Quantity, leg.Type))
			.ToList();
		if (riskyFutures.Count == 0 && riskyShortOptions.Count == 0)
		{
			return;
		}

		// Покрывающая способность хвоста: длинные опционы своего типа и
		// встречные фьючерсы, один контракт закрывает один контракт.
		var longOptionCapacity = group.OrderedOptions()
			.Where(leg => leg.Quantity > 0m && (isBottomTail ? leg.Type == OptionType.Put : leg.Type == OptionType.Call))
			.Sum(leg => leg.Quantity);
		var futuresCapacity = group.OrderedFutures()
			.Where(leg => isBottomTail ? leg.Quantity < 0m : leg.Quantity > 0m)
			.Sum(leg => Math.Abs(leg.Quantity));

		// Детерминированный приоритет покрытия: встречные длинные опционы
		// в первую очередь закрывают фьючерсную часть (торговое правило
		// владельца), их остаток — короткие опционы; затем встречные фьючерсы
		// закрывают остаток фьючерсной части, их остаток — короткие опционы.
		var remaining = Allocate(riskyFutures, longOptionCapacity);
		remaining = Allocate(riskyShortOptions, remaining);
		remaining = Allocate(riskyFutures, futuresCapacity);
		Allocate(riskyShortOptions, remaining);

		// Причина по типу незакрытой ноги: короткий фьючерс на верхнем хвосте
		// теряет без предела, длинный на нижнем — в пределах цены входа;
		// короткий пут снизу ограничен нулём цены, короткий колл сверху —
		// не ограничен.
		var futureReason = isBottomTail ? ConstructionRiskReason.BoundedByEntryPrice : ConstructionRiskReason.UnboundedLoss;
		foreach (var leg in riskyFutures)
		{
			if (leg.Remaining > 0m)
			{
				warnings.Add(ConstructionRiskDiagnostic.UncoveredFuture(leg.Symbol, leg.Remaining, leg.Total, futureReason));
			}
		}

		var optionReason = isBottomTail ? ConstructionRiskReason.BoundedByZeroPrice : ConstructionRiskReason.UnboundedLoss;
		foreach (var leg in riskyShortOptions)
		{
			if (leg.Remaining > 0m)
			{
				warnings.Add(ConstructionRiskDiagnostic.UncoveredShortOption(
					leg.Symbol, leg.Remaining, leg.Total, leg.OptionKind!.Value, optionReason));
			}
		}
	}

	/// <summary>Распределяет покрывающую способность по инструментам в порядке их перечисления; возвращает неиспользованный остаток.</summary>
	private static decimal Allocate(List<RiskyLeg> legs, decimal capacity)
	{
		foreach (var leg in legs)
		{
			if (capacity <= 0m)
			{
				break;
			}

			var taken = Math.Min(leg.Remaining, capacity);
			leg.Remaining -= taken;
			capacity -= taken;
		}

		return capacity;
	}

	/// <summary>Возвращает группу базового актива, создавая её при первом появлении.</summary>
	private static UnderlyingGroup GetGroup(Dictionary<string, UnderlyingGroup> groups, string baseCoin)
	{
		if (groups.TryGetValue(baseCoin, out var group) == false)
		{
			group = new UnderlyingGroup();
			groups.Add(baseCoin, group);
		}

		return group;
	}

	#endregion

	/// <summary>Незакрытая нога хвоста: инструмент, полное число контрактов, тип опциона и незакрытый остаток.</summary>
	private sealed class RiskyLeg
	{
		public RiskyLeg(string symbol, decimal quantity, OptionType? optionKind = null)
		{
			Symbol = symbol;
			Total = Math.Abs(quantity);
			OptionKind = optionKind;
			Remaining = Total;
		}

		/// <summary>Инструмент ноги.</summary>
		public string Symbol { get; }

		/// <summary>Полное число контрактов ноги — знаменатель частичного покрытия.</summary>
		public decimal Total { get; }

		/// <summary>Тип опциона; у фьючерсной ноги отсутствует.</summary>
		public OptionType? OptionKind { get; }

		/// <summary>Число ещё не закрытых контрактов ноги.</summary>
		public decimal Remaining { get; set; }
	}

	/// <summary>Группа одного базового актива: линейные и опционные ноги через все экспирации и обе котируемые валюты.</summary>
	private sealed class UnderlyingGroup
	{
		private readonly Dictionary<string, decimal> _futures = new(StringComparer.Ordinal);
		private readonly Dictionary<string, (OptionType Type, decimal Quantity)> _options = new(StringComparer.Ordinal);

		/// <summary>Добавляет знаковое количество линейной ноги инструмента.</summary>
		public void AddFuture(string symbol, decimal quantity)
		{
			_futures[symbol] = _futures.GetValueOrDefault(symbol) + quantity;
		}

		/// <summary>Добавляет знаковое количество опционной ноги инструмента.</summary>
		public void AddOption(string symbol, OptionType type, decimal quantity)
		{
			var existing = _options.GetValueOrDefault(symbol);
			_options[symbol] = (type, existing.Quantity + quantity);
		}

		/// <summary>Линейные ноги группы в детерминированном порядке символов.</summary>
		public List<(string Symbol, decimal Quantity)> OrderedFutures() =>
			_futures.Select(pair => (pair.Key, pair.Value)).OrderBy(leg => leg.Key, StringComparer.Ordinal).ToList();

		/// <summary>Опционные ноги группы в детерминированном порядке символов.</summary>
		public List<(string Symbol, OptionType Type, decimal Quantity)> OrderedOptions() =>
			_options
				.Select(pair => (pair.Key, pair.Value.Type, pair.Value.Quantity))
				.OrderBy(leg => leg.Key, StringComparer.Ordinal)
				.ToList();
	}
}
