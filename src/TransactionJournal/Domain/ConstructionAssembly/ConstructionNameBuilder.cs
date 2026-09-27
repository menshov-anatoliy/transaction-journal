using System.Globalization;
using TransactionJournal.Materialization;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Построение детерминированного имени конструкции из состава открывающего
/// окна: «{Актив} {стреддл|стренгл|колл-спред|пут-спред|CALL|PUT} {доска}
/// {страйк(и)}». Вид выводится из набора ног (коллы/путы, совпадение страйков),
/// страйки упорядочены по возрастанию, доска — в формате символа dMMMyy.
/// Имя чисто презентационное: на группировку оно не влияет.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-full-rebuild-semantics
/// Traceability: change:add-construction-auto-assembly/design#d4
/// </summary>
public static class ConstructionNameBuilder
{
	/// <summary>
	/// Строит имя конструкции из разобранных ног открывающего окна; повторные ноги схлопываются.
	/// </summary>
	/// <param name="baseCoin">Базовый актив, например ETH или BTC.</param>
	/// <param name="legs">Разобранные ноги открывающего окна.</param>
	public static string BuildName(string baseCoin, IReadOnlyCollection<OptionSymbolParts> legs)
	{
		ArgumentNullException.ThrowIfNull(legs);
		if (legs.Count == 0)
		{
			throw new ArgumentException("Для имени конструкции нужна хотя бы одна нога открывающего окна.", nameof(legs));
		}

		var distinctLegs = legs
			.Distinct()
			.OrderBy(leg => leg.ExpiryDate)
			.ThenBy(leg => leg.Strike)
			.ThenBy(leg => leg.Type)
			.ToList();

		// Вид конструкции определяется составом ног: колл+пут на одном страйке и доске —
		// стреддл, колл+пут на разных — стренгл; одиночный тип с двумя страйками — спред,
		// иначе направленная позиция CALL или PUT.
		var hasCall = distinctLegs.Any(leg => leg.Type == OptionType.Call);
		var hasPut = distinctLegs.Any(leg => leg.Type == OptionType.Put);
		string kind;
		if (hasCall && hasPut)
		{
			var strikeBoardPairs = distinctLegs
				.Select(leg => (leg.Strike, leg.ExpiryDate))
				.Distinct()
				.Count();
			kind = strikeBoardPairs == 1 ? "стреддл" : "стренгл";
		}
		else
		{
			var strikeCount = distinctLegs.Select(leg => leg.Strike).Distinct().Count();
			kind = hasCall
				? (strikeCount == 2 ? "колл-спред" : "CALL")
				: (strikeCount == 2 ? "пут-спред" : "PUT");
		}

		// Доски перечисляются по возрастанию экспирации; внутри доски страйки идут через
		// «/» по возрастанию — имя читается слева направо от ближней доски к дальней.
		var segments = distinctLegs
			.GroupBy(leg => leg.ExpiryDate.Date)
			.OrderBy(group => group.Key)
			.Select(group => $"{FormatBoard(group.Key)} {FormatStrikes(group)}");
		return $"{baseCoin} {kind} {string.Join("/", segments)}";
	}

	/// <summary>Форматирует доску в стиле символа Bybit: день без ведущего нуля, месяц заглавными, две цифры года.</summary>
	private static string FormatBoard(DateTime board) =>
		board.ToString("dMMMyy", CultureInfo.InvariantCulture).ToUpperInvariant();

	/// <summary>Форматирует страйки одной доски по возрастанию, без хвостовых нулей, инвариантным разделителем.</summary>
	private static string FormatStrikes(IEnumerable<OptionSymbolParts> legs) =>
		string.Join("/", legs
			.Select(leg => leg.Strike)
			.Distinct()
			.OrderBy(strike => strike)
			.Select(strike => strike.ToString("0.########", CultureInfo.InvariantCulture)));
}
