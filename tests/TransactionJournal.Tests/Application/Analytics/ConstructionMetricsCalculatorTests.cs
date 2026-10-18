using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки калькулятора метрик конструкции: итог суммирует результаты позиций
/// и внешние корректировки PnL, проценты относятся к текущему выделенному
/// капиталу и меняются только от его правки, даты и длительность выводятся из
/// записей позиций, а длительность открытой конструкции считается до текущего
/// момента. Худший исход собирается как реализованный результат плюс
/// неклэмпированный минимум платежа остатков: гарантированная прибыль смещает
/// его выше реализованного, без открытых остатков он равен реализованному.
/// Негативные проверки отклоняют незаданные наборы и чужую позицию.
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
	[Description("Незаданный капитал не образует процентных метрик")]
	public void TryIfNoCapitalLeavesPercentMetricsNull()
	{
		// Arrange: закрытая позиция с результатом 100 и корректировка +50 при
		// незаданном капитале; позиция открыта в минуту 0 и закрыта в 30.
		// Требование: при незаданном капитале процентные метрики отсутствуют,
		// абсолютные метрики, даты и длительность возвращаются без изменений.
		// Traceability: openspec:analytics/performance#scenario-no-capital-no-percent-metrics
		var positions = new[] { ClosedPosition(FirstSymbol, 0, 30, 100m) };
		var adjustments = new[] { Adjustment(60, 50m) };

		// Act
		var metrics = _calculator.Calculate(ConstructionId, null, positions, adjustments, Now);

		// Assert: все процентные величины null, итог и слагаемые на месте,
		// капитал передаётся незаданным, даты и длительность не задеты.
		Assert.That(metrics.AllocatedCapitalUsdt, Is.Null);
		Assert.That(metrics.RealizedPnL, Is.EqualTo(100m));
		Assert.That(metrics.AdjustmentsPnL, Is.EqualTo(50m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(150m));
		Assert.That(metrics.RealizedPnLPercent, Is.Null);
		Assert.That(metrics.UnrealizedPnLPercent, Is.Null);
		Assert.That(metrics.AdjustmentsPnLPercent, Is.Null);
		Assert.That(metrics.TotalPnLPercent, Is.Null);
		Assert.That(metrics.OpenedAt, Is.EqualTo(At(0)));
		Assert.That(metrics.ClosedAt, Is.EqualTo(At(30)));
		Assert.That(metrics.Duration, Is.EqualTo(TimeSpan.FromMinutes(30)));
	}

	[TestMethod]
	[Description("Стоимость конструкции — сумма стоимостей открытых позиций")]
	public void TryIfConstructionValueSumsOpenPositionValues()
	{
		// Arrange: открытый лонг BTC со стоимостью 240 и открытый шорт ETH со
		// стоимостью −150; выделенный капитал 3000.
		// Требование: стоимость конструкции — сумма стоимостей открытых позиций,
		// занято капитала — стоимость в процентах от капитала.
		// Traceability: openspec:analytics/performance#scenario-construction-value-sums-positions
		// Traceability: openspec:analytics/performance#scenario-capital-usage-computed-when-capital-set
		var positions = new[]
		{
			OpenPosition(FirstSymbol, 0, 9.97m, markValue: 240m),
			OpenPosition(SecondSymbol, 10, -2.5m, residual: -3m, markValue: -150m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 3000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: 240 − 150 = 90; занято капитала 90 / 3000 × 100 = 3 %.
		Assert.That(metrics.MarkValue, Is.EqualTo(90m));
		Assert.That(metrics.CapitalUsagePercent, Is.EqualTo(3m));
	}

	[TestMethod]
	[Description("Закрытая конструкция стоимости не имеет")]
	public void TryIfClosedConstructionHasNoMarkValue()
	{
		// Arrange: обе позиции конструкции закрыты — стоимостей у позиций нет.
		// Требование: без открытых остатков стоимость конструкции отсутствует,
		// занятость капитала не вычисляется, остальные метрики не задеты.
		// Traceability: openspec:analytics/performance#scenario-closed-position-has-no-mark-value
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 9.97m),
			ClosedPosition(SecondSymbol, 10, 50, -2.5m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: стоимость и занятость null, реализованный итог на месте.
		Assert.That(metrics.MarkValue, Is.Null);
		Assert.That(metrics.CapitalUsagePercent, Is.Null);
		Assert.That(metrics.RealizedPnL, Is.EqualTo(7.47m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(7.47m));
	}

	[TestMethod]
	[Description("Неоцененный открытый остаток обнуляет стоимость конструкции")]
	public void TryIfUnevaluatedOpenResidualNullsConstructionValue()
	{
		// Arrange: открытый остаток ETH оценен марками, открытый остаток BTC — нет.
		// Требование: стоимость конструкции недоступна, пока хоть один открытый
		// остаток не оценен; занятость капитала деградирует вместе со стоимостью.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-mark-value
		var positions = new[]
		{
			OpenPosition(FirstSymbol, 0, 0m),
			OpenPosition(SecondSymbol, 10, 0m, markValue: 90m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: стоимость и занятость null, реализованные величины не задеты.
		Assert.That(metrics.MarkValue, Is.Null);
		Assert.That(metrics.CapitalUsagePercent, Is.Null);
		Assert.That(metrics.RealizedPnL, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Отрицательная стоимость даёт отрицательную занятость капитала")]
	public void TryIfNegativeValueYieldsNegativeCapitalUsage()
	{
		// Arrange: открытый шорт со стоимостью −40; капитал то задан, то нет.
		// Требование: занятость капитала сохраняет знак стоимости; незаданный
		// капитал оставляет стоимость, но занятость не вычисляется.
		// Traceability: openspec:analytics/performance#scenario-capital-usage-computed-when-capital-set
		// Traceability: openspec:analytics/performance#scenario-capital-usage-absent-without-capital
		var positions = new[] { OpenPosition(FirstSymbol, 0, 0m, residual: -1m, markValue: -40m) };

		// Act: один набор при капитале 200 и при незаданном капитале.
		var withCapital = _calculator.Calculate(ConstructionId, 200m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);
		var withoutCapital = _calculator.Calculate(ConstructionId, null, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: −40 / 200 × 100 = −20 %; без капитала стоимость остаётся.
		Assert.That(withCapital.MarkValue, Is.EqualTo(-40m));
		Assert.That(withCapital.CapitalUsagePercent, Is.EqualTo(-20m));
		Assert.That(withoutCapital.MarkValue, Is.EqualTo(-40m));
		Assert.That(withoutCapital.CapitalUsagePercent, Is.Null);
	}

	[TestMethod]
	[Description("Метрики конструкции раскрывают реальный риск открытых остатков со статусом конечного риска")]
	public void TryIfConstructionMetricsExposeRealRisk()
	{
		// Arrange: дебетовый спред из открытых остатков одной экспирации — длинный
		// колл 65000 по средней 300 и короткий колл 70000 по средней 150.
		// Требование: калькулятор метрик вычисляет реальный риск конструкции
		// совместным минимумом платежа ног и публикует его в метриках вместе
		// со статусом конечного риска.
		// Traceability: openspec:analytics/performance#scenario-real-risk-debit-spread-net-debit
		// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
		var positions = new[]
		{
			OpenPosition("BTC-27DEC24-65000-C", 0, 0m, averageOpenPrice: 300m),
			OpenPosition("BTC-27DEC24-70000-C", 0, 0m, residual: -1m, averageOpenPrice: 150m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: реальный риск равен нетто-дебету спреда и помечен конечным.
		Assert.That(metrics.RealRiskUsdt, Is.EqualTo(150m));
		Assert.That(metrics.RealRiskStatus, Is.EqualTo(RealRiskStatus.Finite));
	}

	[TestMethod]
	[Description("Одинокий короткий колл переносит неограниченный статус без величины, не задевая результат")]
	public void TryIfUnboundedShortCallCarriesStatusWithoutValue()
	{
		// Arrange: закрытая нога с результатом 9.97 и открытый остаток — одинокий
		// короткий колл; суммарная позиция по коллам нетто-короткая.
		// Требование: неограниченный хвост сопровождается null со статусом
		// unbounded, а расчёт результата конструкции не меняется.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 9.97m),
			OpenPosition("BTC-27DEC24-70000-C", 0, 0m, residual: -1m, averageOpenPrice: 150m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: величины нет, статус неограниченный, а реализованный результат
		// и его доля от капитала остаются прежними — статус риска не трогает P&L.
		Assert.That(metrics.RealRiskUsdt, Is.Null);
		Assert.That(metrics.RealRiskStatus, Is.EqualTo(RealRiskStatus.Unbounded));
		Assert.That(metrics.RealizedPnL, Is.EqualTo(9.97m));
		Assert.That(metrics.RealizedPnLPercent, Is.EqualTo(0.997m));
	}

	[TestMethod]
	[Description("Неразобранный символ открытого остатка оставляет реальный риск отсутствующим со статусом нерассчитанного")]
	public void TryIfUnparseableResidualLeavesRealRiskNull()
	{
		// Arrange: открытый остаток с символом, который не разбирается как символ
		// опциона, рядом с разобранным остатком.
		// Требование: неопределённый совместный минимум деградирует только метрику
		// реального риска со статусом unavailable, остальные метрики не задеты.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
		var positions = new[]
		{
			OpenPosition("BTCUSDT", 0, 0m, residual: 2m),
			OpenPosition(SecondSymbol, 10, -2.5m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: реальный риск отсутствует со статусом «не рассчитан»,
		// реализованные величины на месте.
		Assert.That(metrics.RealRiskUsdt, Is.Null);
		Assert.That(metrics.RealRiskStatus, Is.EqualTo(RealRiskStatus.Unavailable));
		Assert.That(metrics.RealizedPnL, Is.EqualTo(-2.5m));
	}

	[TestMethod]
	[Description("Закрытая конструкция имеет нулевой конечный реальный риск")]
	public void TryIfClosedConstructionHasZeroRealRisk()
	{
		// Arrange: обе позиции конструкции закрыты — открытых остатков нет.
		// Требование: без открытых остатков реальный риск нулевой, а не отсутствует,
		// и ноль помечен конечным статусом, а не нерассчитанным.
		// Traceability: openspec:analytics/performance#scenario-real-risk-zero-without-open-residuals
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 9.97m),
			ClosedPosition(SecondSymbol, 10, 50, -2.5m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: нулевой риск при полном наборе прочих метрик, статус конечный.
		Assert.That(metrics.RealRiskUsdt, Is.EqualTo(0m));
		Assert.That(metrics.RealRiskStatus, Is.EqualTo(RealRiskStatus.Finite));
		Assert.That(metrics.RealizedPnL, Is.EqualTo(7.47m));
	}

	[TestMethod]
	[Description("Гарантированная прибыль остатков смещает худший исход выше реализованного результата")]
	public void TryIfGuaranteedResidualProfitShiftsWorstCaseAboveRealized()
	{
		// Arrange: открытые остатки полностью зафиксированы синтетическим
		// паритетным локом — длинный колл 100 по средней 60 и короткий перп
		// BTCUSDT −1 по средней 195.7 с суммарным минимумом платежа +35.7 —
		// при реализованном результате позиций −46.6.
		// Требование: худший исход равен реализованному результату плюс
		// неклэмпированный минимум платежа остатков и остаётся отрицательным
		// даже при нулевом реальном риске.
		// Traceability: openspec:analytics/performance#scenario-worst-case-guaranteed-profit
		// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
		var positions = new[]
		{
			OpenPosition("BTC-27DEC24-100-C", 0, -23.3m, residual: 1m, averageOpenPrice: 60m),
			OpenPosition("BTCUSDT", 0, -23.3m, residual: -1m, averageOpenPrice: 195.7m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: −46.6 + 35.7 = −10.9; реальный риск клэмпится нулём, статус
		// конечный — гарантированная прибыль остатков риском не считается.
		Assert.That(metrics.WorstCaseUsdt, Is.EqualTo(-10.9m));
		Assert.That(metrics.RealRiskUsdt, Is.EqualTo(0m));
		Assert.That(metrics.RealRiskStatus, Is.EqualTo(RealRiskStatus.Finite));
	}

	[TestMethod]
	[Description("Без открытых остатков худший исход равен реализованному результату")]
	public void TryIfClosedConstructionWorstCaseEqualsRealized()
	{
		// Arrange: конструкция полностью закрыта с реализованным результатом +200 —
		// открытых остатков нет.
		// Требование: худший платёж остатков нулевой, поэтому худший исход
		// совпадает с реализованным результатом при конечном статусе.
		// Traceability: openspec:analytics/performance#scenario-worst-case-closed-equals-realized
		var positions = new[]
		{
			ClosedPosition(FirstSymbol, 0, 30, 200m),
			ClosedPosition(SecondSymbol, 10, 50, 0m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);

		// Assert: худший исход +200, риска нет, статус конечный.
		Assert.That(metrics.WorstCaseUsdt, Is.EqualTo(200m));
		Assert.That(metrics.RealRiskUsdt, Is.EqualTo(0m));
		Assert.That(metrics.RealRiskStatus, Is.EqualTo(RealRiskStatus.Finite));
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

	[TestMethod]
	[Description("Предупреждения об экспирации входят в единый канал предупреждений и растят счётчик бейджа")]
	// Метрики времени жизни встраиваются тем же вызовом калькулятора: саммари
	// публикуется рядом с метриками конструкции, записи предупреждения об
	// экспирации сливаются с диагностикой незакрытых рисков, счётчик бейджа
	// «⚠ N» равен их сумме, перечень и счётчик не расходятся.
	// Traceability: openspec:analytics/option-lifetime#requirement-expiry-warning-two-weeks
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	public void TryIfLifetimeWarningsMergeIntoRiskWarningsAndBadge()
	{
		// Arrange: открытый фьючерсный остаток даёт одну диагностику незакрытых
		// рисков, опционная нога с остатком девять полных суток — предупреждение
		// об экспирации; фальшивые часы фиксируют остаток суток.
		var calculator = new ConstructionMetricsCalculator(new FixedTimeProvider(Now));
		var positions = new[] { OpenPosition("BTCUSDT", 0, 0m, residual: 2m) };
		var lifetime = new OptionLifetimeInput
		{
			Legs = new[] { NearExpiryLeg("BTC-28OCT26-90000-C") },
			Marks = new Dictionary<string, InstrumentMarkSnapshot>(StringComparer.Ordinal),
			SnapshotHistory = Array.Empty<InstrumentMarketSnapshot>(),
		};

		// Act
		var metrics = calculator.Calculate(ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now, lifetime);

		// Assert: саммари времени жизни опубликовано рядом, в канале предупреждений
		// две записи — диагностика и экспирация, счётчик бейджа равен их сумме.
		Assert.That(metrics.Lifetime, Is.Not.Null);
		Assert.That(metrics.Lifetime!.Summary, Is.Not.Null);
		Assert.That(metrics.Lifetime.Summary!.DaysToNearestExpiry, Is.EqualTo(9));
		Assert.That(metrics.Lifetime.Summary.NetDailyDecayUsdt, Is.Null);
		Assert.That(metrics.RiskWarnings, Has.Count.EqualTo(2));
		Assert.That(metrics.RiskWarnings.Count(warning => warning.Reason == ConstructionRiskReason.ExpiryApproaching), Is.EqualTo(1));
		Assert.That(metrics.RiskWarnings.Single(warning => warning.Reason == ConstructionRiskReason.ExpiryApproaching).Text, Does.Contain("BTC-28OCT26-90000-C"));
		Assert.That(metrics.RiskWarningsCount, Is.EqualTo(2));
	}

	[TestMethod]
	[Description("Без опционных ног метрики времени жизни отсутствуют и канал предупреждений не меняется")]
	// Негативная сторона встраивания: без расчётных данных времени жизни
	// саммари отсутствует, предупреждений об экспирации нет, счётчик бейджа
	// остаётся равным диагностике незакрытых рисков.
	// Traceability: openspec:analytics/option-lifetime#scenario-summary-no-option-legs
	public void TryIfMetricsWithoutLifetimeKeepDiagnosticsOnly()
	{
		// Arrange: тот же открытый фьючерсный остаток, но без опционных ног.
		var positions = new[] { OpenPosition("BTCUSDT", 0, 0m, residual: 2m) };

		// Act
		var withLifetime = _calculator.Calculate(
			ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now);
		var withoutLifetime = _calculator.Calculate(
			ConstructionId, 1000m, positions, Array.Empty<ConstructionPnLAdjustment>(), Now, lifetime: null);

		// Assert: в обоих чтениях саммари нет, канал состоит из одной диагностики,
		// счётчик бейджа не вырос от отсутствия опционных ног.
		Assert.That(withLifetime.Lifetime, Is.Null);
		Assert.That(withoutLifetime.Lifetime, Is.Null);
		Assert.That(withLifetime.RiskWarnings, Has.Count.EqualTo(1));
		Assert.That(withoutLifetime.RiskWarnings, Is.EquivalentTo(withLifetime.RiskWarnings));
		Assert.That(withoutLifetime.RiskWarningsCount, Is.EqualTo(1));
	}

	/// <summary>Открытая опционная нога с остатком девять полных суток на момент фальшивых часов.</summary>
	private static OptionLegLifetimeInput NearExpiryLeg(string symbol) => new()
	{
		Symbol = symbol,
		Strike = 90000m,
		Type = OptionType.Call,
		DeliveryTime = Now.AddDays(9.5),
		Residual = 1m,
	};

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
		AverageEntryPrice = null,
		AverageClosePrice = null,
		TotalPnL = realizedPnL,
		MarkPrice = null,
		UnrealizedPnL = 0m,
		OpenedAt = At(openedAt),
		ClosedAt = At(closedAt),
		Duration = TimeSpan.FromMinutes(closedAt - openedAt),
	};

	/// <summary>Строит метрики открытой позиции: ненулевой остаток, нереализованная оценка ещё не подставлена.</summary>
	private static PositionMetrics OpenPosition(string symbol, int openedAt, decimal realizedPnL, long? constructionId = null, decimal residual = 1m, decimal? markValue = null, decimal? averageOpenPrice = 120m) => new()
	{
		ConstructionId = constructionId ?? ConstructionId,
		Symbol = symbol,
		Residual = residual,
		RealizedPnL = realizedPnL,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = averageOpenPrice,
		AverageEntryPrice = null,
		AverageClosePrice = null,
		TotalPnL = null,
		MarkPrice = null,
		UnrealizedPnL = null,
		MarkValue = markValue,
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
