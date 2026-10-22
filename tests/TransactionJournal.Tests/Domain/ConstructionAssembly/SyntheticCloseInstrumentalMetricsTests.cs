using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Domain.ConstructionAssembly;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверка неизменности инструментальных метрик при распознанной связке:
/// остатки, «Реализованный результат», нереализованный PnL, итог и статус
/// конструкции совпадают со значением без логического слоя — распознавание
/// связки и расчёт её лока ничего не добавляют в потоки позиций и не
/// пересчитывают инструментальную истину.
/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-instrumental-metrics-untouched
/// </summary>
[TestClass]
public class SyntheticCloseInstrumentalMetricsTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";
	private const string XautPerp = "XAUTUSDT";
	private const long ConstructionId = 1;

	/// <summary>Текущий момент пересбора метрик — после последней сделки кейса.</summary>
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

	/// <summary>Момент получения котировок стаба — фиксированный для детерминизма.</summary>
	private static readonly DateTimeOffset QuotesReceivedAt = new(2026, 10, 15, 15, 1, 0, TimeSpan.Zero);

	/// <summary>Описатель сделки кейса: материализованная сделка с ценой и комиссией.</summary>
	private sealed record CaseTrade(
		string ExecId, string Category, string Symbol, DateTimeOffset At, decimal SignedQuantity, decimal Price, decimal Fee);

	/// <summary>
	/// Полный состав кейса XAUT 15.10.2026: стрэддл (пут 0.5 @ 139 fee 1.5,
	/// колл 0.5 @ 12 fee 0.6), связка 15.10 (покупка 0.3 XAUTUSDT @ 4141.91
	/// fee 0.55, продажа 0.3 колла @ 5.9 fee 0.35) и скальп 0.04 @ 4200 fee 0.09.
	/// </summary>
	private static List<CaseTrade> CaseTrades() =>
	[
		new("open-call", "option", XautCall4400, new DateTimeOffset(2026, 9, 30, 12, 5, 0, TimeSpan.Zero), 0.5m, 12m, 0.6m),
		new("open-put", "option", XautPut4400, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), 0.5m, 139m, 1.5m),
		new("case-perp", "linear", XautPerp, new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero), 0.3m, 4141.91m, 0.55m),
		new("case-call", "option", XautCall4400, new DateTimeOffset(2026, 10, 15, 13, 4, 0, TimeSpan.Zero), -0.3m, 5.9m, 0.35m),
		new("scalp-perp", "linear", XautPerp, new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero), 0.04m, 4200m, 0.09m),
	];

	/// <summary>Описатели → исполнения для распознавателя связок.</summary>
	private static List<AssemblyExecution> ExecutionsOf(IEnumerable<CaseTrade> trades) => trades
		.Select(trade => new AssemblyExecution
		{
			ExecId = trade.ExecId,
			Category = trade.Category,
			Symbol = trade.Symbol,
			ExecTimeMs = trade.At.ToUnixTimeMilliseconds(),
			SignedQuantity = trade.SignedQuantity,
		})
		.ToList();

	/// <summary>Описатели → FIFO-потоки позиций по инструментам в стабильном порядке символов.</summary>
	private static Dictionary<string, List<PositionFifoEntry>> StreamsOf(IEnumerable<CaseTrade> trades) => trades
		.Select(trade => (trade.Symbol, Entry: new PositionFifoEntry
		{
			At = trade.At,
			Kind = PositionFifoEntryKind.Trade,
			SourceKey = trade.ExecId,
			Quantity = trade.SignedQuantity,
			Price = trade.Price,
			Fee = trade.Fee,
		}))
		.GroupBy(pair => pair.Symbol, StringComparer.Ordinal)
		.ToDictionary(
			group => group.Key,
			group => group.Select(pair => pair.Entry).ToList(),
			StringComparer.Ordinal);

	/// <summary>Метрики позиций конструкции из FIFO-потоков — в том же порядке символов.</summary>
	private static List<PositionMetrics> PositionMetricsOf(Dictionary<string, List<PositionFifoEntry>> streams)
	{
		var calculator = new PositionMetricsCalculator();
		return streams
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => calculator.Calculate(ConstructionId, pair.Key, pair.Value))
			.ToList();
	}

	/// <summary>Метрики конструкции из метрик позиций: капитал 1000 USDT, без корректировок.</summary>
	private static ConstructionMetrics ConstructionMetricsOf(IEnumerable<PositionMetrics> positions) =>
		new ConstructionMetricsCalculator().Calculate(
			ConstructionId,
			1000m,
			positions,
			[],
			Now);

	/// <summary>
	/// Ценовые данные связки кейса — из того же состава описателей: линейная
	/// нога, встречный колл и сделки входа пут-ноги.
	/// </summary>
	private static SyntheticCloseValuation ValuationOf(IReadOnlyList<CaseTrade> trades, IReadOnlyList<PositionFifoEntry> legEntries)
	{
		var linear = trades.Single(trade => trade.ExecId == "case-perp");
		var counterOption = trades.Single(trade => trade.ExecId == "case-call");
		return new SyntheticCloseValuation
		{
			LinearPrice = linear.Price,
			LinearFee = linear.Fee,
			CounterOptionPrice = counterOption.Price,
			CounterOptionFee = counterOption.Fee,
			ClosedLegEntries = legEntries,
		};
	}

	/// <summary>Стаб источника котировок кейса: XAUTUSDT → 4300, пут → 250, колл → 5.</summary>
	private sealed class StubQuoteSource : IFreshInstrumentQuoteSource
	{
		private static readonly IReadOnlyDictionary<string, decimal> Quotes = new Dictionary<string, decimal>(StringComparer.Ordinal)
		{
			[XautPerp] = 4300m,
			[XautPut4400] = 250m,
			[XautCall4400] = 5m,
		};

		public Task<InstrumentQuoteSnapshot?> GetFreshQuoteAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentQuoteSnapshot?>(
				Quotes.TryGetValue(symbol, out var quotePrice)
					? new InstrumentQuoteSnapshot(symbol, quotePrice, QuotesReceivedAt)
					: null);
	}

	[TestMethod]
	[Description("Распознанная связка не меняет инструментальные метрики: без котировок строки совпадают с базовыми")]
	// Логический слой — распознавание связки и расчёт лока — не вносит записей
	// в потоки позиций: метрики позиций и конструкции равны побайтово.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-instrumental-metrics-untouched
	public void TryIfInstrumentalMetricsMatchWithoutLogicalLayer()
	{
		// Arrange: базовые метрики из того же состава описателей сделок.
		var baselinePositions = PositionMetricsOf(StreamsOf(CaseTrades()));
		var baselineConstruction = ConstructionMetricsOf(baselinePositions);

		// Act: логический слой — распознавание связки и расчёт лока кейса.
		var links = SyntheticCloseRecognizer.Recognize(ExecutionsOf(CaseTrades()));
		var legStreams = StreamsOf(CaseTrades());
		var locked = SyntheticCloseValuationCalculator.CalculateToExpiry(
			links.Single(link => link.ClosedLegSymbol == XautPut4400),
			ValuationOf(CaseTrades(), legStreams[XautPut4400]));

		// Метрики пересчитываются над тем же составом записей — без новых строк.
		var afterPositions = PositionMetricsOf(StreamsOf(CaseTrades()));
		var afterConstruction = ConstructionMetricsOf(afterPositions);

		// Assert: распознана ровно одна связка, лок вычислен — слой не пустой.
		Assert.That(links, Has.Count.EqualTo(1));
		Assert.That(locked, Is.EqualTo(35.697m));

		// Метрики позиций идентичны по записям, а не только по суммам.
		Assert.That(afterPositions, Is.EqualTo(baselinePositions));
		foreach (var (baseline, after) in baselinePositions.Zip(afterPositions, (first, second) => (first, second)))
		{
			Assert.That(after.Symbol, Is.EqualTo(baseline.Symbol));
			Assert.That(after.Residual, Is.EqualTo(baseline.Residual), $"остаток {baseline.Symbol}");
			Assert.That(after.RealizedPnL, Is.EqualTo(baseline.RealizedPnL), $"реализованный результат {baseline.Symbol}");
			Assert.That(after.AccumulatedFees, Is.EqualTo(baseline.AccumulatedFees), $"комиссии {baseline.Symbol}");
			Assert.That(after.UnrealizedPnL, Is.EqualTo(baseline.UnrealizedPnL), $"нереализованный PnL {baseline.Symbol}");
			Assert.That(after.ClosedAt, Is.EqualTo(baseline.ClosedAt), $"статус закрытия {baseline.Symbol}");
		}

		// Итог и статус конструкции совпадают со значением без логического слоя.
		// Реализованный итог включает продажу колла в связке: матч FIFO
		// −1.83 плюс комиссии 0.95, пут −1.5, линейная −0.64.
		// Коллекция предупреждений в записи метрик сравнивается по ссылке:
		// скалярные величины сверяются записью целиком, перечень — по содержанию.
		Assert.That(afterConstruction with { RiskWarnings = [] }, Is.EqualTo(baselineConstruction with { RiskWarnings = [] }));
		Assert.That(afterConstruction.RiskWarnings, Is.EqualTo(baselineConstruction.RiskWarnings));
		Assert.That(afterConstruction.RealizedPnL, Is.EqualTo(-4.92m));
		Assert.That(afterConstruction.UnrealizedPnL, Is.Null);
		Assert.That(afterConstruction.TotalPnL, Is.Null);
		Assert.That(afterConstruction.ClosedAt, Is.Null);
		Assert.That(afterConstruction.Duration, Is.EqualTo(baselineConstruction.Duration));
		Assert.That(afterConstruction.RealRiskStatus, Is.EqualTo(baselineConstruction.RealRiskStatus));
		Assert.That(afterConstruction.RealRiskUsdt, Is.EqualTo(baselineConstruction.RealRiskUsdt));
	}

	[TestMethod]
	[Description("Распознанная связка не меняет инструментальные метрики и при доступных котировках")]
	// С оценёнными нереализованными частями равенство сохраняется: лок не
	// зависит от котировок и не перестраивает оценку позиций.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-instrumental-metrics-untouched
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-locked-value-marks-failure-independent
	public async Task TryIfInstrumentalMetricsMatchWithMarksAvailable()
	{
		// Arrange: базовый прогон с оценкой открытых остатков по котировкам.
		var evaluator = new UnrealizedPnlQuoteEvaluator(new StubQuoteSource());
		var baselinePositions = PositionMetricsOf(StreamsOf(CaseTrades()));
		var baselineEvaluation = await evaluator.EvaluateAsync(baselinePositions);
		var baselineConstruction = ConstructionMetricsOf(baselineEvaluation.Positions);

		// Act: логический слой выполняется между двумя полными пересборами.
		var links = SyntheticCloseRecognizer.Recognize(ExecutionsOf(CaseTrades()));
		var legStreams = StreamsOf(CaseTrades());
		var locked = SyntheticCloseValuationCalculator.CalculateToExpiry(
			links.Single(link => link.ClosedLegSymbol == XautPut4400),
			ValuationOf(CaseTrades(), legStreams[XautPut4400]));

		var afterPositions = PositionMetricsOf(StreamsOf(CaseTrades()));
		var afterEvaluation = await evaluator.EvaluateAsync(afterPositions);
		var afterConstruction = ConstructionMetricsOf(afterEvaluation.Positions);

		// Assert: слой сработал, лок не изменился от наличия котировок.
		Assert.That(links, Has.Count.EqualTo(1));
		Assert.That(locked, Is.EqualTo(35.697m));

		// Нереализованные части оценены и идентичны в обоих прогонах.
		Assert.That(baselineEvaluation.HasQuoteFailure, Is.False);
		Assert.That(afterEvaluation.Positions.Select(position => position.UnrealizedPnL), Has.All.Not.Null);
		Assert.That(afterEvaluation.Positions, Is.EqualTo(baselineEvaluation.Positions));

		// Итог конструкции с котировками совпадает со значением без логического слоя.
		// Коллекция предупреждений в записи метрик сравнивается по ссылке:
		// скалярные величины сверяются записью целиком, перечень — по содержанию.
		Assert.That(afterConstruction with { RiskWarnings = [] }, Is.EqualTo(baselineConstruction with { RiskWarnings = [] }));
		Assert.That(afterConstruction.RiskWarnings, Is.EqualTo(baselineConstruction.RiskWarnings));
		Assert.That(afterConstruction.UnrealizedPnL, Is.EqualTo(baselineConstruction.UnrealizedPnL));
		Assert.That(afterConstruction.TotalPnL, Is.EqualTo(baselineConstruction.TotalPnL));
	}
}
