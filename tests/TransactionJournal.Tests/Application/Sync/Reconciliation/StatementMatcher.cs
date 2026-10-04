using System.Globalization;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;

namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>
/// Движок сопоставления строк выгрузки с сырыми записями журнала:
/// TRADE — двухэтапный мультимножественный матчинг по атрибутам,
/// DELIVERY — сопоставление по инструменту с допуском времени.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-trade-rows-match-executions
/// </summary>
public static class StatementMatcher
{
	/// <summary>Допуск времени точных и остаточных пар TRADE. Откалиброван по живому
	/// прогону: серия из пяти исполнений одного ордера XAUT-30OCT26-4400-C растянута
	/// по времени биржи примерно на 4.8 секунды, допуск ±2 сек оставлял крайние
	/// записи серий непарными.
	/// Traceability: change:reconcile-bybit-statement/design#d9</summary>
	public static readonly TimeSpan TradeTimeTolerance = TimeSpan.FromSeconds(5);

	/// <summary>Допуск времени пар DELIVERY.</summary>
	public static readonly TimeSpan DeliveryTimeTolerance = TimeSpan.FromMinutes(10);

	/// <summary>Относительный допуск комиссии по модулю.</summary>
	public const decimal FeeRelativeTolerance = 0.005m;

	/// <summary>
	/// Признак того, что запись исполнения входит в сверяемую TRADE-вселенную:
	/// калибровка живого прогона показала, что историческая выгрузка исполнения
	/// linear-категории содержит записи Funding, соответствующие SETTLEMENT-строкам
	/// выгрузки вне области сверки; оставить их в матчинге — значит получить
	/// ложные «отсутствует в выгрузке». Сверяемые записи — только исполненные
	/// сделки (execType=Trade).
	/// Traceability: change:reconcile-bybit-statement/design#d9
	/// </summary>
	public static bool IsTradeExecution(JournalExecution execution) =>
		string.Equals(execution.ExecType, "Trade", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Сопоставляет торговые строки выгрузки с записями исполнения. Этап 1 — точные
	/// ключи (инструмент, сторона, количество, цена) с выбором ближайшего по времени
	/// в допуске; этап 2 — остатки по (инструмент, сторона) образуют пары «расхождение
	/// атрибутов»; итоговые остатки дают классификации «отсутствует в журнале» и
	/// «отсутствует в выгрузке». Каждая строка и запись участвуют не более одного раза.
	/// Записи вне сверяемой вселенной (execType не Trade) исключаются до матчинга.
	/// </summary>
	/// <param name="rows">Торговые строки выгрузки.</param>
	/// <param name="executions">Записи исполнения журнала в диапазоне сверки.</param>
	/// <returns>Итог сопоставления с классификациями и агрегатами комиссий.</returns>
	public static TradeReconciliationResult MatchTrades(IReadOnlyList<StatementRow> rows, IReadOnlyList<JournalExecution> executions)
	{
		ArgumentNullException.ThrowIfNull(rows);
		ArgumentNullException.ThrowIfNull(executions);

		// Список доступен для изъятия пар: изъятая запись не может быть сопоставлена дважды.
		var available = new List<JournalExecution>(executions.Where(IsTradeExecution));
		var matched = new List<TradeMatchPair>();
		var attributeMismatches = new List<TradeAttributeMismatch>();
		var stageOneUnpaired = new List<StatementRow>();

		// Этап 1: точные ключи (symbol, side, qty, price) с ближайшим временем в допуске.
		// Traceability: change:reconcile-bybit-statement/design#d4
		foreach (var row in rows)
		{
			var pair = TakeNearest(
				available,
				execution => SymbolsEqual(execution.Symbol, row.Contract)
					&& SidesEqual(execution.Side, row.Direction)
					&& execution.Quantity == row.Quantity
					&& execution.Price == row.FilledPrice,
				execution => execution.TimeUtc,
				row.TimeUtc,
				TradeTimeTolerance);
			if (pair != null)
			{
				// Точная пара по ключу всё равно сверяется по комиссии: расхождение
				// за пределами допуска переводит пару в классификацию расхождений атрибутов.
				// Traceability: change:reconcile-bybit-statement/design#d5
				var feeMismatch = CollectFeeMismatch(row, pair);
				if (feeMismatch.Count > 0)
				{
					attributeMismatches.Add(new TradeAttributeMismatch(row, pair, feeMismatch));
				}
				else
				{
					matched.Add(new TradeMatchPair(row, pair));
				}
			}
			else
			{
				stageOneUnpaired.Add(row);
			}
		}

		// Этап 2: остатки по (symbol, side) становятся парами «расхождение атрибутов»,
		// чтобы расхождение количества или цены не дало пары фиктивных «отсутствий».
		var missingInJournal = new List<StatementRow>();
		foreach (var row in stageOneUnpaired)
		{
			var residual = TakeNearest(
				available,
				execution => SymbolsEqual(execution.Symbol, row.Contract) && SidesEqual(execution.Side, row.Direction),
				execution => execution.TimeUtc,
				row.TimeUtc,
				TradeTimeTolerance);
			if (residual != null)
			{
				attributeMismatches.Add(new TradeAttributeMismatch(row, residual, CollectTradeFieldMismatches(row, residual)));
			}
			else
			{
				missingInJournal.Add(row);
			}
		}

		return new TradeReconciliationResult(
			Matched: matched,
			AttributeMismatches: attributeMismatches,
			MissingInJournal: missingInJournal,
			MissingInStatement: available.ToList(),
			FeeAggregates: BuildFeeAggregates(matched, attributeMismatches));
	}

	/// <summary>
	/// Сопоставляет delivery-строки выгрузки с delivery-записями журнала по инструменту
	/// с допуском времени; количество пары сверяется, когда обе стороны его раскрывают.
	/// </summary>
	/// <param name="rows">Delivery-строки выгрузки.</param>
	/// <param name="deliveries">Delivery-записи журнала в диапазоне сверки.</param>
	/// <returns>Итог сопоставления с классификациями.</returns>
	public static DeliveryReconciliationResult MatchDeliveries(IReadOnlyList<StatementRow> rows, IReadOnlyList<JournalDelivery> deliveries)
	{
		ArgumentNullException.ThrowIfNull(rows);
		ArgumentNullException.ThrowIfNull(deliveries);

		// Сопоставление по символу с ближайшим временем delivery в допуске ±10 минут.
		// Traceability: change:reconcile-bybit-statement/design#d6
		var available = new List<JournalDelivery>(deliveries);
		var matched = new List<DeliveryMatchPair>();
		var quantityMismatches = new List<DeliveryQuantityMismatch>();
		var missingInJournal = new List<StatementRow>();
		foreach (var row in rows)
		{
			var pair = TakeNearest(
				available,
				delivery => SymbolsEqual(delivery.Symbol, row.Contract),
				delivery => delivery.TimeUtc,
				row.TimeUtc,
				DeliveryTimeTolerance);
			if (pair == null)
			{
				missingInJournal.Add(row);
				continue;
			}

			matched.Add(new DeliveryMatchPair(row, pair));
			if (row.Quantity != null && pair.Quantity != null && row.Quantity != pair.Quantity)
			{
				quantityMismatches.Add(new DeliveryQuantityMismatch(
					row, pair, FormatValue(row.Quantity), FormatValue(pair.Quantity)));
			}
		}

		return new DeliveryReconciliationResult(
			Matched: matched,
			QuantityMismatches: quantityMismatches,
			MissingInJournal: missingInJournal,
			MissingInStatement: available.ToList());
	}

	/// <summary>
	/// Проверяет согласованность комиссий пары по модулю с относительным допуском:
	/// знаковые конвенции сторон различаются, поэтому сравниваются абсолютные значения.
	/// </summary>
	/// <param name="statementFee">Комиссия строки выгрузки.</param>
	/// <param name="journalFee">Комиссия записи журнала.</param>
	/// <returns>Признак согласованности комиссий.</returns>
	public static bool AreFeesConsistent(decimal statementFee, decimal journalFee)
	{
		var statementAbs = Math.Abs(statementFee);
		var journalAbs = Math.Abs(journalFee);
		var reference = Math.Max(statementAbs, journalAbs);
		return Math.Abs(statementAbs - journalAbs) <= reference * FeeRelativeTolerance;
	}

	/// <summary>
	/// Изымает из списка кандидатов ближайший по времени элемент, удовлетворяющий
	/// предикату и укладывающийся в допуск; изъятый элемент повторно не участвует.
	/// </summary>
	/// <typeparam name="T">Тип кандидата.</typeparam>
	/// <param name="candidates">Список доступных кандидатов, мутируется изъятием.</param>
	/// <param name="predicate">Предикат пригодности кандидата.</param>
	/// <param name="timeSelector">Селектор времени кандидата.</param>
	/// <param name="timeUtc">Время строки, к которой подбирается пара.</param>
	/// <param name="tolerance">Допуск времени.</param>
	/// <returns>Ближайший кандидат или null, когда пригодных в допуске нет.</returns>
	private static T? TakeNearest<T>(
		List<T> candidates,
		Func<T, bool> predicate,
		Func<T, DateTime> timeSelector,
		DateTime timeUtc,
		TimeSpan tolerance)
		where T : class
	{
		T? best = null;
		var bestDelta = long.MaxValue;
		foreach (var candidate in candidates)
		{
			if (predicate(candidate) == false)
			{
				continue;
			}

			var delta = Math.Abs(timeSelector(candidate).Ticks - timeUtc.Ticks);
			if (delta > tolerance.Ticks)
			{
				continue;
			}

			if (delta < bestDelta)
			{
				bestDelta = delta;
				best = candidate;
			}
		}

		if (best != null)
		{
			candidates.Remove(best);
		}

		return best;
	}

	/// <summary>
	/// Собирает расхождения атрибутов остаточной пары: количество, цена и комиссия,
	/// когда обе стороны раскрывают значение.
	/// </summary>
	/// <param name="row">Строка выгрузки.</param>
	/// <param name="execution">Запись исполнения.</param>
	/// <returns>Перечень разошедшихся атрибутов.</returns>
	private static List<TradeFieldMismatch> CollectTradeFieldMismatches(StatementRow row, JournalExecution execution)
	{
		var fields = CollectFeeMismatch(row, execution);
		if (row.Quantity != null && execution.Quantity != null && row.Quantity != execution.Quantity)
		{
			fields.Add(new TradeFieldMismatch(
				TradeField.Quantity, FormatValue(row.Quantity), FormatValue(execution.Quantity)));
		}

		if (row.FilledPrice != null && execution.Price != null && row.FilledPrice != execution.Price)
		{
			fields.Add(new TradeFieldMismatch(
				TradeField.Price, FormatValue(row.FilledPrice), FormatValue(execution.Price)));
		}

		return fields;
	}

	/// <summary>
	/// Сверяет комиссию пары по модулю с относительным допуском: расхождение
	/// за пределами допуска даёт один элемент перечня, а неоднозначное поле
	/// комиссии любой из сторон — пустой перечень без построчной ошибки.
	/// </summary>
	/// <param name="row">Строка выгрузки.</param>
	/// <param name="execution">Запись исполнения.</param>
	/// <returns>Перечень с расхождением комиссии или пустой перечень.</returns>
	private static List<TradeFieldMismatch> CollectFeeMismatch(StatementRow row, JournalExecution execution)
	{
		var fields = new List<TradeFieldMismatch>();

		// Комиссия записи неоднозначна (нулевая или отсутствует) либо строка её не
		// раскрыла — постричная ошибка не ставится, пара попадает в агрегат по инструменту.
		// Traceability: change:reconcile-bybit-statement/design#d5
		if (execution.Fee is { } journalFee && journalFee != 0m && row.FeePaid is { } statementFee)
		{
			if (AreFeesConsistent(statementFee, journalFee) == false)
			{
				fields.Add(new TradeFieldMismatch(
					TradeField.Fee, FormatValue(statementFee), FormatValue(journalFee)));
			}
		}

		return fields;
	}

	/// <summary>
	/// Строит агрегаты комиссий по инструментам, где построчная сверка невозможна:
	/// суммы модулей комиссий сопоставленных пар выводятся в отчёт как fallback-сверка.
	/// </summary>
	/// <param name="matched">Точные пары этапа 1.</param>
	/// <param name="attributeMismatches">Пары этапа 2 с расхождением атрибутов.</param>
	/// <returns>Агрегаты по инструментам с неоднозначным полем комиссии.</returns>
	private static IReadOnlyList<FeeAggregate> BuildFeeAggregates(
		IReadOnlyList<TradeMatchPair> matched, IReadOnlyList<TradeAttributeMismatch> attributeMismatches)
	{
		return matched.Select(pair => (pair.Row, pair.Execution))
			.Concat(attributeMismatches.Select(mismatch => (mismatch.Row, mismatch.Execution)))
			.GroupBy(pair => pair.Execution.Symbol, StringComparer.Ordinal)
			.Select(group =>
			{
				var statementSum = group.Sum(pair => pair.Row.FeePaid is { } statementFee ? Math.Abs(statementFee) : 0m);
				var journalSum = group.Sum(pair => pair.Execution.Fee is { } journalFee ? Math.Abs(journalFee) : 0m);
				var uncompared = group.Count(pair =>
					pair.Execution.Fee is not { } journalFee || journalFee == 0m || pair.Row.FeePaid == null);
				return new FeeAggregate(group.Key, statementSum, journalSum, uncompared);
			})
			.Where(aggregate => aggregate.UncomparedPairs > 0)
			.OrderBy(aggregate => aggregate.Symbol, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>
	/// Сравнивает инструменты строки и записи: символы биржи совпадают буквально,
	/// регистр сравнивается без учёта для устойчивости к выгрузке.
	/// </summary>
	private static bool SymbolsEqual(string? statementSymbol, string? journalSymbol) =>
		string.Equals(statementSymbol?.Trim(), journalSymbol?.Trim(), StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Сравнивает стороны строки и записи: выгрузка пишет «BUY»/«SELL»,
	/// запись биржи — «Buy»/«Sell».
	/// </summary>
	private static bool SidesEqual(string? statementSide, string? journalSide) =>
		string.Equals(statementSide?.Trim(), journalSide?.Trim(), StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Форматирует значение атрибута для отчёта; null означает нераскрытое значение.
	/// </summary>
	private static string FormatValue(decimal? value) =>
		value?.ToString(CultureInfo.InvariantCulture) ?? "--";
}
