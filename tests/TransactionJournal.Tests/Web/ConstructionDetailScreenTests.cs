using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Components;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Infrastructure.Ops;
using TransactionJournal.Application;
using TransactionJournal.Application.Ops;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Web;

/// <summary>
/// Проверки экрана деталей конструкции: сводка метрик с периодом и отметкой
/// марок, комментарий рядом со сводкой, четыре таблицы записей — позиции,
/// сделки, закрывающие записи и корректировки PnL; действия конструкции —
/// переименование, смена статуса с архивацией, изменение капитала и удаление
/// с подтверждением и причиной отказа; действия сделок — возврат во «Входящие»
/// и перенос в другую конструкцию с выбором цели; ручная пометка закрытия —
/// форма с дефолтом последней марки, правка и удаление из закрывающих записей
/// и предупреждение об избыточной записи; внешние корректировки PnL — форма
/// добавления и inline-правка/удаление строкой таблицы; пустые таблицы
/// показывают явное сообщение об отсутствии, сбой марок — признак на месте
/// нереализованных величин, недоступный журнал и отсутствующая конструкция —
/// явные состояния.
/// Traceability: openspec:ui/screens#requirement-construction-detail-screen
/// Traceability: openspec:ui/screens#requirement-construction-actions
/// Traceability: openspec:ui/screens#requirement-comments-inline-editing
/// Traceability: openspec:ui/screens#requirement-trade-actions-in-detail
/// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
/// Traceability: openspec:ui/screens#requirement-adjustments-in-detail
/// </summary>
[TestClass]
public class ConstructionDetailScreenTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IConstructionDetailReadModel> _detail = null!;
	private Mock<IConstructionService> _constructions = null!;
	private Mock<ICommentService> _comments = null!;
	private Mock<ITradeBindingService> _bindings = null!;
	private Mock<IManualCloseMarkService> _marks = null!;
	private Mock<IInstrumentMarkSource> _markSource = null!;
	private Mock<IPnLAdjustmentService> _adjustments = null!;
	private Mock<IJournalBackupService> _backups = null!;
	private Mock<IHintDisplayReadModel> _hints = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();
		_detail = new Mock<IConstructionDetailReadModel>();
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()));
		_context.Services.AddSingleton(_detail.Object);

		// Действия конструкции выполняются use-case сервисом домена: проверкам
		// экрана достаточно заглушки интерфейса с контролем вызовов.
		_constructions = new Mock<IConstructionService>();
		_context.Services.AddSingleton(_constructions.Object);

		// Комментарии сделки, позиции и конструкции сохраняются сервисом
		// комментариев домена — экран проверяется против заглушки интерфейса.
		_comments = new Mock<ICommentService>();
		_context.Services.AddSingleton(_comments.Object);

		// Перенос сделки в другую конструкцию и возврат во «Входящие» выполняются
		// use-case сервисом привязки домена — экран проверяется против заглушки.
		_bindings = new Mock<ITradeBindingService>();
		_context.Services.AddSingleton(_bindings.Object);

		// Ручная пометка закрытия ставится, правится и удаляется use-case сервисом
		// пометок домена, а её дефолт цены читается у источника последних марок —
		// экран проверяется против заглушек обоих контрактов.
		_marks = new Mock<IManualCloseMarkService>();
		_context.Services.AddSingleton(_marks.Object);
		_markSource = new Mock<IInstrumentMarkSource>();
		_context.Services.AddSingleton(_markSource.Object);

		// Внешние корректировки PnL добавляются, правятся и удаляются use-case
		// сервисом корректировок домена — экран проверяется против заглушки.
		_adjustments = new Mock<IPnLAdjustmentService>();
		_context.Services.AddSingleton(_adjustments.Object);

		// Опциональная резервная копия перед удалением создаётся сервисом копий:
		// по умолчанию копия удаётся — проверки отказа копии переопределяют настройку.
		_backups = new Mock<IJournalBackupService>();
		_backups
			.Setup(service => service.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new JournalBackupResult { FileName = "journal-delete-construction.db" });
		_context.Services.AddSingleton(_backups.Object);

		// Сигнал изменений журнала оповещает каркас после действий экрана;
		// без подписчиков в изолированном рендере он безопасно бездействует.
		_context.Services.AddScoped<JournalChangeSignal>();

		// Read-модель подсказок для панели деталей: по умолчанию панель
		// конструкции пуста — проверки подсказок переопределяют выдачу.
		var hints = new Mock<IHintDisplayReadModel>();
		hints
			.Setup(model => model.ReadConstructionPanelAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPanelData
			{
				Subject = HintSubject.ForConstruction(7),
				LiveGroups = [],
				History = [],
			});
		_context.Services.AddSingleton(hints.Object);
		_hints = hints;
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
		// реализованным −1, нереализованным +400 и отметкой марок 2026-09-20 12:00 UTC.
		var marksAsOf = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(realized: -1m, unrealized: 400m, adjustments: 0m, percent: 13.3m)) with
			{
				MarksAsOf = marksAsOf,
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
			Assert.That(summary, Does.Contain("Общий P&L"));
			Assert.That(summary, Does.Contain("Реализ. P&L"));
			Assert.That(summary, Does.Contain("Нереализ. P&L"));
			Assert.That(summary, Does.Contain("+399 USDT"));
			Assert.That(summary, Does.Contain("+13.3%"));
			Assert.That(summary, Does.Contain("-1"));
			Assert.That(summary, Does.Contain("+400"));
			// Даты хранятся в UTC и рендерятся локальным временем.
			// Traceability: openspec:ui/screens#scenario-sync-dates-shown-local
			Assert.That(summary, Does.Contain(DisplayTime.FormatMoment(marksAsOf)));
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
			Assert.That(summary, Does.Contain(
				$"{DisplayTime.FormatMoment(openedAt)} — {DisplayTime.FormatMoment(closedAt)}"));
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
					new ConstructionPositionRow(
						"BTCUSDT",
						0.1m,
						42000m,
						null,
						-1m,
						-0.1m,
						null,
						null,
						null,
						null,
						1m,
						new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
						null,
						true,
						null),
				]));

		var cut = RenderDetail();

		// Assert: признаки сбоя марок занимают «Стоимость», «Изм. цены, %»,
		// «Нереализ. P&L» и «Общий P&L» строки позиции, а «Стоимость»,
		// «Нереализ. P&L», итог и отметка марок — в сводке; реализованные
		// величины — реализованный P&L, средняя цена входа, комиссии и время
		// открытия — остаются видимыми.
		// Требование: сбой марок — видимое состояние, реализованные величины
		// остаются видимыми; сводочная стоимость деградирует тем же признаком.
		// Traceability: openspec:ui/screens#scenario-detail-position-total-pnl-marks-failure
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-mark-value
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll(".kstrip .markfail").Count, Is.EqualTo(4));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("-1"));
			var row = cut.Find("tbody tr");
			Assert.That(row.QuerySelectorAll(".markfail").Length, Is.EqualTo(4));
			var cells = row.QuerySelectorAll("td");
			Assert.That(cells[4].QuerySelectorAll(".markfail").Length, Is.EqualTo(1));
			Assert.That(cells[5].QuerySelectorAll(".markfail").Length, Is.EqualTo(1));
			Assert.That(cells[6].TextContent.Trim(), Is.EqualTo("-1 (-0.1%)"));
			Assert.That(cells[7].QuerySelectorAll(".markfail").Length, Is.EqualTo(1));
			Assert.That(cells[8].QuerySelectorAll(".markfail").Length, Is.EqualTo(1));
			Assert.That(row.TextContent, Does.Contain("42000"));
			Assert.That(row.TextContent, Does.Contain("1"));
			Assert.That(row.TextContent, Does.Contain(DisplayTime.FormatMoment(new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero))));
		});
	}

	[TestMethod]
	[Description("Таблица позиций показывает состав колонок, вход, выход и общий P&L с процентом")]
	public void TryIfPositionsTableShowsEntryCloseAndTotalPnl()
	{
		// Arrange: закрытая прибыльная и закрытая убыточная позиции
		// с ценами входа и выхода, комиссиями и временами открытия/закрытия.
		var openedAt = new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero);
		var closedAt = new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero);
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), positions:
			[
				new ConstructionPositionRow("BTCUSDT", 0m, 42000m, 44000m, 199m, 6.6m, 0m, 0m, 199m, 6.6m, 1.5m, openedAt, closedAt, false, null),
				new ConstructionPositionRow("ETHUSDT", 0m, 3000m, 2900m, -45m, -1.5m, 0m, 0m, -45m, -1.5m, 0.8m, openedAt, closedAt, false, null),
			]));

		var cut = RenderDetail();

		// Assert: состав колонок ровно выводимый — колонки «Средняя», «Марка»,
		// «Результат» и «% P&L от стоимости» отсутствуют; общий P&L — абсолют
		// со знаком и процент от капитала в скобках.
		// Требование: строка позиции показывает вход, выход и итог.
		// Traceability: openspec:ui/screens#scenario-detail-position-row-entry-close-total
		cut.WaitForAssertion(() =>
		{
			var headers = cut.FindAll("table")[0].QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()).ToArray();
			Assert.That(headers, Is.EqualTo(new[]
			{
				"Инструмент",
				"Остаток",
				"Сред. цена входа",
				"Сред. цена закрытия",
				"Стоимость",
				"Изм. цены, %",
				"Реализ. P&L",
				"Нереализ. P&L",
				"Общий P&L",
				"Всего комиссий",
				"Время открытия",
				"Время закрытия",
				"Статус",
				"Комментарий",
				"Действия",
			}));

			var rows = cut.FindAll("table")[0].QuerySelectorAll("tbody tr");
			Assert.That(rows, Has.Length.EqualTo(2));
			Assert.That(rows[0].TextContent, Does.Contain("+199 (+6.6%)"));
			Assert.That(rows[0].TextContent, Does.Contain("42000"));
			Assert.That(rows[0].TextContent, Does.Contain("44000"));
			Assert.That(rows[0].TextContent, Does.Contain("1.5"));
			Assert.That(rows[0].TextContent, Does.Contain(DisplayTime.FormatMoment(openedAt)));
			Assert.That(rows[0].TextContent, Does.Contain(DisplayTime.FormatMoment(closedAt)));
			Assert.That(rows[1].TextContent, Does.Contain("-45 (-1.5%)"));
			// Закрытая позиция не имеет ни стоимости, ни процента изменения
			// цены: обе колонки прочерком.
			// Traceability: openspec:ui/screens#scenario-detail-position-value-price-change-degradation
			var closedCells = rows[1].QuerySelectorAll("td");
			Assert.That(closedCells[4].TextContent.Trim(), Is.EqualTo("—"));
			Assert.That(closedCells[5].TextContent.Trim(), Is.EqualTo("—"));
		});
	}

	[TestMethod]
	[Description("Таблица позиций показывает раздельные части P&L, нереализованная закрытой — прочерком")]
	public void TryIfPositionsTableShowsSeparatePnlParts()
	{
		// Arrange: открытая позиция с доступными марками — реализованная часть 150
		// (5% капитала), нереализованная 49 (1.6% капитала), общий 199 (6.6%),
		// стоимость остатка 4249 и движение цены +1.2%; рядом закрытая позиция,
		// у которой нереализованной части нет.
		var openedAt = new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero);
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), hasOpenResidual: true, positions:
			[
				new ConstructionPositionRow("BTCUSDT", 0.1m, 42000m, null, 150m, 5m, 49m, 1.6m, 199m, 6.6m, 1.5m, openedAt, null, true, null, 4249m, 1.2m),
				new ConstructionPositionRow("ETHUSDT", 0m, 3000m, 2900m, -45m, -1.5m, 0m, 0m, -45m, -1.5m, 0.8m, openedAt, openedAt, false, null),
			]));

		var cut = RenderDetail();

		// Assert: реализованная и нереализованная части выводятся отдельными
		// колонками перед «Общий P&L» с процентом от выделенного капитала,
		// общий равен их сумме с процентом; у закрытой позиции реализованный
		// виден, нереализованная часть — прочерком.
		// Требование: строка позиции показывает раздельные части P&L.
		// Traceability: openspec:ui/screens#scenario-detail-position-pnl-parts
		cut.WaitForAssertion(() =>
		{
			var rows = cut.FindAll("table")[0].QuerySelectorAll("tbody tr");
			var openCells = rows[0].QuerySelectorAll("td");
			Assert.That(openCells[6].TextContent.Trim(), Is.EqualTo("+150 (+5%)"));
			Assert.That(openCells[7].TextContent.Trim(), Is.EqualTo("+49 (+1.6%)"));
			Assert.That(openCells[8].TextContent.Trim(), Is.EqualTo("+199 (+6.6%)"));
			var closedCells = rows[1].QuerySelectorAll("td");
			Assert.That(closedCells[6].TextContent.Trim(), Is.EqualTo("-45 (-1.5%)"));
			Assert.That(closedCells[7].TextContent.Trim(), Is.EqualTo("—"));
		});
	}

	[TestMethod]
	[Description("Колонки стоимости и изменения цены показывают знаковые величины открытых позиций")]
	public void TryIfPositionsTableShowsValueAndPriceChangeColumns()
	{
		// Arrange: открытая длинная позиция — средняя цена остатка 100 при марке
		// 110 (стоимость +11, изменение цены +10%); открытая короткая — средняя
		// цена 50 при марке 45 (стоимость -9, изменение цены тоже +10%):
		// плюс всегда движение «в прибыль».
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), hasOpenResidual: true, positions:
			[
				new ConstructionPositionRow("BTCUSDT", 0.1m, 100m, null, 0m, null, 1m, null, 1m, null, 0m, new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero), null, true, null, 11m, 10m),
				new ConstructionPositionRow("ETHUSDT", -0.2m, 50m, null, 0m, null, 1m, null, 1m, null, 0m, new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero), null, true, null, -9m, 10m),
			]));

		var cut = RenderDetail();

		// Assert: «Стоимость» выводит нетто-величину «марка × знаковый остаток»
		// со знаком, «Изм. цены, %» — движение марки от средней цены остатка,
		// приведённое к направлению позиции.
		// Требование: колонки стоимости и изменения цены открытой позиции.
		// Traceability: openspec:ui/screens#scenario-detail-position-value-and-price-change-columns
		cut.WaitForAssertion(() =>
		{
			var rows = cut.FindAll("table")[0].QuerySelectorAll("tbody tr");
			var longCells = rows[0].QuerySelectorAll("td");
			Assert.That(longCells[4].TextContent.Trim(), Is.EqualTo("+11"));
			Assert.That(longCells[5].TextContent.Trim(), Is.EqualTo("+10%"));
			var shortCells = rows[1].QuerySelectorAll("td");
			Assert.That(shortCells[4].TextContent.Trim(), Is.EqualTo("-9"));
			Assert.That(shortCells[5].TextContent.Trim(), Is.EqualTo("+10%"));
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

	[TestMethod]
	[Description("Переименование сохраняется сервисом домена и сразу видно в заголовке деталей")]
	public void TryIfRenameSavesNewNameAndShowsItImmediately()
	{
		// Arrange: конструкция «Календарь сентябрь»; после переименования read-модель
		// возвращает снимок с новым именем.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Name = "Плечо на сентябрь" });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find("h1").TextContent, Does.Contain("Календарь сентябрь")));

		// Act: пользователь открывает форму переименования и сохраняет новое имя.
		FindButton(cut, "Переименовать").Click();
		cut.Find(".action-input").Change("Плечо на сентябрь");
		FindButton(cut, "Сохранить имя").Click();

		// Assert: имя сохранено сервисом домена и немедленно видно в заголовке.
		// Требование: свободное переименование доступно из деталей.
		// Traceability: openspec:ui/screens#requirement-construction-actions
		_constructions.Verify(service =>
			service.RenameAsync(7, "Плечо на сентябрь", It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(cut.Find("h1").TextContent, Does.Contain("Плечо на сентябрь")));
	}

	[TestMethod]
	[Description("Смена статуса обновляет статусный бейдж деталей немедленно")]
	public void TryIfStatusChangeUpdatesBadgeImmediately()
	{
		// Arrange: открытая конструкция; после закрытия read-модель возвращает
		// снимок со статусом «закрыта».
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Status = ConstructionStatus.Closed });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find("h1 .status").ClassList, Does.Contain("status-open")));

		// Act: пользователь закрывает конструкцию командой статуса.
		FindButton(cut, "Закрыть").Click();

		// Assert: статус сменён сервисом домена; бейдж деталей отражает «закрыта»,
		// команда статуса меняется на обратную.
		// Требование: смена статуса меняет индикацию статуса.
		// Traceability: openspec:ui/screens#scenario-detail-status-change-indicated
		_constructions.Verify(service =>
			service.ChangeStatusAsync(7, ConstructionStatus.Closed, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			var badge = cut.Find("h1 .status");
			Assert.That(badge.TextContent, Is.EqualTo("закрыта"));
			Assert.That(badge.ClassList, Does.Contain("status-closed"));
		});
		cut.WaitForAssertion(() => Assert.That(FindButton(cut, "Открыть"), Is.Not.Null));
	}

	[TestMethod]
	[Description("Архивация и возврат из архива меняют индикацию и команды действий")]
	public void TryIfArchiveAndReturnChangeIndicationAndCommands()
	{
		// Arrange: конструкция проходит путь «открыта» → «архив» → «закрыта».
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Status = ConstructionStatus.Archived })
			.ReturnsAsync(CreateData(MetricsOf()) with { Status = ConstructionStatus.Closed });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find("h1 .status").TextContent, Is.EqualTo("открыта")));

		// Act: пользователь переводит конструкцию в архив.
		FindButton(cut, "В архив").Click();

		// Assert: индикация — «архив», команда меняется на возврат из архива.
		// Требование: перевод в архив и возврат из архива доступны из деталей.
		// Traceability: openspec:ui/screens#requirement-construction-actions
		_constructions.Verify(service =>
			service.ChangeStatusAsync(7, ConstructionStatus.Archived, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find("h1 .status").TextContent, Is.EqualTo("архив"));
			Assert.That(cut.Find("h1 .status").ClassList, Does.Contain("status-archived"));
		});
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll(".detail-actions button").Any(button => button.TextContent.Trim() == "Вернуть из архива"), Is.True);
			Assert.That(cut.FindAll(".detail-actions button").Any(button => button.TextContent.Trim() == "В архив"), Is.False);
		});

		// Act: пользователь возвращает конструкцию из архива.
		FindButton(cut, "Вернуть из архива").Click();

		// Assert: возврат восстанавливает ручной статус «закрыта».
		_constructions.Verify(service =>
			service.ChangeStatusAsync(7, ConstructionStatus.Closed, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(cut.Find("h1 .status").TextContent, Is.EqualTo("закрыта")));
	}

	[TestMethod]
	[Description("Изменение капитала обновляет только процентные величины сводки")]
	public void TryIfCapitalChangeUpdatesOnlyPercentages()
	{
		// Arrange: конструкция с итогом +399 (13.3% от капитала 3000); после
		// изменения капитала read-модель возвращает те же абсолютные величины
		// с процентом от нового капитала 6000.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(realized: -1m, unrealized: 400m, adjustments: 0m, percent: 13.3m)))
			.ReturnsAsync(CreateData(MetricsOf(realized: -1m, unrealized: 400m, adjustments: 0m, percent: 6.5m)) with
			{
				AllocatedCapitalUsdt = 6000m,
			});

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("+13.3%")));

		// Act: пользователь меняет выделенный капитал с 3000 на 6000.
		FindButton(cut, "Изменить капитал").Click();
		var input = cut.Find(".action-input");
		Assert.That(input.GetAttribute("value"), Is.EqualTo("3000"));
		input.Change("6000");
		FindButton(cut, "Сохранить капитал").Click();

		// Assert: капитал сохранён; абсолютные величины не изменились, процент
		// от капитала пересчитан немедленно.
		// Требование: изменение капитала обновляет только проценты.
		// Traceability: openspec:ui/screens#scenario-detail-capital-change-percent-only
		_constructions.Verify(service =>
			service.UpdateAllocatedCapitalAsync(7, 6000m, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			var summary = cut.Find(".kstrip").TextContent;
			Assert.That(summary, Does.Contain("+6.5%"));
			Assert.That(summary, Does.Contain("(6000)"));
			Assert.That(summary, Does.Contain("+399 USDT"));
			Assert.That(summary, Does.Contain("-1"));
			Assert.That(summary, Does.Contain("+400"));
		});
	}

	[TestMethod]
	[Description("Нечисловое значение капитала не уходит в домен, форма просит число")]
	public void TryIfInvalidCapitalRejectedWithoutAction()
	{
		// Arrange: форма капитала открыта.
		var cut = RenderDetail();
		FindButton(cut, "Изменить капитал").Click();

		// Act: пользователь вводит не число и сохраняет.
		cut.Find(".action-input").Change("не число");
		FindButton(cut, "Сохранить капитал").Click();

		// Assert: команда в домен не ушла, форма показывает сообщение о числе.
		_constructions.Verify(service =>
			service.UpdateAllocatedCapitalAsync(It.IsAny<long>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
		Assert.That(cut.Markup, Does.Contain("Введите число в USDT"));
	}

	[TestMethod]
	[Description("Пустое сохранение формы капитала убирает капитал и скрывает проценты")]
	public void TryIfCapitalClearedByEmptySaveHidesPercent()
	{
		// Arrange: конструкция с капиталом 3000; после очистки капитал не задан,
		// процентные величины отсутсвуют.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(percent: 13.3m)))
			.ReturnsAsync(CreateData(MetricsOf(percent: null)) with
			{
				AllocatedCapitalUsdt = null,
			});

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("(3000)")));

		// Act: пользователь сохраняет пустое поле капитала.
		FindButton(cut, "Изменить капитал").Click();
		cut.Find(".action-input").Change("");
		FindButton(cut, "Сохранить капитал").Click();

		// Assert: капитал убран командой с null; сводка показывает «% капитала»
		// без скобочной величины, строки позиций без процентного «(—)».
		// Требование: пустое сохранение формы капитала убирает капитал.
		// Traceability: openspec:ui/screens#scenario-detail-capital-removal-hides-percent
		_constructions.Verify(service =>
			service.UpdateAllocatedCapitalAsync(7, null, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("% капитала"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Not.Contain("(3000)"));
		});
		Assert.That(cut.Markup, Does.Not.Contain("(—)"));
	}

	[TestMethod]
	[Description("Незаданный капитал оставляет сводку и строки позиций без процентных скобок")]
	public void TryIfNoCapitalSummaryAndRowsOmitPercentParens()
	{
		// Arrange: конструкция без капитала, у позиции итог без процента.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(realized: 10m, unrealized: 5m)) with
			{
				AllocatedCapitalUsdt = null,
				Positions =
				[
					new ConstructionPositionRow(
						"BTCUSDT",
						0.05m,
						63000m,
						null,
						10m,
						null,
						5m,
						null,
						15m,
						null,
						0.1m,
						new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
						null,
						true,
						null),
				],
			});

		var cut = RenderDetail();

		// Assert: сводка показывает «% капитала» без скобки с величиной, строка
		// позиции выводит итог без «(—)» — проценты скрыты целиком.
		// Требование: без капитала процентные величины не показываются.
		// Traceability: openspec:ui/screens#scenario-detail-no-capital-no-percent
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("% капитала"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Not.Contain("("));
			Assert.That(cut.Markup, Does.Contain("+15"));
			Assert.That(cut.Markup, Does.Not.Contain("(—)"));
		});
	}

	[TestMethod]
	[Description("Риск вводится в процентах: вторая единица вычисляется только для чтения, домену уходит процент")]
	public void TryIfRiskSavedInPercentShowsUsdtEcho()
	{
		// Arrange: конструкция с капиталом 3000, риск не задан.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".kstrip"), Is.Not.Null));

		// Act: пользователь вводит риск 5% — при заданном капитале USDT-поле
		// получает вычисленное значение и блокируется; сохранение уходит в домен.
		FindButton(cut, "Риск…").Click();
		cut.FindAll(".action-form .target-input")[0].Input("5");

		// Assert: вычисленное эхо «150» только для чтения в поле USDT.
		// Требование: вторая единица вычисляется для подсказки и не редактируется.
		// Traceability: openspec:ui/screens#scenario-detail-risk-profit-edit
		var inputs = cut.FindAll(".action-form .target-input");
		Assert.That(inputs[1].GetAttribute("value"), Is.EqualTo("150"));
		Assert.That(inputs[1].HasAttribute("readonly"), Is.True);

		FindButton(cut, "Сохранить").Click();

		// Assert: домен получает значение ровно введённой единицы — процент.
		// Traceability: openspec:domain/constructions#requirement-risk-profit-params
		_constructions.Verify(service =>
			service.UpdateRiskAsync(7, 5m, TargetUnit.Percent, It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestMethod]
	[Description("Профит вводится в USDT: вторая единица вычисляется только для чтения, домену уходят USDT")]
	public void TryIfProfitSavedInUsdtShowsPercentEcho()
	{
		// Arrange: конструкция с капиталом 3000, профит не задан.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".kstrip"), Is.Not.Null));

		// Act: пользователь вводит профит 150 USDT — процентное поле получает
		// вычисленные «5» и блокируется; сохранение уходит в домен.
		FindButton(cut, "Профит…").Click();
		cut.FindAll(".action-form .target-input")[1].Input("150");
		var inputs = cut.FindAll(".action-form .target-input");
		Assert.That(inputs[0].GetAttribute("value"), Is.EqualTo("5"));
		Assert.That(inputs[0].HasAttribute("readonly"), Is.True);

		FindButton(cut, "Сохранить").Click();

		// Assert: домен получает значение ровно введённой единицы — USDT.
		// Требование: сохраняется пара «значение + введённая единица».
		// Traceability: openspec:ui/screens#scenario-detail-risk-profit-edit
		_constructions.Verify(service =>
			service.UpdateProfitAsync(7, 150m, TargetUnit.Usdt, It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestMethod]
	[Description("Заполненные обе единицы риска не уходят в домен: форма требует значение только одной")]
	public void TryIfRiskWithBothUnitsFilledRejectedWithoutCall()
	{
		// Arrange: конструкция без капитала — оба поля остаются доступными вводу.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with
			{
				AllocatedCapitalUsdt = null,
			});

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".kstrip"), Is.Not.Null));

		// Act: пользователь заполняет обе единицы и сохраняет.
		FindButton(cut, "Риск…").Click();
		var inputs = cut.FindAll(".action-form .target-input");
		inputs[0].Input("5");
		cut.FindAll(".action-form .target-input")[1].Input("150");
		FindButton(cut, "Сохранить").Click();

		// Assert: форма показывает ошибку одной единицы, команда в домен не уходит
		// и снимок не перечитывается.
		// Требование: значение вводится ровно в одной единице.
		// Traceability: openspec:ui/screens#scenario-detail-risk-profit-edit
		Assert.That(cut.Markup, Does.Contain("Введите значение только в одной единице"));
		_constructions.Verify(service =>
			service.UpdateRiskAsync(It.IsAny<long>(), It.IsAny<decimal>(), It.IsAny<TargetUnit>(), It.IsAny<CancellationToken>()), Times.Never);
		_detail.Verify(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestMethod]
	[Description("Кнопка «Убрать» очищает риск целиком командой с пустыми величинами")]
	public void TryIfRiskRemoveClearsParameter()
	{
		// Arrange: риск задан процентами — «Убрать» предложена в форме.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with
			{
				RiskPercent = 5m,
				RiskUsdt = 150m,
				RiskUnit = TargetUnit.Percent,
			});

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".kstrip"), Is.Not.Null));

		// Act: пользователь убирает риск.
		FindButton(cut, "Риск…").Click();
		FindButton(cut, "Убрать").Click();

		// Assert: домен получил очистку параметра null-величиной и null-единицей.
		// Требование: «Убрать» в форме очищает параметр целиком.
		// Traceability: openspec:ui/screens#scenario-detail-risk-profit-edit
		// Traceability: openspec:domain/constructions#scenario-risk-profit-clear-removes-param
		_constructions.Verify(service =>
			service.UpdateRiskAsync(7, null, null, It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestMethod]
	[Description("Сводка показывает шкалу «риск — итог — профит» с подписями границ и заполнением внутри границ")]
	public void TryIfSummaryShowsHintScaleWithBounds()
	{
		// Arrange: границы риск 5%/150 и профит 10%/300, итог 100 — внутри профита.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(realized: -50m, unrealized: 150m)) with
			{
				RiskPercent = 5m,
				RiskUsdt = 150m,
				ProfitPercent = 10m,
				ProfitUsdt = 300m,
			});

		var cut = RenderDetail();

		// Assert: подсказка показывает обе границы обеими единицами; заполнение
		// идёт от нулевой отметки в сторону профита, риска не касается, пробоя нет.
		// Требование: шкала нормирована в USDT, заполнение по текущему итогу.
		// Traceability: openspec:ui/screens#scenario-hint-inside-bounds
		cut.WaitForAssertion(() =>
		{
			var hint = cut.Find(".rp-detail .rp-hint");
			Assert.That(hint.TextContent, Does.Contain("риск 5% / 150"));
			Assert.That(hint.TextContent, Does.Contain("профит 10% / 300"));
			Assert.That(cut.FindAll(".rp-detail .rp-fill-profit"), Has.Count.EqualTo(1));
			Assert.That(cut.FindAll(".rp-detail .rp-fill-risk"), Has.Count.EqualTo(0));
			Assert.That(cut.FindAll(".rp-detail .rp-breakout"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Итог за границей запоминается признаком пробоя, пропорции шкалы не меняются")]
	public void TryIfHintMarksBoundaryBreakout()
	{
		// Arrange: итог 400 за границей профита 300.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(realized: 100m, unrealized: 300m)) with
			{
				RiskUsdt = 150m,
				ProfitUsdt = 300m,
			});

		var cut = RenderDetail();

		// Assert: сторона профита заполнена до конца с признаком пробоя.
		// Требование: итог за границей виден признаком пробоя границы.
		// Traceability: openspec:ui/screens#scenario-hint-boundary-breakout
		cut.WaitForAssertion(() =>
			Assert.That(cut.FindAll(".rp-detail .rp-fill-profit.rp-breakout"), Has.Count.EqualTo(1)));
	}

	[TestMethod]
	[Description("Односторонняя шкала показывает только доступную границу, вторая сторона открыта")]
	public void TryIfHintShowsSingleOpenSide()
	{
		// Arrange: задан только риск в USDT — процента нет без капитала.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with
			{
				RiskUsdt = 150m,
			});

		var cut = RenderDetail();

		// Assert: подпись риска без процентов, стороны профита с границей нет —
		// полоса содержит открытую зону и нулевую отметку, заполнения нет.
		// Требование: граница без USDT-величины свою сторону не показывает.
		// Traceability: openspec:ui/screens#scenario-hint-single-bound
		cut.WaitForAssertion(() =>
		{
			var hint = cut.Find(".rp-detail .rp-hint");
			Assert.That(hint.TextContent, Does.Contain("риск 150"));
			Assert.That(hint.TextContent, Does.Not.Contain("профит"));
			Assert.That(cut.FindAll(".rp-detail .rp-zone-open"), Has.Count.EqualTo(1));
			Assert.That(cut.FindAll(".rp-detail .rp-zero"), Has.Count.EqualTo(1));
			Assert.That(cut.FindAll(".rp-detail .rp-fill"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Без обеих USDT-величин подсказка целиком отсутствует")]
	public void TryIfHintAbsentWithoutUsdtParams()
	{
		// Arrange: риск и профит не заданы вовсе.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()));

		var cut = RenderDetail();

		// Assert: ни полосы, ни подписей — подсказка не выводится.
		// Требование: отсутствие обоих параметров убирает подсказку целиком.
		// Traceability: openspec:ui/screens#scenario-hint-absent-without-params
		cut.WaitForAssertion(() =>
			Assert.That(cut.FindAll(".rp-detail .rp-hint"), Has.Count.EqualTo(0)));
	}

	[TestMethod]
	[Description("Недоступный из-за сбоя марок итог показывается признаком вместо полосы шкалы")]
	public void TryIfHintShowsFailureSignWhenTotalUnavailable()
	{
		// Arrange: границы заданы, но итог не построен — нереализованная часть
		// не оценена из-за сбоя марок.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(unrealized: null), hasMarkFailure: true) with
			{
				RiskUsdt = 150m,
				ProfitUsdt = 300m,
			});

		var cut = RenderDetail();

		// Assert: подсказка показывает признак сбоя марок вместо полосы — итог
		// не подменяется реализованным результатом.
		// Требование: недоступный итог занят признаком сбоя, а не частичной шкалой.
		// Traceability: openspec:ui/screens#scenario-hint-unavailable-on-marks-failure
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".rp-detail").TextContent, Does.Contain("сбой котировок"));
			Assert.That(cut.FindAll(".rp-detail .rp-bar"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Удаление не предлагается конструкции со сделками или корректировками")]
	public void TryIfDeleteNotOfferedForNonEmptyConstruction()
	{
		// Arrange: у конструкции одна привязанная сделка.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with
			{
				Trades =
				[
					new ConstructionTradeRow(
						"e-1024",
						"BTCUSDT",
						new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
						true,
						0.008m,
						63181m,
						505.448m,
						0.010m,
						null),
				],
			});

		var cut = RenderDetail();

		// Assert: кнопки удаления у непустой конструкции нет.
		// Требование: удаление предлагается только для конструкций без сделок
		// и внешних корректировок PnL.
		// Traceability: openspec:ui/screens#requirement-construction-actions
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll(".detail-actions button").Any(button => button.TextContent.Trim() == "Удалить…"),
			Is.False));
	}

	[TestMethod]
	[Description("Удаление требует явного подтверждения и не запускается без него")]
	public void TryIfDeleteRequiresConfirmation()
	{
		// Arrange: пустая конструкция — кнопка удаления предложена.
		var cut = RenderDetail();

		// Act: пользователь открывает удаление, но не подтверждает его.
		FindButton(cut, "Удалить…").Click();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".action-warning").TextContent, Does.Contain("Действие необратимо")));
		FindButton(cut, "Отмена").Click();

		// Assert: без подтверждения команда удаления не запускается, панель закрыта.
		// Требование: удаление требует подтверждения.
		// Traceability: openspec:ui/screens#requirement-construction-actions
		_constructions.Verify(service => service.DeleteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".action-warning"), Has.Count.EqualTo(0)));
	}

	[TestMethod]
	[Description("Отказ удаления непустой конструкции показывает причину и сохраняет экран")]
	public void TryIfDeleteRefusalShowsReasonAndKeepsConstruction()
	{
		// Arrange: снимок показывает пустую конструкцию, но домен отказывает —
		// у конструкции появились привязанные сделки и корректировка.
		_constructions
			.Setup(service => service.DeleteAsync(7, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new ConstructionDeletionRefusedException(3, 1));

		var cut = RenderDetail();

		// Act: пользователь подтверждает удаление.
		FindButton(cut, "Удалить…").Click();
		FindButton(cut, "Удалить").Click();

		// Assert: отказ объясняет причину — какие записи блокируют удаление;
		// конструкция остаётся на экране без изменений.
		// Требование: отказ в удалении показывает причину.
		// Traceability: openspec:ui/screens#scenario-detail-delete-refused-with-reason
		cut.WaitForAssertion(() =>
		{
			var error = cut.Find(".note-error").TextContent;
			Assert.That(error, Does.Contain("Удаление конструкции невозможно"));
			Assert.That(error, Does.Contain("привязанных сделок — 3"));
			Assert.That(error, Does.Contain("внешних корректировок PnL — 1"));
		});
		Assert.That(cut.Find("h1").TextContent, Does.Contain("Календарь сентябрь"));
		Assert.That(cut.FindAll("table"), Has.Count.EqualTo(4));
	}

	[TestMethod]
	[Description("Успешное удаление возвращает пользователя к списку конструкций")]
	public void TryIfDeleteSuccessNavigatesToList()
	{
		// Arrange: пустая конструкция; после удаления read-модель сообщает
		// об отсутствии конструкции.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ThrowsAsync(new ConstructionNotFoundException(7));

		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/constructions/7");
		var cut = RenderDetail();

		// Act: пользователь подтверждает удаление.
		FindButton(cut, "Удалить…").Click();
		FindButton(cut, "Удалить").Click();

		// Assert: удаление выполнено, экран закрывается переходом к списку.
		_constructions.Verify(service => service.DeleteAsync(7, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(navigation.Uri, Does.EndWith("/")));
	}

	[TestMethod]
	[Description("Диалог удаления предлагает флажок резервной копии, включённый по умолчанию")]
	public void TryIfDeleteDialogOffersBackupCheckboxEnabledByDefault()
	{
		// Arrange: пустая конструкция — кнопка удаления предложена.
		var cut = RenderDetail();

		// Act: пользователь открывает диалог удаления.
		FindButton(cut, "Удалить…").Click();

		// Assert: диалог содержит флажок резервной копии, включённый по умолчанию.
		// Требование: подтверждение удаления предлагает резервную копию базы
		// перед удалением, включённую по умолчанию.
		// Traceability: openspec:ui/screens#scenario-detail-delete-offers-backup
		cut.WaitForAssertion(() =>
		{
			var checkbox = cut.Find(".action-form input[type=checkbox]");
			Assert.That(checkbox.HasAttribute("checked"), Is.True);
			Assert.That(cut.Find(".action-form").TextContent, Does.Contain("резервную копию базы"));
		});
	}

	[TestMethod]
	[Description("Подтверждённое удаление с включённым флажком создаёт копию базы до удаления")]
	public void TryIfDeleteWithBackupCreatesCopyBeforeDeletion()
	{
		// Arrange: пустая конструкция; копия и удаление фиксируются в общем
		// порядке вызовов — копия обязана предшествовать команде домена.
		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/constructions/7");
		var cut = RenderDetail();

		var sequence = new MockSequence();
		_backups.InSequence(sequence)
			.Setup(service => service.CreateBackupAsync("delete-construction", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new JournalBackupResult { FileName = "journal-delete-construction.db" });
		_constructions.InSequence(sequence)
			.Setup(service => service.DeleteAsync(7, It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		// Act: пользователь подтверждает удаление с флажком по умолчанию.
		FindButton(cut, "Удалить…").Click();
		FindButton(cut, "Удалить").Click();

		// Assert: сначала создаётся копия с причиной delete-construction и только
		// затем конструкция удаляется, экран закрывается переходом к списку.
		// Требование: при включённом флажке копия базы предшествует удалению.
		// Traceability: openspec:ui/screens#scenario-detail-delete-offers-backup
		_backups.Verify(
			service => service.CreateBackupAsync("delete-construction", It.IsAny<CancellationToken>()),
			Times.Once);
		_constructions.Verify(service => service.DeleteAsync(7, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(navigation.Uri, Does.EndWith("/")));
	}

	[TestMethod]
	[Description("Неудача резервной копии отменяет удаление с сообщением об ошибке")]
	public void TryIfDeleteBackupFailureCancelsDeletion()
	{
		// Arrange: сервис копий имитирует неудачу копирования.
		_backups
			.Setup(service => service.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new IOException("нет места на диске"));

		var cut = RenderDetail();

		// Act: пользователь подтверждает удаление при включённом флажке.
		FindButton(cut, "Удалить…").Click();
		FindButton(cut, "Удалить").Click();

		// Assert: удаление не запускается, конструкция остаётся на экране,
		// пользователь видит причину неудавшейся копии.
		// Требование: пока опция копирования включена, неудача копии блокирует
		// операцию удаления.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		_backups.Verify(
			service => service.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Once);
		_constructions.Verify(service => service.DeleteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
		cut.WaitForAssertion(() =>
		{
			var error = cut.Find(".note-error").TextContent;
			Assert.That(error, Does.Contain("Удаление отменено"));
			Assert.That(error, Does.Contain("резервная копия не создана"));
			Assert.That(error, Does.Contain("нет места на диске"));
		});
		Assert.That(cut.Find("h1").TextContent, Does.Contain("Календарь сентябрь"));
	}

	[TestMethod]
	[Description("Снятый флажок удаляет конструкцию без создания резервной копии")]
	public void TryIfDeleteWithoutBackupSkipsCopy()
	{
		// Arrange: пустая конструкция; пользователь снимает флажок копии.
		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/constructions/7");
		var cut = RenderDetail();

		// Act: пользователь снимает флажок и подтверждает удаление.
		FindButton(cut, "Удалить…").Click();
		cut.Find(".action-form input[type=checkbox]").Change(false);
		FindButton(cut, "Удалить").Click();

		// Assert: копия не создаётся, удаление выполняется, экран закрывается
		// переходом к списку.
		// Требование: при выключенной опции операция выполняется без копии.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		_backups.Verify(
			service => service.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Never);
		_constructions.Verify(service => service.DeleteAsync(7, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(navigation.Uri, Does.EndWith("/")));
	}

	[TestMethod]
	[Description("Комментарий сделки сохраняется из таблицы сделок и сразу виден в строке")]
	public void TryIfTradeCommentSavesAndShowsImmediately()
	{
		// Arrange: у сделки e-1024 комментария нет; после сохранения read-модель
		// возвращает снимок с новым комментарием сделки.
		var trade = new ConstructionTradeRow(
			"e-1024",
			"BTCUSDT",
			new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
			true,
			0.008m,
			63181m,
			505.448m,
			0.010m,
			null);
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Trades = [trade] })
			.ReturnsAsync(CreateData(MetricsOf()) with { Trades = [trade with { Comment = "вход половиной" }] });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[1].TextContent, Does.Contain("e-1024")));

		// Act: пользователь правит комментарий сделки в строке таблицы сделок.
		var tradesTable = cut.FindAll("table")[1];
		FindRowButton(tradesTable, "изменить").Click();
		tradesTable = cut.FindAll("table")[1];
		// Привязка черновика комментария идёт по oninput — ввод имитируется
		// событием input, а не change.
		tradesTable.QuerySelectorAll(".cell-input").Single().Input("вход половиной");
		// Bind обновляет черновик и перерисовывает строку — кнопка берётся из
		// свежего дерева после ре-рендера.
		FindRowButton(cut.FindAll("table")[1], "Сохранить").Click();

		// Assert: комментарий сохранён сервисом домена по ключу execId и сразу
		// виден в строке сделки.
		// Требование: новый текст сохраняется и сразу виден в строке сделки.
		// Traceability: openspec:ui/screens#scenario-trade-comment-inline-edit
		_comments.Verify(service =>
			service.SetTradeCommentAsync("e-1024", "вход половиной", It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[1].QuerySelectorAll("tbody tr").Single().TextContent, Does.Contain("вход половиной")));
	}

	[TestMethod]
	[Description("Правка комментария позиции не открывает правку остатка")]
	public void TryIfPositionCommentEditLeavesResidualReadOnly()
	{
		// Arrange: у конструкции позиция BTCUSDT с остатком и комментарием.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), positions:
			[
				new ConstructionPositionRow(
					"BTCUSDT",
					0.1m,
					42000m,
					42100m,
					9.5m,
					0.3m,
					0.5m,
					0m,
					10m,
					0.3m,
					1.5m,
					new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
					null,
					true,
					"наблюдение"),
			]));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[0].TextContent, Does.Contain("BTCUSDT")));

		// Act: пользователь начинает правку комментария позиции.
		var positionsTable = cut.FindAll("table")[0];
		FindRowButton(positionsTable, "изменить").Click();

		// Assert: в строке появилось ровно одно многострочное поле — поле
		// комментария в своей колонке (тринадцатой); остаток и прочие величины
		// позиции остаются текстом. Требование: доступно только текстовое поле
		// комментария, остаток позиции не редактируется.
		// Traceability: openspec:ui/screens#scenario-position-comment-without-residual-edit
		var row = cut.FindAll("table")[0].QuerySelectorAll("tbody tr").Single();
		Assert.That(row.QuerySelectorAll("textarea"), Has.Length.EqualTo(1));
		Assert.That(row.QuerySelectorAll("td")[13].QuerySelectorAll("textarea"), Has.Length.EqualTo(1));
		Assert.That(row.QuerySelectorAll("td")[1].QuerySelectorAll("textarea"), Is.Empty);
		Assert.That(row.QuerySelectorAll("td")[1].TextContent.Trim(), Is.EqualTo("+0.1"));

		// Act: пользователь сохраняет новый текст комментария позиции.
		cut.FindAll("table")[0].QuerySelectorAll(".cell-input").Single().Input("переворот ближе к экспирации");
		FindRowButton(cut.FindAll("table")[0], "Сохранить").Click();

		// Assert: комментарий позиции сохранён по ключу «конструкция × инструмент».
		_comments.Verify(service =>
			service.SetPositionCommentAsync(7, "BTCUSDT", "переворот ближе к экспирации", It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestMethod]
	[Description("Комментарий конструкции сохраняется из шапки деталей")]
	public void TryIfConstructionCommentSavesFromHeader()
	{
		// Arrange: после сохранения read-модель возвращает снимок с комментарием.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Comment = "стратегия календаря" });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".detail-comment").TextContent, Does.Contain("—")));

		// Act: пользователь правит комментарий конструкции в шапке деталей.
		cut.Find(".detail-comment button").Click();
		cut.Find(".detail-comment .cell-input").Input("стратегия календаря");
		FindHeaderButton(cut, "Сохранить").Click();

		// Assert: комментарий сохранён сервисом домена и сразу виден в шапке.
		// Требование: комментарий конструкции редактируется в шапке деталей,
		// сохранение не требует перезагрузки экрана.
		// Traceability: openspec:ui/screens#requirement-comments-inline-editing
		_comments.Verify(service =>
			service.SetConstructionCommentAsync(7, "стратегия календаря", It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(
			cut.Find(".detail-comment").TextContent, Does.Contain("стратегия календаря")));
	}

	[TestMethod]
	[Description("Многострочный комментарий через textarea сохраняется с переносами и отображается разрывами строк")]
	public void TryIfCommentTextareaSavesMultilineText()
	{
		// Arrange: у конструкции комментария нет; после сохранения read-модель
		// возвращает снимок с многострочным комментарием.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Comment = "цель: набор\nстоп под минимумом" });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".detail-comment").TextContent, Does.Contain("—")));

		// Act: пользователь вводит текст в несколько строк в textarea шапки
		// и сохраняет.
		cut.Find(".detail-comment button").Click();
		cut.Find(".detail-comment textarea.cell-input").Input("цель: набор\nстоп под минимумом");
		FindHeaderButton(cut, "Сохранить").Click();

		// Assert: переносы строк сохранены при записи сервиса и отображение
		// рендерит их разрывом строки. Требование: комментарий сохраняется
		// вместе с переносами строк и воспроизводится без искажений.
		// Traceability: openspec:ui/screens#scenario-comment-multiline-textarea-edit
		_comments.Verify(service =>
			service.SetConstructionCommentAsync(7, "цель: набор\nстоп под минимумом", It.IsAny<CancellationToken>()), Times.Once);
		// InnerHtml сериализуется без самозакрывающего слэша у void-элементов.
		cut.WaitForAssertion(() => Assert.That(
			cut.Find(".detail-comment .comment-md").InnerHtml, Does.Contain("<br>")));
	}

	[TestMethod]
	[Description("Ctrl+Enter в textarea сохраняет комментарий сразу, Enter без Ctrl не сохраняет и оставляет редактор открытым")]
	public void TryIfCommentCtrlEnterSavesAndPlainEnterKeepsEditing()
	{
		// Arrange: у конструкции комментария нет; после сохранения read-модель
		// возвращает снимок с комментарием из Markdown.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Comment = "**удержание** до конца недели" });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".detail-comment").TextContent, Does.Contain("—")));

		cut.Find(".detail-comment button").Click();
		var editor = cut.Find(".detail-comment textarea.cell-input");
		// Привязка черновика идёт по oninput — ввод имитируется событием input.
		editor.Input("**удержание** до конца недели");

		// Act: пользователь нажимает Enter без Ctrl в textarea шапки.
		editor.KeyDown(new KeyboardEventArgs { Key = "Enter" });

		// Assert: сохранения нет, редактор остался открытым. Требование: Enter
		// без Ctrl вставляет перенос строки и не сохраняет комментарий.
		_comments.Verify(service =>
			service.SetConstructionCommentAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		Assert.That(cut.FindAll(".detail-comment textarea.cell-input"), Has.Count.EqualTo(1));

		// Act: пользователь нажимает Ctrl+Enter в той же textarea.
		cut.Find(".detail-comment textarea.cell-input").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

		// Assert: комментарий сохранён с актуальным черновиком и сразу показан
		// отрендеренным из Markdown. Требование: Ctrl+Enter сохраняет комментарий
		// и сразу отображает его в отрендеренном виде.
		// Traceability: openspec:ui/screens#scenario-comment-saved-on-ctrl-enter
		_comments.Verify(service =>
			service.SetConstructionCommentAsync(7, "**удержание** до конца недели", It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(
			cut.Find(".detail-comment .comment-md").InnerHtml, Does.Contain("<strong>удержание</strong>")));
	}

	[TestMethod]
	[Description("Комментарий в шапке рендерится из Markdown, сырой HTML экранируется")]
	public void TryIfCommentDisplayRendersMarkdownAndEscapesHtml()
	{
		// Arrange: комментарий со списком, выделением, кодом и попыткой
		// внедрить сырой HTML.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with
			{
				Comment = "- пункт\n**выделено** `код`\n<script>alert('x')</script>",
			});

		// Act: пользователь открывает детали конструкции.
		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.Find(".detail-comment .comment-md").InnerHtml, Does.Contain("<ul>")));

		// Assert: разметка Markdown показана форматированной, а сырой HTML —
		// экранированным текстом без исполняемых тегов. Требование: отображение
		// рендерит Markdown, сырой HTML не исполняется браузером.
		// Traceability: openspec:ui/screens#scenario-comment-renders-markdown
		// Traceability: openspec:ui/screens#scenario-comment-raw-html-escaped
		var html = cut.Find(".detail-comment .comment-md").InnerHtml;
		Assert.That(html, Does.Contain("<strong>выделено</strong>"));
		Assert.That(html, Does.Contain("<code>код</code>"));
		Assert.That(html, Does.Contain("&lt;script&gt;"));
		Assert.That(html, Does.Not.Contain("<script"));
	}

	[TestMethod]
	[Description("Возврат сделки во «Входящие» снимает привязку и убирает сделку из таблицы")]
	public void TryIfReturnTradeToInboxUnbindsAndRemovesRowFromTable()
	{
		// Arrange: у конструкции одна привязанная сделка; после возврата
		// read-модель возвращает снимок без сделок — таблица пустеет.
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Trades = [CreateTrade()] })
			.ReturnsAsync(CreateData(MetricsOf()));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[1].TextContent, Does.Contain("e-1024")));

		// Act: пользователь возвращает сделку из таблицы деталей во «Входящие».
		FindRowButton(cut.FindAll("table")[1], "Во «Входящие»").Click();

		// Assert: привязка снята сервисом домена по ключу execId; сделка исчезла
		// из таблицы — снимок перечитан, позиции и метрики конструкции берутся
		// из нового чтения.
		// Требование: возврат сделки из деталей во «Входящие» пересчитывает
		// производные конструкции при очередном чтении.
		// Traceability: openspec:ui/screens#scenario-detail-return-trade-to-inbox
		_bindings.Verify(service =>
			service.UnbindAsync("e-1024", It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[1].TextContent, Does.Contain("сделок нет")));
	}

	[TestMethod]
	[Description("Перенос сделки предлагает выбор конструкции и привязывает к выбранной")]
	public void TryIfMoveTradeOffersTargetChoiceAndBindsToChosen()
	{
		// Arrange: кроме текущей конструкции 7 активна целевая 9 «Плечо на
		// октябрь»; после переноса read-модель возвращает снимок без сделки.
		_constructions
			.Setup(service => service.ListActiveAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Construction[]
			{
				new() { Id = 7, Name = "Календарь сентябрь", Status = ConstructionStatus.Open },
				new() { Id = 9, Name = "Плечо на октябрь", Status = ConstructionStatus.Open },
			});
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Trades = [CreateTrade()] })
			.ReturnsAsync(CreateData(MetricsOf()));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[1].TextContent, Does.Contain("e-1024")));

		// Act: пользователь открывает перенос сделки и выбирает целевую конструкцию.
		FindRowButton(cut.FindAll("table")[1], "Перенести…").Click();
		cut.WaitForAssertion(() =>
		{
			// Выбор предлагает только другие активные конструкции: текущая
			// в список кандидатов не попадает.
			var options = cut.FindAll(".action-form select option");
			Assert.That(options, Has.Count.EqualTo(1));
			Assert.That(options[0].TextContent, Is.EqualTo("Плечо на октябрь"));
		});
		cut.Find(".action-form select").Change("9");
		FindButton(cut, "Перенести").Click();

		// Assert: сделка привязана к целевой конструкции ровно один раз —
		// принадлежность заменена, а не задвоена; форма закрыта, таблица сделок
		// текущей конструкции опустела перечитанным снимком.
		// Требование: после подтверждения сделка принадлежит ровно одной —
		// целевой — конструкции.
		// Traceability: openspec:ui/screens#scenario-detail-move-trade-choose-target
		_bindings.Verify(service =>
			service.BindAsync(9, "e-1024", It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll("table")[1].TextContent, Does.Contain("сделок нет"));
			Assert.That(cut.FindAll(".action-form"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Отмена переноса закрывает форму без вызова привязки")]
	public void TryIfMoveCancelClosesFormWithoutBinding()
	{
		// Arrange: есть целевая конструкция, у текущей — одна сделка.
		_constructions
			.Setup(service => service.ListActiveAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Construction[]
			{
				new() { Id = 9, Name = "Плечо на октябрь", Status = ConstructionStatus.Open },
			});
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Trades = [CreateTrade()] });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[1].TextContent, Does.Contain("e-1024")));
		FindRowButton(cut.FindAll("table")[1], "Перенести…").Click();
		cut.WaitForAssertion(() => Assert.That(cut.Find(".action-form select"), Is.Not.Null));

		// Act: пользователь отменяет перенос.
		FindButton(cut, "Отмена").Click();

		// Assert: команда привязки не запускалась, форма закрыта, сделка
		// осталась в таблице текущей конструкции.
		// Требование: без подтверждения перенос не выполняется.
		// Traceability: openspec:ui/screens#requirement-trade-actions-in-detail
		_bindings.Verify(service =>
			service.BindAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".action-form"), Has.Count.EqualTo(0)));
		Assert.That(cut.FindAll("table")[1].TextContent, Does.Contain("e-1024"));
	}

	[TestMethod]
	[Description("Перенос без других активных конструкций показывает явное сообщение")]
	public void TryIfMoveWithoutOtherConstructionsShowsExplicitMessage()
	{
		// Arrange: активна только текущая конструкция — кандидатов переноса нет.
		_constructions
			.Setup(service => service.ListActiveAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Construction[]
			{
				new() { Id = 7, Name = "Календарь сентябрь", Status = ConstructionStatus.Open },
			});
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()) with { Trades = [CreateTrade()] });

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[1].TextContent, Does.Contain("e-1024")));

		// Act: пользователь открывает перенос сделки.
		FindRowButton(cut.FindAll("table")[1], "Перенести…").Click();

		// Assert: форма называет причину невозможности переноса вместо пустого
		// выбора; команды переноса нет, привязка не менялась.
		// Требование: отсутствие данных — видимое состояние, а не пустой выбор.
		// Traceability: change:add-ui-screens/design#goals-non-goals
		cut.WaitForAssertion(() => Assert.That(
			cut.Find(".action-form").TextContent,
			Does.Contain("Нет других активных конструкций")));
		Assert.That(cut.FindAll(".action-form select"), Has.Count.EqualTo(0));
		Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Перенести"), Is.False);
	}

	[TestMethod]
	[Description("Форма пометки закрытия предзаполнена последней маркой и доступна только открытой позиции")]
	public void TryIfCloseMarkFormDefaultsToLastKnownMark()
	{
		// Arrange: открытая позиция BTCUSDT и закрытая ETHUSDT; последняя известная
		// марка BTCUSDT в кэше провайдера — 44000.
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), hasOpenResidual: true, positions:
			[
				new ConstructionPositionRow(
					"BTCUSDT",
					0.1m,
					42000m,
					null,
					19m,
					0.6m,
					1m,
					0m,
					20m,
					0.7m,
					1m,
					new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
					null,
					true,
					null),
				new ConstructionPositionRow(
					"ETHUSDT",
					0m,
					3000m,
					3100m,
					5m,
					0.2m,
					0m,
					0m,
					5m,
					0.2m,
					0.5m,
					new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero),
					new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
					false,
					null),
			]));
		_markSource
			.Setup(source => source.GetLastMarkAsync("BTCUSDT", It.IsAny<CancellationToken>()))
			.ReturnsAsync(44000m);

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[0].TextContent, Does.Contain("BTCUSDT")));

		// Assert: действие «закрыть пометкой» предложено только открытой позиции —
		// у закрытой строки действия нет.
		// Требование: действие доступно из строки открытой позиции.
		// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
		Assert.That(
			cut.FindAll("button").Count(button => button.TextContent.Trim() == "закрыть пометкой…"),
			Is.EqualTo(1));

		// Act: пользователь открывает форму пометки из строки BTCUSDT.
		FindRowButton(cut.FindAll("table")[0], "закрыть пометкой…").Click();

		// Assert: поле цены предзаполнено последней известной маркой инструмента,
		// поле времени заполнено.
		// Требование: форма пометки подставляет последнюю марку.
		// Traceability: openspec:ui/screens#scenario-mark-form-defaults-last-mark
		var inputs = cut.FindAll(".action-form .action-input");
		Assert.That(inputs.Count, Is.EqualTo(2));
		Assert.That(inputs[0].GetAttribute("value"), Is.EqualTo("44000"));
		Assert.That(inputs[1].GetAttribute("value"), Is.Not.Empty);

		// Act: пользователь задаёт время и подтверждает пометку.
		inputs[1].Change("2026-09-20 14:30");
		FindButton(cut, "Поставить пометку").Click();

		// Assert: пометка сохранена сервисом домена с ценой по умолчанию и временем
		// из формы (время без смещения разбирается как локальное); форма закрыта
		// после успеха.
		_marks.Verify(service =>
			service.AddAsync(
				7,
				"BTCUSDT",
				new DateTimeOffset(2026, 9, 20, 14, 30, 0, LocalOffset),
				44000m,
				It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("button").Any(button => button.TextContent.Trim() == "Поставить пометку"),
			Is.False));
	}

	[TestMethod]
	[Description("Удаление ручной пометки возвращает позиции открытый статус с прежним остатком")]
	public void TryIfMarkRemovalReopensPositionWithFormerResidual()
	{
		// Arrange: позиция BTCUSDT закрыта ручной пометкой (остаток 0); после
		// удаления read-модель возвращает снимок с открытой позицией прежнего
		// остатка и пустой таблицей закрывающих записей.
		var closedByMark = CreateData(MetricsOf(), positions:
		[
			new ConstructionPositionRow(
				"BTCUSDT",
				0m,
				42000m,
				42100m,
				1m,
				0m,
				0m,
				0m,
				1m,
				0m,
				0m,
				new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
				new DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero),
				false,
				null),
		], closingEntries:
		[
			new ConstructionClosingEntryRow(
				new DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero),
				PositionClosingKind.ManualMark,
				"BTCUSDT",
				-0.01m,
				42100m,
				421m,
				5),
		]);
		var reopened = CreateData(MetricsOf(), hasOpenResidual: true, positions:
		[
			new ConstructionPositionRow(
				"BTCUSDT",
				0.01m,
				42000m,
				null,
				1m,
				0m,
				0m,
				0m,
				1m,
				0m,
				0m,
				new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
				null,
				true,
				null),
		]);
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(closedByMark)
			.ReturnsAsync(reopened);

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[2].TextContent, Does.Contain("ручная пометка")));

		// Act: пользователь удаляет пометку из таблицы закрывающих записей.
		FindRowButton(cut.FindAll("table")[2], "удалить").Click();

		// Assert: пометка удалена сервисом домена по идентификатору; позиция снова
		// открыта с прежним остатком, закрывающих записей больше нет.
		// Требование: удаление пометки отражается на статусе позиции.
		// Traceability: openspec:ui/screens#scenario-mark-removal-reflects-open
		_marks.Verify(service => service.DeleteAsync(5, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			var positionRow = cut.FindAll("table")[0].QuerySelectorAll("tbody tr").Single();
			Assert.That(positionRow.TextContent, Does.Contain("открыта"));
			Assert.That(positionRow.TextContent, Does.Contain("+0.01"));
			Assert.That(cut.FindAll("table")[2].TextContent, Does.Contain("закрывающих записей нет"));
		});
	}

	[TestMethod]
	[Description("Избыточная закрывающая запись показывается предупреждением")]
	public void TryIfRedundantClosingEntryWarningShown()
	{
		// Arrange: пометка поставлена на позицию с уже нулевым остатком — read-модель
		// возвращает предупреждение об избыточной закрывающей записи.
		var redundantClosedAt = new DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero);
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), positions:
			[
				new ConstructionPositionRow(
					"BTCUSDT",
					0m,
					42000m,
					42100m,
					1m,
					0m,
					0m,
					0m,
					1m,
					0m,
					0m,
					new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
					new DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero),
					false,
					null),
			], closingWarnings:
			[
				new RedundantClosingEntryWarning
				{
					ConstructionId = 7,
					Symbol = "BTCUSDT",
					Kind = PositionClosingKind.ManualMark,
					ClosedAt = redundantClosedAt,
					SourceKey = "manual:9",
				},
			]));

		var cut = RenderDetail();

		// Assert: экран показывает предупреждение об избыточной записи с видом,
		// инструментом и временем — запись не применена, позиция не перевёрнута.
		// Требование: избыточная закрывающая запись предупреждает.
		// Traceability: openspec:ui/screens#scenario-redundant-closing-entry-warned
		cut.WaitForAssertion(() =>
		{
			var warning = cut.Find(".note-error");
			Assert.That(warning.TextContent, Does.Contain("Избыточная закрывающая запись"));
			Assert.That(warning.TextContent, Does.Contain("ручная пометка"));
			Assert.That(warning.TextContent, Does.Contain("BTCUSDT"));
			Assert.That(warning.TextContent, Does.Contain(DisplayTime.FormatMoment(redundantClosedAt)));
		});
	}

	[TestMethod]
	[Description("Ручная пометка правится из таблицы закрывающих записей")]
	public void TryIfManualMarkEditedFromClosingEntries()
	{
		// Arrange: у конструкции ручная пометка BTCUSDT по цене 42100
		// от 2026-09-20 14:30 UTC.
		var markedAt = new DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero);
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), closingEntries:
			[
				new ConstructionClosingEntryRow(
					markedAt,
					PositionClosingKind.ManualMark,
					"BTCUSDT",
					-0.01m,
					42100m,
					421m,
					5),
			]));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[2].TextContent, Does.Contain("ручная пометка")));

		// Act: пользователь открывает правку пометки из закрывающих записей.
		FindRowButton(cut.FindAll("table")[2], "править").Click();

		// Assert: форма предзаполнена ценой и локальным временем существующей записи.
		var inputs = cut.FindAll(".action-form .action-input");
		Assert.That(inputs[0].GetAttribute("value"), Is.EqualTo("42100"));
		Assert.That(inputs[1].GetAttribute("value"), Is.EqualTo(DisplayTime.FormatMoment(markedAt)));

		// Act: пользователь меняет цену и время и сохраняет.
		inputs[0].Change("42300");
		inputs = cut.FindAll(".action-form .action-input");
		inputs[1].Change("2026-09-20 15:00");
		FindButton(cut, "Сохранить пометку").Click();

		// Assert: новые цена и время ушли сервису домена (время без смещения
		// разбирается как локальное), форма закрыта после успеха.
		// Требование: поставленная пометка правится из закрывающих записей.
		// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
		_marks.Verify(service =>
			service.EditAsync(
				5,
				"BTCUSDT",
				new DateTimeOffset(2026, 9, 20, 15, 0, 0, LocalOffset),
				42300m,
				It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("button").Any(button => button.TextContent.Trim() == "Сохранить пометку"),
			Is.False));
	}

	[TestMethod]
	[Description("Правка пометки без изменения времени сохраняет исходное мгновение")]
	public void TryIfMarkEditedWithoutTimeChangePreservesInstant()
	{
		// Arrange: у конструкции ручная пометка BTCUSDT от 2026-09-20 14:30 UTC.
		var markedAt = new DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero);
		_detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf(), closingEntries:
			[
				new ConstructionClosingEntryRow(
					markedAt,
					PositionClosingKind.ManualMark,
					"BTCUSDT",
					-0.01m,
					42100m,
					421m,
					5),
			]));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[2].TextContent, Does.Contain("ручная пометка")));

		// Act: пользователь открывает правку и сохраняет, не меняя предзаполненное
		// локальное время.
		FindRowButton(cut.FindAll("table")[2], "править").Click();
		FindButton(cut, "Сохранить пометку").Click();

		// Assert: сервис домена получил исходное мгновение — предзаполнение формы
		// и разбор ввода работают в одной локальной зоне, смещения не возникает.
		// Требование: правка закрывающей записи без изменения времени сохраняет мгновение.
		// Traceability: openspec:ui/screens#scenario-entry-edit-preserves-instant
		_marks.Verify(service =>
			service.EditAsync(
				5,
				"BTCUSDT",
				markedAt,
				42100m,
				It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestMethod]
	[Description("Корректировка добавляется формой из деталей и входит в таблицу и сводку")]
	public void TryIfAdjustmentAddedFromDetailShowsInTableAndSummary()
	{
		// Arrange: конструкция без корректировок; после добавления read-модель
		// возвращает снимок с корректировкой +87.4 от 2026-09-20 и суммой
		// корректировок в метриках.
		var adjustmentDate = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
		var withAdjustment = CreateData(
			MetricsOf(adjustments: 87.4m),
			adjustments:
			[
				new ConstructionAdjustmentRow(
					3,
					adjustmentDate,
					"PnL робота grid-ETH за сентябрь",
					PnLAdjustmentSource.Manual,
					87.4m),
			]);
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(MetricsOf()))
			.ReturnsAsync(withAdjustment);

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[3].TextContent, Does.Contain("корректировок нет")));

		// Act: пользователь открывает форму и заполняет дату, источник, знаковую
		// сумму и описание.
		FindButton(cut, "Добавить корректировку…").Click();
		var inputs = cut.FindAll(".action-form .action-input");
		Assert.That(inputs.Count, Is.EqualTo(4));
		inputs[0].Change("2026-09-20");
		inputs = cut.FindAll(".action-form .action-input");
		inputs[2].Change("+87.4");
		inputs = cut.FindAll(".action-form .action-input");
		inputs[3].Change("PnL робота grid-ETH за сентябрь");
		FindButton(cut, "Добавить корректировку").Click();

		// Assert: корректировка сохранена сервисом домена с атрибутами формы
		// (дата без смещения разбирается как локальная, источник — «ручная»);
		// после перечитывания строка видна в таблице, сумма — в сводке,
		// форма закрыта успехом.
		// Требование: корректировка добавляется из деталей конструкции.
		// Traceability: openspec:ui/screens#scenario-adjustment-added-from-detail
		_adjustments.Verify(service =>
			service.AddAsync(
				7,
				new DateTimeOffset(2026, 9, 20, 0, 0, 0, LocalOffset),
				PnLAdjustmentSource.Manual,
				87.4m,
				"PnL робота grid-ETH за сентябрь",
				It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			var table = cut.FindAll("table")[3].TextContent;
			Assert.That(table, Does.Contain(DisplayTime.FormatDay(adjustmentDate)));
			Assert.That(table, Does.Contain("PnL робота grid-ETH за сентябрь"));
			Assert.That(table, Does.Contain("ручная"));
			Assert.That(table, Does.Contain("+87.4"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("+87.4"));
		});
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("button").Any(button => button.TextContent.Trim() == "Добавить корректировку"),
			Is.False));
	}

	[TestMethod]
	[Description("Корректировка правится inline в таблице и удаляется без ограничений")]
	public void TryIfAdjustmentEditedAndDeletedInline()
	{
		// Arrange: у конструкции корректировка −12 «старая поправка»; после
		// правки read-модель возвращает строку +87.4 источником «робот», после
		// удаления — пустую таблицу.
		var originalAdjustmentDate = new DateTimeOffset(2026, 9, 20, 14, 30, 0, TimeSpan.Zero);
		var original = CreateData(
			MetricsOf(adjustments: -12m),
			adjustments:
			[
				new ConstructionAdjustmentRow(
					3,
					originalAdjustmentDate,
					"старая поправка",
					PnLAdjustmentSource.Manual,
					-12m),
			]);
		var edited = CreateData(
			MetricsOf(adjustments: 87.4m),
			adjustments:
			[
				new ConstructionAdjustmentRow(
					3,
					new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
					"новое описание",
					PnLAdjustmentSource.Robot,
					87.4m),
			]);
		_detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(original)
			.ReturnsAsync(edited)
			.ReturnsAsync(CreateData(MetricsOf()));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[3].TextContent, Does.Contain("-12")));

		// Act: пользователь открывает inline-правку строки — поля предзаполнены
		// значениями строки.
		FindRowButton(cut.FindAll("table")[3], "править").Click();
		var rowInputs = cut.FindAll("table")[3].QuerySelectorAll("input.cell-input");
		Assert.That(rowInputs.Count, Is.EqualTo(3));
		Assert.That(rowInputs[0].GetAttribute("value"), Is.EqualTo(DisplayTime.FormatDay(originalAdjustmentDate)));
		Assert.That(rowInputs[1].GetAttribute("value"), Is.EqualTo("старая поправка"));
		Assert.That(rowInputs[2].GetAttribute("value"), Is.EqualTo("-12"));

		// Act: пользователь меняет дату, описание, источник и сумму, сохраняет.
		rowInputs[0].Change("2026-09-21");
		rowInputs = cut.FindAll("table")[3].QuerySelectorAll("input.cell-input");
		rowInputs[1].Change("новое описание");
		rowInputs = cut.FindAll("table")[3].QuerySelectorAll("input.cell-input");
		rowInputs[2].Change("+87.4");
		var sourceSelect = cut.FindAll("table")[3].QuerySelector("select");
		Assert.That(sourceSelect, Is.Not.Null);
		sourceSelect!.Change("Robot");
		FindRowButton(cut.FindAll("table")[3], "Сохранить").Click();

		// Assert: правка ушла сервису домена всеми атрибутами строки; таблица и
		// сводка отражают новый набор корректировок без ограничений.
		// Требование: корректировка правится и удаляется в таблице.
		// Traceability: openspec:ui/screens#scenario-adjustment-edited-and-deleted-inline
		_adjustments.Verify(service =>
			service.EditAsync(
				3,
				new DateTimeOffset(2026, 9, 21, 0, 0, 0, LocalOffset),
				PnLAdjustmentSource.Robot,
				87.4m,
				"новое описание",
				It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			var table = cut.FindAll("table")[3].TextContent;
			Assert.That(table, Does.Contain("+87.4"));
			Assert.That(table, Does.Contain("робот"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("+87.4"));
		});

		// Act: пользователь удаляет строку корректировки.
		FindRowButton(cut.FindAll("table")[3], "удалить").Click();

		// Assert: удаление выполнено сервисом домена; таблица пуста с явным
		// сообщением, сводка больше не показывает сумму корректировок.
		// Traceability: openspec:ui/screens#scenario-adjustment-edited-and-deleted-inline
		_adjustments.Verify(service =>
			service.DeleteAsync(3, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll("table")[3].TextContent, Does.Contain("корректировок нет"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("—"));
		});
	}

	[TestMethod]
	[Description("Детали конструкции показывают панель подсказок этого субъекта")]
	public void TryIfDetailShowsHintsPanel()
	{
		// Arrange: панель конструкции 7 с одной живой подсказкой.
		_hints
			.Setup(model => model.ReadConstructionPanelAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPanelData
			{
				Subject = HintSubject.ForConstruction(7),
				LiveGroups =
				[
					new HintSection
					{
						Group = HintSectionGroups.V1.Single(group => group.Id == "construction-management"),
						Hints =
						[
							new HintRecord
							{
								Id = 31,
								RuleId = "rule-exit",
								Subject = HintSubject.ForConstruction(7),
								Character = "exit",
								Clarity = "crisp",
								Sources = [],
								Text = "Рассмотри выход по плану",
								Facts = new Dictionary<string, string>(StringComparer.Ordinal),
								AsOf = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
								Status = HintStatus.New,
								FirstSeenAt = new DateTimeOffset(2026, 9, 19, 12, 5, 0, TimeSpan.Zero),
							},
						],
					},
				],
				History = [],
			});

		// Act: пользователь открывает детали конструкции.
		var cut = RenderDetail();

		// Assert: панель подсказок показана в деталях и читает панель именно
		// этой конструкции; подсказка видна с кнопками жизненного цикла.
		// Требование: панель «Подсказки» в деталях конструкции.
		// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll(".hints-panel"), Has.Count.EqualTo(1));
			Assert.That(cut.Find(".hints-panel .hint-text").TextContent, Does.Contain("Рассмотри выход по плану"));
			Assert.That(cut.FindAll(".hints-panel .hint-actions button"), Has.Count.EqualTo(2));
		});
		_hints.Verify(model => model.ReadConstructionPanelAsync(7, It.IsAny<CancellationToken>()), Times.Once);
	}

	#region Помощники

	/// <summary>Рендерит экран деталей конструкции с идентификатором 7.</summary>
	private IRenderedComponent<ConstructionDetail> RenderDetail() =>
		_context.RenderComponent<ConstructionDetail>(parameters => parameters.Add(detail => detail.ConstructionId, 7L));

	/// <summary>Находит кнопку экрана по точному тексту подписи.</summary>
	private static IElement FindButton(IRenderedComponent<ConstructionDetail> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

	/// <summary>Смещение локального времени: ввод времени в форме разбирается как локальное.</summary>
	private static TimeSpan LocalOffset => DateTimeOffset.Now.Offset;

	/// <summary>Находит кнопку внутри таблицы по точному тексту подписи.</summary>
	private static IElement FindRowButton(IElement table, string text) =>
		table.QuerySelectorAll("button").Single(button => button.TextContent.Trim() == text);

	/// <summary>Находит кнопку inline-правки комментария конструкции в шапке деталей.</summary>
	private static IElement FindHeaderButton(IRenderedComponent<ConstructionDetail> cut, string text) =>
		cut.Find(".detail-comment").QuerySelectorAll("button").Single(button => button.TextContent.Trim() == text);

	/// <summary>Сделка деталей с ключом e-1024 — строка для действий таблицы сделок.</summary>
	private static ConstructionTradeRow CreateTrade() => new(
		"e-1024",
		"BTCUSDT",
		new DateTimeOffset(2026, 9, 19, 21, 32, 0, TimeSpan.Zero),
		true,
		0.008m,
		63181m,
		505.448m,
		0.010m,
		null);

	/// <summary>Данные деталей с пустыми таблицами по умолчанию.</summary>
	private static ConstructionDetailData CreateData(
		ConstructionMetrics metrics,
		bool hasOpenResidual = false,
		bool hasMarkFailure = false,
		DateTimeOffset? marksAsOf = null,
		IReadOnlyList<ConstructionPositionRow>? positions = null,
		IReadOnlyList<ConstructionClosingEntryRow>? closingEntries = null,
		IReadOnlyList<RedundantClosingEntryWarning>? closingWarnings = null,
		IReadOnlyList<ConstructionAdjustmentRow>? adjustments = null) => new(
		7,
		"Календарь сентябрь",
		ConstructionStatus.Open,
		3000m,
		null,
		null,
		null,
		null,
		null,
		metrics,
		hasOpenResidual,
		hasMarkFailure,
		marksAsOf,
		positions ?? [],
		[],
		closingEntries ?? [],
		closingWarnings ?? [],
		adjustments ?? []);

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
