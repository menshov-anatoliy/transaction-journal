using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки калькулятора реального риска: дебетовый вертикальный спред даёт
/// нетто-дебет, группы разных экспираций суммируются консервативно,
/// неограниченный худший случай и неразобранный символ оставляют величину
/// отсутствующей, сбой марок на метрику не влияет, без открытых остатков риск
/// нулевой, а null-набор позиций отклоняется.
/// </summary>
[TestClass]
public class RealRiskCalculatorTests
{
	private const long ConstructionId = 7;

	/// <summary>Базовый момент записей: 1 января 2026 года 10:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

	private RealRiskCalculator _calculator = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Калькулятор чистый — каждая проверка получает свежий экземпляр без состояния.
		_calculator = new RealRiskCalculator();
	}

	[TestMethod]
	[Description("Дебетовый вертикальный спред даёт нетто-дебет")]
	public void TryIfDebitSpreadYieldsNetDebit()
	{
		// Arrange: одна экспирация — длинный колл 65000 по средней 300 и короткий
		// колл 70000 по средней 150, по одному контракту.
		// Требование: совместный минимум платежа достигается ниже нижнего страйка
		// и равен нетто-дебету 300 − 150 = 150.
		// Traceability: openspec:analytics/performance#scenario-real-risk-debit-spread-net-debit
		var positions = new[]
		{
			OpenResidual("BTC-27DEC24-65000-C", 1m, 300m),
			OpenResidual("BTC-27DEC24-70000-C", -1m, 150m),
		};

		// Act
		var realRisk = _calculator.Calculate(positions);

		// Assert: ниже нижнего страйка платёж равен −150, риск положителен.
		Assert.That(realRisk, Is.EqualTo(150m));
	}

	[TestMethod]
	[Description("Группы разных экспираций суммируются консервативно")]
	public void TryIfMultiExpiryGroupsSumConservatively()
	{
		// Arrange: два длинных колла одной монеты с разными экспирациями; минимум
		// каждой группы — нетто-дебет её единственной ноги: −100 и −50.
		// Требование: реальный риск — сумма минимумов групп без предположения
		// об одновременности худших исходов.
		// Traceability: openspec:analytics/performance#scenario-real-risk-multi-expiry-sums
		var positions = new[]
		{
			OpenResidual("BTC-27DEC24-100-C", 1m, 100m),
			OpenResidual("BTC-27MAR25-200-C", 1m, 50m),
		};

		// Act
		var realRisk = _calculator.Calculate(positions);

		// Assert: −100 + (−50) = −150 — суммарный худший исход, риск 150.
		Assert.That(realRisk, Is.EqualTo(150m));
	}

	[TestMethod]
	[Description("Неограниченный худший случай возвращает отсутствие величины")]
	public void TryIfUnboundedWorstCaseYieldsNull()
	{
		// Arrange: одна экспирация — короткие коллы (−2) перекрывают длинный (+1):
		// суммарная позиция по коллам нетто-короткая, платёж убывает до −∞.
		// Требование: минимум платежа не ограничен — величина отсутствует.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
		var positions = new[]
		{
			OpenResidual("BTC-27DEC24-65000-C", 1m, 300m),
			OpenResidual("BTC-27DEC24-70000-C", -2m, 150m),
		};

		// Act
		var realRisk = _calculator.Calculate(positions);

		// Assert: наклон платежа на +∞ отрицателен — минимум не ограничен снизу.
		Assert.That(realRisk, Is.Null);
	}

	[TestMethod]
	[Description("Неразобранный символ возвращает отсутствие величины")]
	public void TryIfUnparseableSymbolYieldsNull()
	{
		// Arrange: открытый остаток с символом линейного инструмента, который не
		// разбирается как символ опциона.
		// Требование: без страйка и экспирации совместный минимум не определён.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
		var positions = new[] { OpenResidual("BTCUSDT", 1m, 300m) };

		// Act
		var realRisk = _calculator.Calculate(positions);

		// Assert: величина отсутствует целиком, а не обнуляется частично.
		Assert.That(realRisk, Is.Null);
	}

	[TestMethod]
	[Description("Сбой марок не влияет на реальный риск")]
	public void TryIfMarksFailureDoesNotChangeRealRisk()
	{
		// Arrange: один и тот же дебетовый спред в двух наборах — с оценёнными
		// марками и без них (марочные поля null, как при сбое котировок).
		// Требование: величина выводится из структуры ног и средних цен остатков,
		// а не из оценок марок.
		// Traceability: openspec:analytics/performance#scenario-real-risk-marks-failure-independent
		var withMarks = new[]
		{
			OpenResidual("BTC-27DEC24-65000-C", 1m, 300m, markPrice: 320m, unrealizedPnL: 20m, markValue: 20m),
			OpenResidual("BTC-27DEC24-70000-C", -1m, 150m, markPrice: 140m, unrealizedPnL: 10m, markValue: 10m),
		};
		var withoutMarks = new[]
		{
			OpenResidual("BTC-27DEC24-65000-C", 1m, 300m),
			OpenResidual("BTC-27DEC24-70000-C", -1m, 150m),
		};

		// Act
		var evaluated = _calculator.Calculate(withMarks);
		var failed = _calculator.Calculate(withoutMarks);

		// Assert: сбой марок не меняет величину — она определена структурой ног.
		Assert.That(evaluated, Is.EqualTo(150m));
		Assert.That(failed, Is.EqualTo(evaluated));
	}

	[TestMethod]
	[Description("Без открытых остатков реальный риск нулевой")]
	public void TryIfNoOpenResidualsYieldsZero()
	{
		// Arrange: обе позиции конструкции закрыты — открытых остатков нет.
		// Требование: худшего исхода на экспирацию больше нет, риск нулевой.
		// Traceability: openspec:analytics/performance#scenario-real-risk-zero-without-open-residuals
		var positions = new[]
		{
			ClosedPosition("BTC-27DEC24-65000-C"),
			ClosedPosition("ETH-27MAR25-2000-P"),
		};

		// Act
		var realRisk = _calculator.Calculate(positions);

		// Assert: риск нулевой, а не отсутствующий.
		Assert.That(realRisk, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Null-набор позиций отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullPositions()
	{
		// Arrange — Act — Assert
		_calculator.Calculate(null!);
	}

	#region Помощники

	/// <summary>
	/// Строит метрики открытого остатка: знаковое количество и средняя цена
	/// остатка заданы, марочные поля заполняются по желанию — как при сбое марок.
	/// </summary>
	private static PositionMetrics OpenResidual(
		string symbol,
		decimal residual,
		decimal averageOpenPrice,
		decimal? markPrice = null,
		decimal? unrealizedPnL = null,
		decimal? markValue = null) => new()
	{
		ConstructionId = ConstructionId,
		Symbol = symbol,
		Residual = residual,
		RealizedPnL = 0m,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = averageOpenPrice,
		AverageEntryPrice = averageOpenPrice,
		AverageClosePrice = null,
		TotalPnL = unrealizedPnL,
		MarkPrice = markPrice,
		UnrealizedPnL = unrealizedPnL,
		MarkValue = markValue,
		OpenedAt = BaseAt,
		ClosedAt = null,
		Duration = null,
	};

	/// <summary>Строит метрики закрытой позиции: нулевой остаток, средняя цена остатка не вычисляется.</summary>
	private static PositionMetrics ClosedPosition(string symbol) => new()
	{
		ConstructionId = ConstructionId,
		Symbol = symbol,
		Residual = 0m,
		RealizedPnL = 9.97m,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = null,
		AverageEntryPrice = null,
		AverageClosePrice = null,
		TotalPnL = 9.97m,
		MarkPrice = null,
		UnrealizedPnL = 0m,
		OpenedAt = BaseAt,
		ClosedAt = BaseAt.AddMinutes(30),
		Duration = TimeSpan.FromMinutes(30),
	};

	#endregion
}
