using Microsoft.VisualStudio.TestTools.UnitTesting;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>
/// Проверки классификации типов строк выгрузки: TRADE и DELIVERY сверяемые,
/// SETTLEMENT, TRANSFER_IN/OUT и типы fund-файла вне области, неизвестный
/// тип UTA-файла фиксируется явно.
/// </summary>
[TestClass]
public class StatementRowClassifierTests
{
	[TestMethod]
	[Description("Строка TRADE классифицируется как сверяемая с записями исполнения")]
	public void TryIfTradeRowIsReconcilable()
	{
		// Arrange: строка UTA-файла типа TRADE.
		var row = BuildRow(StatementFileKind.Uta, "TRADE");

		// Act
		var category = StatementRowClassifier.Classify(row);

		// Assert: сделка сверяется с сырыми записями исполнения журнала.
		// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-trade-rows-match-executions
		Assert.That(category, Is.EqualTo(StatementRowCategory.Trade));
	}

	[TestMethod]
	[Description("Строка DELIVERY классифицируется как сверяемая с delivery-записями")]
	public void TryIfDeliveryRowIsReconcilable()
	{
		// Arrange: строка UTA-файла типа DELIVERY.
		var row = BuildRow(StatementFileKind.Uta, "DELIVERY");

		// Act
		var category = StatementRowClassifier.Classify(row);

		// Assert: экспирация сверяется с сырыми delivery-записями журнала.
		// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-delivery-rows-match-deliveries
		Assert.That(category, Is.EqualTo(StatementRowCategory.Delivery));
	}

	[TestMethod]
	[DataRow("SETTLEMENT")]
	[DataRow("TRANSFER_IN")]
	[DataRow("TRANSFER_OUT")]
	[Description("Фандинг и переводы UTA-файла классифицируются как вне области сверки")]
	public void TryIfSettlementAndTransfersAreOutOfScope(string type)
	{
		// Arrange: строка UTA-файла с типом фандинга или перевода.
		var row = BuildRow(StatementFileKind.Uta, type);

		// Act
		var category = StatementRowClassifier.Classify(row);

		// Assert: журнал такие строки не загружает, поэтому расхождением они не отмечаются.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-funding-and-transfers-summarized
		Assert.That(category, Is.EqualTo(StatementRowCategory.OutOfScope));
	}

	[TestMethod]
	[DataRow("Earn")]
	[DataRow("Deposit")]
	[DataRow("Withdraw")]
	[DataRow("Transfer in")]
	[DataRow("Transfer out")]
	[Description("Все строки fund-файла классифицируются как вне области сверки независимо от типа")]
	public void TryIfFundFileRowsAreOutOfScope(string type)
	{
		// Arrange: строка fund-файла с любым известным типом операции фонда.
		var row = BuildRow(StatementFileKind.Fund, type);

		// Act
		var category = StatementRowClassifier.Classify(row);

		// Assert: операции fund-аккаунта журнал не загружает и расхождениями не считает.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-fund-account-rows-summarized
		Assert.That(category, Is.EqualTo(StatementRowCategory.OutOfScope));
	}

	[TestMethod]
	[DataRow("SWEEP")]
	[DataRow("--")]
	[DataRow("")]
	[Description("Неизвестный тип UTA-файла фиксируется явно как неизвестный, а не скрывается")]
	public void TryIfUnknownUtaTypeStaysExplicit(string type)
	{
		// Arrange: строка UTA-файла с типом, которого сверка не знает.
		var row = BuildRow(StatementFileKind.Uta, type);

		// Act
		var category = StatementRowClassifier.Classify(row);

		// Assert: неизвестный тип обязан быть виден в сводке отчёта как отдельная категория.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-unknown-type-listed-in-summary
		Assert.That(category, Is.EqualTo(StatementRowCategory.Unknown));
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Классификация пустой строки выгрузки даёт ошибку")]
	public void ThrowOnNullRow()
	{
		// Arrange: ссылки на строку выгрузки нет.

		// Act
		StatementRowClassifier.Classify(null!);

		// Assert: ожидается ArgumentNullException (атрибут ExpectedException).
	}

	/// <summary>
	/// Собирает строку выгрузки с заданным источником и типом.
	/// </summary>
	/// <param name="kind">Файл-источник строки.</param>
	/// <param name="type">Тип строки как в выгрузке.</param>
	/// <returns>Строка выгрузки.</returns>
	private static StatementRow BuildRow(StatementFileKind kind, string type) => new()
	{
		FileKind = kind,
		SourceFile = "AssetChangeDetails_fragment.csv",
		LineNumber = 3,
		Type = type,
		Currency = "USDT",
		Contract = "XAUTUSDT",
		Direction = "BUY",
		Quantity = 0.02m,
		FilledPrice = 4301m,
		FeePaid = -0.03m,
		TimeUtc = new DateTime(2026, 9, 22, 8, 29, 11, DateTimeKind.Utc),
	};
}
