using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки калькулятора реального риска: дебетовый вертикальный спред даёт
/// нетто-дебет, группы разных экспираций суммируются консервативно,
/// неограниченный худший случай и неполные исходные данные различаются
/// состояниями типизированного результата, сбой марок на метрику не влияет,
/// без открытых остатков риск конечен и нулевой, конструкция с хедж-перпом
/// больше не деградирует в недоступность, хеджированный короткий перп даёт
/// конечный риск с числовым совпадением и ручным расчётом, голый короткий
/// перп — в опционной группе и без опций — неограничен, линейная нога входит
/// в группу ранней экспирации своей базы, перп без опций той же базы
/// образует собственную группу, а null-набор позиций отклоняется.
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
		var result = _calculator.CalculateResult(positions);

		// Assert: ниже нижнего страйка платёж равен −150, риск конечен и положителен.
		Assert.That(result, Is.EqualTo(RealRiskResult.Finite(150m)));
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
		var result = _calculator.CalculateResult(positions);

		// Assert: −100 + (−50) = −150 — суммарный худший исход, риск конечен и равен 150.
		Assert.That(result, Is.EqualTo(RealRiskResult.Finite(150m)));
	}

	[TestMethod]
	[Description("Неограниченный худший случай возвращает отсутствие величины")]
	public void TryIfUnboundedWorstCaseOmitsValue()
	{
		// Arrange: одна экспирация — короткие коллы (−2) перекрывают длинный (+1):
		// суммарная позиция по коллам нетто-короткая, платёж убывает до −∞.
		// Требование: минимум платежа не ограничен — величина отсутствует,
		// статус результата «не ограничен».
		// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
		var positions = new[]
		{
			OpenResidual("BTC-27DEC24-65000-C", 1m, 300m),
			OpenResidual("BTC-27DEC24-70000-C", -2m, 150m),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: наклон платежа на +∞ отрицателен — статус unbounded без числа.
		Assert.That(result.Status, Is.EqualTo(RealRiskStatus.Unbounded));
		Assert.That(result.Usdt, Is.Null);
	}

	[TestMethod]
	[Description("Одинокий короткий колл после закрытия длинной ноги неограничен")]
	public void TryIfLoneShortCallAfterClosedLongYieldsUnbounded()
	{
		// Arrange: защитная длинная нога закрыта — её остаток нулевой и средней
		// цены остатка у неё больше нет; остался только короткий колл при
		// положительном реализованном результате конструкции.
		// Требование: нулевой остаток исключается из групп и не делает данные
		// неполными, а оставшийся нетто-короткий колл даёт неограниченный риск
		// независимо от реализованного итога.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
		// Traceability: change:show-unbounded-finresult-risk/design#d4
		var positions = new[]
		{
			ClosedPosition("BTC-27DEC24-65000-C"),
			OpenResidual("BTC-27DEC24-70000-C", -1m, 150m),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: закрытая нога не маскирует неограниченный хвост короткого колла.
		Assert.That(result.Status, Is.EqualTo(RealRiskStatus.Unbounded));
		Assert.That(result.Usdt, Is.Null);
	}

	[TestMethod]
	[Description("Неизвестные данные при наличии короткого колла дают недоступность")]
	public void TryIfUnknownDataTakePrecedenceOverShortCallTail()
	{
		// Arrange: один открытый остаток не разбирается ни как символ опциона,
		// ни как символ линейного фьючерса, а в известной группе есть
		// нетто-короткий колл.
		// Требование: неполнота данных обнаруживается раньше классификации
		// хвостов групп — статус «не рассчитан», а не «не ограничен».
		// Traceability: openspec:analytics/performance#scenario-real-risk-unknown-group-takes-precedence
		var positions = new[]
		{
			OpenResidual("BTCUSD", 1m, 300m),
			OpenResidual("BTC-27DEC24-70000-C", -1m, 150m),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: отсутствие данных не маскируется выводом о неограниченности конструкции.
		Assert.That(result.Status, Is.EqualTo(RealRiskStatus.Unavailable));
		Assert.That(result.Usdt, Is.Null);
	}

	[TestMethod]
	[Description("Стреддл с открытым хедж-перпом больше не даёт недоступность")]
	public void TryIfStraddleWithHedgePerpIsNoLongerUnavailable()
	{
		// Arrange: проданный стреддл одной экспирации — короткие колл и пут
		// 4400 — с открытым хеджем длинным перпом XAUTUSDT, как в живой
		// конструкции «XAUT стреддл 30OCT26 4400».
		// Требование: символ перпа разбирается как линейная нога, поэтому
		// конструкция больше не деградирует в состояние «не рассчитан».
		// Traceability: openspec:analytics/performance#scenario-real-risk-linear-hedge-is-finite
		var positions = new[]
		{
			OpenResidual("XAUT-30OCT26-4400-C", -1m, 80m),
			OpenResidual("XAUT-30OCT26-4400-P", -1m, 60m),
			OpenResidual("XAUTUSDT", 1m, 4100m),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: наклон за последним узлом равен −1 + 1 = 0 — риск конечен
		// и совпадает с ручным расчётом: узел нулевой цены платит
		// +80 − 4340 − 4100 = −8360, узел страйка платит +80 + 60 + 300 = +440,
		// минимум группы −8360.
		Assert.That(result, Is.EqualTo(RealRiskResult.Finite(8360m)));
	}

	[TestMethod]
	[Description("Хеджированный короткий перп даёт конечный риск")]
	public void TryIfHedgedShortPerpYieldsFiniteRisk()
	{
		// Arrange: короткая линейная нога BTCUSDT −1 по средней 90 накрыта
		// длинным коллом со страйком 100 по средней 60 одной экспирации.
		// Требование: наклон платежа за последним узлом равен +1 + (−1) = 0,
		// риск конечен и совпадает с ручным расчётом.
		// Traceability: openspec:analytics/performance#scenario-real-risk-linear-hedge-is-finite
		var positions = new[]
		{
			OpenResidual("BTC-27DEC24-100-C", 1m, 60m),
			OpenResidual("BTCUSDT", -1m, 90m),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: узел нулевой цены платит −60 + 90 = +30, узел страйка 100
		// платит −60 − 10 = −70; минимум группы −70, риск конечен и равен 70.
		Assert.That(result, Is.EqualTo(RealRiskResult.Finite(70m)));
	}

	[TestMethod]
	[Description("Голый короткий перп в опционной группе неограничен")]
	public void TryIfNakedShortPerpInOptionGroupYieldsUnbounded()
	{
		// Arrange: короткая линейная нога BTCUSDT −1 по средней 90 без
		// накрывающих коллов — в опционной группе той же базы только длинный
		// пут со страйком 100.
		// Требование: наклон платежа за последним узлом равен 0 + (−1) = −1,
		// минимум платежа не ограничен — величина отсутствует.
		// Traceability: openspec:analytics/performance#scenario-real-risk-naked-short-linear-is-unbounded
		var positions = new[]
		{
			OpenResidual("BTC-27DEC24-100-P", 1m, 5m),
			OpenResidual("BTCUSDT", -1m, 90m),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: короткий перп не накрыт коллами — статус unbounded без числа.
		Assert.That(result.Status, Is.EqualTo(RealRiskStatus.Unbounded));
		Assert.That(result.Usdt, Is.Null);
	}

	[TestMethod]
	[Description("Голый короткий перп без опций той же базы неограничен")]
	public void TryIfNakedShortPerpWithoutOptionsYieldsUnbounded()
	{
		// Arrange: единственная нога конструкции — короткий перп BTCUSDT −1
		// по средней 90, опционных групп той же базы нет.
		// Требование: наклон прямой платежа отрицателен, минимум платежа
		// не ограничен по росту цены базового актива.
		// Traceability: openspec:analytics/performance#scenario-real-risk-naked-short-linear-is-unbounded
		var positions = new[] { OpenResidual("BTCUSDT", -1m, 90m) };

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: статус unbounded без числа, а не конечная величина.
		Assert.That(result.Status, Is.EqualTo(RealRiskStatus.Unbounded));
		Assert.That(result.Usdt, Is.Null);
	}

	[TestMethod]
	[Description("Линейная нога входит в группу ранней экспирации своей базы")]
	public void TryIfLinearLegJoinsEarliestExpiryGroup()
	{
		// Arrange: опционные группы двух экспираций одной базы — длинный колл
		// DEC24 со страйком 100 по средней 60 и длинный колл MAR25 со страйком
		// 200 по средней 50 — и короткий перп BTCUSDT −1 по средней 90,
		// накрытый коллом ранней экспирации.
		// Требование: перп учитывается только в группе самой ранней экспирации,
		// минимум второй группы не пересчитывается.
		// Traceability: openspec:analytics/performance#scenario-real-risk-linear-joins-earliest-expiry-group
		var positions = new[]
		{
			OpenResidual("BTC-27DEC24-100-C", 1m, 60m),
			OpenResidual("BTC-27MAR25-200-C", 1m, 50m),
			OpenResidual("BTCUSDT", -1m, 90m),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: ранняя группа платит минимум −70 (узел страйка 100: −60 от
		// колла и −10 от перпа), вторая группа — −50, риск равен 70 + 50 = 120.
		// Прикрепление перпа ко второй группе дало бы 220, отдельная группа
		// короткого перпа — неограниченность.
		Assert.That(result, Is.EqualTo(RealRiskResult.Finite(120m)));
	}

	[TestMethod]
	[Description("Линейная нога без опций той же базы образует собственную группу")]
	public void TryIfLinearOnlyConstructionYieldsResidualTimesAveragePrice()
	{
		// Arrange: единственная нога конструкции — длинный перп XAUTUSDT +0.04
		// по средней 4100, опционных групп той же базы нет.
		// Требование: перп образует собственную группу с узлом нулевой цены,
		// реальный риск равен произведению остатка на среднюю цену.
		// Traceability: openspec:analytics/performance#scenario-real-risk-linear-only-construction
		var positions = new[] { OpenResidual("XAUTUSDT", 0.04m, 4100m) };

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: платёж в узле нулевой цены равен −0.04 × 4100 = −164,
		// риск конечен и равен 164.
		Assert.That(result, Is.EqualTo(RealRiskResult.Finite(164m)));
	}

	[TestMethod]
	[Description("Неразобранный символ возвращает состояние недоступности")]
	public void TryIfUnparseableSymbolYieldsUnavailable()
	{
		// Arrange: открытый остаток с символом, который не разбирается ни как
		// символ опциона, ни как символ линейного фьючерса.
		// Требование: без страйка, экспирации или базового актива совместный
		// минимум не определён.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
		var positions = new[] { OpenResidual("BTCUSD", 1m, 300m) };

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: статус «не рассчитан» без числа, а не ноль.
		Assert.That(result.Status, Is.EqualTo(RealRiskStatus.Unavailable));
		Assert.That(result.Usdt, Is.Null);
	}

	[TestMethod]
	[Description("Нет средней цены открытого остатка — риск не рассчитан")]
	public void TryIfMissingOpenPriceYieldsUnavailable()
	{
		// Arrange: символ открытого остатка разбирается, но средняя цена остатка
		// отсутствует.
		// Требование: без средней цены платеж ноги не определён — числовой
		// результат не вычисляется.
		// Traceability: openspec:analytics/performance#scenario-real-risk-missing-open-price-unavailable
		var positions = new[] { OpenResidualWithoutOpenPrice("BTC-27DEC24-65000-C", 1m) };

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: статус «не рассчитан» без числа.
		Assert.That(result.Status, Is.EqualTo(RealRiskStatus.Unavailable));
		Assert.That(result.Usdt, Is.Null);
	}

	[TestMethod]
	[Description("Сбой марок не влияет на реальный риск")]
	public void TryIfMarksFailureDoesNotChangeRealRisk()
	{
		// Arrange: один и тот же дебетовый спред в двух наборах — с оценёнными
		// марками и без них (марочные поля null, как при сбое котировок).
		// Требование: результат выводится из структуры ног и средних цен остатков,
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
		var evaluated = _calculator.CalculateResult(withMarks);
		var failed = _calculator.CalculateResult(withoutMarks);

		// Assert: сбой марок не меняет ни величину, ни состояние результата.
		Assert.That(evaluated, Is.EqualTo(RealRiskResult.Finite(150m)));
		Assert.That(failed, Is.EqualTo(evaluated));
	}

	[TestMethod]
	[Description("Без открытых остатков реальный риск конечен и нулевой")]
	public void TryIfNoOpenResidualsYieldFiniteZero()
	{
		// Arrange: обе позиции конструкции закрыты — открытых остатков нет.
		// Требование: худшего исхода на экспирацию больше нет, риск конечен и нулевой.
		// Traceability: openspec:analytics/performance#scenario-real-risk-zero-without-open-residuals
		var positions = new[]
		{
			ClosedPosition("BTC-27DEC24-65000-C"),
			ClosedPosition("ETH-27MAR25-2000-P"),
		};

		// Act
		var result = _calculator.CalculateResult(positions);

		// Assert: риск конечен и нулевой, а не отсутствующий.
		Assert.That(result, Is.EqualTo(RealRiskResult.Finite(0m)));
	}

	[TestMethod]
	[Description("Null-набор позиций отклоняется типизированным расчётом")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullPositionsInTypedResult()
	{
		// Arrange — Act — Assert
		_calculator.CalculateResult(null!);
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

	/// <summary>Строит метрики открытого остатка без средней цены: она не вычислена или не оценена.</summary>
	private static PositionMetrics OpenResidualWithoutOpenPrice(string symbol, decimal residual) => new()
	{
		ConstructionId = ConstructionId,
		Symbol = symbol,
		Residual = residual,
		RealizedPnL = 0m,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = null,
		AverageEntryPrice = null,
		AverageClosePrice = null,
		TotalPnL = null,
		MarkPrice = null,
		UnrealizedPnL = null,
		MarkValue = null,
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
