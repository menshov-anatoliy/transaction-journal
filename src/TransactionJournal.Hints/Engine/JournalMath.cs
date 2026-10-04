namespace TransactionJournal.Hints.Engine;

using System.Globalization;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Вычислительные помощники триггеров поверх движков домена: построночная
/// атрибуция реализованного PnL сделкам через FIFO-движок Domain (разность
/// результатов префиксов потока) и границы периодов недели/месяца/квартала.
/// Атрибуция O(n²) по числу сделок потока — для личного журнала объёмов
/// достаточно, а переиспользование FIFO-движка гарантирует те же результаты,
/// что и метрики экранов.
/// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public static class JournalMath
{
	/// <summary>Форматирует число факта инвариантно и без хвостовых нулей: 4.8 → «4.8», 3 → «3».</summary>
	/// <param name="value">Числовое значение факта.</param>
	public static string FormatNumber(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

	/// <summary>
	/// Атрибутирует сделке её долю реализованного PnL потока символа: разность
	/// FIFO-результатов префиксов «до сделки» и «со сделкой» — включает комиссию
	/// самой сделки, поэтому прибыльная по цене сделка с большой комиссией
	/// честно считается неплюсовой.
	/// </summary>
	/// <param name="orderedSymbolTrades">Сделки одного символа в хронологии исполнения.</param>
	/// <param name="index">Индекс атрибутируемой сделки в хронологии.</param>
	public static decimal FifoContribution(IReadOnlyList<TradeView> orderedSymbolTrades, int index)
	{
		var engine = new PositionFifoEngine();
		var before = index == 0 ? 0m : MatchPrefix(engine, orderedSymbolTrades, index).RealizedPnL;
		var after = MatchPrefix(engine, orderedSymbolTrades, index + 1).RealizedPnL;
		return after - before;
	}

	/// <summary>Границы периодов убытка (неделя/месяц/квартал), содержащих отметку as-of.</summary>
	/// <param name="asOf">Отметка as-of прохода.</param>
	public static IReadOnlyList<LossPeriod> LossPeriods(DateTimeOffset asOf)
	{
		var date = asOf.UtcDateTime.Date;

		// ISO-неделя с понедельника: ключ периода стабилен относительно культуры среды.
		var weekStart = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
		var weekEnd = weekStart.AddDays(7);
		var weekKey = $"{ISOWeek.GetYear(weekStart):0000}-W{ISOWeek.GetWeekOfYear(weekStart):00}";

		var monthStart = new DateTime(date.Year, date.Month, 1);
		var monthEnd = monthStart.AddMonths(1);
		var monthKey = $"{monthStart:yyyy-MM}";

		var quarterStartMonth = (date.Month - 1) / 3 * 3 + 1;
		var quarterStart = new DateTime(date.Year, quarterStartMonth, 1);
		var quarterEnd = quarterStart.AddMonths(3);
		var quarterKey = $"{quarterStart:yyyy}-Q{(quarterStartMonth - 1) / 3 + 1}";

		return
		[
			new LossPeriod("неделя", weekKey, weekStart, weekEnd),
			new LossPeriod("месяц", monthKey, monthStart, monthEnd),
			new LossPeriod("квартал", quarterKey, quarterStart, quarterEnd),
		];
	}

	/// <summary>Период убытка: название, ключ окна дедупа и полуинтервал [Start, End).</summary>
	public sealed record LossPeriod(string Name, string Key, DateTime Start, DateTime End)
	{
		/// <summary>Сделка исполнилась внутри периода.</summary>
		/// <param name="trade">Сделка хронологии журнала.</param>
		public bool Contains(TradeView trade)
		{
			var executed = trade.ExecutedAt.UtcDateTime;
			return executed >= Start && executed < End;
		}
	}

	/// <summary>FIFO-результат префикса потока символа — переиспользование движка Domain.</summary>
	private static PositionFifoResult MatchPrefix(PositionFifoEngine engine, IReadOnlyList<TradeView> trades, int count)
		=> engine.Match(trades
			.Take(count)
			.Select(trade => new PositionFifoEntry
			{
				At = trade.ExecutedAt,
				Kind = PositionFifoEntryKind.Trade,
				SourceKey = trade.ExecId,
				Quantity = trade.Quantity,
				Price = trade.Price,
				Fee = trade.Fee,
			}));
}
