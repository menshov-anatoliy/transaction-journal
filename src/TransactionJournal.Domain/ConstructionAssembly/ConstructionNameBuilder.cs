using System.Globalization;
using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Построение детерминированного имени конструкции из состава живых опционных
/// ног: «{Актив} {вид[ N:M]} {доска} {страйк(и)}». Вид — лексика трейдера:
/// «стреддл» и «стреддл с акцентом вверх/вниз N:M» (колл+пут одной пары
/// «страйк + доска»), «стренгл» и «стренгл с акцентом» (колл+пут разных пар),
/// «колл-спред»/«пут-спред» с соотношением при неравных размерах (один тип,
/// два страйка), «направленная CALL/PUT» (одиночная нога). Размеры — модули
/// остатков ног; N:M — целочисленное сокращение по НОД при целых размерах,
/// направление акцента — по доминирующей ноге. Имя вычисляется только по
/// ненулевым ногам; при полном обнулении выводить имя не из чего — сохранение
/// последнего производного имени лежит на вызывающем (сборке). Страйки
/// упорядочены по возрастанию, доска — в формате символа dMMMyy. Имя чисто
/// презентационное: на группировку оно не влияет.
// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
/// Traceability: adr:docs/adr/0005-derived-construction-naming.md#derived-construction-naming
/// </summary>
public static class ConstructionNameBuilder
{
	/// <summary>
	/// Строит имя конструкции из ног с количествами: повторные ноги одного символа
	/// схлопываются суммой количеств, нулевые (в том числе взаимно погасившиеся)
	/// в именовании не участвуют.
	/// </summary>
	/// <param name="baseCoin">Базовый актив, например ETH или BTC.</param>
	/// <param name="legs">Ноги конструкции с знаковыми остатками.</param>
	/// <exception cref="ArgumentException">Живых (ненулевых) ног нет.</exception>
	public static string BuildName(string baseCoin, IReadOnlyCollection<PlannedLeg> legs)
	{
		ArgumentNullException.ThrowIfNull(legs);

		// Имя выводится только из ненулевых опционных ног: погашенная нога смысла
		// конструкции не меняет, а при полном обнулении ног имя не выводится —
		// за сохранение последнего производного имени отвечает пересчёт имени сборки.
		// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
		var liveLegs = legs
			.GroupBy(leg => leg.Symbol, StringComparer.Ordinal)
			.Select(group =>
			{
				var head = group.First();
				return new PlannedLeg
				{
					Symbol = head.Symbol,
					Strike = head.Strike,
					BoardExpiryDate = head.BoardExpiryDate,
					Type = head.Type,
					Quantity = group.Sum(leg => leg.Quantity),
				};
			})
			.Where(leg => leg.Quantity != 0m)
			.OrderBy(leg => leg.BoardExpiryDate)
			.ThenBy(leg => leg.Strike)
			.ThenBy(leg => leg.Type)
			.ToList();
		if (liveLegs.Count == 0)
		{
			throw new ArgumentException("Для имени конструкции нужна хотя бы одна ненулевая опционная нога.", nameof(legs));
		}

		// Вид конструкции определяется составом живых ног и соотношением их размеров:
		// колл+пут на одной паре «страйк + доска» — стреддл, на разных — стренгл
		// (оба — с акцентом при неравных размерах); одиночный тип с одним страйком —
		// направленная CALL/PUT, с двумя страйками — спред с соотношением при
		// неравных размерах.
		var hasCall = liveLegs.Any(leg => leg.Type == OptionType.Call);
		var hasPut = liveLegs.Any(leg => leg.Type == OptionType.Put);
		string kind;
		if (hasCall && hasPut)
		{
			var strikeBoardPairs = liveLegs
				.Select(leg => (leg.Strike, leg.BoardExpiryDate))
				.Distinct()
				.Count();
			kind = BuildCombinedKind(liveLegs, strikeBoardPairs == 1 ? "стреддл" : "стренгл");
		}
		else if (liveLegs.Select(leg => leg.Strike).Distinct().Count() == 1)
		{
			kind = hasCall ? "направленная CALL" : "направленная PUT";
		}
		else
		{
			kind = BuildSpreadKind(liveLegs, hasCall ? "колл-спред" : "пут-спред");
		}

		// Доски перечисляются по возрастанию экспирации; внутри доски страйки идут через
		// «/» по возрастанию — имя читается слева направо от ближней доски к дальней.
		var segments = liveLegs
			.GroupBy(leg => leg.BoardExpiryDate.Date)
			.OrderBy(group => group.Key)
			.Select(group => $"{FormatBoard(group.Key)} {FormatStrikes(group)}");
		return $"{baseCoin} {kind} {string.Join("/", segments)}";
	}

	/// <summary>
	/// Вид конструкции из колла и пута: при равных суммарных размерах ног — базовое
	/// имя, при неравных — акцент по доминирующей ноге («вверх» — колл, «вниз» —
	/// пут) с соотношением «доминирующий размер : подчинённый размер».
	/// </summary>
	private static string BuildCombinedKind(IReadOnlyList<PlannedLeg> legs, string baseKind)
	{
		var callSize = legs.Where(leg => leg.Type == OptionType.Call).Sum(leg => Math.Abs(leg.Quantity));
		var putSize = legs.Where(leg => leg.Type == OptionType.Put).Sum(leg => Math.Abs(leg.Quantity));
		if (callSize == putSize)
		{
			return baseKind;
		}

		var accentUp = callSize > putSize;
		var dominant = Math.Max(callSize, putSize);
		var subordinate = Math.Min(callSize, putSize);
		return $"{baseKind} с акцентом {(accentUp ? "вверх" : "вниз")} {FormatRatio(dominant, subordinate)}";
	}

	/// <summary>
	/// Вид спреда из ног одного типа: соотношение N:M выводится только для двух
	/// страйков при неравных суммарных размерах — в порядке возрастания страйков.
	/// </summary>
	private static string BuildSpreadKind(IReadOnlyList<PlannedLeg> legs, string baseKind)
	{
		var strikes = legs
			.GroupBy(leg => leg.Strike)
			.OrderBy(group => group.Key)
			.ToList();
		if (strikes.Count != 2)
		{
			return baseKind;
		}

		var firstSize = strikes[0].Sum(leg => Math.Abs(leg.Quantity));
		var secondSize = strikes[1].Sum(leg => Math.Abs(leg.Quantity));
		return firstSize == secondSize
			? baseKind
			: $"{baseKind} {FormatRatio(firstSize, secondSize)}";
	}

	/// <summary>Форматирует соотношение размеров N:M: целые размеры сокращаются по НОД, дробные выводятся как есть.</summary>
	private static string FormatRatio(decimal first, decimal second)
	{
		if (IsWhole(first) && IsWhole(second)
			&& first <= long.MaxValue && second <= long.MaxValue)
		{
			var wholeFirst = (long)first;
			var wholeSecond = (long)second;
			var gcd = GreatestCommonDivisor(wholeFirst, wholeSecond);
			return $"{wholeFirst / gcd}:{wholeSecond / gcd}";
		}

		return $"{FormatSize(first)}:{FormatSize(second)}";
	}

	/// <summary>Проверяет, что десятичное значение целое.</summary>
	private static bool IsWhole(decimal value) => decimal.Truncate(value) == value;

	/// <summary>Наибольший общий делитель двух положительных чисел.</summary>
	private static long GreatestCommonDivisor(long first, long second)
	{
		while (second != 0)
		{
			var remainder = first % second;
			first = second;
			second = remainder;
		}

		return first;
	}

	/// <summary>Форматирует размер без хвостовых нулей, инвариантно.</summary>
	private static string FormatSize(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);

	/// <summary>Форматирует доску в стиле символа Bybit: день без ведущего нуля, месяц заглавными, две цифры года.</summary>
	private static string FormatBoard(DateTime board) =>
		board.ToString("dMMMyy", CultureInfo.InvariantCulture).ToUpperInvariant();

	/// <summary>Форматирует страйки одной доски по возрастанию, без хвостовых нулей, инвариантным разделителем.</summary>
	private static string FormatStrikes(IEnumerable<PlannedLeg> legs) =>
		string.Join("/", legs
			.Select(leg => leg.Strike)
			.Distinct()
			.OrderBy(strike => strike)
			.Select(strike => strike.ToString("0.########", CultureInfo.InvariantCulture)));
}
