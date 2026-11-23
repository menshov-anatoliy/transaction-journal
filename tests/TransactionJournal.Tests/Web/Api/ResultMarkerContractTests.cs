namespace TransactionJournal.Tests.Web.Api;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api.Constructions;
using TransactionJournal.Application.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Юнит-проверки сериализатора контракта маркеров изменения результата:
/// стабильные kebab-case строки видов событий и операций расшифровки,
/// единый маппинг перечня операций для карточки, списка и превью.
/// Неизвестный вид не сериализуется, а падает с явной ошибкой.
/// </summary>
[TestClass]
public sealed class ResultMarkerContractTests
{
	[TestMethod]
	[DataRow(ResultMarkerKind.Opening, "opening")]
	[DataRow(ResultMarkerKind.Closing, "closing")]
	[DataRow(ResultMarkerKind.Roll, "roll")]
	[DataRow(ResultMarkerKind.SyntheticClose, "synthetic-close")]
	[DataRow(ResultMarkerKind.SyntheticRelease, "synthetic-release")]
	[DataRow(ResultMarkerKind.FuturesClose, "futures-close")]
	[DataRow(ResultMarkerKind.PnLAdjustment, "pnl-adjustment")]
	[DataRow(ResultMarkerKind.Delivery, "delivery")]
	[Description("Виды событий и операций сериализуются стабильными kebab-case строками контракта")]
	// Один набор строк обслуживает вид маркера и вид операции расшифровки:
	// SPA сопоставляет строку перечня со строкой события одним словарём.
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	public void TryIfSerializesValidKinds(ResultMarkerKind kind, string expected)
	{
		// Act: сериализация валидного вида.
		var serialized = ResultMarkerContract.SerializeKind(kind);

		// Assert: строка контракта стабильна.
		Assert.That(serialized, Is.EqualTo(expected));
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	[Description("Неизвестный вид маркера не сериализуется")]
	// Вне перечня видов контракта нет строк: значение за пределами enum —
	// ошибка вычислителя, сериализатор падает, а не отдаёт мусор в API.
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	public void ThrowOnUnknownKindSerialization()
	{
		// Act: сериализация значения вне перечня видов.
		ResultMarkerContract.SerializeKind((ResultMarkerKind)999);
	}

	[TestMethod]
	[Description("Перечень операций расшифровки сериализуется с видом, моментом и вкладом")]
	// Сервер публикует перечни целиком: каждая операция несёт вид, дату и
	// сумму вклада — сумма перечня равна изменению маркера, SPA сверяет
	// расшифровку сложением.
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	public void TryIfSerializesOperationsWithKindDateAndContribution()
	{
		// Arrange: перечень из двух операций расшифровки.
		var at = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
		var operations = new[]
		{
			new ResultMarkerOperation { Kind = ResultMarkerKind.FuturesClose, EventTime = at, AmountUsdt = 0.92m },
			new ResultMarkerOperation { Kind = ResultMarkerKind.Roll, EventTime = at.AddDays(1), AmountUsdt = -99.17m },
		};

		// Act: сериализация перечня.
		var serialized = ResultMarkerContract.SerializeOperations(operations);

		// Assert: строки перечня сохраняют вид, момент и вклад операции.
		Assert.That(serialized, Has.Count.EqualTo(2));
		Assert.That(serialized[0].Kind, Is.EqualTo("futures-close"));
		Assert.That(serialized[0].Date, Is.EqualTo(at));
		Assert.That(serialized[0].ContributionUsdt, Is.EqualTo(0.92m));
		Assert.That(serialized[1].Kind, Is.EqualTo("roll"));
		Assert.That(serialized[1].ContributionUsdt, Is.EqualTo(-99.17m));
	}

	[TestMethod]
	[Description("Пустой перечень операций сериализуется пустым списком контракта")]
	// У маркера без операций расшифровки контракт пуст: пустой перечень —
	// не ошибка и не null, «детализации нет» отличается от «операций нет».
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	public void TryIfSerializesEmptyOperationsToEmptyList()
	{
		// Act: сериализация пустого перечня.
		var serialized = ResultMarkerContract.SerializeOperations([]);

		// Assert: контракт отдаёт пустой список.
		Assert.That(serialized, Is.Empty);
	}

	[TestMethod]
	[Description("Перечень операций сериализует детализацию: сделки окна с суммами, записи фьючерсного цикла без сумм, периоды связок")]
	// Окно несёт сделки с индивидуальными суммами, складывающимися во вклад;
	// фьючерсный цикл — записи без сумм; связка — период действия; операция
	// без детализации отдаёт null вместо пустого перечня — «детализации нет»
	// отличимо от пустоты, прежние поля контракта не меняются.
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	public void TryIfSerializesOperationDetailTradesAndPeriods()
	{
		// Arrange: перечень из четырёх операций — окно с двумя сделками,
		// фьючерсный цикл с двумя записями без сумм, связка с периодом
		// и корректировка без детализации.
		var at = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
		var operations = new[]
		{
			new ResultMarkerOperation
			{
				Kind = ResultMarkerKind.Roll,
				EventTime = at,
				AmountUsdt = -35m,
				Trades =
				[
					new ResultMarkerTradeDetail("ETH-25SEP26-1600-C-USDT", "buy", 1m, 60m, -60m),
					new ResultMarkerTradeDetail("ETH-25SEP26-1600-P-USDT", "sell", 0.5m, 50m, 25m),
				],
			},
			new ResultMarkerOperation
			{
				Kind = ResultMarkerKind.FuturesClose,
				EventTime = at.AddHours(1),
				AmountUsdt = 1m,
				Trades =
				[
					new ResultMarkerTradeDetail("ETHUSDT", "buy", 0.5m, 3500m, null),
					new ResultMarkerTradeDetail("ETHUSDT", "sell", 0.5m, 3502m, null),
				],
			},
			new ResultMarkerOperation
			{
				Kind = ResultMarkerKind.SyntheticClose,
				EventTime = at.AddHours(2),
				AmountUsdt = 30m,
				PeriodSince = at.AddHours(2),
			},
			new ResultMarkerOperation { Kind = ResultMarkerKind.PnLAdjustment, EventTime = at.AddHours(3), AmountUsdt = 5m },
		};

		// Act: сериализация перечня.
		var serialized = ResultMarkerContract.SerializeOperations(operations);

		// Assert: детализация проезжает в контракт построчно.
		Assert.That(serialized, Has.Count.EqualTo(4));
		var rollTrades = serialized[0].Trades!;
		Assert.That(rollTrades, Has.Count.EqualTo(2));
		Assert.That(rollTrades[0].Symbol, Is.EqualTo("ETH-25SEP26-1600-C-USDT"));
		Assert.That(rollTrades[0].Side, Is.EqualTo("buy"));
		Assert.That(rollTrades[0].Quantity, Is.EqualTo(1m));
		Assert.That(rollTrades[0].Price, Is.EqualTo(60m));
		Assert.That(rollTrades[0].ContributionUsdt, Is.EqualTo(-60m));
		Assert.That(rollTrades[1].Symbol, Is.EqualTo("ETH-25SEP26-1600-P-USDT"));
		Assert.That(rollTrades[1].Side, Is.EqualTo("sell"));
		Assert.That(rollTrades[1].Quantity, Is.EqualTo(0.5m));
		Assert.That(rollTrades[1].ContributionUsdt, Is.EqualTo(25m));
		Assert.That(serialized[0].PeriodSince, Is.Null);
		Assert.That(serialized[0].PeriodUntil, Is.Null);
		var futuresTrades = serialized[1].Trades!;
		Assert.That(futuresTrades, Has.Count.EqualTo(2));
		Assert.That(futuresTrades[0].Symbol, Is.EqualTo("ETHUSDT"));
		Assert.That(futuresTrades[0].Side, Is.EqualTo("buy"));
		Assert.That(futuresTrades[0].ContributionUsdt, Is.Null);
		Assert.That(futuresTrades[1].Side, Is.EqualTo("sell"));
		Assert.That(futuresTrades[1].ContributionUsdt, Is.Null);
		Assert.That(serialized[2].Trades, Is.Null);
		Assert.That(serialized[2].PeriodSince, Is.EqualTo(at.AddHours(2)));
		Assert.That(serialized[2].PeriodUntil, Is.Null);
		Assert.That(serialized[3].Trades, Is.Null);
		Assert.That(serialized[3].PeriodSince, Is.Null);
		Assert.That(serialized[3].PeriodUntil, Is.Null);
	}

	[TestMethod]
	[Description("Сводка последнего маркера сериализуется с перечнем операций расшифровки")]
	// Хвост списка и последний маркер карточки публикуют одинаковый контракт:
	// итог, время события и перечень операций — расшифровка доступна и в
	// компактных видах индикатора.
	// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
	public void TryIfSerializesLastMarkerSummariesWithOperations()
	{
		// Arrange: сводка хвоста и последний маркер карточки.
		var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
		var operations = new[] { new ResultMarkerOperation { Kind = ResultMarkerKind.SyntheticClose, EventTime = at, AmountUsdt = 15.44m } };
		var summary = new ResultMarkerSummary(-67.54m, at, operations);
		var lastMarker = new ResultMarker
		{
			Number = 3,
			EventTime = at,
			Kind = ResultMarkerKind.SyntheticClose,
			ChangeUsdt = 15.44m,
			TotalUsdt = -67.54m,
			Operations = operations,
		};

		// Act: сериализация обеих форм последнего маркера.
		var fromSummary = ResultMarkerContract.ToSummaryResponse(summary);
		var fromMarker = ResultMarkerContract.ToSummaryResponse(lastMarker);

		// Assert: контракты идентичны по итогу, времени и перечню.
		Assert.That(fromSummary.TotalUsdt, Is.EqualTo(-67.54m));
		Assert.That(fromSummary.EventAt, Is.EqualTo(at));
		Assert.That(fromSummary.Operations, Has.Count.EqualTo(1));
		Assert.That(fromSummary.Operations[0].Kind, Is.EqualTo("synthetic-close"));
		Assert.That(fromMarker.TotalUsdt, Is.EqualTo(fromSummary.TotalUsdt));
		Assert.That(fromMarker.EventAt, Is.EqualTo(fromSummary.EventAt));
		Assert.That(fromMarker.Operations[0].ContributionUsdt, Is.EqualTo(fromSummary.Operations[0].ContributionUsdt));
	}
}
