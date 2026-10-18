using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки матрицы сценариев диагностики незакрытых рисков конструкции:
/// голый короткий фьючерс неограничен, голый длинный ограничен ценой входа,
/// фьючерсы, закрытые опционами один к одному, не предупреждаются, частичное
/// покрытие выражается числом незакрытых контрактов, короткие опционы сверх
/// покрытия фьючерсом предупреждаются своим остатком, короткий пут без
/// встречных ног ограничен нулём цены, короткий фьючерс закрывает короткий
/// пут снизу, covered call предупреждает только по фьючерсу, голый длинный
/// опцион не предупреждается, а null-набор позиций отклоняется.
/// </summary>
[TestClass]
public class ConstructionRiskDiagnosticsCalculatorTests
{
	/// <summary>Идентификатор конструкции тестовых позиций.</summary>
	private const long ConstructionId = 11;

	/// <summary>Базовый момент записей: 1 января 2026 года 10:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

	private ConstructionRiskDiagnosticsCalculator _calculator = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Калькулятор чистый — каждая проверка получает свежий экземпляр без состояния.
		_calculator = new ConstructionRiskDiagnosticsCalculator();
	}

	[TestMethod]
	[Description("Голый короткий фьючерс предупреждается неограниченным убытком")]
	public void TryIfNakedShortFutureWarnsUnbounded()
	{
		// Arrange: один открытый остаток — короткий вечный фьючерс без опционов.
		// Требование: на верхнем хвосте короткая фьючерсная позиция теряет без
		// предела и выдаёт предупреждение о незакрытой фьючерсной части.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-uncovered-short-perp-unbounded
		var positions = new[] { OpenResidual("BTCUSDT", -1m) };

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: одно предупреждение по фьючерсу с причиной «Риск не ограничен».
		Assert.That(warnings, Has.Count.EqualTo(1));
		var warning = warnings.Single();
		Assert.That(warning.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(warning.Reason, Is.EqualTo(ConstructionRiskReason.UnboundedLoss));
		Assert.That(warning.UncoveredContracts, Is.EqualTo(1m));
		Assert.That(warning.TotalContracts, Is.EqualTo(1m));
	}

	[TestMethod]
	[Description("Голый длинный фьючерс предупреждается убытком, ограниченным ценой входа")]
	public void TryIfNakedLongFutureWarnsBoundedByEntryPrice()
	{
		// Arrange: один открытый остаток — длинный вечный фьючерс без опционов.
		// Требование: на нижнем хвосте длинная фьючерсная позиция теряет в
		// пределах цены входа и выдаёт предупреждение о незакрытой части.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-uncovered-long-perp-bounded
		var positions = new[] { OpenResidual("BTCUSDT", 1m) };

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: одно предупреждение с причиной «убыток ограничен ценой входа».
		Assert.That(warnings, Has.Count.EqualTo(1));
		var warning = warnings.Single();
		Assert.That(warning.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(warning.Reason, Is.EqualTo(ConstructionRiskReason.BoundedByEntryPrice));
		Assert.That(warning.UncoveredContracts, Is.EqualTo(1m));
		Assert.That(warning.TotalContracts, Is.EqualTo(1m));
	}

	[TestMethod]
	[Description("Длинный фьючерс, закрытый длинным путом один к одному, не предупреждается")]
	public void TryIfLongFutureCoveredByLongPutProducesNoWarnings()
	{
		// Arrange: длинный фьючерс полностью закрыт длинным путом один к одному.
		// Требование: полностью закрытые фьючерсы не дают предупреждений.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-covered-legs-no-warning
		var positions = new[]
		{
			OpenResidual("BTCUSDT", 1m),
			OpenResidual("BTC-27DEC24-50000-P", 1m),
		};

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: набор предупреждений пуст на обоих хвостах.
		Assert.That(warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Короткий фьючерс, закрытый длинным коллом один к одному, не предупреждается")]
	public void TryIfShortFutureCoveredByLongCallProducesNoWarnings()
	{
		// Arrange: короткий фьючерс полностью закрыт длинным коллом один к одному.
		// Требование: полностью закрытые фьючерсы не дают предупреждений.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-covered-legs-no-warning
		var positions = new[]
		{
			OpenResidual("BTCUSDT", -1m),
			OpenResidual("BTC-27DEC24-70000-C", 1m),
		};

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: набор предупреждений пуст на обоих хвостах.
		Assert.That(warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Частичное покрытие сообщает один незакрытый контракт из двух")]
	public void TryIfPartialCoverageReportsOneUncoveredContractOfTwo()
	{
		// Arrange: два длинных фьючерса при одном покрывающем длинном путе.
		// Требование: частичное покрытие выражается числом незакрытых
		// контрактов — один незакрытый из двух.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-partial-coverage-counts-contracts
		var positions = new[]
		{
			OpenResidual("BTCUSDT", 2m),
			OpenResidual("BTC-27DEC24-50000-P", 1m),
		};

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: одно предупреждение по фьючерсу с числом «1 из 2».
		Assert.That(warnings, Has.Count.EqualTo(1));
		var warning = warnings.Single();
		Assert.That(warning.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(warning.Reason, Is.EqualTo(ConstructionRiskReason.BoundedByEntryPrice));
		Assert.That(warning.UncoveredContracts, Is.EqualTo(1m));
		Assert.That(warning.TotalContracts, Is.EqualTo(2m));
	}

	[TestMethod]
	[Description("Короткие коллы сверх покрытия фьючерсом дают два предупреждения")]
	public void TryIfShortCallsBeyondFutureProduceTwoWarnings()
	{
		// Arrange: один длинный фьючерс и три коротких колла того же актива.
		// Требование: фьючерс остаётся незакрытым снизу (ограничен ценой
		// входа), а два коротких колла сверх покрытия — неограниченным риском.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-short-options-beyond-futures
		var positions = new[]
		{
			OpenResidual("BTCUSDT", 1m),
			OpenResidual("BTC-27DEC24-70000-C", -3m),
		};

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: два предупреждения — по фьючерсу и по остатку коллов 2 из 3.
		Assert.That(warnings, Has.Count.EqualTo(2));
		var futureWarning = warnings.Single(warning => warning.Symbol == "BTCUSDT");
		Assert.That(futureWarning.Reason, Is.EqualTo(ConstructionRiskReason.BoundedByEntryPrice));
		Assert.That(futureWarning.UncoveredContracts, Is.EqualTo(1m));
		var callWarning = warnings.Single(warning => warning.Symbol == "BTC-27DEC24-70000-C");
		Assert.That(callWarning.Reason, Is.EqualTo(ConstructionRiskReason.UnboundedLoss));
		Assert.That(callWarning.UncoveredContracts, Is.EqualTo(2m));
		Assert.That(callWarning.TotalContracts, Is.EqualTo(3m));
	}

	[TestMethod]
	[Description("Короткий пут без встречных ног предупреждается нулём цены")]
	public void TryIfNakedShortPutWarnsBoundedByZeroPrice()
	{
		// Arrange: один открытый остаток — короткий пут без встречных ног.
		// Требование: короткий пут теряет на нижнем хвосте, его убыток
		// ограничен нулём цены, и он даёт предупреждение.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-uncovered-short-put
		var positions = new[] { OpenResidual("BTC-27DEC24-50000-P", -1m) };

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: одно предупреждение с причиной «убыток ограничен нулём цены».
		Assert.That(warnings, Has.Count.EqualTo(1));
		var warning = warnings.Single();
		Assert.That(warning.Symbol, Is.EqualTo("BTC-27DEC24-50000-P"));
		Assert.That(warning.Reason, Is.EqualTo(ConstructionRiskReason.BoundedByZeroPrice));
		Assert.That(warning.UncoveredContracts, Is.EqualTo(1m));
		Assert.That(warning.TotalContracts, Is.EqualTo(1m));
	}

	[TestMethod]
	[Description("Короткий фьючерс закрывает короткий пут снизу и остаётся незакрытым сверху")]
	public void TryIfShortFutureCoversShortPutBottomTail()
	{
		// Arrange: короткий фьючерс и короткий пут одного базового актива.
		// Требование: встречный наклон фьючерса закрывает пут на нижнем
		// хвосте, незакрытым остаётся только фьючерс сверху.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-short-futures-covers-short-put
		var positions = new[]
		{
			OpenResidual("BTCUSDT", -1m),
			OpenResidual("BTC-27DEC24-50000-P", -1m),
		};

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: предупреждение одно — неограниченный сверху фьючерс, по путу
		// предупреждения нет.
		Assert.That(warnings, Has.Count.EqualTo(1));
		var warning = warnings.Single();
		Assert.That(warning.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(warning.Reason, Is.EqualTo(ConstructionRiskReason.UnboundedLoss));
	}

	[TestMethod]
	[Description("Covered call предупреждает только по незакрытому снизу фьючерсу")]
	public void TryIfCoveredCallWarnsFutureOnly()
	{
		// Arrange: длинный фьючерс и короткий колл того же актива.
		// Требование: короткий колл закрыт встречным фьючерсом на верхнем
		// хвосте, предупреждение одно — незакрытый снизу фьючерс.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-covered-call-warns-futures-only
		var positions = new[]
		{
			OpenResidual("BTCUSDT", 1m),
			OpenResidual("BTC-27DEC24-70000-C", -1m),
		};

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: предупреждение одно — фьючерс с причиной «убыток ограничен
		// ценой входа», короткий колл не предупреждается.
		Assert.That(warnings, Has.Count.EqualTo(1));
		var warning = warnings.Single();
		Assert.That(warning.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(warning.Reason, Is.EqualTo(ConstructionRiskReason.BoundedByEntryPrice));
	}

	[TestMethod]
	[DataRow("BTC-27DEC24-60000-C")]
	[DataRow("BTC-27DEC24-60000-P")]
	[Description("Голый длинный опцион не предупреждается")]
	public void TryIfNakedLongOptionProducesNoWarnings(string symbol)
	{
		// Arrange: один открытый остаток — длинный колл либо длинный пут без
		// встречных ног.
		// Требование: убыток длинного опциона ограничен уплаченной премией,
		// поэтому предупреждение не выдаётся.
		// Traceability: openspec:analytics/risk-diagnostics#scenario-naked-long-option-no-warning
		var positions = new[] { OpenResidual(symbol, 1m) };

		// Act
		var warnings = _calculator.Calculate(positions);

		// Assert: набор предупреждений пуст на обоих хвостах.
		Assert.That(warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Null-набор позиций отклоняется вычислителем диагностики")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullPositions()
	{
		// Arrange — Act — Assert: null-вход отвергается аргументной проверкой.
		_calculator.Calculate(null!);
	}

	#region Помощники

	/// <summary>
	/// Строит метрики открытого остатка: калькулятор диагностики использует
	/// только символ и знаковый остаток, остальные метрики нейтральны.
	/// </summary>
	private static PositionMetrics OpenResidual(string symbol, decimal residual) => new()
	{
		ConstructionId = ConstructionId,
		Symbol = symbol,
		Residual = residual,
		RealizedPnL = 0m,
		AccumulatedFees = 0m,
		AverageOpenPrice = null,
		AverageEntryPrice = null,
		AverageClosePrice = null,
		TotalPnL = null,
		MarkPrice = null,
		UnrealizedPnL = null,
		OpenedAt = BaseAt,
		ClosedAt = null,
		Duration = null,
	};

	#endregion
}
