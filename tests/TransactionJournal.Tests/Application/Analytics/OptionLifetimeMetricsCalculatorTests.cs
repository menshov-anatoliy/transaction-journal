using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки вычислителя метрик времени жизни открытых опционных ног: колл
/// в деньгах раскладывается на внутреннюю и временную стоимость, пут вне
/// денег имеет нулевую внутреннюю, стоимости и распад масштабируются чистым
/// остатком со знаком, остаток суток — floor полных суток до deliveryTime
/// и вычисляется без рыночных данных. Прирост распада — фактическая разность
/// тет с ближайшим снимком истории суточной давности, молодая история даёт
/// прочерк. Саммари конструкции берёт минимум суток, нетто-распад, суммарную
/// временную и суммарную внутреннюю стоимость открытых опционных ног,
/// исключает ноги без тикеров из слагаемых и отсутствует без них.
/// Предупреждение об экспирации срабатывает при остатке не более четырнадцати
/// полных суток, по одной записи на инструмент, только по открытым ногам
/// и не зависит от проходов агента подсказок. Негативная проверка отклоняет
/// незаданные входные наборы.
/// </summary>
[TestClass]
public class OptionLifetimeMetricsCalculatorTests
{
	private const string CallSymbol = "BTC-25JUN27-106000-C";

	private const string PutSymbol = "BTC-25JUN27-106000-P";

	private const string SecondCallSymbol = "ETH-25JUN27-3000-C";

	/// <summary>Базовый момент проверок: 15 июня 2026 года 12:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

	private FixedTimeProvider _time = null!;

	private OptionLifetimeMetricsCalculator _calculator = null!;

	private Dictionary<string, InstrumentQuoteSnapshot> _marks = null!;

	private List<InstrumentMarketSnapshot> _history = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Время подменяется фальшивыми часами: остаток суток и давность снимков
		// проверяются без привязки к системному времени.
		_time = new FixedTimeProvider(BaseAt);
		_calculator = new OptionLifetimeMetricsCalculator(_time);
		_marks = new Dictionary<string, InstrumentQuoteSnapshot>(StringComparer.Ordinal);
		_history = new List<InstrumentMarketSnapshot>();
	}

	[TestMethod]
	[Description("Колл в деньгах раскладывается на внутреннюю и временную стоимость")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-itm-call-split
	public void TryIfLegItmCallSplitsIntrinsicAndTimeValue()
	{
		// Arrange: колл со страйком 100 при индексе 110 и котировке 15 — внутренняя
		// стоимость 10, временная 5; остаток единичный, значения совпадают.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m);

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 30, residual: 1m) }, _marks, _history);

		// Assert: внутренняя — превышение индекса над страйком, временная —
		// остаток котировки, на контракт и на чистый остаток значения равны.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.IntrinsicValueUsdtPerContract, Is.EqualTo(10m));
		Assert.That(leg.TimeValueUsdtPerContract, Is.EqualTo(5m));
		Assert.That(leg.IntrinsicValueUsdt, Is.EqualTo(10m));
		Assert.That(leg.TimeValueUsdt, Is.EqualTo(5m));
	}

	[TestMethod]
	[Description("Пут вне денег имеет нулевую внутреннюю стоимость")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-otm-put-zero-intrinsic
	public void TryIfLegOtmPutHasZeroIntrinsic()
	{
		// Arrange: пут со страйком 100 при индексе 110 вне денег — внутренняя
		// стоимость нулевая, вся котировка 3 временная.
		_marks[PutSymbol] = FreshQuote(PutSymbol, quotePrice: 3m, underlyingPrice: 110m);

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(PutSymbol, strike: 100m, OptionType.Put, daysToDelivery: 30, residual: 1m) }, _marks, _history);

		// Assert: зеркальная формула пута не даёт отрицательной внутренней
		// стоимости, временная равна полной котировке.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.IntrinsicValueUsdtPerContract, Is.EqualTo(0m));
		Assert.That(leg.TimeValueUsdtPerContract, Is.EqualTo(3m));
	}

	[TestMethod]
	[Description("Стоимость ноги масштабируется чистым остатком со знаком")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-value-scaled-by-residual
	public void TryIfLegValueScaledByResidual()
	{
		// Arrange: временная стоимость контракта 5 (котировка 15, внутренняя 10),
		// чистый остаток короткий −2.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m);

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 30, residual: -2m) }, _marks, _history);

		// Assert: временная стоимость позиции 5 × (−2) = −10, внутренняя
		// 10 × (−2) = −20 — знак остатка переносится на величины.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.TimeValueUsdtPerContract, Is.EqualTo(5m));
		Assert.That(leg.TimeValueUsdt, Is.EqualTo(-10m));
		Assert.That(leg.IntrinsicValueUsdt, Is.EqualTo(-20m));
	}

	[TestMethod]
	[Description("Дневной распад ноги равен тете контракта на чистый остаток")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-decay-theta-times-residual
	public void TryIfLegDecayEqualsThetaTimesResidual()
	{
		// Arrange: тета контракта −2.5 USDT/день, длинный остаток +2.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 30, residual: 2m) }, _marks, _history);

		// Assert: распад −2.5 × 2 = −5 USDT/день — длинная нога теряет премию.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.DailyDecayUsdt, Is.EqualTo(-5m));
	}

	[TestMethod]
	[Description("Короткий чистый остаток меняет знак дневного распада")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-decay-short-sign
	public void TryIfLegDecayShortResidualFlipsSign()
	{
		// Arrange: та же тета −2.5, чистый остаток короткий −1.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 30, residual: -1m) }, _marks, _history);

		// Assert: распад −2.5 × (−1) = +2.5 USDT/день — короткая нога распад
		// зарабатывает.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.DailyDecayUsdt, Is.EqualTo(2.5m));
	}

	[TestMethod]
	[DataRow(13.9, 13)]
	[DataRow(14.1, 14)]
	[Description("Остаток суток до экспирации считается полными сутками floor")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-days-floor
	public void TryIfLegDaysFlooredToFullDays(double daysToDelivery, int expectedDays)
	{
		// Arrange: deliveryTime отложен на дробное число суток от текущего
		// момента фальшивых часов.
		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: daysToDelivery, residual: 1m) }, _marks, _history);

		// Assert: остаток суток — floor до полных суток: 13.9 → 13, 14.1 → 14.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.DaysToExpiry, Is.EqualTo(expectedDays));
	}

	[TestMethod]
	[Description("Остаток суток вычисляется без рыночных данных, распад прочерком")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-days-no-market-data
	public void TryIfLegDaysAvailableWithoutMarketData()
	{
		// Arrange: тикеры ноги недоступны — словарь котировок символа не содержит,
		// deliveryTime инструмента известен.
		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 5, residual: 1m) }, _marks, _history);

		// Assert: сутки вычислены, все рыночные величины null — прочерк
		// без затрагивания остальных метрик.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.DaysToExpiry, Is.EqualTo(5));
		Assert.That(leg.IntrinsicValueUsdt, Is.Null);
		Assert.That(leg.TimeValueUsdt, Is.Null);
		Assert.That(leg.DailyDecayUsdt, Is.Null);
		Assert.That(leg.DecayAccelerationUsdt, Is.Null);
	}

	[TestMethod]
	[Description("Прирост распада считается со снимком суточной давности")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-acceleration-24h-snapshot
	public void TryIfLegAccelerationFrom24hOldSnapshot()
	{
		// Arrange: текущая тета −2.5 при остатке 2 даёт распад −5; в истории
		// два снимка старше суток — ближайший (25 часов) с тетой −2 и более
		// старый (48 часов) с тетой −3; базой прироста обязан стать ближайший.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);
		_history.Add(HistorySnapshot(CallSymbol, quotePrice: 12m, receivedAt: _time.UtcNow - TimeSpan.FromHours(48), theta: -3m));
		_history.Add(HistorySnapshot(CallSymbol, quotePrice: 13m, receivedAt: _time.UtcNow - TimeSpan.FromHours(25), theta: -2m));

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 30, residual: 2m) }, _marks, _history);

		// Assert: прирост (−2.5 − (−2)) × 2 = −1 USDT/день; распад на момент
		// снимка, приведённый к текущему остатку, равен −4.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.DailyDecayUsdt, Is.EqualTo(-5m));
		Assert.That(leg.DecayDayAgoUsdt, Is.EqualTo(-4m));
		Assert.That(leg.DecayAccelerationUsdt, Is.EqualTo(-1m));
	}

	[TestMethod]
	[Description("Молодая история снимков даёт прочерк прироста распада")]
	// Traceability: openspec:analytics/option-lifetime#scenario-leg-acceleration-no-24h-snapshot
	public void TryIfLegAccelerationDashWithout24hOldSnapshot()
	{
		// Arrange: единственный снимок истории моложе суток — 23 часа; текущая
		// тета и остаток те же, что в парном сценарии.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);
		_history.Add(HistorySnapshot(CallSymbol, quotePrice: 13m, receivedAt: _time.UtcNow - TimeSpan.FromHours(23), theta: -2m));

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 30, residual: 2m) }, _marks, _history);

		// Assert: прирост и «вчерашний» распад — прочерк, текущий распад −5
		// и остаток суток остаются доступны.
		var leg = evaluation.Legs.Single();
		Assert.That(leg.DecayAccelerationUsdt, Is.Null);
		Assert.That(leg.DecayDayAgoUsdt, Is.Null);
		Assert.That(leg.DailyDecayUsdt, Is.EqualTo(-5m));
		Assert.That(leg.DaysToExpiry, Is.EqualTo(30));
	}

	[TestMethod]
	[Description("Саммари берёт минимум суток по открытым опционным ногам")]
	// Traceability: openspec:analytics/option-lifetime#scenario-summary-nearest-expiry-min
	public void TryIfSummaryNearestExpiryIsMinOfLegs()
	{
		// Arrange: две открытые опционные ноги с остатками 10 и 25 суток.
		// Act
		var evaluation = _calculator.Calculate(new[]
		{
			Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 10, residual: 1m),
			Leg(SecondCallSymbol, strike: 3000m, OptionType.Call, daysToDelivery: 25, residual: 1m),
		}, _marks, _history);

		// Assert: ближайшая экспирация — минимум ног, 10 суток.
		Assert.That(evaluation.Summary, Is.Not.Null);
		Assert.That(evaluation.Summary!.DaysToNearestExpiry, Is.EqualTo(10));
	}

	[TestMethod]
	[Description("Нетто-распад саммари суммирует ноги со знаком")]
	// Traceability: openspec:analytics/option-lifetime#scenario-summary-net-decay-sum
	public void TryIfSummaryNetDecaySumsLegsWithSign()
	{
		// Arrange: распад первой ноги −5 (тета −2.5 × остаток 2), второй +3
		// (тета +1 × остаток 3); тикеры обеих ног доступны.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);
		_marks[SecondCallSymbol] = FreshQuote(SecondCallSymbol, quotePrice: 20m, underlyingPrice: 110m, theta: 1m);

		// Act
		var evaluation = _calculator.Calculate(new[]
		{
			Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 10, residual: 2m),
			Leg(SecondCallSymbol, strike: 3000m, OptionType.Call, daysToDelivery: 25, residual: 3m),
		}, _marks, _history);

		// Assert: нетто-дневной распад конструкции −5 + 3 = −2 USDT/день.
		Assert.That(evaluation.Summary, Is.Not.Null);
		Assert.That(evaluation.Summary!.NetDailyDecayUsdt, Is.EqualTo(-2m));
	}

	[TestMethod]
	[Description("Суммарная внутренняя стоимость саммари суммирует ноги")]
	// Внутренняя стоимость сводится по ногам рядом с временной: ноль вне денег —
	// полноценное слагаемое, сумма не теряет его.
	// Traceability: openspec:analytics/option-lifetime#scenario-summary-intrinsic-sum
	public void TryIfSummaryIntrinsicSumsLegs()
	{
		// Arrange: внутренняя стоимость колла 90,2 на контракт (индекс 190,2 при
		// страйке 100) × остаток 2 = 180,4; пут вне денег даёт нулевую внутреннюю.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 200m, underlyingPrice: 190.2m);
		_marks[PutSymbol] = FreshQuote(PutSymbol, quotePrice: 3m, underlyingPrice: 110m);

		// Act
		var evaluation = _calculator.Calculate(new[]
		{
			Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 10, residual: 2m),
			Leg(PutSymbol, strike: 100m, OptionType.Put, daysToDelivery: 25, residual: 3m),
		}, _marks, _history);

		// Assert: суммарная внутренняя стоимость конструкции 180,4 + 0 = 180,4 USDT.
		Assert.That(evaluation.Summary, Is.Not.Null);
		Assert.That(evaluation.Summary!.TotalIntrinsicValueUsdt, Is.EqualTo(180.4m));
	}

	[TestMethod]
	[Description("Нога без тикеров исключается из суммарной внутренней стоимости, остальные не подменяются")]
	// Деградация прочерком не превращает недоступную ногу в ноль: сумма
	// строится только по доступным слагаемым, остальные не трогает.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	public void TryIfSummaryIntrinsicSkipsLegWithoutTickers()
	{
		// Arrange: тикеры только у первой ноги с внутренней 180,4; вторая
		// без тикеров — прочерк, а не ноль в слагаемых.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 200m, underlyingPrice: 190.2m);

		// Act
		var evaluation = _calculator.Calculate(new[]
		{
			Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 10, residual: 2m),
			Leg(SecondCallSymbol, strike: 3000m, OptionType.Call, daysToDelivery: 25, residual: 3m),
		}, _marks, _history);

		// Assert: суммарная внутренняя 180,4 — только из доступной ноги.
		Assert.That(evaluation.Summary, Is.Not.Null);
		Assert.That(evaluation.Summary!.TotalIntrinsicValueUsdt, Is.EqualTo(180.4m));
	}

	[TestMethod]
	[Description("Все ноги без тикеров дают null суммарной внутренней стоимости")]
	// Полное отсутствие величины — null: прочерк, а не нулевая сумма.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	public void TryIfSummaryIntrinsicNullWhenAllLegsLackTickers()
	{
		// Arrange: обе открытые ноги без тикеров — словарь котировок пуст.
		// Act
		var evaluation = _calculator.Calculate(new[]
		{
			Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 10, residual: 2m),
			Leg(SecondCallSymbol, strike: 3000m, OptionType.Call, daysToDelivery: 25, residual: 3m),
		}, _marks, _history);

		// Assert: суммарная внутренняя стоимость — null, сутки остаются доступны.
		Assert.That(evaluation.Summary, Is.Not.Null);
		Assert.That(evaluation.Summary!.TotalIntrinsicValueUsdt, Is.Null);
		Assert.That(evaluation.Summary!.DaysToNearestExpiry, Is.EqualTo(10));
	}

	[TestMethod]
	[Description("Конструкция без открытых опционных ног саммари не имеет")]
	// Traceability: openspec:analytics/option-lifetime#scenario-summary-no-option-legs
	public void TryIfSummaryAbsentWithoutOpenOptionLegs()
	{
		// Arrange: открыт только фьючерсный остаток — в вычислитель он не
		// попадает, набор опционных ног пуст.
		// Act
		var evaluation = _calculator.Calculate(Array.Empty<OptionLegLifetimeInput>(), _marks, _history);

		// Assert: саммари отсутствует, метрик ног и предупреждений нет.
		Assert.That(evaluation.Summary, Is.Null);
		Assert.That(evaluation.Legs, Is.Empty);
		Assert.That(evaluation.ExpiryWarnings, Is.Empty);
	}

	[TestMethod]
	[DataRow(14, 1)]
	[DataRow(15, 0)]
	[Description("Порог предупреждения об экспирации срабатывает на четырнадцати сутках")]
	// Traceability: openspec:analytics/option-lifetime#scenario-expiry-warning-threshold-14
	public void TryIfExpiryWarningFiresAtFourteenDays(int daysToDelivery, int expectedWarnings)
	{
		// Arrange: открытая опционная нога с остатком суток на границе порога
		// и рыночные данные с распадом.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);

		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: daysToDelivery, residual: 1m) }, _marks, _history);

		// Assert: при 14 сутках предупреждение существует, при 15 — нет.
		Assert.That(evaluation.ExpiryWarnings, Has.Count.EqualTo(expectedWarnings));
	}

	[TestMethod]
	[Description("Предупреждение об экспирации одно на инструмент")]
	// Traceability: openspec:analytics/option-lifetime#scenario-expiry-warning-one-per-instrument
	public void TryIfExpiryWarningSinglePerInstrument()
	{
		// Arrange: один опционный инструмент с остатком 10 суток; повторная
		// нога того же символа не должна задваивать запись.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);

		// Act
		var evaluation = _calculator.Calculate(new[]
		{
			Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 10, residual: 1m),
			Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 10, residual: 2m),
		}, _marks, _history);

		// Assert: одна запись с инструментом, остатком суток и дневным распадом.
		var warning = evaluation.ExpiryWarnings.Single();
		Assert.That(warning.Symbol, Is.EqualTo(CallSymbol));
		Assert.That(warning.DaysToExpiry, Is.EqualTo(10));
		Assert.That(warning.DailyDecayUsdt, Is.EqualTo(-2.5m));
		Assert.That(warning.Text, Does.Contain(CallSymbol));
	}

	[TestMethod]
	[Description("Закрытая нога предупреждения об экспирации не порождает")]
	// Traceability: openspec:analytics/option-lifetime#scenario-expiry-warning-open-legs-only
	public void TryIfExpiryWarningSkipsClosedLeg()
	{
		// Arrange: опционная позиция закрыта (нулевой остаток) при 5 сутках
		// до экспирации — временной порог формально нарушен.
		// Act
		var evaluation = _calculator.Calculate(new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 5, residual: 0m) }, _marks, _history);

		// Assert: предупреждения нет, закрытая нога в метриках не участвует.
		Assert.That(evaluation.ExpiryWarnings, Is.Empty);
		Assert.That(evaluation.Legs, Is.Empty);
		Assert.That(evaluation.Summary, Is.Null);
	}

	[TestMethod]
	[Description("Предупреждение об экспирации вычисляется при чтении без проходов агента")]
	// Traceability: openspec:analytics/option-lifetime#scenario-expiry-warning-independent-of-agent
	public void TryIfExpiryWarningComputedOnEveryReadWithoutAgent()
	{
		// Arrange: открытая нога в зоне порога; агент подсказок не запускался,
		// вычислителю никаких его данных не передаётся.
		_marks[CallSymbol] = FreshQuote(CallSymbol, quotePrice: 15m, underlyingPrice: 110m, theta: -2.5m);
		var legs = new[] { Leg(CallSymbol, strike: 100m, OptionType.Call, daysToDelivery: 14, residual: 1m) };

		// Act: два независимых чтения подряд.
		var first = _calculator.Calculate(legs, _marks, _history);
		var second = _calculator.Calculate(legs, _marks, _history);

		// Assert: каждое чтение само вычисляет и публикует предупреждение.
		Assert.That(first.ExpiryWarnings, Has.Count.EqualTo(1));
		Assert.That(second.ExpiryWarnings, Has.Count.EqualTo(1));
	}

	[TestMethod]
	[DataRow("legs")]
	[DataRow("quotes")]
	[DataRow("history")]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Вычислитель отклоняет незаданные входные наборы")]
	public void ThrowOnNullCalculationInput(string argument)
	{
		// Arrange, Act, Assert: каждый из трёх входных наборов обязателен —
		// null в любом из них повреждает расчёт и отклоняется исключением.
		switch (argument)
		{
			case "legs":
				_calculator.Calculate(null!, _marks, _history);
				break;
			case "quotes":
				_calculator.Calculate(Array.Empty<OptionLegLifetimeInput>(), null!, _history);
				break;
			default:
				_calculator.Calculate(Array.Empty<OptionLegLifetimeInput>(), _marks, null!);
				break;
		}
	}

	#region Помощники

	/// <summary>Открытая опционная нога с deliveryTime через заданное число суток от фальшивых часов.</summary>
	private OptionLegLifetimeInput Leg(string symbol, decimal strike, OptionType type, double daysToDelivery, decimal residual) => new()
	{
		Symbol = symbol,
		Strike = strike,
		Type = type,
		DeliveryTime = _time.UtcNow + TimeSpan.FromDays(daysToDelivery),
		Residual = residual,
	};

	/// <summary>Свежий снимок тикера на текущий момент фальшивых часов.</summary>
	private static InstrumentQuoteSnapshot FreshQuote(string symbol, decimal quotePrice, decimal? underlyingPrice = null, decimal? theta = null) =>
		new(symbol, quotePrice, BaseAt, underlyingPrice, theta);

	/// <summary>Строка истории рыночных снимков с моментом получения и тетой.</summary>
	private static InstrumentMarketSnapshot HistorySnapshot(string symbol, decimal quotePrice, DateTimeOffset receivedAt, decimal? theta) => new()
	{
		Symbol = symbol,
		QuotePrice = quotePrice,
		UnderlyingPrice = 110m,
		Theta = theta,
		ReceivedAt = receivedAt,
	};

	#endregion
}
