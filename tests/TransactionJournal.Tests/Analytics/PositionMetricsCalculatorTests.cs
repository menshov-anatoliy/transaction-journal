using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки калькулятора метрик позиции: агрегаты выводятся из сделок и
/// закрывающих записей самой позиции, закрытая позиция не имеет нереализованной
/// части, средней цены и марки, а переоткрытие позиции сдвигает её даты под
/// текущий состав записей без следов прежнего расчёта. Негативные проверки
/// отклоняют пустой и незаданный поток записей.
/// </summary>
[TestClass]
public class PositionMetricsCalculatorTests
{
	private const string Symbol = "BTC-29DEC23-45000-C";

	private const long ConstructionId = 7;

	private PositionMetricsCalculator _calculator = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Калькулятор чистый — каждая проверка получает свежий экземпляр без состояния.
		_calculator = new PositionMetricsCalculator();
	}

	[TestMethod]
	[Description("Позиция показывает агрегаты своих записей")]
	public void TryIfPositionShowsAggregatesOfItsOwnEntries()
	{
		// Arrange: две покупки, встречная продажа и ручная пометка с комиссиями;
		// записи переданы не по порядку, чтобы хронологию выстраивал калькулятор.
		// Требование: остаток, реализованный PnL и комиссии выведены из сделок
		// и закрывающих записей этой позиции.
		// Traceability: openspec:analytics/performance#scenario-position-aggregates
		var entries = new[]
		{
			Trade(30, "exec-sell-1", -1.5m, 115m, 0.01m),
			Closing(40, "manual:1", PositionFifoEntryKind.ManualMark, -0.5m, 90m),
			Trade(0, "exec-buy-1", 1m, 100m, 0.02m),
			Trade(10, "exec-buy-2", 1m, 120m, 0.03m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, Symbol, entries);

		// Assert: продажа закрыла слой 1@100 (+15) и половину слоя 1@120 (−2.5),
		// пометка — остаток 0.5@120 (−15): ценовой поток −2.5 минус комиссии 0.06.
		Assert.That(metrics.ConstructionId, Is.EqualTo(ConstructionId));
		Assert.That(metrics.Symbol, Is.EqualTo(Symbol));
		Assert.That(metrics.Residual, Is.EqualTo(0m));
		Assert.That(metrics.RealizedPnL, Is.EqualTo(-2.56m));
		Assert.That(metrics.AccumulatedFees, Is.EqualTo(0.06m));
		Assert.That(metrics.OpenedAt, Is.EqualTo(At(0)));
	}

	[TestMethod]
	[Description("Закрытая позиция не имеет нереализованной части, средней цены и марки")]
	public void TryIfClosedPositionHasNoUnrealizedPart()
	{
		// Arrange: покупка и продажа закрывают позицию встречными сделками;
		// закрывающих марок расчёт не требует вовсе.
		// Требование: при нулевом остатке нереализованный PnL равен нулю и не
		// требует марок, средняя цена и марка закрытой позиции не вычисляются.
		// Traceability: openspec:analytics/performance#scenario-closed-position-has-no-unrealized
		var entries = new[]
		{
			Trade(0, "exec-buy-1", 1m, 100m, 0.02m),
			Trade(10, "exec-sell-1", -1m, 110m, 0.01m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, Symbol, entries);

		// Assert: реализованный PnL 10 − 0.03; нереализованная часть — ровно ноль,
		// средней цены и марки у закрытой позиции нет; дата закрытия — момент
		// обнуления остатка, длительность — между открытием и закрытием.
		Assert.That(metrics.Residual, Is.EqualTo(0m));
		Assert.That(metrics.RealizedPnL, Is.EqualTo(9.97m));
		Assert.That(metrics.UnrealizedPnL, Is.EqualTo(0m));
		Assert.That(metrics.AverageOpenPrice, Is.Null);
		Assert.That(metrics.MarkPrice, Is.Null);
		Assert.That(metrics.ClosedAt, Is.EqualTo(At(10)));
		Assert.That(metrics.Duration, Is.EqualTo(TimeSpan.FromMinutes(10)));
	}

	[TestMethod]
	[Description("Открытая позиция показывает среднюю цену остатка без даты закрытия")]
	public void TryIfOpenPositionShowsAveragePriceWithoutCloseDate()
	{
		// Arrange: две покупки и частичная продажа оставляют открытый остаток;
		// средняя цена выводится из непокрытых FIFO-слоёв.
		// Требование: у позиции с ненулевым остатком средняя цена — из непокрытых
		// слоёв; нереализованная оценка марками подключается слоем марок отдельно.
		// Traceability: openspec:analytics/performance#requirement-position-metrics
		// Traceability: openspec:analytics/performance#scenario-open-position-average-and-mark
		var entries = new[]
		{
			Trade(30, "exec-sell-1", -1.5m, 115m),
			Trade(0, "exec-buy-1", 1m, 100m),
			Trade(10, "exec-buy-2", 1m, 120m),
		};

		// Act
		var metrics = _calculator.Calculate(ConstructionId, Symbol, entries);

		// Assert: продажа закрыла 1@100 (+15) и 0.5@120 (−2.5); остаток 0.5 по 120.
		// Позиция открыта: даты закрытия и длительности нет, нереализованная оценка
		// ждёт слой марок и остаётся null.
		Assert.That(metrics.Residual, Is.EqualTo(0.5m));
		Assert.That(metrics.RealizedPnL, Is.EqualTo(12.5m));
		Assert.That(metrics.AverageOpenPrice, Is.EqualTo(120m));
		Assert.That(metrics.UnrealizedPnL, Is.Null);
		Assert.That(metrics.MarkPrice, Is.Null);
		Assert.That(metrics.OpenedAt, Is.EqualTo(At(0)));
		Assert.That(metrics.ClosedAt, Is.Null);
		Assert.That(metrics.Duration, Is.Null);
	}

	[TestMethod]
	[Description("Переоткрытие позиции сдвигает её даты под новый состав записей")]
	public void TryIfReopeningShiftsPositionDates()
	{
		// Arrange — Act: одна и та же позиция пересчитывается по мере изменения
		// состава записей — перенос сделок закрывает её и открывает заново.
		// Требование: дата открытия, дата закрытия и длительность отражают новый
		// состав записей при очередном чтении; следов прежнего расчёта нет.
		// Traceability: openspec:analytics/performance#scenario-reopen-updates-dates
		var closed = _calculator.Calculate(ConstructionId, Symbol, new[]
		{
			Trade(0, "exec-buy-1", 1m, 100m),
			Trade(10, "exec-sell-1", -1m, 110m),
		});

		// Assert: закрытая позиция — открытие в момент первой записи, закрытие
		// в момент обнуления, длительность между ними.
		Assert.That(closed.ClosedAt, Is.EqualTo(At(10)));
		Assert.That(closed.Duration, Is.EqualTo(TimeSpan.FromMinutes(10)));

		// Arrange — Act: переоткрытие — новая покупка после обнуления.
		var reopened = _calculator.Calculate(ConstructionId, Symbol, new[]
		{
			Trade(0, "exec-buy-1", 1m, 100m),
			Trade(10, "exec-sell-1", -1m, 110m),
			Trade(20, "exec-buy-2", 1m, 105m),
		});

		// Assert: позиция снова открыта — даты закрытия и длительности нет.
		Assert.That(reopened.Residual, Is.EqualTo(1m));
		Assert.That(reopened.ClosedAt, Is.Null);
		Assert.That(reopened.Duration, Is.Null);

		// Arrange — Act: повторное закрытие после переоткрытия.
		var closedAgain = _calculator.Calculate(ConstructionId, Symbol, new[]
		{
			Trade(0, "exec-buy-1", 1m, 100m),
			Trade(10, "exec-sell-1", -1m, 110m),
			Trade(20, "exec-buy-2", 1m, 105m),
			Trade(30, "exec-sell-2", -1m, 108m),
		});

		// Assert: датой закрытия стало последнее обнуление, длительность выросла
		// до него; дата открытия остаётся временем первой записи позиции.
		Assert.That(closedAgain.ClosedAt, Is.EqualTo(At(30)));
		Assert.That(closedAgain.Duration, Is.EqualTo(TimeSpan.FromMinutes(30)));
		Assert.That(closedAgain.OpenedAt, Is.EqualTo(At(0)));

		// Arrange — Act: перенос первых сделок в другую конструкцию — в позиции
		// остаются только более поздние записи.
		var moved = _calculator.Calculate(ConstructionId, Symbol, new[]
		{
			Trade(20, "exec-buy-2", 1m, 105m),
			Trade(30, "exec-sell-2", -1m, 108m),
		});

		// Assert: даты следуют за новым составом — открытие сдвинулось к первой
		// оставшейся записи, и прежних дат нигде не осталось.
		Assert.That(moved.OpenedAt, Is.EqualTo(At(20)));
		Assert.That(moved.ClosedAt, Is.EqualTo(At(30)));
		Assert.That(moved.Duration, Is.EqualTo(TimeSpan.FromMinutes(10)));
	}

	[TestMethod]
	[Description("Null-коллекция записей отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullEntries()
	{
		// Arrange — Act — Assert
		_calculator.Calculate(ConstructionId, Symbol, null!);
	}

	[TestMethod]
	[Description("Пустой поток записей отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnEmptyEntries()
	{
		// Arrange: позиция существует только своими записями — пустой поток
		// означал бы метрики позиции, которой нет.
		var entries = Array.Empty<PositionFifoEntry>();

		// Act — Assert
		_calculator.Calculate(ConstructionId, Symbol, entries);
	}

	#region Помощники

	/// <summary>Базовый момент потока: 1 января 2026 года 10:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

	private static DateTimeOffset At(int minutes) => BaseAt.AddMinutes(minutes);

	/// <summary>Строит запись сделки потока позиции.</summary>
	private static PositionFifoEntry Trade(int minutes, string execId, decimal quantity, decimal price, decimal fee = 0m) => new()
	{
		At = At(minutes),
		Kind = PositionFifoEntryKind.Trade,
		SourceKey = execId,
		Quantity = quantity,
		Price = price,
		Fee = fee,
	};

	/// <summary>Строит закрывающую запись потока позиции.</summary>
	private static PositionFifoEntry Closing(
		int minutes,
		string sourceKey,
		PositionFifoEntryKind kind,
		decimal quantity,
		decimal price,
		decimal fee = 0m) => new()
	{
		At = At(minutes),
		Kind = kind,
		SourceKey = sourceKey,
		Quantity = quantity,
		Price = price,
		Fee = fee,
	};

	#endregion
}
