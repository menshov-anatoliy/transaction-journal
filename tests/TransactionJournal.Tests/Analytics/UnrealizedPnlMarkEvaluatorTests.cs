using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки оценщика нереализованного PnL: открытые остатки оцениваются свежей
/// маркой инструмента на момент запроса и несут отметку времени марок, закрытые
/// позиции марок не требуют вовсе, а сбой тикеров деградирует только в null
/// нереализованной части и отметки времени — реализованные метрики, комиссии,
/// даты и проценты возвращаются без изменений. Негативные проверки отклоняют
/// незаданные метрики и отсутствующий источник марок.
/// </summary>
[TestClass]
public class UnrealizedPnlMarkEvaluatorTests
{
	private const string LongSymbol = "BTC-29DEC23-45000-C";

	private const string ShortSymbol = "ETH-29DEC23-3000-C";

	private const string ClosedSymbol = "BTCUSDT";

	private const long FirstConstructionId = 7;

	private const long SecondConstructionId = 8;

	/// <summary>Базовый момент метрик: 1 января 2026 года 10:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

	/// <summary>Текущий момент проверок: 1 января 2026 года 12:00 UTC.</summary>
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private Mock<IFreshInstrumentMarkSource> _markSource = null!;

	private UnrealizedPnlMarkEvaluator _evaluator = null!;

	private ConstructionMetricsCalculator _constructionCalculator = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Источник марок подменяется моком: оценщик проверяется без сети и базы,
		// каждая проверка получает свежие экземпляры без состояния.
		_markSource = new Mock<IFreshInstrumentMarkSource>(MockBehavior.Strict);
		_evaluator = new UnrealizedPnlMarkEvaluator(_markSource.Object);
		_constructionCalculator = new ConstructionMetricsCalculator();
	}

	[TestMethod]
	[Description("Открытый остаток оценён маркой на момент запроса с отметкой времени марок")]
	public async Task TryIfOpenResidualIsValuedAtMarkOnRequest()
	{
		// Arrange: длинный остаток 0.5 со средней 120, короткий остаток 1 со средней
		// 100 и закрытая позиция; свежие марки приходят по своим инструментам —
		// BTC в 12:00, ETH в 12:01.
		// Требование: нереализованный PnL оценён по последней марке инструмента на
		// момент запроса, вместе с оценкой возвращается отметка времени марок.
		// Traceability: openspec:analytics/performance#scenario-open-residual-valued-at-request
		_markSource
			.Setup(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InstrumentMarkSnapshot(LongSymbol, 130m, At(120)));
		_markSource
			.Setup(source => source.GetFreshMarkAsync(ShortSymbol, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InstrumentMarkSnapshot(ShortSymbol, 90m, At(121)));
		var positions = new[]
		{
			OpenPosition(LongSymbol, FirstConstructionId, 0.5m, 120m, 12.5m),
			OpenPosition(ShortSymbol, FirstConstructionId, -1m, 100m, -2.5m),
			ClosedPosition(ClosedSymbol, FirstConstructionId, 9.97m),
		};

		// Act
		var evaluation = await _evaluator.EvaluateAsync(positions);

		// Assert: длинный остаток оценён маркой вверх (130 − 120) × 0.5 = 5,
		// короткий — вниз (90 − 100) × (−1) = 10; знак задаёт знаковый остаток.
		var longPosition = evaluation.Positions.Single(position => position.Symbol == LongSymbol);
		Assert.That(longPosition.MarkPrice, Is.EqualTo(130m));
		Assert.That(longPosition.UnrealizedPnL, Is.EqualTo(5m));
		var shortPosition = evaluation.Positions.Single(position => position.Symbol == ShortSymbol);
		Assert.That(shortPosition.MarkPrice, Is.EqualTo(90m));
		Assert.That(shortPosition.UnrealizedPnL, Is.EqualTo(10m));

		// Assert: закрытая позиция осталась без марки и нереализованной части,
		// её инструмент у тикеров не запрашивался.
		var closedPosition = evaluation.Positions.Single(position => position.Symbol == ClosedSymbol);
		Assert.That(closedPosition.MarkPrice, Is.Null);
		Assert.That(closedPosition.UnrealizedPnL, Is.EqualTo(0m));
		_markSource.Verify(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()), Times.Once);
		_markSource.Verify(source => source.GetFreshMarkAsync(ShortSymbol, It.IsAny<CancellationToken>()), Times.Once);
		_markSource.VerifyNoOtherCalls();

		// Assert: отметка времени марок — старейшая из полученных марок (12:00),
		// сбоя нет.
		Assert.That(evaluation.MarksAsOf, Is.EqualTo(At(120)));
		Assert.That(evaluation.HasMarkFailure, Is.False);

		// Assert: оценка втекает в итог конструкции — нереализованная часть
		// суммируется с реализованной и даёт свои проценты от капитала.
		var metrics = _constructionCalculator.Calculate(
			FirstConstructionId, 1000m, evaluation.Positions, Array.Empty<ConstructionPnLAdjustment>(), Now);
		Assert.That(metrics.RealizedPnL, Is.EqualTo(19.97m));
		Assert.That(metrics.UnrealizedPnL, Is.EqualTo(15m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(34.97m));
		Assert.That(metrics.UnrealizedPnLPercent, Is.EqualTo(1.5m));
	}

	[TestMethod]
	[Description("Сбой марок обнуляет только нереализованную часть и отметку времени")]
	public async Task TryIfMarkFailureNullsOnlyUnrealizedPart()
	{
		// Arrange: закрытая позиция с результатом 9.97, открытый остаток ETH и
		// корректировка +20; биржа отвечает ошибкой конверта на запрос марки ETH.
		// Требование: при недоступности тикеров нереализованный PnL и отметка
		// времени марок — null с признаком ошибки, реализованный PnL, комиссии,
		// даты и проценты возвращаются без изменений.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
		_markSource
			.Setup(source => source.GetFreshMarkAsync(ShortSymbol, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new BybitApiException(10001, "params error"));
		var positions = new[]
		{
			ClosedPosition(LongSymbol, FirstConstructionId, 9.97m),
			OpenPosition(ShortSymbol, FirstConstructionId, -1m, 100m, -2.5m),
		};
		var adjustments = new[] { Adjustment(60, 20m) };

		// Act
		var evaluation = await _evaluator.EvaluateAsync(positions);
		var metrics = _constructionCalculator.Calculate(
			FirstConstructionId, 1000m, evaluation.Positions, adjustments, Now);

		// Assert: открытый остаток остался без марки и оценки, отметка времени
		// марок — null с признаком сбоя.
		var openPosition = evaluation.Positions.Single(position => position.Symbol == ShortSymbol);
		Assert.That(openPosition.MarkPrice, Is.Null);
		Assert.That(openPosition.UnrealizedPnL, Is.Null);
		Assert.That(evaluation.MarksAsOf, Is.Null);
		Assert.That(evaluation.HasMarkFailure, Is.True);

		// Assert: реализованный PnL, корректировки, даты и их проценты не
		// изменились; нереализованная часть и итог деградировали в null.
		Assert.That(metrics.RealizedPnL, Is.EqualTo(7.47m));
		Assert.That(metrics.AdjustmentsPnL, Is.EqualTo(20m));
		Assert.That(metrics.RealizedPnLPercent, Is.EqualTo(0.747m));
		Assert.That(metrics.AdjustmentsPnLPercent, Is.EqualTo(2m));
		Assert.That(metrics.UnrealizedPnL, Is.Null);
		Assert.That(metrics.TotalPnL, Is.Null);
		Assert.That(metrics.UnrealizedPnLPercent, Is.Null);
		Assert.That(metrics.TotalPnLPercent, Is.Null);
		Assert.That(metrics.OpenedAt, Is.EqualTo(At(0)));
		Assert.That(metrics.ClosedAt, Is.Null);
		Assert.That(metrics.Duration, Is.EqualTo(TimeSpan.FromMinutes(120)));
	}

	[TestMethod]
	[Description("Недоступность сети до тикеров деградирует так же, как ошибка биржи")]
	public async Task TryIfNetworkFailureNullsUnrealizedPart()
	{
		// Arrange: открытый остаток; источник марок падает сетевой ошибкой —
		// сеть до публичных тикеров не дошла.
		// Требование: недоступность тикеров в момент запроса — деградация оценки,
		// а не сбой чтения метрик.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
		_markSource
			.Setup(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("Соединение не установлено"));
		var positions = new[] { OpenPosition(LongSymbol, FirstConstructionId, 0.5m, 120m, 12.5m) };

		// Act
		var evaluation = await _evaluator.EvaluateAsync(positions);

		// Assert: остаток без оценки, отметка времени — null с признаком сбоя.
		var openPosition = evaluation.Positions.Single();
		Assert.That(openPosition.UnrealizedPnL, Is.Null);
		Assert.That(openPosition.MarkPrice, Is.Null);
		Assert.That(evaluation.MarksAsOf, Is.Null);
		Assert.That(evaluation.HasMarkFailure, Is.True);
	}

	[TestMethod]
	[Description("Таймаут тикеров деградирует в null нереализованной части")]
	public async Task TryIfTimeoutNullsUnrealizedPart()
	{
		// Arrange: открытый остаток; запрос марки истёк по таймауту — тикеры
		// недоступны в момент запроса.
		// Требование: таймаут — та же деградация, отмена по токену под неё
		// не попадает.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
		_markSource
			.Setup(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new TaskCanceledException("Таймаут запроса", new TimeoutException()));
		var positions = new[] { OpenPosition(LongSymbol, FirstConstructionId, 0.5m, 120m, 12.5m) };

		// Act
		var evaluation = await _evaluator.EvaluateAsync(positions);

		// Assert: оценка деградировала без подъёма ошибки наружу.
		Assert.That(evaluation.Positions.Single().UnrealizedPnL, Is.Null);
		Assert.That(evaluation.MarksAsOf, Is.Null);
		Assert.That(evaluation.HasMarkFailure, Is.True);
	}

	[TestMethod]
	[Description("Марка без инструмента деградирует в null только нереализованной части")]
	public async Task TryIfMissingInstrumentMarkDegradesUnrealizedOnly()
	{
		// Arrange: открытый остаток; биржа не отдала марку инструмента либо
		// инструмент неизвестен справочнику — источнику оценки не из чего
		// построиться.
		// Требование: без марки нереализованная часть и отметка времени — null
		// с признаком ошибки, остальные метрики позиции не трогаются.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
		_markSource
			.Setup(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()))
			.ReturnsAsync((InstrumentMarkSnapshot?)null);
		var positions = new[] { OpenPosition(LongSymbol, FirstConstructionId, 0.5m, 120m, 12.5m) };

		// Act
		var evaluation = await _evaluator.EvaluateAsync(positions);

		// Assert: остаток не оценён, реализованные метрики позиции на месте.
		var openPosition = evaluation.Positions.Single();
		Assert.That(openPosition.RealizedPnL, Is.EqualTo(12.5m));
		Assert.That(openPosition.AverageOpenPrice, Is.EqualTo(120m));
		Assert.That(openPosition.MarkPrice, Is.Null);
		Assert.That(openPosition.UnrealizedPnL, Is.Null);
		Assert.That(evaluation.MarksAsOf, Is.Null);
		Assert.That(evaluation.HasMarkFailure, Is.True);
	}

	[TestMethod]
	[Description("Закрытые позиции не запрашивают марок: отметки времени нет без сбоя")]
	public async Task TryIfClosedPositionsRequireNoMarks()
	{
		// Arrange: только закрытые позиции — нереализованной части нет ни у одной,
		// тикеры не запрашиваются вовсе.
		// Требование: закрытая позиция не имеет нереализованной части и не требует
		// марок; отметка времени марок не нужна.
		// Traceability: openspec:analytics/performance#scenario-closed-position-has-no-unrealized
		var positions = new[]
		{
			ClosedPosition(LongSymbol, FirstConstructionId, 9.97m),
			ClosedPosition(ShortSymbol, FirstConstructionId, -2.5m),
		};

		// Act
		var evaluation = await _evaluator.EvaluateAsync(positions);

		// Assert: позиции не тронуты, сетевых обращений нет, сбоя нет.
		Assert.That(evaluation.Positions.All(position => position.UnrealizedPnL == 0m), Is.True);
		Assert.That(evaluation.MarksAsOf, Is.Null);
		Assert.That(evaluation.HasMarkFailure, Is.False);
		_markSource.VerifyNoOtherCalls();
	}

	[TestMethod]
	[Description("Марка инструмента запрашивается один раз на все позиции с ним")]
	public async Task TryIfSingleSymbolIsFetchedOnceForAllItsPositions()
	{
		// Arrange: два открытых остатка одного инструмента в разных конструкциях —
		// марка инструмента общая.
		// Требование: марка позиции и оценка нереализованной части берутся тем же
		// запросом к тикерам, без повторных обращений на тот же инструмент.
		// Traceability: change:add-analytics/design#d1
		_markSource
			.Setup(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new InstrumentMarkSnapshot(LongSymbol, 130m, At(120)));
		var positions = new[]
		{
			OpenPosition(LongSymbol, FirstConstructionId, 0.5m, 120m, 12.5m),
			OpenPosition(LongSymbol, SecondConstructionId, -0.2m, 125m, -1m),
		};

		// Act
		var evaluation = await _evaluator.EvaluateAsync(positions);

		// Assert: один запрос на инструмент, оба остатка оценены той же маркой:
		// первый (130 − 120) × 0.5 = 5, второй (130 − 125) × (−0.2) = −1.
		_markSource.Verify(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()), Times.Once);
		_markSource.VerifyNoOtherCalls();
		Assert.That(evaluation.Positions[0].UnrealizedPnL, Is.EqualTo(5m));
		Assert.That(evaluation.Positions[1].UnrealizedPnL, Is.EqualTo(-1m));
		Assert.That(evaluation.MarksAsOf, Is.EqualTo(At(120)));
		Assert.That(evaluation.HasMarkFailure, Is.False);
	}

	[TestMethod]
	[Description("Отмена оценки поднимается наружу, а не деградирует")]
	[ExpectedException(typeof(OperationCanceledException))]
	public async Task ThrowOnCancelledEvaluationPropagates()
	{
		// Arrange: открытый остаток; источник марок отменён по токену пользователя.
		// Отмена — не сбой тикеров: она поднимается вызывающему коду, а не
		// превращается в деградацию нереализованной части.
		_markSource
			.Setup(source => source.GetFreshMarkAsync(LongSymbol, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new OperationCanceledException());
		var positions = new[] { OpenPosition(LongSymbol, FirstConstructionId, 0.5m, 120m, 12.5m) };

		// Act — Assert
		await _evaluator.EvaluateAsync(positions);
	}

	[TestMethod]
	[Description("Null-набор метрик позиций отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnNullPositions()
	{
		// Arrange — Act — Assert
		await _evaluator.EvaluateAsync(null!);
	}

	[TestMethod]
	[Description("Оценщик без источника марок не создаётся")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnMissingMarkSource()
	{
		// Arrange — Act — Assert
		new UnrealizedPnlMarkEvaluator(null!);
	}

	#region Помощники

	private static DateTimeOffset At(int minutes) => BaseAt.AddMinutes(minutes);

	/// <summary>Строит метрики открытой позиции: знаковый остаток со средней ценой, нереализованная оценка ещё не подставлена.</summary>
	private static PositionMetrics OpenPosition(
		string symbol,
		long constructionId,
		decimal residual,
		decimal averageOpenPrice,
		decimal realizedPnL) => new()
	{
		ConstructionId = constructionId,
		Symbol = symbol,
		Residual = residual,
		RealizedPnL = realizedPnL,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = averageOpenPrice,
		MarkPrice = null,
		UnrealizedPnL = null,
		OpenedAt = At(10),
		ClosedAt = null,
		Duration = null,
	};

	/// <summary>Строит метрики закрытой позиции: нулевой остаток и нереализованный ноль без марки.</summary>
	private static PositionMetrics ClosedPosition(string symbol, long constructionId, decimal realizedPnL) => new()
	{
		ConstructionId = constructionId,
		Symbol = symbol,
		Residual = 0m,
		RealizedPnL = realizedPnL,
		AccumulatedFees = 0.03m,
		AverageOpenPrice = null,
		MarkPrice = null,
		UnrealizedPnL = 0m,
		OpenedAt = At(0),
		ClosedAt = At(30),
		Duration = TimeSpan.FromMinutes(30),
	};

	/// <summary>Строит внешнюю корректировку PnL конструкции.</summary>
	private static ConstructionPnLAdjustment Adjustment(int minutes, decimal amountUsdt) => new()
	{
		Date = At(minutes),
		AmountUsdt = amountUsdt,
	};

	#endregion
}
