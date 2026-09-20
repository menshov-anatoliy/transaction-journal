using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки калькулятора метрик конструкции: итог суммирует результаты позиций
/// и внешние корректировки PnL, проценты относятся к текущему выделенному
/// капиталу и меняются только от его правки, даты и длительность выводятся из
/// записей позиций, а длительность открытой конструкции считается до текущего
/// момента. Негативные проверки отклоняют незаданные наборы и чужую позицию.
/// </summary>
[TestClass]
public class ConstructionMetricsCalculatorTests
{
	private const string FirstSymbol = "BTC-29DEC23-45000-C";

	private const string SecondSymbol = "ETH-29DEC23-3000-C";

	private const long ConstructionId = 7;

	/// <summary>Базовый момент записей: 1 января 2026 года 10:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

	/// <summary>Текущий момент проверок: 1 января 2026 года 12:00 UTC — граница длительности открытых конструкций.</summary>
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private ConstructionMetricsCalculator _calculator = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Калькулятор чистый — каждая проверка получает свежий экземпляр без состояния.
		_calculator = new ConstructionMetricsCalculator();
	}

	[TestMethod]
	[Description("Итог конструкции включает результаты позиций и корректировки")]
	public void TryIfConstructionTotalIncludesAdjustments()
	{
		// Arrange: две закрытые позиции с результатами 9.97 и −2.5 и две внешние
		// корректировки +50 и −7.5; закрытые позиции не имеют нереализованной части.
		// Требование: итог конструкции равен сумме результатов позиций и корректировок.
		// Traceability: openspec:analytics/performance#scenario-construction-total-includes-adjustments
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 9.97m),
			ClosedPosition(SecondSymbol, 10, 50, -2.5m),
		};
		var adjustments = new[]
		{
			Adjustment(60, 50m),
			Adjustment(70, -7.5m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, adjustments, Now);

		// Assert: итог = 9.97 − 2.5 + 50 − 7.5 = 49.97; закрытые позиции дают
		// нереализованный ноль, корректировки — слагаемое без сделок.
		Assert.That(metrics.ConstructionId, Is.EqualTo(ConstructionId));
		Assert.That(metrics.AllocatedCapitalUsdt, Is.EqualTo(1000m));
		Assert.That(metrics.RealizedPnL, Is.EqualTo(7.47m));
		Assert.That(metrics.UnrealizedPnL, Is.EqualTo(0m));
		Assert.That(metrics.AdjustmentsPnL, Is.EqualTo(42.5m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(49.97m));
	}

	[TestMethod]
	[Description("Проценты считаются от текущего капитала и меняются только от его правки")]
	public void TryIfPercentsAreComputedFromCurrentCapital()
	{
		// Arrange: закрытая позиция с результатом 100 и корректировка +50.
		// Требование: проценты отнесены к текущему значению выделенного капитала;
		// изменение капитала меняет только проценты, не абсолютные величины.
		// Traceability: openspec:analytics/performance#scenario-percent-from-current-capital
		// Traceability: openspec:domain/constructions#scenario-capital-change-affects-percent-only
		var positions = new[] { ClosedPosition(FirstSymbol, 0, 30, 100m) };
		var adjustments = new[] { Adjustment(60, 50m) };

		// Act: один и тот же набор при капитале 1000 и после правки капитала до 2000.
		var before = _calculator.Calculate(ConstructionId, 1000m, positions, adjustments, Now);
		var after = _calculator.Calculate(ConstructionId, 2000m, positions, adjustments, Now);

		// Assert: капитал 1000 — реализованные 10 %, корректировки 5 %, итог 15 %.
		Assert.That(before.RealizedPnLPercent, Is.EqualTo(10m));
		Assert.That(before.AdjustmentsPnLPercent, Is.EqualTo(5m));
		Assert.That(before.UnrealizedPnLPercent, Is.EqualTo(0m));
		Assert.That(before.TotalPnLPercent, Is.EqualTo(15m));

		// Assert: удвоенная база уполовинила проценты, абсолютные величины
		// не изменились.
		Assert.That(after.RealizedPnLPercent, Is.EqualTo(5m));
		Assert.That(after.AdjustmentsPnLPercent, Is.EqualTo(2.5m));
		Assert.That(after.TotalPnLPercent, Is.EqualTo(7.5m));
		Assert.That(after.RealizedPnL, Is.EqualTo(before.RealizedPnL));
		Assert.That(after.AdjustmentsPnL, Is.EqualTo(before.AdjustmentsPnL));
		Assert.That(after.TotalPnL, Is.EqualTo(before.TotalPnL));
	}

	[TestMethod]
	[Description("Даты конструкции выводятся из записей позиций")]
	public void TryIfConstructionDatesAreDerivedFromEntries()
	{
		// Arrange: позиция BTC открыта в минуту 0 и закрыта в 30, позиция ETH —
		// открыта в 10 и закрыта в 50; обе закрыты встречными записями.
		// Требование: открытие — время первой сделки, закрытие — момент обнуления
		// последней позиции, длительность — разница между ними.
		// Traceability: openspec:analytics/performance#scenario-construction-dates-derived
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 9.97m),
			ClosedPosition(SecondSymbol, 10, 50, -2.5m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: первая сделка — минута 0, последняя позиция обнулилась в минуту 50.
		Assert.That(metrics.OpenedAt, Is.EqualTo(At(0)));
		Assert.That(metrics.ClosedAt, Is.EqualTo(At(50)));
		Assert.That(metrics.Duration, Is.EqualTo(TimeSpan.FromMinutes(50)));
	}

	[TestMethod]
	[Description("Длительность открытой конструкции считается до текущего момента")]
	public void TryIfOpenConstructionDurationCountsToNow()
	{
		// Arrange: закрытая позиция BTC (0→30) и открытый остаток ETH с десятой
		// минуты; текущий момент — 120 минут от базового.
		// Требование: при ненулевой позиции длительность считается от первой сделки
		// до текущего момента, даты закрытия нет.
		// Traceability: openspec:analytics/performance#scenario-open-construction-duration-to-now
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 9.97m),
			OpenPosition(SecondSymbol, 10, -2.5m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: конструкция открыта остатком ETH — закрытия нет, длительность
		// тянется от первой сделки (минута 0) до текущего момента (минута 120).
		Assert.That(metrics.OpenedAt, Is.EqualTo(At(0)));
		Assert.That(metrics.ClosedAt, Is.Null);
		Assert.That(metrics.Duration, Is.EqualTo(TimeSpan.FromMinutes(120)));
	}

	[TestMethod]
	[Description("Неоцененный остаток обнуляет только нереализованную часть и итог")]
	public void TryIfUnevaluatedResidualNullsOnlyUnrealizedPart()
	{
		// Arrange: открытый остаток без оценки марками, закрытая позиция и
		// корректировка +20 в результате участвуют.
		// Требование: нереализованная оценка отсутствует, пока открытый остаток
		// не оценен; реализованные метрики, корректировки, даты и их проценты
		// возвращаются как есть, итог деградирует вместе с нереализованной частью.
		// Traceability: openspec:analytics/performance#requirement-construction-metrics
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 9.97m),
			OpenPosition(SecondSymbol, 10, -2.5m),
		};
		var adjustments = new[] { Adjustment(60, 20m) };

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, adjustments, Now);

		// Assert: реализованный результат и корректировки на месте, нереализованная
		// часть, итог и их проценты — null; проценты реализованных величин считаются.
		Assert.That(metrics.RealizedPnL, Is.EqualTo(7.47m));
		Assert.That(metrics.AdjustmentsPnL, Is.EqualTo(20m));
		Assert.That(metrics.UnrealizedPnL, Is.Null);
		Assert.That(metrics.TotalPnL, Is.Null);
		Assert.That(metrics.UnrealizedPnLPercent, Is.Null);
		Assert.That(metrics.TotalPnLPercent, Is.Null);
		Assert.That(metrics.RealizedPnLPercent, Is.EqualTo(0.747m));
	}

	[TestMethod]
	[Description("Конструкция без записей даёт нулевой результат без дат")]
	public void TryIfEmptyConstructionYieldsZeroResultWithoutDates()
	{
		// Arrange — Act: конструкция без позиций и корректировок существует и
		// читается наравне с прочими.
		// Требование: итог пустой конструкции — ноль; дат нет, пока нет сделок,
		// длительность не выводится.
		// Traceability: openspec:analytics/performance#requirement-construction-metrics
		var metrics = _calculator.Calculate(
			ConstructionId, 1000m, Array.Empty<PositionMetrics>(), Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: нулевой результат с нулевыми процентами; даты и длительность — null.
		Assert.That(metrics.RealizedPnL, Is.EqualTo(0m));
		Assert.That(metrics.UnrealizedPnL, Is.EqualTo(0m));
		Assert.That(metrics.AdjustmentsPnL, Is.EqualTo(0m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(0m));
		Assert.That(metrics.TotalPnLPercent, Is.EqualTo(0m));
		Assert.That(metrics.OpenedAt, Is.Null);
		Assert.That(metrics.ClosedAt, Is.Null);
		Assert.That(metrics.Duration, Is.Null);
	}

	[TestMethod]
	[Description("Нулевой капитал не образует базы процентов")]
	public void TryIfZeroCapitalLeavesPercentsNull()
	{
		// Arrange: закрытая позиция с результатом 100 при нулевом капитале.
		// Требование: проценты относятся к текущему капиталу; без базы проценты
		// не вычисляются, абсолютные величины возвращаются.
		// Traceability: openspec:analytics/performance#scenario-percent-from-current-capital
		var positions = new[] { ClosedPosition(FirstSymbol, 0, 30, 100m) };

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 0m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: абсолютные величины на месте, процентные — null.
		Assert.That(metrics.RealizedPnL, Is.EqualTo(100m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(100m));
		Assert.That(metrics.RealizedPnLPercent, Is.Null);
		Assert.That(metrics.UnrealizedPnLPercent, Is.Null);
		Assert.That(metrics.AdjustmentsPnLPercent, Is.Null);
		Assert.That(metrics.TotalPnLPercent, Is.Null);
	}

	[TestMethod]
	[Description("Null-набор позиций отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullPositions()
	{
		// Arrange — Act — Assert
		_calculator.Calculate(ConstructionId, 1000m, null!, Array.Empty<ConstructionPnLAdjustment>(), Now);
	}

	[TestMethod]
	[Description("Null-набор корректировок отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullAdjustments()
	{
		// Arrange — Act — Assert
		_calculator.Calculate(ConstructionId, 1000m, Array.Empty<PositionMetrics>(), null!, Now);
	}

	[TestMethod]
	[Description("Чужая позиция в наборе отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnForeignPosition()
	{
		// Arrange: в набор попала позиция другой конструкции — её результат
		// задвоил бы итог.
		var positions = new[] { ClosedPosition(FirstSymbol, 0, 30, 9.97m, ConstructionId + 1) };

		// Act — Assert
		_calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);
	}

	#region Помощники

	private static DateTimeOffset At(int minutes) => BaseAt.AddMinutes(minutes);

	/// <summary>Строит метрики закрытой позиции: нулевой остаток, нереализованный ноль, даты открытия и закрытия.</summary>
	private static PositionMetrics ClosedPosition(string symbol, int openedAt, int closedAt, decimal realizedPnL, long? constructionId = null) => new()
	{
		ConstructionId = constructionId ?? ConstructionId,
		Symbol = symbol,
		Residual = 0m,
		RealizedPnL = realizedPnL,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = null,
		MarkPrice = null,
		UnrealizedPnL = 0m,
		OpenedAt = At(openedAt),
		ClosedAt = At(closedAt),
		Duration = TimeSpan.FromMinutes(closedAt - openedAt),
	};

	/// <summary>Строит метрики открытой позиции: ненулевой остаток, нереализованная оценка ещё не подставлена.</summary>
	private static PositionMetrics OpenPosition(string symbol, int openedAt, decimal realizedPnL, long? constructionId = null) => new()
	{
		ConstructionId = constructionId ?? ConstructionId,
		Symbol = symbol,
		Residual = 1m,
		RealizedPnL = realizedPnL,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = 120m,
		MarkPrice = null,
		UnrealizedPnL = null,
		OpenedAt = At(openedAt),
		ClosedAt = null,
		Duration = null,
	};

	/// <summary>Строит внешнюю корректировку PnL конструкции.</summary>
	private static ConstructionPnLAdjustment Adjustment(int minutes, decimal amountUsdt) => new()
	{
		Date = At(minutes),
		AmountUsdt = amountUsdt,
	};

	#endregion
}
