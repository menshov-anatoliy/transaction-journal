using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>
/// Проверки движка сопоставления на синтетических строках выгрузки и записях
/// журнала: точные пары и выбор ближайшего времени, остаточные пары «расхождение
/// атрибутов», классификации «отсутствует в журнале» и «отсутствует в выгрузке»,
/// допуски комиссии и времени, сопоставление delivery-строк.
/// </summary>
[TestClass]
public class StatementMatcherTests
{
	/// <summary>Базовое время синтетических строк: 2026-09-22 19:00:00 UTC.</summary>
	private static readonly DateTime BaseTime = new(2026, 9, 22, 19, 0, 0, DateTimeKind.Utc);

	[TestMethod]
	[Description("Точной торговой строке находится запись исполнения по ключу атрибутов в допуске времени")]
	public void TryIfExactTradeKeyPairsWithinTimeTolerance()
	{
		// Arrange: строка и запись с одинаковыми инструментом, стороной, количеством и ценой.
		var row = TradeRow(time: BaseTime);
		var execution = Execution(time: BaseTime.AddSeconds(1));

		// Act
		var result = StatementMatcher.MatchTrades([row], [execution]);

		// Assert: пара сопоставлена, расхождений и отсутствий нет.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-trade-row-matches-execution
		Assert.That(result.Matched, Has.Count.EqualTo(1));
		Assert.That(result.Matched[0].Row, Is.SameAs(row));
		Assert.That(result.Matched[0].Execution, Is.EqualTo(execution));
		Assert.That(result.AttributeMismatches, Is.Empty);
		Assert.That(result.MissingInJournal, Is.Empty);
		Assert.That(result.MissingInStatement, Is.Empty);
	}

	[TestMethod]
	[Description("Среди кандидатов точного ключа выбирается запись с ближайшим временем")]
	public void TryIfNearestTimeExecutionChosenAmongCandidates()
	{
		// Arrange: две одинаковые записи в допуске, строка ближе к первой.
		var near = Execution(time: BaseTime.AddSeconds(-1), execId: "exec-near");
		var far = Execution(time: BaseTime.AddSeconds(2), execId: "exec-far");

		// Act
		var result = StatementMatcher.MatchTrades([TradeRow(time: BaseTime)], [far, near]);

		// Assert: пара образована с ближайшей записью, вторая осталась без пары.
		Assert.That(result.Matched.Single().Execution.ExecId, Is.EqualTo("exec-near"));
		Assert.That(result.MissingInStatement.Select(execution => execution.ExecId), Is.EqualTo(["exec-far"]));
	}

	[TestMethod]
	[DataRow("BUY", "Buy")]
	[DataRow("SELL", "Sell")]
	[Description("Сторона строки и записи сопоставляется без учёта регистра написания")]
	public void TryIfSideNormalizedCaseInsensitively(string statementDirection, string journalSide)
	{
		// Arrange: строка выгрузки с написанием стороны выгрузки и запись с написанием биржи.
		var row = TradeRow(direction: statementDirection);
		var execution = Execution(side: journalSide);

		// Act
		var result = StatementMatcher.MatchTrades([row], [execution]);

		// Assert: написания сторон сведены к общему виду, пара сопоставлена.
		Assert.That(result.Matched, Has.Count.EqualTo(1));
		Assert.That(result.MissingInJournal, Is.Empty);
	}

	[TestMethod]
	[Description("Остаточная пара по инструменту и стороне классифицируется как расхождение количества")]
	public void TryIfResidualQuantityFormsAttributeMismatch()
	{
		// Arrange: строка и запись совпадают по инструменту и стороне, но расходятся количеством.
		var row = TradeRow(quantity: 2m, time: BaseTime);
		var execution = Execution(quantity: 1m, time: BaseTime);

		// Act
		var result = StatementMatcher.MatchTrades([row], [execution]);

		// Assert: пара попала в «расхождение атрибутов» со значениями обеих сторон.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-trade-attribute-mismatch
		Assert.That(result.AttributeMismatches, Has.Count.EqualTo(1));
		var mismatch = result.AttributeMismatches[0];
		Assert.That(mismatch.Fields, Has.Count.EqualTo(1));
		Assert.That(mismatch.Fields[0].Field, Is.EqualTo(TradeField.Quantity));
		Assert.That(mismatch.Fields[0].StatementValue, Is.EqualTo("2"));
		Assert.That(mismatch.Fields[0].JournalValue, Is.EqualTo("1"));
		Assert.That(result.MissingInJournal, Is.Empty);
		Assert.That(result.MissingInStatement, Is.Empty);
	}

	[TestMethod]
	[Description("Остаточная пара по инструменту и стороне классифицируется как расхождение цены")]
	public void TryIfResidualPriceFormsAttributeMismatch()
	{
		// Arrange: строка и запись совпадают по ключу кроме цены.
		var row = TradeRow(price: 4356m, time: BaseTime);
		var execution = Execution(price: 4357m, time: BaseTime);

		// Act
		var result = StatementMatcher.MatchTrades([row], [execution]);

		// Assert: зафиксировано расхождение цены со значениями обеих сторон.
		Assert.That(result.AttributeMismatches, Has.Count.EqualTo(1));
		var mismatch = result.AttributeMismatches[0].Fields.Single();
		Assert.That(mismatch.Field, Is.EqualTo(TradeField.Price));
		Assert.That(mismatch.StatementValue, Is.EqualTo("4356"));
		Assert.That(mismatch.JournalValue, Is.EqualTo("4357"));
	}

	[TestMethod]
	[Description("Комиссия сопоставленной пары за пределами относительного допуска даёт расхождение")]
	public void TryIfFeeDivergenceBeyondToleranceMarkedAsMismatch()
	{
		// Arrange: точная пара по ключу, комиссии расходятся сильнее 0.5%.
		var row = TradeRow(fee: -0.5m, time: BaseTime);
		var execution = Execution(fee: 0.6m, time: BaseTime);

		// Act
		var result = StatementMatcher.MatchTrades([row], [execution]);

		// Assert: пара по ключу точная, но расхождение комиссии переводит её
		// в классификацию расхождений атрибутов со значениями обеих сторон.
		Assert.That(result.AttributeMismatches, Has.Count.EqualTo(1));
		var mismatch = result.AttributeMismatches[0].Fields.Single();
		Assert.That(mismatch.Field, Is.EqualTo(TradeField.Fee));
		Assert.That(mismatch.StatementValue, Is.EqualTo("-0.5"));
		Assert.That(mismatch.JournalValue, Is.EqualTo("0.6"));
		Assert.That(result.Matched, Is.Empty);
	}

	[TestMethod]
	[DataRow(-0.5, 0.502, true)]
	[DataRow(0.5, 0.51, false)]
	[DataRow(0.02, 0.02, true)]
	[DataRow(-1.0, 1.0, true)]
	[DataRow(0.5, 0.6, false)]
	[Description("Согласованность комиссий проверяется по модулю с относительным допуском 0.5%")]
	public void TryIfFeeConsistencyCheckedByModuleWithRelativeTolerance(double statementFee, double journalFee, bool expected)
	{
		// Arrange: значения комиссий обеих сторон заданы параметрами строки теста.
		var statement = (decimal)statementFee;
		var journal = (decimal)journalFee;

		// Act
		var consistent = StatementMatcher.AreFeesConsistent(statement, journal);

		// Assert: знаки сторон не влияют, допуск считается от большего модуля.
		// Traceability: change:reconcile-bybit-statement/design#d5
		Assert.That(consistent, Is.EqualTo(expected));
	}

	[TestMethod]
	[Description("Неоднозначная комиссия записи не даёт построчной ошибки, пара попадает в агрегат по инструменту")]
	public void TryIfAmbiguousJournalFeeSkipsPerRowCheck()
	{
		// Arrange: точная пара по ключу, комиссия записи нулевая — поле неоднозначно.
		var row = TradeRow(fee: -0.5m, time: BaseTime);
		var execution = Execution(fee: 0m, time: BaseTime);

		// Act
		var result = StatementMatcher.MatchTrades([row], [execution]);

		// Assert: расхождений нет, агрегат по инструменту содержит пару без построчной сверки.
		// Traceability: change:reconcile-bybit-statement/design#d5
		Assert.That(result.AttributeMismatches, Is.Empty);
		Assert.That(result.Matched, Has.Count.EqualTo(1));
		Assert.That(result.FeeAggregates, Has.Count.EqualTo(1));
		var aggregate = result.FeeAggregates[0];
		Assert.That(aggregate.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(aggregate.StatementAbsFeeSum, Is.EqualTo(0.5m));
		Assert.That(aggregate.JournalAbsFeeSum, Is.EqualTo(0m));
		Assert.That(aggregate.UncomparedPairs, Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Торговая строка без пары в журнале отмечается как отсутствующая в журнале")]
	public void TryIfUnmatchedTradeRowReportedMissingInJournal()
	{
		// Arrange: строка с инструментом, по которому записей нет.
		var row = TradeRow(contract: "XAUTUSDT", quantity: 0.02m, price: 4356m, fee: -0.08712m, time: BaseTime);

		// Act
		var result = StatementMatcher.MatchTrades([row], []);

		// Assert: строка попала в «отсутствует в журнале».
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-trade-row-missing-in-journal
		Assert.That(result.MissingInJournal, Has.Count.EqualTo(1));
		Assert.That(result.MissingInJournal[0], Is.SameAs(row));
		Assert.That(result.MissingInStatement, Is.Empty);
	}

	[TestMethod]
	[Description("Запись исполнения без пары среди строк отмечается как отсутствующая в выгрузке")]
	public void TryIfUnmatchedExecutionReportedMissingInStatement()
	{
		// Arrange: запись журнала, которой нет среди строк выгрузки.
		var execution = Execution(symbol: "ETHUSDT", time: BaseTime);

		// Act
		var result = StatementMatcher.MatchTrades([], [execution]);

		// Assert: запись попала в «отсутствует в выгрузке».
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-execution-missing-in-statement
		Assert.That(result.MissingInStatement, Has.Count.EqualTo(1));
		Assert.That(result.MissingInStatement[0], Is.EqualTo(execution));
		Assert.That(result.MissingInJournal, Is.Empty);
	}

	[TestMethod]
	[Description("Каждая строка и запись участвуют в сопоставлении не более одного раза")]
	public void TryIfEachRowAndRecordUsedOnceInMultiset()
	{
		// Arrange: две одинаковые строки выгрузки против одной такой же записи журнала.
		var rows = new[] { TradeRow(time: BaseTime), TradeRow(time: BaseTime.AddSeconds(1)) };
		var executions = new[] { Execution(time: BaseTime) };

		// Act
		var result = StatementMatcher.MatchTrades(rows, executions);

		// Assert: одна пара и один остаток — дублирование пары исключено.
		Assert.That(result.Matched, Has.Count.EqualTo(1));
		Assert.That(result.MissingInJournal, Has.Count.EqualTo(1));
		Assert.That(result.MissingInStatement, Is.Empty);
	}

	[TestMethod]
	[Description("Строка и запись за пределами допуска времени остаются непарными")]
	public void TryIfTimeBeyondToleranceLeavesBothUnmatched()
	{
		// Arrange: атрибуты совпадают, время различается на три секунды.
		var row = TradeRow(time: BaseTime);
		var execution = Execution(time: BaseTime.AddSeconds(3));

		// Act
		var result = StatementMatcher.MatchTrades([row], [execution]);

		// Assert: пара не образована, обе стороны попали в свои классификации отсутствий.
		Assert.That(result.Matched, Is.Empty);
		Assert.That(result.MissingInJournal, Has.Count.EqualTo(1));
		Assert.That(result.MissingInStatement, Has.Count.EqualTo(1));
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Отсутствующий список торговых строк даёт ошибку аргумента")]
	public void ThrowOnNullTradeRowsArgument()
	{
		// Arrange: null вместо списка строк.

		// Act
		StatementMatcher.MatchTrades(null!, []);

		// Assert: ожидается ArgumentNullException (атрибут ExpectedException).
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Отсутствующий список записей исполнения даёт ошибку аргумента")]
	public void ThrowOnNullExecutionsArgument()
	{
		// Arrange: null вместо списка записей.

		// Act
		StatementMatcher.MatchTrades([], null!);

		// Assert: ожидается ArgumentNullException (атрибут ExpectedException).
	}

	[TestMethod]
	[Description("Delivery-строке находится запись по инструменту в допуске десяти минут")]
	public void TryIfDeliveryPairedBySymbolWithinTenMinutes()
	{
		// Arrange: delivery-строка и запись одного инструмента в шести минутах друг от друга.
		var row = DeliveryRow(contract: "BTC-25SEP26-45000-C", quantity: 0.01m, time: BaseTime);
		var delivery = Delivery(symbol: "BTC-25SEP26-45000-C", quantity: 0.01m, time: BaseTime.AddMinutes(6));

		// Act
		var result = StatementMatcher.MatchDeliveries([row], [delivery]);

		// Assert: пара сопоставлена, количество сходится, отсутствий нет.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-delivery-row-matches-record
		Assert.That(result.Matched, Has.Count.EqualTo(1));
		Assert.That(result.QuantityMismatches, Is.Empty);
		Assert.That(result.MissingInJournal, Is.Empty);
		Assert.That(result.MissingInStatement, Is.Empty);
	}

	[TestMethod]
	[Description("Сопоставленная delivery-пара с расходящимся количеством даёт расхождение")]
	public void TryIfDeliveryQuantityMismatchReported()
	{
		// Arrange: delivery-пара по инструменту с разными количествами.
		var row = DeliveryRow(contract: "BTC-25SEP26-45000-C", quantity: 0.01m, time: BaseTime);
		var delivery = Delivery(symbol: "BTC-25SEP26-45000-C", quantity: 0.02m, time: BaseTime);

		// Act
		var result = StatementMatcher.MatchDeliveries([row], [delivery]);

		// Assert: расхождение количества со значениями обеих сторон.
		Assert.That(result.Matched, Has.Count.EqualTo(1));
		Assert.That(result.QuantityMismatches, Has.Count.EqualTo(1));
		Assert.That(result.QuantityMismatches[0].StatementValue, Is.EqualTo("0.01"));
		Assert.That(result.QuantityMismatches[0].JournalValue, Is.EqualTo("0.02"));
	}

	[TestMethod]
	[Description("Delivery-строка без пары в журнале отмечается как отсутствующая в журнале")]
	public void TryIfDeliveryRowMissingInJournal()
	{
		// Arrange: delivery-строка инструмента, записей которого в журнале нет.
		var row = DeliveryRow(contract: "BTC-25SEP26-45000-C", time: BaseTime);

		// Act
		var result = StatementMatcher.MatchDeliveries([row], []);

		// Assert: строка попала в «отсутствует в журнале».
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-delivery-row-missing-in-journal
		Assert.That(result.MissingInJournal, Has.Count.EqualTo(1));
		Assert.That(result.MissingInStatement, Is.Empty);
	}

	[TestMethod]
	[Description("Delivery-запись без пары среди строк отмечается как отсутствующая в выгрузке")]
	public void TryIfDeliveryRecordMissingInStatement()
	{
		// Arrange: delivery-запись, которой нет среди строк выгрузки.
		var delivery = Delivery(symbol: "BTC-25SEP26-40000-P", time: BaseTime);

		// Act
		var result = StatementMatcher.MatchDeliveries([], [delivery]);

		// Assert: запись попала в «отсутствует в выгрузке».
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-delivery-record-missing-in-statement
		Assert.That(result.MissingInStatement, Has.Count.EqualTo(1));
		Assert.That(result.MissingInStatement[0], Is.EqualTo(delivery));
		Assert.That(result.MissingInJournal, Is.Empty);
	}

	[TestMethod]
	[Description("Delivery-строка и запись за пределами допуска десяти минут остаются непарными")]
	public void TryIfDeliveryBeyondTenMinutesLeavesBothUnmatched()
	{
		// Arrange: delivery-пара по инструменту с разницей времени в одиннадцать минут.
		var row = DeliveryRow(contract: "BTC-25SEP26-45000-C", time: BaseTime);
		var delivery = Delivery(symbol: "BTC-25SEP26-45000-C", time: BaseTime.AddMinutes(11));

		// Act
		var result = StatementMatcher.MatchDeliveries([row], [delivery]);

		// Assert: пара не образована, обе стороны попали в свои классификации отсутствий.
		Assert.That(result.Matched, Is.Empty);
		Assert.That(result.MissingInJournal, Has.Count.EqualTo(1));
		Assert.That(result.MissingInStatement, Has.Count.EqualTo(1));
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Отсутствующий список delivery-строк даёт ошибку аргумента")]
	public void ThrowOnNullDeliveryRowsArgument()
	{
		// Arrange: null вместо списка строк.

		// Act
		StatementMatcher.MatchDeliveries(null!, []);

		// Assert: ожидается ArgumentNullException (атрибут ExpectedException).
	}

	/// <summary>
	/// Создаёт синтетическую торговую строку выгрузки.
	/// </summary>
	private static StatementRow TradeRow(
		string contract = "BTCUSDT",
		string direction = "BUY",
		decimal? quantity = 1m,
		decimal? price = 100m,
		decimal? fee = -0.05m,
		DateTime? time = null,
		int line = 3)
	{
		return new StatementRow
		{
			FileKind = StatementFileKind.Uta,
			SourceFile = "AssetChangeDetails_uta_fragment.csv",
			LineNumber = line,
			Type = "TRADE",
			Currency = "USDT",
			Contract = contract,
			Direction = direction,
			Quantity = quantity,
			FilledPrice = price,
			FeePaid = fee,
			TimeUtc = time ?? BaseTime,
		};
	}

	/// <summary>
	/// Создаёт синтетическую запись исполнения журнала.
	/// </summary>
	private static JournalExecution Execution(
		string symbol = "BTCUSDT",
		string side = "Buy",
		decimal? quantity = 1m,
		decimal? price = 100m,
		decimal? fee = 0.05m,
		DateTime? time = null,
		long id = 1,
		string execId = "exec-1")
	{
		return new JournalExecution(id, execId, symbol, side.ToUpperInvariant(), quantity, price, fee, time ?? BaseTime);
	}

	/// <summary>
	/// Создаёт синтетическую delivery-строку выгрузки.
	/// </summary>
	private static StatementRow DeliveryRow(
		string contract = "BTC-25SEP26-45000-C",
		decimal? quantity = 0.01m,
		DateTime? time = null,
		int line = 3)
	{
		return new StatementRow
		{
			FileKind = StatementFileKind.Uta,
			SourceFile = "AssetChangeDetails_uta_fragment.csv",
			LineNumber = line,
			Type = "DELIVERY",
			Currency = "BTC",
			Contract = contract,
			Direction = "BUY",
			Quantity = quantity,
			FilledPrice = null,
			FeePaid = 0m,
			TimeUtc = time ?? BaseTime,
		};
	}

	/// <summary>
	/// Создаёт синтетическую delivery-запись журнала.
	/// </summary>
	private static JournalDelivery Delivery(
		string symbol = "BTC-25SEP26-45000-C",
		decimal? quantity = 0.01m,
		DateTime? time = null,
		long id = 1)
	{
		return new JournalDelivery(id, symbol, quantity, time ?? BaseTime);
	}
}
