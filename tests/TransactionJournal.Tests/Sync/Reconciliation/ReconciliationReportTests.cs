using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>
/// Проверки построения отчёта сверки: секции классификаций с атрибутами обеих
/// сторон, итоги по инструментам, сводка вне области с неизвестными типами,
/// количество исключённых по диапазону записей и нулевые количества при чистой сверке.
/// </summary>
[TestClass]
public class ReconciliationReportTests
{
	/// <summary>Базовое время синтетических строк: 2026-09-22 19:00:00 UTC.</summary>
	private static readonly DateTime BaseTime = new(2026, 9, 22, 19, 0, 0, DateTimeKind.Utc);

	[TestMethod]
	[Description("Отчёт содержит все классификации расхождений, итоги по инструментам, сводку вне области и исключённые записи")]
	public void TryIfReportContainsAllClassificationsAndTotals()
	{
		// Arrange: сценарий со всеми классификациями — точная пара, строка без пары,
		// пара с расхождением количества, запись без строки, delivery-пара,
		// строки вне области и неизвестный тип.
		var statementRows = new List<StatementRow>
		{
			TradeRow("BTCUSDT", "BUY", 1m, 100m, -0.05m, BaseTime, line: 3),
			TradeRow("XAUTUSDT", "SELL", 0.02m, 4356m, -0.08712m, BaseTime, sourceFile: "uta-R2.csv", line: 4),
			TradeRow("BTCUSDT", "BUY", 2m, 100m, -0.05m, BaseTime, line: 5),
			DeliveryRow("BTC-25SEP26-45000-C", 0.01m, BaseTime),
			OutOfScopeRow("SETTLEMENT", StatementFileKind.Uta),
			OutOfScopeRow("TRANSFER_OUT", StatementFileKind.Uta),
			OutOfScopeRow("TRANSFER_IN", StatementFileKind.Uta),
			OutOfScopeRow("Earn", StatementFileKind.Fund),
			UnknownRow("MARGIN_TRADE"),
		};
		var executions = new List<JournalExecution>
		{
			Execution("BTCUSDT", "BUY", 1m, 100m, 0.05m, BaseTime, id: 1, execId: "exec-1"),
			Execution("BTCUSDT", "BUY", 1m, 100m, 0.05m, BaseTime.AddSeconds(1), id: 2, execId: "exec-2"),
			Execution("ETHUSDT", "BUY", 1m, 200m, 0.1m, BaseTime.AddSeconds(2), id: 3, execId: "exec-3"),
		};
		var deliveries = new List<JournalDelivery>
		{
			Delivery("BTC-25SEP26-45000-C", 0.01m, BaseTime.AddMinutes(6)),
		};
		var journal = new JournalRawData(
			executions, deliveries,
			FromMsInclusive: ToMs(BaseTime.AddSeconds(-1)),
			ToMsInclusive: ToMs(BaseTime.AddSeconds(10)),
			ExecutionsOutsideRange: 2,
			DeliveriesOutsideRange: 1);
		var trades = StatementMatcher.MatchTrades(
			statementRows.Where(row => row.Type == "TRADE").ToList(), executions);
		var deliveriesResult = StatementMatcher.MatchDeliveries(
			statementRows.Where(row => row.Type == "DELIVERY").ToList(), deliveries);

		// Act
		var report = ReconciliationReport.Build(statementRows, journal, trades, deliveriesResult);

		// Assert: сводка прогона отражает каждую классификацию и счётчики диапазона.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-report-includes-totals-and-out-of-scope
		var summary = report.Summary;
		Assert.That(summary.StatementRowsTotal, Is.EqualTo(9));
		Assert.That(summary.TradeRows, Is.EqualTo(3));
		Assert.That(summary.DeliveryRows, Is.EqualTo(1));
		Assert.That(summary.OutOfScopeRows, Is.EqualTo(4));
		Assert.That(summary.UnknownRows, Is.EqualTo(1));
		Assert.That(summary.JournalExecutionsInRange, Is.EqualTo(3));
		Assert.That(summary.JournalDeliveriesInRange, Is.EqualTo(1));
		Assert.That(summary.ExecutionsOutsideRange, Is.EqualTo(2));
		Assert.That(summary.DeliveriesOutsideRange, Is.EqualTo(1));
		Assert.That(summary.MatchedTrades, Is.EqualTo(1));
		Assert.That(summary.TradeAttributeMismatches, Is.EqualTo(1));
		Assert.That(summary.TradesMissingInJournal, Is.EqualTo(1));
		Assert.That(summary.TradesMissingInStatement, Is.EqualTo(1));
		Assert.That(summary.MatchedDeliveries, Is.EqualTo(1));
		Assert.That(summary.HasDiscrepancies, Is.True);

		// Assert: итоги по инструментам сводят строки, записи и пары по символу.
		var btc = report.Instruments.Single(instrument => instrument.Symbol == "BTCUSDT");
		Assert.That(btc.StatementRows, Is.EqualTo(2));
		Assert.That(btc.JournalRecords, Is.EqualTo(2));
		Assert.That(btc.MatchedPairs, Is.EqualTo(1));
		Assert.That(btc.AttributeMismatches, Is.EqualTo(1));
		Assert.That(report.Instruments.Single(instrument => instrument.Symbol == "XAUTUSDT").MissingInJournal, Is.EqualTo(1));
		Assert.That(report.Instruments.Single(instrument => instrument.Symbol == "ETHUSDT").MissingInStatement, Is.EqualTo(1));

		// Assert: сводка вне области перечисляет типы по файлам и неизвестный тип явно.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-unknown-type-listed-in-summary
		Assert.That(report.OutOfScopeTypes, Has.Count.EqualTo(4));
		Assert.That(report.OutOfScopeTypes.Single(type => type.Type == "Earn").FileKind, Is.EqualTo(StatementFileKind.Fund));
		Assert.That(report.UnknownTypes.Select(type => type.Type), Is.EqualTo(["MARGIN_TRADE"]));

		// Assert: текст отчёта содержит все секции и значения обеих сторон расхождений.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-report-enables-locating-discrepancies
		var text = report.Render();
		Assert.That(text, Does.Contain("Период строк выгрузки: 2026-09-22 18:59:59 – 2026-09-22 19:00:10 UTC"));
		Assert.That(text, Does.Contain("исключено по диапазону: исполнений 2, delivery-записей 1"));
		Assert.That(text, Does.Contain("--- Отсутствует в журнале (TRADE): 1 ---"));
		Assert.That(text, Does.Contain("XAUTUSDT | SELL | qty=0.02 | price=4356 | fee=-0.08712 | uta-R2.csv:4"));
		Assert.That(text, Does.Contain("--- Отсутствует в выгрузке (исполнения): 1 ---"));
		Assert.That(text, Does.Contain("execId=exec-3"));
		Assert.That(text, Does.Contain("--- Расхождение атрибутов (TRADE): 1 ---"));
		Assert.That(text, Does.Contain("количество: выгрузка=2, журнал=1"));
		Assert.That(text, Does.Contain("--- Итоги по инструментам: 3 ---"));
		Assert.That(text, Does.Contain("BTCUSDT: строки=2, журнал=2, пары=1, расхождений=1, нет в журнале=0, нет в выгрузке=0"));
		Assert.That(text, Does.Contain("--- Вне области журнала ---"));
		Assert.That(text, Does.Contain("SETTLEMENT (UTA): 1"));
		Assert.That(text, Does.Contain("Earn (fund): 1"));
		Assert.That(text, Does.Contain("MARGIN_TRADE: 1"));
	}

	[TestMethod]
	[Description("Чистая сверка отражает нулевые количества расхождений по всем классификациям")]
	public void TryIfCleanReconciliationRendersZeroDiscrepancies()
	{
		// Arrange: единственная торговая и delivery-пары, расхождений нет.
		var statementRows = new List<StatementRow>
		{
			TradeRow("BTCUSDT", "BUY", 1m, 100m, -0.05m, BaseTime),
			DeliveryRow("BTC-25SEP26-45000-C", 0.01m, BaseTime),
		};
		var journal = new JournalRawData(
			[Execution("BTCUSDT", "BUY", 1m, 100m, 0.05m, BaseTime, id: 1, execId: "exec-1")],
			[Delivery("BTC-25SEP26-45000-C", 0.01m, BaseTime)],
			FromMsInclusive: ToMs(BaseTime.AddSeconds(-10)),
			ToMsInclusive: ToMs(BaseTime.AddSeconds(10)),
			ExecutionsOutsideRange: 0,
			DeliveriesOutsideRange: 0);

		// Act
		var report = ReconciliationReport.Build(
			statementRows,
			journal,
			StatementMatcher.MatchTrades(
				statementRows.Where(row => row.Type == "TRADE").ToList(), journal.Executions),
			StatementMatcher.MatchDeliveries(
				statementRows.Where(row => row.Type == "DELIVERY").ToList(), journal.Deliveries));

		// Assert: все классификации расхождений нулевые, текст отчёта это отражает.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-clean-reconciliation-passes
		Assert.That(report.Summary.HasDiscrepancies, Is.False);
		Assert.That(report.Summary.MatchedTrades, Is.EqualTo(1));
		Assert.That(report.Summary.MatchedDeliveries, Is.EqualTo(1));
		var text = report.Render();
		Assert.That(text, Does.Contain("Расхождения: отсутствует в журнале 0+0, отсутствует в выгрузке 0+0, расхождение атрибутов 0+0"));
		Assert.That(text, Does.Contain("--- Отсутствует в журнале (TRADE): 0 ---"));
		Assert.That(text, Does.Contain("--- Расхождение атрибутов (TRADE): 0 ---"));
		Assert.That(text, Does.Contain("--- Неизвестные типы UTA-файла: 0 ---"));
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Отсутствующий список строк выгрузки даёт ошибку аргумента")]
	public void ThrowOnNullStatementRowsArgument()
	{
		// Arrange: null вместо списка строк выгрузки.
		var journal = new JournalRawData([], [], 0, 0, 0, 0);

		// Act
		ReconciliationReport.Build(null!, journal,
			new TradeReconciliationResult([], [], [], [], []),
			new DeliveryReconciliationResult([], [], [], []));

		// Assert: ожидается ArgumentNullException (атрибут ExpectedException).
	}

	/// <summary>Переводит время в миллисекунды эпохи Unix.</summary>
	private static long ToMs(DateTime time) =>
		new DateTimeOffset(time, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>
	/// Создаёт синтетическую торговую строку выгрузки.
	/// </summary>
	private static StatementRow TradeRow(
		string contract,
		string direction,
		decimal quantity,
		decimal price,
		decimal fee,
		DateTime time,
		string sourceFile = "uta-main.csv",
		int line = 3)
	{
		return new StatementRow
		{
			FileKind = StatementFileKind.Uta,
			SourceFile = sourceFile,
			LineNumber = line,
			Type = "TRADE",
			Currency = "USDT",
			Contract = contract,
			Direction = direction,
			Quantity = quantity,
			FilledPrice = price,
			FeePaid = fee,
			TimeUtc = time,
		};
	}

	/// <summary>
	/// Создаёт синтетическую delivery-строку выгрузки.
	/// </summary>
	private static StatementRow DeliveryRow(string contract, decimal quantity, DateTime time)
	{
		return new StatementRow
		{
			FileKind = StatementFileKind.Uta,
			SourceFile = "uta-main.csv",
			LineNumber = 10,
			Type = "DELIVERY",
			Currency = "BTC",
			Contract = contract,
			Direction = "BUY",
			Quantity = quantity,
			FilledPrice = null,
			FeePaid = 0m,
			TimeUtc = time,
		};
	}

	/// <summary>
	/// Создаёт синтетическую строку вне области синхронизации.
	/// </summary>
	private static StatementRow OutOfScopeRow(string type, StatementFileKind fileKind)
	{
		return new StatementRow
		{
			FileKind = fileKind,
			SourceFile = fileKind == StatementFileKind.Uta ? "uta-main.csv" : "fund-main.csv",
			LineNumber = 20,
			Type = type,
			Currency = "USDT",
			Contract = fileKind == StatementFileKind.Uta ? "BTCUSDT" : null,
			Direction = fileKind == StatementFileKind.Uta ? "BUY" : null,
			Quantity = 0.1m,
			FilledPrice = null,
			FeePaid = 0m,
			TimeUtc = BaseTime,
		};
	}

	/// <summary>
	/// Создаёт синтетическую строку неизвестного сверке типа.
	/// </summary>
	private static StatementRow UnknownRow(string type)
	{
		return new StatementRow
		{
			FileKind = StatementFileKind.Uta,
			SourceFile = "uta-main.csv",
			LineNumber = 30,
			Type = type,
			Currency = "USDT",
			Contract = "BTCUSDT",
			Direction = "BUY",
			Quantity = 1m,
			FilledPrice = 100m,
			FeePaid = 0m,
			TimeUtc = BaseTime,
		};
	}

	/// <summary>
	/// Создаёт синтетическую запись исполнения журнала.
	/// </summary>
	private static JournalExecution Execution(
		string symbol, string side, decimal quantity, decimal price, decimal fee, DateTime time, long id, string execId)
	{
		return new JournalExecution(id, execId, symbol, side, quantity, price, fee, time);
	}

	/// <summary>
	/// Создаёт синтетическую delivery-запись журнала.
	/// </summary>
	private static JournalDelivery Delivery(string symbol, decimal quantity, DateTime time)
	{
		return new JournalDelivery(1, symbol, quantity, time);
	}
}
