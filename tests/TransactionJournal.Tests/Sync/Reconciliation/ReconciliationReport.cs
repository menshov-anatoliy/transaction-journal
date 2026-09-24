using System.Globalization;
using System.Text;

namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>Количество строк выгрузки одного типа вне области синхронизации.</summary>
/// <param name="FileKind">Какой файл выгрузки породил тип.</param>
/// <param name="Type">Тип строки как в выгрузке.</param>
/// <param name="Count">Количество строк.</param>
public sealed record OutOfScopeTypeCount(StatementFileKind FileKind, string Type, int Count);

/// <summary>Количество строк UTA-файла неизвестного сверке типа.</summary>
/// <param name="Type">Тип строки как в выгрузке.</param>
/// <param name="Count">Количество строк.</param>
public sealed record UnknownTypeCount(string Type, int Count);

/// <summary>Итоги сверки по одному инструменту — контрольная агрегатная секция отчёта.</summary>
/// <param name="Symbol">Инструмент.</param>
/// <param name="StatementRows">Торговых строк выгрузки по инструменту.</param>
/// <param name="JournalRecords">Записей исполнения журнала в диапазоне по инструменту.</param>
/// <param name="MatchedPairs">Сопоставленных пар.</param>
/// <param name="AttributeMismatches">Пар с расхождением атрибутов.</param>
/// <param name="MissingInJournal">Строк без пары в журнале.</param>
/// <param name="MissingInStatement">Записей без пары в выгрузке.</param>
public sealed record InstrumentReconciliationTotals(
	string Symbol,
	int StatementRows,
	int JournalRecords,
	int MatchedPairs,
	int AttributeMismatches,
	int MissingInJournal,
	int MissingInStatement);

/// <summary>
/// Сводка прогона сверки: количества строк и записей, числа сопоставленных пар
/// и итоги расхождений по всем классификациям.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-reconciliation-report
/// </summary>
public sealed record ReconciliationSummary
{
	/// <summary>Начало диапазона сверки по строкам выгрузки, UTC.</summary>
	public required DateTime PeriodFromUtc { get; init; }

	/// <summary>Конец диапазона сверки по строкам выгрузки, UTC.</summary>
	public required DateTime PeriodToUtc { get; init; }

	/// <summary>Всего строк выгрузки.</summary>
	public required int StatementRowsTotal { get; init; }

	/// <summary>Торговых строк выгрузки.</summary>
	public required int TradeRows { get; init; }

	/// <summary>Delivery-строк выгрузки.</summary>
	public required int DeliveryRows { get; init; }

	/// <summary>Строк вне области синхронизации.</summary>
	public required int OutOfScopeRows { get; init; }

	/// <summary>Строк неизвестного сверке типа.</summary>
	public required int UnknownRows { get; init; }

	/// <summary>Записей исполнения журнала в диапазоне.</summary>
	public required int JournalExecutionsInRange { get; init; }

	/// <summary>Delivery-записей журнала в диапазоне.</summary>
	public required int JournalDeliveriesInRange { get; init; }

	/// <summary>Записей исполнения, исключённых по диапазону.</summary>
	public required int ExecutionsOutsideRange { get; init; }

	/// <summary>Delivery-записей, исключённых по диапазону.</summary>
	public required int DeliveriesOutsideRange { get; init; }

	/// <summary>Сопоставленных торговых пар.</summary>
	public required int MatchedTrades { get; init; }

	/// <summary>Торговых пар с расхождением атрибутов.</summary>
	public required int TradeAttributeMismatches { get; init; }

	/// <summary>Торговых строк, отсутствующих в журнале.</summary>
	public required int TradesMissingInJournal { get; init; }

	/// <summary>Записей исполнения, отсутствующих в выгрузке.</summary>
	public required int TradesMissingInStatement { get; init; }

	/// <summary>Сопоставленных delivery-пар.</summary>
	public required int MatchedDeliveries { get; init; }

	/// <summary>Delivery-пар с расхождением количества.</summary>
	public required int DeliveryQuantityMismatches { get; init; }

	/// <summary>Delivery-строк, отсутствующих в журнале.</summary>
	public required int DeliveriesMissingInJournal { get; init; }

	/// <summary>Delivery-записей, отсутствующих в выгрузке.</summary>
	public required int DeliveriesMissingInStatement { get; init; }

	/// <summary>Признак наличия хотя бы одного расхождения сверяемой области.</summary>
	public bool HasDiscrepancies =>
		TradeAttributeMismatches > 0
		|| TradesMissingInJournal > 0
		|| TradesMissingInStatement > 0
		|| DeliveryQuantityMismatches > 0
		|| DeliveriesMissingInJournal > 0
		|| DeliveriesMissingInStatement > 0;
}

/// <summary>
/// Отчёт сверки: классифицированные расхождения с атрибутами обеих сторон,
/// итоги по инструментам, сводка строк вне области журнала и текстовая
/// отрисовка для вывода теста и сообщения об ошибке.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-reconciliation-report
/// </summary>
public sealed record ReconciliationReport
{
	/// <summary>Формат времени в тексте отчёта.</summary>
	private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

	/// <summary>Сводка прогона.</summary>
	public required ReconciliationSummary Summary { get; init; }

	/// <summary>Итоги по инструментам.</summary>
	public required IReadOnlyList<InstrumentReconciliationTotals> Instruments { get; init; }

	/// <summary>Строки вне области синхронизации по типам.</summary>
	public required IReadOnlyList<OutOfScopeTypeCount> OutOfScopeTypes { get; init; }

	/// <summary>Неизвестные сверке типы UTA-файла.</summary>
	public required IReadOnlyList<UnknownTypeCount> UnknownTypes { get; init; }

	/// <summary>Итог сопоставления торговых строк.</summary>
	public required TradeReconciliationResult Trades { get; init; }

	/// <summary>Итог сопоставления delivery-строк.</summary>
	public required DeliveryReconciliationResult Deliveries { get; init; }

	/// <summary>
	/// Строит отчёт по строкам выгрузки, снимку журнала и итогам сопоставления.
	/// </summary>
	/// <param name="statementRows">Все строки выгрузки каталога.</param>
	/// <param name="journal">Снимок сырых записей журнала в диапазоне.</param>
	/// <param name="trades">Итог сопоставления торговых строк.</param>
	/// <param name="deliveries">Итог сопоставления delivery-строк.</param>
	/// <returns>Отчёт сверки.</returns>
	public static ReconciliationReport Build(
		IReadOnlyList<StatementRow> statementRows,
		JournalRawData journal,
		TradeReconciliationResult trades,
		DeliveryReconciliationResult deliveries)
	{
		ArgumentNullException.ThrowIfNull(statementRows);
		ArgumentNullException.ThrowIfNull(journal);
		ArgumentNullException.ThrowIfNull(trades);
		ArgumentNullException.ThrowIfNull(deliveries);

		return new ReconciliationReport
		{
			Summary = BuildSummary(statementRows, journal, trades, deliveries),
			Instruments = BuildInstrumentTotals(statementRows, journal, trades, deliveries),
			OutOfScopeTypes = BuildOutOfScopeTypes(statementRows),
			UnknownTypes = BuildUnknownTypes(statementRows),
			Trades = trades,
			Deliveries = deliveries,
		};
	}

	/// <summary>
	/// Отрисовывает отчёт как структурированный текст: секция на классификацию,
	/// итоги по инструментам, сводка вне области и агрегаты комиссий.
	/// Traceability: change:reconcile-bybit-statement/design#d8
	/// </summary>
	/// <returns>Текст отчёта.</returns>
	public string Render()
	{
		var builder = new StringBuilder();
		var summary = Summary;
		builder.AppendLine("=== Сверка журнала с выгрузкой Bybit (AssetChangeDetails) ===");
		builder.AppendLine(string.Create(
			CultureInfo.InvariantCulture,
			$"Период строк выгрузки: {summary.PeriodFromUtc.ToString(TimeFormat, CultureInfo.InvariantCulture)} – {summary.PeriodToUtc.ToString(TimeFormat, CultureInfo.InvariantCulture)} UTC"));
		builder.AppendLine(string.Create(CultureInfo.InvariantCulture,
			$"Выгрузка: строк {summary.StatementRowsTotal} (TRADE {summary.TradeRows}, DELIVERY {summary.DeliveryRows}, вне области {summary.OutOfScopeRows}, неизвестные {summary.UnknownRows})"));
		builder.AppendLine(string.Create(CultureInfo.InvariantCulture,
			$"Журнал: исполнений в диапазоне {summary.JournalExecutionsInRange}, delivery-записей {summary.JournalDeliveriesInRange}; исключено по диапазону: исполнений {summary.ExecutionsOutsideRange}, delivery-записей {summary.DeliveriesOutsideRange}"));
		builder.AppendLine(string.Create(CultureInfo.InvariantCulture,
			$"Сопоставлено: TRADE {summary.MatchedTrades}, DELIVERY {summary.MatchedDeliveries}"));
		builder.AppendLine(string.Create(CultureInfo.InvariantCulture,
			$"Расхождения: отсутствует в журнале {summary.TradesMissingInJournal}+{summary.DeliveriesMissingInJournal}, отсутствует в выгрузке {summary.TradesMissingInStatement}+{summary.DeliveriesMissingInStatement}, расхождение атрибутов {summary.TradeAttributeMismatches}+{summary.DeliveryQuantityMismatches}"));

		AppendTradeSections(builder);
		AppendDeliverySections(builder);
		AppendInstrumentTotals(builder);
		AppendOutOfScopeSummary(builder);
		AppendFeeAggregates(builder);
		return builder.ToString();
	}

	#region Секции отчёта

	/// <summary>
	/// Добавляет торговые секции: отсутствует в журнале, отсутствует в выгрузке, расхождения атрибутов.
	/// </summary>
	private void AppendTradeSections(StringBuilder builder)
	{
		builder.AppendLine($"--- Отсутствует в журнале (TRADE): {Summary.TradesMissingInJournal} ---");
		if (Trades.MissingInJournal.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var row in Trades.MissingInJournal)
			{
				builder.AppendLine($"  {DescribeStatementRow(row)}");
			}
		}

		builder.AppendLine($"--- Отсутствует в выгрузке (исполнения): {Summary.TradesMissingInStatement} ---");
		if (Trades.MissingInStatement.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var execution in Trades.MissingInStatement)
			{
				builder.AppendLine($"  {DescribeExecution(execution)}");
			}
		}

		builder.AppendLine($"--- Расхождение атрибутов (TRADE): {Summary.TradeAttributeMismatches} ---");
		if (Trades.AttributeMismatches.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var mismatch in Trades.AttributeMismatches)
			{
				var fields = string.Join(
					"; ",
					mismatch.Fields.Select(field => $"{DescribeField(field.Field)}: выгрузка={field.StatementValue}, журнал={field.JournalValue}"));
				builder.AppendLine($"  {DescribeStatementRow(mismatch.Row)} | {fields}");
			}
		}
	}

	/// <summary>
	/// Добавляет delivery-секции отчёта.
	/// </summary>
	private void AppendDeliverySections(StringBuilder builder)
	{
		builder.AppendLine($"--- Отсутствует в журнале (DELIVERY): {Summary.DeliveriesMissingInJournal} ---");
		if (Deliveries.MissingInJournal.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var row in Deliveries.MissingInJournal)
			{
				builder.AppendLine($"  {DescribeStatementRow(row)}");
			}
		}

		builder.AppendLine($"--- Отсутствует в выгрузке (delivery-записи): {Summary.DeliveriesMissingInStatement} ---");
		if (Deliveries.MissingInStatement.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var delivery in Deliveries.MissingInStatement)
			{
				builder.AppendLine($"  {DescribeDelivery(delivery)}");
			}
		}

		builder.AppendLine($"--- Расхождение количества (DELIVERY): {Summary.DeliveryQuantityMismatches} ---");
		if (Deliveries.QuantityMismatches.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var mismatch in Deliveries.QuantityMismatches)
			{
				builder.AppendLine($"  {DescribeStatementRow(mismatch.Row)} | количество: выгрузка={mismatch.StatementValue}, журнал={mismatch.JournalValue}");
			}
		}
	}

	/// <summary>
	/// Добавляет контрольные итоги по инструментам.
	/// </summary>
	private void AppendInstrumentTotals(StringBuilder builder)
	{
		builder.AppendLine($"--- Итоги по инструментам: {Instruments.Count} ---");
		if (Instruments.Count == 0)
		{
			builder.AppendLine("нет");
			return;
		}

		foreach (var instrument in Instruments)
		{
			builder.AppendLine(string.Create(CultureInfo.InvariantCulture,
				$"  {instrument.Symbol}: строки={instrument.StatementRows}, журнал={instrument.JournalRecords}, пары={instrument.MatchedPairs}, расхождений={instrument.AttributeMismatches}, нет в журнале={instrument.MissingInJournal}, нет в выгрузке={instrument.MissingInStatement}"));
		}
	}

	/// <summary>
	/// Добавляет сводку строк вне области журнала по типам, включая неизвестные типы UTA-файла.
	/// </summary>
	private void AppendOutOfScopeSummary(StringBuilder builder)
	{
		builder.AppendLine("--- Вне области журнала ---");
		if (OutOfScopeTypes.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var type in OutOfScopeTypes)
			{
				var fileKind = type.FileKind == StatementFileKind.Uta ? "UTA" : "fund";
				builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {type.Type} ({fileKind}): {type.Count}"));
			}
		}

		builder.AppendLine($"--- Неизвестные типы UTA-файла: {UnknownTypes.Count} ---");
		if (UnknownTypes.Count == 0)
		{
			builder.AppendLine("нет");
		}
		else
		{
			foreach (var type in UnknownTypes)
			{
				builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {type.Type}: {type.Count}"));
			}
		}
	}

	/// <summary>
	/// Добавляет агрегированную сверку комиссий по инструментам с неоднозначным полем комиссии.
	/// </summary>
	private void AppendFeeAggregates(StringBuilder builder)
	{
		builder.AppendLine("--- Комиссии (агрегат по инструментам, построчная сверка неоднозначна) ---");
		if (Trades.FeeAggregates.Count == 0)
		{
			builder.AppendLine("нет");
			return;
		}

		foreach (var aggregate in Trades.FeeAggregates)
		{
			builder.AppendLine(string.Create(CultureInfo.InvariantCulture,
				$"  {aggregate.Symbol}: |комиссии| выгрузка={aggregate.StatementAbsFeeSum}, журнал={aggregate.JournalAbsFeeSum}, пар без построчной сверки={aggregate.UncomparedPairs}"));
		}
	}

	#endregion

	#region Описания элементов

	/// <summary>
	/// Описывает строку выгрузки атрибутами, достаточными для локализации.
	/// </summary>
	private static string DescribeStatementRow(StatementRow row) =>
		string.Create(CultureInfo.InvariantCulture,
			$"{row.TimeUtc.ToString(TimeFormat, CultureInfo.InvariantCulture)} | {row.Contract ?? row.Currency} | {row.Direction ?? "--"} | qty={Format(row.Quantity)} | price={Format(row.FilledPrice)} | fee={Format(row.FeePaid)} | {row.SourceFile}:{row.LineNumber}");

	/// <summary>
	/// Описывает запись исполнения атрибутами, достаточными для локализации.
	/// </summary>
	private static string DescribeExecution(JournalExecution execution) =>
		string.Create(CultureInfo.InvariantCulture,
			$"{execution.TimeUtc.ToString(TimeFormat, CultureInfo.InvariantCulture)} | {execution.Symbol} | {execution.Side} | qty={Format(execution.Quantity)} | price={Format(execution.Price)} | fee={Format(execution.Fee)} | execId={execution.ExecId}");

	/// <summary>
	/// Описывает delivery-запись атрибутами, достаточными для локализации.
	/// </summary>
	private static string DescribeDelivery(JournalDelivery delivery) =>
		string.Create(CultureInfo.InvariantCulture,
			$"{delivery.TimeUtc.ToString(TimeFormat, CultureInfo.InvariantCulture)} | {delivery.Symbol} | qty={Format(delivery.Quantity)} | id={delivery.Id}");

	/// <summary>
	/// Возвращает человеческое имя атрибута для текста отчёта.
	/// </summary>
	private static string DescribeField(TradeField field) => field switch
	{
		TradeField.Quantity => "количество",
		TradeField.Price => "цена",
		TradeField.Fee => "комиссия",
		_ => field.ToString(),
	};

	/// <summary>
	/// Форматирует значение для текста отчёта; null означает нераскрытое значение.
	/// </summary>
	private static string Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "--";

	#endregion

	#region Построение секций

	/// <summary>
	/// Строит сводку прогона по классификациям строк и итогам сопоставления.
	/// </summary>
	private static ReconciliationSummary BuildSummary(
		IReadOnlyList<StatementRow> statementRows,
		JournalRawData journal,
		TradeReconciliationResult trades,
		DeliveryReconciliationResult deliveries)
	{
		// Классификация строк повторяет решение сверки: категории считаются одним проходом.
		var tradeRows = 0;
		var deliveryRows = 0;
		var outOfScopeRows = 0;
		var unknownRows = 0;
		foreach (var row in statementRows)
		{
			switch (StatementRowClassifier.Classify(row))
			{
				case StatementRowCategory.Trade:
					tradeRows++;
					break;
				case StatementRowCategory.Delivery:
					deliveryRows++;
					break;
				case StatementRowCategory.OutOfScope:
					outOfScopeRows++;
					break;
				case StatementRowCategory.Unknown:
					unknownRows++;
					break;
			}
		}

		return new ReconciliationSummary
		{
			PeriodFromUtc = DateTimeOffset.FromUnixTimeMilliseconds(journal.FromMsInclusive).UtcDateTime,
			PeriodToUtc = DateTimeOffset.FromUnixTimeMilliseconds(journal.ToMsInclusive).UtcDateTime,
			StatementRowsTotal = statementRows.Count,
			TradeRows = tradeRows,
			DeliveryRows = deliveryRows,
			OutOfScopeRows = outOfScopeRows,
			UnknownRows = unknownRows,
			JournalExecutionsInRange = journal.Executions.Count,
			JournalDeliveriesInRange = journal.Deliveries.Count,
			ExecutionsOutsideRange = journal.ExecutionsOutsideRange,
			DeliveriesOutsideRange = journal.DeliveriesOutsideRange,
			MatchedTrades = trades.Matched.Count,
			TradeAttributeMismatches = trades.AttributeMismatches.Count,
			TradesMissingInJournal = trades.MissingInJournal.Count,
			TradesMissingInStatement = trades.MissingInStatement.Count,
			MatchedDeliveries = deliveries.Matched.Count,
			DeliveryQuantityMismatches = deliveries.QuantityMismatches.Count,
			DeliveriesMissingInJournal = deliveries.MissingInJournal.Count,
			DeliveriesMissingInStatement = deliveries.MissingInStatement.Count,
		};
	}

	/// <summary>
	/// Собирает контрольные итоги по инструментам из торговых строк, записей и пар.
	/// </summary>
	private static IReadOnlyList<InstrumentReconciliationTotals> BuildInstrumentTotals(
		IReadOnlyList<StatementRow> statementRows,
		JournalRawData journal,
		TradeReconciliationResult trades,
		DeliveryReconciliationResult deliveries)
	{
		var counters = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
		int[] CounterOf(string? symbol)
		{
			ArgumentNullException.ThrowIfNullOrWhiteSpace(symbol);
			if (counters.TryGetValue(symbol, out var counter) == false)
			{
				counter = new int[6];
				counters[symbol] = counter;
			}

			return counter;
		}

		foreach (var row in statementRows)
		{
			if (StatementRowClassifier.Classify(row) == StatementRowCategory.Trade)
			{
				CounterOf(row.Contract)[0]++;
			}
		}

		foreach (var execution in journal.Executions)
		{
			CounterOf(execution.Symbol)[1]++;
		}

		foreach (var pair in trades.Matched)
		{
			CounterOf(pair.Execution.Symbol)[2]++;
		}

		foreach (var mismatch in trades.AttributeMismatches)
		{
			CounterOf(mismatch.Execution.Symbol)[3]++;
		}

		foreach (var row in trades.MissingInJournal)
		{
			CounterOf(row.Contract)[4]++;
		}

		foreach (var execution in trades.MissingInStatement)
		{
			CounterOf(execution.Symbol)[5]++;
		}

		return counters.Select(pair => new InstrumentReconciliationTotals(
			Symbol: pair.Key,
			StatementRows: pair.Value[0],
			JournalRecords: pair.Value[1],
			MatchedPairs: pair.Value[2],
			AttributeMismatches: pair.Value[3],
			MissingInJournal: pair.Value[4],
			MissingInStatement: pair.Value[5])).ToList();
	}

	/// <summary>
	/// Считает строки вне области синхронизации по типам файлов.
	/// </summary>
	private static IReadOnlyList<OutOfScopeTypeCount> BuildOutOfScopeTypes(IReadOnlyList<StatementRow> statementRows) =>
		statementRows
			.Where(row => StatementRowClassifier.Classify(row) == StatementRowCategory.OutOfScope)
			.GroupBy(row => (row.FileKind, row.Type))
			.OrderBy(group => group.Key.FileKind)
			.ThenBy(group => group.Key.Type, StringComparer.Ordinal)
			.Select(group => new OutOfScopeTypeCount(group.Key.FileKind, group.Key.Type, group.Count()))
			.ToList();

	/// <summary>
	/// Считает строки неизвестного сверке типа: они не скрываются молча.
	/// </summary>
	private static IReadOnlyList<UnknownTypeCount> BuildUnknownTypes(IReadOnlyList<StatementRow> statementRows) =>
		statementRows
			.Where(row => StatementRowClassifier.Classify(row) == StatementRowCategory.Unknown)
			.GroupBy(row => row.Type, StringComparer.Ordinal)
			.OrderBy(group => group.Key, StringComparer.Ordinal)
			.Select(group => new UnknownTypeCount(group.Key, group.Count()))
			.ToList();

	#endregion
}
