using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки экрана деталей конструкции: сводка метрик с периодом и отметкой
/// марок, комментарий рядом со сводкой, четыре таблицы записей — позиции,
/// сделки, закрывающие записи и корректировки PnL; пустые таблицы показывают
/// явное сообщение об отсутствии, сбой марок — признак на месте нереализованных
/// величин, недоступный журнал и отсутствующая конструкция — явные состояния.
/// Traceability: openspec:ui/screens#requirement-construction-detail-screen
/// </summary>
[TestClass]
public class ConstructionDetailScreenTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IConstructionDetailReadModel> _detail = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();
		_detail = new Mock<IConstructionDetailReadModel>();
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()));
		_context.Services.AddSingleton(_detail.Object);
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Сводка показывает метрики, период с длительностью и отметку времени марок")]
	public void TryIfSummaryShowsMetricsPeriodAndMarks()
	{
		// Arrange: открытая конструкция с итогом 399 (+13.3% от капитала 3000),
		// реализованным −1, нереализованным +400 и отметкой марок 2026-09-20 12:00.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(realized: -1m, unrealized: 400m, adjustments: 0m, percent: 13.3m)) with
			{
				MarksAsOf = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
				HasOpenResidual = true,
			});

		// Act: пользователь открывает детали конструкции.
		var cut = RenderDetail();

		// Assert: сводка показывает итог, процент от капитала, разбивку по
		// реализованному и нереализованному результату и отметку времени марок.
		// Требование: сводка показывает метрики; нереализованные величины
		// сопровождаются отметкой времени марок.
		// Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period
		cut.WaitForAssertion(() =>
		{
			var summary = cut.Find(".kstrip").TextContent;
			Assert.That(summary, Does.Contain("итог"));
			Assert.That(summary, Does.Contain("+399 USDT"));
			Assert.That(summary, Does.Contain("+13.3%"));
			Assert.That(summary, Does.Contain("-1"));
			Assert.That(summary, Does.Contain("+400"));
			Assert.That(summary, Does.Contain("2026-09-20 12:00"));
		});
	}

	[TestMethod]
	[Description("Сводка показывает период с датами и длительностью")]
	public void TryIfSummaryShowsPeriodWithDatesAndDuration()
	{
		// Arrange: закрытая конструкция с датами открытия и закрытия и длительностью.
		var openedAt = new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero);
		var closedAt = new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero);
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(openedAt: openedAt, closedAt: closedAt, duration: TimeSpan.FromDays(87).Add(TimeSpan.FromHours(4)))) with
			{
				HasOpenResidual = false,
			});

		var cut = RenderDetail();

		// Assert: период несёт обе даты и длительность; открытых остатков нет —
		// марки оценке не нужны.
		// Требование: сводка показывает период с датами и длительностью.
		// Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period
		cut.WaitForAssertion(() =>
		{
			var summary = cut.Find(".kstrip").TextContent;
			Assert.That(summary, Does.Contain("2026-06-20 09:30 — 2026-09-15 18:00"));
			Assert.That(summary, Does.Contain("87 дн. 4 ч."));
			Assert.That(summary, Does.Contain("не нужны"));
		});
	}

	[TestMethod]
	[Description("Комментарий конструкции показывается рядом со сводкой")]
	public void TryIfConstructionCommentShownBesideSummary()
	{
		// Arrange: конструкция с комментарием.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Comment = "тестовая конструкция" });

		var cut = RenderDetail();

		// Assert: комментарий виден рядом со сводкой, до таблиц записей.
		// Требование: комментарий конструкции — рядом со сводкой.
		// Traceability: openspec:ui/screens#requirement-construction-detail-screen
		cut.WaitForAssertion(() => Assert.That(cut.Find(".detail-comment").TextContent, Does.Contain("тестовая конструкция")));
	}

	[TestMethod]
	[Description("Закрывающие записи перечисляются с типом, инструментом и суммой")]
	public void TryIfClosingEntriesListedWithKindSymbolAndAmount()
	{
		// Arrange: у конструкции delivery-запись и ручная пометка.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), closingEntries:
			[
				new ConstructionClosingEntryRow(
					new DateTimeOffset(2023, 12, 29, 8, 0, 0, TimeSpan.Zero),
					PositionClosingKind.Delivery,
					"BTC-29DEC23-45000-C",
					-0.0001m,
					1000m,
					0.1m),
				new ConstructionClosingEntryRow(
					new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero),
					PositionClosingKind.ManualMark,
					"BTCUSDT",
					-0.01m,
					42100m,
					421m),
			]));

		var cut = RenderDetail();

		// Assert: таблица закрывающих записей перечисляет обе записи с типом,
		// инструментом и суммой закрытия.
		// Требование: закрывающие записи показываются при их наличии.
		// Traceability: openspec:ui/screens#scenario-detail-closing-entries-shown
		cut.WaitForAssertion(() =>
		{
			// Третья таблица экрана — закрывающие записи: позиции, сделки, закрытия.
			var closing = cut.FindAll("table")[2];
			var rows = closing.QuerySelectorAll("tbody tr");
			Assert.That(rows, Has.Length.EqualTo(2));
			Assert.That(rows[0].TextContent, Does.Contain("delivery"));
			Assert.That(rows[0].TextContent, Does.Contain("BTC-29DEC23-45000-C"));
			Assert.That(rows[0].TextContent, Does.Contain("+0.1"));
			Assert.That(rows[1].TextContent, Does.Contain("ручная пометка"));
			Assert.That(rows[1].TextContent, Does.Contain("BTCUSDT"));
			Assert.That(rows[1].TextContent, Does.Contain("+421"));
		});
	}

	[TestMethod]
	[Description("Пустые таблицы показывают явные сообщения об отсутствии записей")]
	public void TryIfEmptyTablesShowExplicitMessages()
	{
		// Arrange: конструкция без записей — все четыре таблицы пусты.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(openedAt: null, closedAt: null, duration: null)) with { HasOpenResidual = false });

		var cut = RenderDetail();

		// Assert: каждая таблица показывает сообщение об отсутствии записей
		// своего вида, а не пустую разметку.
		// Требование: пустая таблица показывает явное сообщение.
		// Traceability: openspec:ui/screens#scenario-detail-empty-table-message
		cut.WaitForAssertion(() =>
		{
			var messages = cut.FindAll("td.empty").Select(cell => cell.TextContent).ToArray();
			Assert.That(messages, Is.EqualTo(new[]
			{
				"позиций нет",
				"сделок нет",
				"закрывающих записей нет",
				"корректировок нет",
			}));
		});
	}

	[TestMethod]
	[Description("Сбой марок показывается признаком в сводке и строке позиции, реализованные величины видны")]
	public void TryIfMarkFailureIndicatedInSummaryAndPositionRow()
	{
		// Arrange: открытый остаток без оценки — нереализованная часть и итог
		// не построены, отметка времени марок неизвестна.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(
				MetricsOf(realized: -1m, unrealized: null),
				hasOpenResidual: true,
				hasMarkFailure: true,
				positions:
				[
					new ConstructionPositionRow("BTCUSDT", 0.1m, 42000m, null, null, true, null),
				]));

		var cut = RenderDetail();

		// Assert: нереализованная часть сводки, итог, отметка марок и оценка
		// строки позиции заняты признаком сбоя марок; реализованный результат
		// и средняя цена остаются видимыми.
		// Требование: сбой марок — видимое состояние, реализованные величины
		// остаются видимыми.
		// Traceability: change:add-ui-screens/design#d4
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll(".kstrip .markfail").Count, Is.EqualTo(3));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("-1"));
			var row = cut.Find("tbody tr");
			Assert.That(row.QuerySelectorAll(".markfail").Length, Is.EqualTo(2));
			Assert.That(row.TextContent, Does.Contain("42000"));
		});
	}

	[TestMethod]
	[Description("Отсутствующая конструкция показывается явным сообщением")]
	public void TryIfUnknownConstructionShowsExplicitMessage()
	{
		// Arrange: read-модель сообщает, что конструкции нет.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new ConstructionNotFoundException(999));

		var cut = RenderDetail();

		// Assert: экран сообщает об отсутствии конструкции без таблиц.
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Конструкция не найдена"));
			Assert.That(cut.FindAll("table"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Недоступный журнал показывается явным состоянием, а не пустым экраном")]
	public void TryIfUnavailableJournalShowsExplicitState()
	{
		// Arrange: чтение журнала падает — сырьё повреждено или база недоступна.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("база недоступна"));

		var cut = RenderDetail();

		// Assert: экран показывает явное состояние недоступности без таблиц.
		// Отсутствие данных — видимое состояние, а не пустой экран.
		// Traceability: change:add-ui-screens/design#goals-non-goals
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Журнал недоступен"));
			Assert.That(cut.FindAll("table"), Has.Count.EqualTo(0));
		});
	}

	#region Помощники

	/// <summary>Рендерит экран деталей конструкции с идентификатором 7.</summary>
	private IRenderedComponent<ConstructionDetail> RenderDetail() =>
		_context.RenderComponent<ConstructionDetail>(parameters => parameters.Add(detail => detail.ConstructionId, 7L));

	/// <summary>Данные деталей с пустыми таблицами по умолчанию.</summary>
	private static ConstructionDetailData CreateData(
		ConstructionMetrics metrics,
		bool hasOpenResidual = false,
		bool hasMarkFailure = false,
		DateTimeOffset? marksAsOf = null,
		IReadOnlyList<ConstructionPositionRow>? positions = null,
		IReadOnlyList<ConstructionClosingEntryRow>? closingEntries = null) => new(
		7,
		"Календарь сентябрь",
		ConstructionStatus.Open,
		3000m,
		null,
		metrics,
		hasOpenResidual,
		hasMarkFailure,
		marksAsOf,
		positions ?? [],
		[],
		closingEntries ?? [],
		[]);

	/// <summary>Метрики конструкции с простыми значениями; сбойная нереализованная оценка оставляет итог null.</summary>
	private static ConstructionMetrics MetricsOf(
		decimal realized = 0m,
		decimal? unrealized = 0m,
		decimal adjustments = 0m,
		decimal? percent = null,
		DateTimeOffset? openedAt = null,
		DateTimeOffset? closedAt = null,
		TimeSpan? duration = null) => new()
	{
		ConstructionId = 7,
		AllocatedCapitalUsdt = 3000m,
		RealizedPnL = realized,
		UnrealizedPnL = unrealized,
		AdjustmentsPnL = adjustments,
		TotalPnL = unrealized is null ? null : realized + unrealized.Value + adjustments,
		RealizedPnLPercent = null,
		UnrealizedPnLPercent = null,
		AdjustmentsPnLPercent = null,
		TotalPnLPercent = percent,
		OpenedAt = openedAt,
		ClosedAt = closedAt,
		Duration = duration,
	};

	#endregion
}
