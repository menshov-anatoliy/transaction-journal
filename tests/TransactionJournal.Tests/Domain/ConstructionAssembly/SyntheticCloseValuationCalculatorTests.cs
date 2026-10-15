using System.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки расчёта паритетного лока «зафиксировано синтетикой»: формула
/// кейса XAUT 15.10.2026 (≈ +35.7 USDT), количество-взвешенная стоимость
/// входа по нескольким сделкам ноги и вычисление при недоступных марках.
/// Негативные проверки отклоняют незаданные входы и поток ноги без сделок
/// входа.
/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-locked-value-to-expiry
/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-locked-value-marks-failure-independent
/// </summary>
[TestClass]
public class SyntheticCloseValuationCalculatorTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";
	private const long ConstructionId = 1;

	/// <summary>Момент открытия пут-ноги кейса: 30.09.2026 12:00 UTC.</summary>
	private static DateTimeOffset OpenTime() => new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

	/// <summary>Связка кейса XAUT: пут-нога 4400 закрыта линейной покупкой 0.3 и продажей колла 0.3.</summary>
	private static SyntheticCloseLink XautLink() => new()
	{
		LinearExecId = "case-perp",
		LinearExecTimeMs = new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
		CounterOptionExecId = "case-call",
		CounterOptionExecTimeMs = new DateTimeOffset(2026, 10, 15, 13, 4, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
		BaseCoin = "XAUT",
		ClosedLegSymbol = XautPut4400,
		ClosedLegType = OptionType.Put,
		Strike = 4400m,
		BoardExpiryDate = new DateTime(2026, 10, 30),
		Quantity = 0.3m,
	};

	/// <summary>
	/// Ценовые данные связки кейса XAUT: линейная покупка 0.3 @ 4141.91
	/// (fee 0.55), продажа колла 0.3 @ 5.9 (fee 0.35), вход ноги — одна сделка
	/// 0.5 @ 139 с комиссией 1.5 (опционально переопределяется).
	/// </summary>
	private static SyntheticCloseValuation XautValuation(decimal putFee = 1.5m) => new()
	{
		LinearPrice = 4141.91m,
		LinearFee = 0.55m,
		CounterOptionPrice = 5.9m,
		CounterOptionFee = 0.35m,
		ClosedLegEntries = [LegTrade("open-put", 0.5m, 139m, putFee)],
	};

	/// <summary>Trade-запись потока ноги: сделка входа опциона.</summary>
	private static PositionFifoEntry LegTrade(string sourceKey, decimal quantity, decimal price, decimal fee) => new()
	{
		At = OpenTime(),
		Kind = PositionFifoEntryKind.Trade,
		SourceKey = sourceKey,
		Quantity = quantity,
		Price = price,
		Fee = fee,
	};

	[TestMethod]
	[Description("Величина лока кейса XAUT вычисляется по формуле и даёт ≈ +35.7 USDT")]
	// Кейс XAUT 15.10.2026: связка закрывает 0.3 из 0.5 путов 4400, ожидание
	// сценария — около +35.7 USDT.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-locked-value-to-expiry
	public void TryIfXautCaseLockedValueMatchesFormula()
	{
		// Act: расчёт лока по формуле решения 4.
		var locked = SyntheticCloseValuationCalculator.CalculateToExpiry(XautLink(), XautValuation());

		// Assert: точное значение из формулы и входных данных ноги.
		var expected = 0.3m * (4400m - 4141.91m)
			+ 0.3m * 5.9m
			- 0.3m * 139m
			- (0.55m + 0.35m + 0.3m * (1.5m / 0.5m));
		Assert.That(locked, Is.EqualTo(expected));
		Assert.That(locked, Is.EqualTo(35.697m));

		// Округлённое значение совпадает с ожиданием сценария ≈ +35.7 USDT.
		Assert.That(Math.Round(locked, 1), Is.EqualTo(35.7m));
		Assert.That(locked, Is.InRange(35.0m, 36.5m));
	}

	[TestMethod]
	[Description("Стоимость входа и доля комиссий взвешиваются по открывающим частям при двух сделках ноги")]
	// Нога из двух входов: 0.3 @ 130 (fee 0.9) и 0.2 @ 151 (fee 0.6) —
	// FIFO-средняя 138.4, удельная комиссия 3.
	// Traceability: openspec:analytics/performance#scenario-position-average-entry-from-opening-parts
	public void TryIfLegEntryWeightingUsesOpeningParts()
	{
		// Arrange: та же связка, но нога открыта двумя сделками с разными ценами.
		var valuation = XautValuation() with
		{
			ClosedLegEntries =
			[
				LegTrade("open-put-1", 0.3m, 130m, 0.9m),
				LegTrade("open-put-2", 0.2m, 151m, 0.6m),
			],
		};

		// Act: расчёт лока по взвешенной стоимости входа.
		var locked = SyntheticCloseValuationCalculator.CalculateToExpiry(XautLink(), valuation);

		// Assert: (77.427 + 1.77) − (0.3 × 138.4) − (0.9 + 0.3 × 3) = 35.877.
		var expected = 0.3m * (4400m - 4141.91m)
			+ 0.3m * 5.9m
			- 0.3m * ((0.3m * 130m + 0.2m * 151m) / 0.5m)
			- (0.55m + 0.35m + 0.3m * ((0.9m + 0.6m) / 0.5m));
		Assert.That(locked, Is.EqualTo(expected));
		Assert.That(locked, Is.EqualTo(35.877m));
	}

	[TestMethod]
	[Description("Величина лока вычисляется при недоступных марках — деградация затрагивает только нереализованные метрики")]
	// Сбой источника свежих марок не влияет на паритетный лок: строка
	// вычисляется, метрики деградируют, а не падают.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-locked-value-marks-failure-independent
	public async Task TryIfLockedValueComputesWhenMarksUnavailable()
	{
		// Arrange: полный состав кейса XAUT и падающий источник марок.
		var openCall = new PositionFifoEntry { At = OpenTime(), Kind = PositionFifoEntryKind.Trade, SourceKey = "open-call", Quantity = 0.5m, Price = 12m, Fee = 0.6m };
		var openPut = new PositionFifoEntry { At = OpenTime(), Kind = PositionFifoEntryKind.Trade, SourceKey = "open-put", Quantity = 0.5m, Price = 139m, Fee = 1.5m };
		var positions = new List<PositionMetrics>
		{
			new PositionMetricsCalculator().Calculate(ConstructionId, XautCall4400, [openCall]),
			new PositionMetricsCalculator().Calculate(ConstructionId, XautPut4400, [openPut]),
		};

		// Act: пересбор метрик при сбое котировок и расчёт лока той же связки.
		var evaluation = await new UnrealizedPnlMarkEvaluator(new FailingMarkSource())
			.EvaluateAsync(positions);
		var locked = SyntheticCloseValuationCalculator.CalculateToExpiry(XautLink(), XautValuation());

		// Assert: лок вычислен точно, деградация коснулась только нереализованных метрик.
		Assert.That(locked, Is.EqualTo(35.697m));
		Assert.That(evaluation.HasMarkFailure, Is.True);
		Assert.That(evaluation.Positions.Select(position => position.UnrealizedPnL), Has.All.Null);
	}

	[TestMethod]
	[Description("Расчёт лока отклоняет незаданную связку")]
	public void ThrowOnCalculateToExpiryWithoutLink()
	{
		Assert.Throws<ArgumentNullException>(
			() => SyntheticCloseValuationCalculator.CalculateToExpiry(null!, XautValuation()));
	}

	[TestMethod]
	[Description("Расчёт лока отклоняет незаданные ценовые данные")]
	public void ThrowOnCalculateToExpiryWithoutValuation()
	{
		Assert.Throws<ArgumentNullException>(
			() => SyntheticCloseValuationCalculator.CalculateToExpiry(XautLink(), null!));
	}

	[TestMethod]
	[Description("Расчёт лока отклоняет незаданный поток закрываемой ноги")]
	public void ThrowOnCalculateToExpiryWithoutClosedLegEntries()
	{
		var valuation = XautValuation() with { ClosedLegEntries = null! };

		Assert.Throws<ArgumentNullException>(
			() => SyntheticCloseValuationCalculator.CalculateToExpiry(XautLink(), valuation));
	}

	[TestMethod]
	[Description("Расчёт лока отклоняет поток ноги без сделок входа")]
	// Пометка без цены не образует стоимость входа закрываемой части.
	public void ThrowOnCalculateToExpiryWithoutLegEntryTrades()
	{
		var valuation = XautValuation() with
		{
			ClosedLegEntries =
			[
				new PositionFifoEntry { At = OpenTime(), Kind = PositionFifoEntryKind.ManualMark, SourceKey = "put-note", Quantity = 0.5m, Price = 139m, Fee = 0m },
			],
		};

		Assert.Throws<ArgumentException>(
			() => SyntheticCloseValuationCalculator.CalculateToExpiry(XautLink(), valuation));
	}

	/// <summary>Заглушка сбоя: свежая марка всегда недоступна — сеть до публичных тикеров не доходит.</summary>
	private sealed class FailingMarkSource : IFreshInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			throw new HttpRequestException("публичные тикеры недоступны");
	}
}
