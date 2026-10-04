using Bunit;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Components;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Web;

/// <summary>
/// Проверки экрана «Конструкции»: сводка журнала (итог, отметка марок, счётчик
/// конструкций) и таблица со столбцами метрик аналитики; клик по строке открывает
/// детали конструкции; тулбар выполняет команды «Синхронизировать» и «Разобрать
/// входящие»; сбой марок показывается признаком в нереализованных столбцах;
/// пустой и недоступный журнал показываются явно.
/// Traceability: openspec:ui/screens#requirement-construction-list-screen
/// </summary>
[TestClass]
public class ConstructionListScreenTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IConstructionListReadModel> _list = null!;
	private Mock<IFrameReadModel> _frame = null!;
	private Mock<IJournalSyncService> _sync = null!;
	private Mock<IHintDisplayReadModel> _hints = null!;
	private Mock<IHintPassRunner> _passRunner = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();
		_list = new Mock<IConstructionListReadModel>();
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(EmptyData());
		_context.Services.AddSingleton(_list.Object);

		// Счётчик непривязанных сделок для команды «Разобрать входящие»: по
		// умолчанию «Входящие» пусты.
		_frame = new Mock<IFrameReadModel>();
		_frame
			.Setup(model => model.CountInboxAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(0);
		_context.Services.AddSingleton(_frame.Object);

		// Сервис единственной ручной команды синхронизации тулбара.
		_sync = new Mock<IJournalSyncService>();
		_context.Services.AddSingleton(_sync.Object);

		// Read-модель подсказок: по умолчанию панель журнала пуста, живых
		// подсказок у конструкций нет — проверки подсказок переопределяют.
		_hints = new Mock<IHintDisplayReadModel>();
		_hints
			.Setup(model => model.ReadJournalPanelAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(EmptyPanel());
		_hints
			.Setup(model => model.ReadLiveCountsByConstructionAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Dictionary<long, int>());
		_context.Services.AddSingleton(_hints.Object);

		// Запуск прохода подсказок кнопкой обзорного экрана: по умолчанию
		// проход завершается без созданных записей.
		_passRunner = new Mock<IHintPassRunner>();
		_passRunner
			.Setup(runner => runner.RunAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CompletedPass());
		_context.Services.AddSingleton(_passRunner.Object);
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Клик по строке конструкции открывает её детали")]
	public void TryIfRowClickOpensConstructionDetail()
	{
		// Arrange: в списке две конструкции — открытая и закрытая.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open), Item(8, "Контртренд ETH", ConstructionStatus.Closed)));
		var cut = _context.RenderComponent<Constructions>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tr.clickable"), Has.Count.EqualTo(2)));

		// Act: пользователь кликает строку первой конструкции.
		cut.Find("tr.clickable").Click();

		// Assert: открываются детали этой конструкции — адрес ведёт на её экран,
		// где каркас откроет транзитную вкладку.
		// Требование: строка открывается в детали конструкции кликом.
		// Traceability: openspec:ui/screens#scenario-list-row-opens-detail
		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		Assert.That(navigation.Uri, Does.EndWith("/constructions/7"));
	}

	[TestMethod]
	[Description("Сводка показывает итог, отметку марок и счётчик конструкций с числом открытых")]
	public void TryIfSummaryShowsJournalTotalMarksAndCounts()
	{
		// Arrange: журнал с итогом, отметкой марок и четырьмя конструкциями,
		// из которых открыта одна.
		var marksAsOf = new DateTimeOffset(2026, 9, 19, 12, 34, 0, TimeSpan.Zero);
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(
				Item(7, "Календарь сентябрь", ConstructionStatus.Open, realized: 214.32m, unrealized: -58.2m),
				Item(8, "Контртренд ETH", ConstructionStatus.Closed),
				Item(9, "Спред NATGAS", ConstructionStatus.Closed),
				Item(10, "Разбор коллапса", ConstructionStatus.Closed))
				with
			{
				TotalPnL = 125.5m,
				MarksAsOf = marksAsOf,
				OpenCount = 1,
			});

		// Act: пользователь открывает экран «Конструкции».
		var cut = _context.RenderComponent<Constructions>();

		// Assert: сводка показывает итог журнала со знаком, отметку времени марок
		// и счётчик конструкций с числом открытых.
		// Требование: экран показывает сводку журнала — итог, марки и счётчик.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("+125.5 USDT"));
			// Даты хранятся в UTC и рендерятся локальным временем.
			// Traceability: openspec:ui/screens#scenario-sync-dates-shown-local
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain(DisplayTime.FormatMoment(marksAsOf)));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("4 (1 открыта)"));
		});
	}

	[TestMethod]
	[Description("Таблица выводит столбцы метрик аналитики и статус каждой конструкции")]
	public void TryIfTableShowsMetricColumnsAndStatus()
	{
		// Arrange: открытая конструкция со всеми заполненными метриками.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open,
				capital: 3000m, realized: 214.32m, unrealized: -58.2m, adjustments: 87.4m, total: 243.52m, percent: 8.1m)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: строка несёт имя, статус, капитал и все метрики аналитики,
		// заголовок таблицы перечисляет столбцы метрик.
		// Требование: таблица выводит имя, статус, капитал, PnL, корректировки,
		// итог, процент и даты открытия/закрытия.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		cut.WaitForAssertion(() =>
		{
			var row = cut.Find("tr.clickable");
			Assert.That(row.TextContent, Does.Contain("Календарь сентябрь"));
			Assert.That(row.TextContent, Does.Contain("открыта"));
			Assert.That(row.TextContent, Does.Contain("3000"));
			Assert.That(row.TextContent, Does.Contain("+214.32"));
			Assert.That(row.TextContent, Does.Contain("-58.2"));
			Assert.That(row.TextContent, Does.Contain("+87.4"));
			Assert.That(row.TextContent, Does.Contain("+243.52"));
			Assert.That(row.TextContent, Does.Contain("+8.1%"));
			var header = cut.Find("thead");
			Assert.That(header.TextContent, Does.Contain("Нереализов."));
			Assert.That(header.TextContent, Does.Contain("% капитала"));
			Assert.That(header.TextContent, Does.Contain("Закрыта"));
		});
	}

	[TestMethod]
	[Description("Пустой список показывает явное сообщение об отсутствии конструкций")]
	public void TryIfEmptyListShowsExplicitMessage()
	{
		// Arrange: журнал без конструкций.
		var cut = _context.RenderComponent<Constructions>();

		// Assert: таблица показывает сообщение об отсутствии, а не пустую разметку.
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find("td.empty").TextContent, Does.Contain("конструкций нет")));
	}

	[TestMethod]
	[Description("Тулбар предлагает команды «Синхронизировать» и «Разобрать входящие» с текущим числом непривязанных")]
	public void TryIfToolbarOffersSyncAndInboxCommandsWithCount()
	{
		// Arrange: во «Входящих» три непривязанные сделки.
		_frame
			.Setup(model => model.CountInboxAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(3);
		var cut = _context.RenderComponent<Constructions>();

		// Assert: тулбар показывает обе команды; «Разобрать входящие» несёт
		// текущее число непривязанных сделок.
		// Требование: тулбар предлагает команды «Синхронизировать» и «Разобрать
		// входящие» с текущим числом непривязанных сделок.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		cut.WaitForAssertion(() =>
		{
			var buttons = cut.FindAll(".toolbar .btn");
			Assert.That(buttons, Has.Count.EqualTo(2));
			Assert.That(buttons[0].TextContent, Does.Contain("Синхронизировать"));
			Assert.That(buttons[1].TextContent, Does.Contain("Разобрать входящие (3)"));
		});
	}

	[TestMethod]
	[Description("Команда «Разобрать входящие» ведёт на экран «Входящие»")]
	public void TryIfInboxCommandNavigatesToInboxScreen()
	{
		// Arrange: экран списка с пустым журналом.
		var cut = _context.RenderComponent<Constructions>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".toolbar .btn"), Has.Count.EqualTo(2)));

		// Act: нажатие «Разобрать входящие».
		cut.FindAll(".toolbar .btn")[1].Click();

		// Assert: пользователь попадает на экран «Входящие» — разбор непривязанных
		// сделок выполняется там.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		Assert.That(navigation.Uri, Does.EndWith("/inbox"));
	}

	[TestMethod]
	[Description("Команда «Синхронизировать» выполняет запуск, показывает итог и перечитывает список")]
	public void TryIfSyncCommandRunsShowsResultAndRefreshesList()
	{
		// Arrange: синхронизация завершается backfill-запуском с новыми записями;
		// после синка перечитанный список показывает новую конструкцию.
		_sync
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateCompletedResult());
		_list
			.SetupSequence(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(EmptyData())
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open)));

		var cut = _context.RenderComponent<Constructions>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tr.clickable"), Has.Count.EqualTo(0)));

		// Act: нажатие «Синхронизировать».
		cut.Find(".toolbar .btn").Click();

		// Assert: запуск выполнен один раз, итог с режимом и счётчиками новых
		// записей показан, список перечитан — строка новой конструкции видна
		// без перезагрузки экрана.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		cut.WaitForAssertion(() =>
		{
			_sync.Verify(svc => svc.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
			Assert.That(cut.Markup, Does.Contain("Синхронизация завершена"));
			Assert.That(cut.Markup, Does.Contain("первичная загрузка (backfill)"));
			Assert.That(cut.Markup, Does.Contain("новых записей исполнения 2"));
			Assert.That(cut.Markup, Does.Contain("delivery-записей 1"));
			Assert.That(cut.FindAll("tr.clickable"), Has.Count.EqualTo(1));
		});
	}

	[TestMethod]
	[Description("Прерванная синхронизация показывает ошибку и возвращает кнопку для повтора")]
	public void TryIfFailedSyncShowsErrorAndKeepsCommandForRetry()
	{
		// Arrange: биржа отвечает ошибкой лимитов после всех повторов — синк прерван.
		_sync
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new BybitApiException(10006, "превышение частоты запросов"));
		var cut = _context.RenderComponent<Constructions>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".toolbar .btn"), Has.Count.EqualTo(2)));

		// Act: нажатие «Синхронизировать».
		cut.Find(".toolbar .btn").Click();

		// Assert: тулбар показывает прерывание с текстом ошибки биржи; кнопка
		// возвращается — прерванный запуск повторяется новым нажатием без дублей.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация прервана"));
			Assert.That(cut.Markup, Does.Contain("retCode=10006"));
			Assert.That(cut.FindAll(".toolbar .btn")[0].HasAttribute("disabled"), Is.False);
		});
	}

	[TestMethod]
	[Description("Сбой марок показывается признаком в нереализованных столбцах, реализованные величины видны")]
	public void TryIfMarkFailureIndicatedInUnrealizedColumns()
	{
		// Arrange: провайдер марок недоступен при чтении метрик — нереализованная
		// оценка и итог деградировали в null, отметка времени марок неизвестна.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConstructionListData(
				null,
				214.32m,
				null,
				null,
				true,
				1,
				1,
				[Item(7, "Календарь сентябрь", ConstructionStatus.Open, realized: 214.32m, unrealized: null, adjustments: 87.4m, total: null)]));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: нереализованный PnL и итог строки заняты признаком сбоя марок,
		// сводка помечает отметку марок, нереализованный агрегат и итог;
		// реализованный результат, корректировки и реализованный агрегат
		// остаются видимыми.
		// Требование: сбой марок показывается признаком, реализованные величины
		// и проценты остаются видимыми; деградирует только нереализованная часть.
		// Traceability: openspec:ui/screens#scenario-list-marks-failure-indicated
		// Traceability: openspec:ui/screens#scenario-frame-marks-failure-degrades-unrealized-only
		cut.WaitForAssertion(() =>
		{
			var row = cut.Find("tr.clickable");
			Assert.That(row.QuerySelectorAll(".markfail").Length, Is.EqualTo(2));
			Assert.That(row.TextContent, Does.Contain("сбой марок"));
			Assert.That(row.TextContent, Does.Contain("неполный"));
			Assert.That(row.TextContent, Does.Contain("+214.32"));
			Assert.That(row.TextContent, Does.Contain("+87.4"));
			// Сбой марок занимает две ячейки сводки: нереализованный агрегат
			// и отметку времени марок; реализованный агрегат остаётся числом.
			Assert.That(cut.FindAll(".kstrip .markfail").Count, Is.EqualTo(2));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("неполный (сбой марок)"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("+214.32"));
		});
	}

	[TestMethod]
	[Description("Сводка показывает разбивку итога на реализованный и нереализованный PnL рядом с итогом")]
	public void TryIfSummaryShowsPnlBreakdown()
	{
		// Arrange: журнал с итогом 125.5, разбивкой на реализованный 214.32
		// и нереализованный −58.2, отметка времени марок задана.
		var marksAsOf = new DateTimeOffset(2026, 9, 19, 12, 34, 0, TimeSpan.Zero);
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(
				Item(7, "Календарь сентябрь", ConstructionStatus.Open, realized: 214.32m, unrealized: -58.2m))
				with
			{
				TotalPnL = 125.5m,
				RealizedPnL = 214.32m,
				UnrealizedPnL = -58.2m,
				MarksAsOf = marksAsOf,
			});

		// Act: пользователь открывает экран «Конструкции».
		var cut = _context.RenderComponent<Constructions>();

		// Assert: сводка показывает итог по журналу и рядом с ним реализованный
		// и нереализованный PnL журнала в том же знаковом формате.
		// Требование: сводка журнала показывает разбивку итога.
		// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
		cut.WaitForAssertion(() =>
		{
			var summary = cut.Find(".kstrip").TextContent;
			Assert.That(summary, Does.Contain("+125.5 USDT"));
			Assert.That(summary, Does.Contain("реализованный"));
			Assert.That(summary, Does.Contain("+214.32"));
			Assert.That(summary, Does.Contain("нереализованный"));
			Assert.That(summary, Does.Contain("-58.2"));
			Assert.That(cut.FindAll(".kstrip .markfail"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Строка без капитала показывает прочерки в капитале и проценте")]
	public void TryIfNoCapitalRowShowsPercentDash()
	{
		// Arrange: конструкция без выделенного капитала — процентные величины
		// не построены.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open,
				capital: null, realized: 214.32m, unrealized: -58.2m, total: 156.12m)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: капитал и процент строки заняты прочерком — нулевой подмены нет.
		// Требование: строка без капитала показывает прочерк в капитале и проценте.
		// Traceability: openspec:ui/screens#scenario-list-no-capital-percent-dash
		cut.WaitForAssertion(() =>
		{
			var row = cut.Find("tr.clickable");
			Assert.That(row.TextContent, Does.Contain("—"));
			Assert.That(row.TextContent, Does.Contain("+156.12"));
			Assert.That(row.TextContent, Does.Not.Contain("%"));
		});
	}

	[TestMethod]
	[Description("Строка списка показывает компактную шкалу «риск — итог — профит» с величинами в подсказке наведения")]
	public void TryIfRowShowsCompactHintInsideBounds()
	{
		// Arrange: границы риск 5%/150 и профит 10%/300, итог 100 — внутри профита.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open,
				capital: 3000m, riskPercent: 5m, riskUsdt: 150m, profitPercent: 10m, profitUsdt: 300m,
				realized: -50m, unrealized: 150m, total: 100m)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: компактная подсказка несёт величины обеих границ в подсказке
		// наведения; заполнение идёт в сторону профита, пробоя нет.
		// Требование: в списке подсказка компактная — тонкая полоса строки.
		// Traceability: openspec:ui/screens#scenario-hint-inside-bounds
		cut.WaitForAssertion(() =>
		{
			var hint = cut.Find(".rp-compact");
			Assert.That(hint.GetAttribute("title"), Does.Contain("риск 5% / 150"));
			Assert.That(hint.GetAttribute("title"), Does.Contain("профит 10% / 300"));
			Assert.That(cut.FindAll(".rp-fill-profit"), Has.Count.EqualTo(1));
			Assert.That(cut.FindAll(".rp-breakout"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Компактная шкала строки помечает пробой границы итогом за ней")]
	public void TryIfRowHintMarksBreakout()
	{
		// Arrange: итог 400 за границей профита 300.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open,
				capital: 3000m, riskUsdt: 150m, profitUsdt: 300m,
				realized: 100m, unrealized: 300m, total: 400m)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: сторона профита заполнена до конца с признаком пробоя.
		// Требование: итог за границей виден признаком пробоя.
		// Traceability: openspec:ui/screens#scenario-hint-boundary-breakout
		cut.WaitForAssertion(() =>
			Assert.That(cut.FindAll(".rp-fill-profit.rp-breakout"), Has.Count.EqualTo(1)));
	}

	[TestMethod]
	[Description("Компактная шкала с одной границей показывает открытую сторону и величину в подсказке")]
	public void TryIfRowHintSingleBound()
	{
		// Arrange: задан только риск в USDT — процента нет без капитала.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open,
				capital: null, riskUsdt: 150m)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: подсказка наведения несёт только риск, открытая зона и нулевая
		// отметка на месте, стороны профита с границей нет.
		// Требование: граница без USDT-величины свою сторону не показывает.
		// Traceability: openspec:ui/screens#scenario-hint-single-bound
		cut.WaitForAssertion(() =>
		{
			var hint = cut.Find(".rp-compact");
			Assert.That(hint.GetAttribute("title"), Does.Contain("риск 150"));
			Assert.That(hint.GetAttribute("title"), Does.Not.Contain("профит"));
			Assert.That(cut.FindAll(".rp-zone-open"), Has.Count.EqualTo(1));
			Assert.That(cut.FindAll(".rp-zero"), Has.Count.EqualTo(1));
		});
	}

	[TestMethod]
	[Description("Строка без обеих USDT-величин не показывает компактную шкалу")]
	public void TryIfRowHintAbsentWithoutParams()
	{
		// Arrange: риск и профит не заданы вовсе.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: ни полосы, ни подсказки наведения в строке нет.
		// Требование: отсутствие обоих параметров убирает подсказку целиком.
		// Traceability: openspec:ui/screens#scenario-hint-absent-without-params
		cut.WaitForAssertion(() =>
			Assert.That(cut.FindAll(".rp-compact"), Has.Count.EqualTo(0)));
	}

	[TestMethod]
	[Description("Компактная шкала показывает признак сбоя марок вместо полосы при недоступном итоге")]
	public void TryIfRowHintFailureSignWhenTotalUnavailable()
	{
		// Arrange: границы заданы, но нереализованная часть не оценена — итог
		// недоступен из-за сбоя марок.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open,
				capital: 3000m, riskUsdt: 150m, profitUsdt: 300m,
				realized: 100m, unrealized: null)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: подсказка строки занята признаком сбоя марок, полоса не строится.
		// Требование: недоступный итог занят признаком сбоя, а не частичной шкалой.
		// Traceability: openspec:ui/screens#scenario-hint-unavailable-on-marks-failure
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll(".rp-fail"), Has.Count.EqualTo(1));
			Assert.That(cut.Find(".rp-fail").TextContent, Does.Contain("сбой марок"));
			Assert.That(cut.FindAll(".rp-bar"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Недоступный журнал показывается явным состоянием, а не пустым экраном")]
	public void TryIfUnavailableJournalShowsExplicitState()
	{
		// Arrange: чтение журнала падает — сырьё повреждено или база недоступна.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("база недоступна"));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: экран показывает явное состояние недоступности без таблицы.
		// Отсутствие данных — видимое состояние, а не пустой экран.
		// Traceability: change:add-ui-screens/design#goals-non-goals
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Журнал недоступен"));
			Assert.That(cut.FindAll("table"), Has.Count.EqualTo(0));
		});
	}

	[TestMethod]
	[Description("Строка конструкции с живыми подсказками показывает индикатор с их числом")]
	public void TryIfBadgeShowsLiveCount()
	{
		// Arrange: в списке две конструкции, у первой — две живые подсказки,
		// у второй живых подсказок нет.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(
				Item(7, "Календарь сентябрь", ConstructionStatus.Open),
				Item(8, "Контртренд ETH", ConstructionStatus.Open)));
		_hints
			.Setup(model => model.ReadLiveCountsByConstructionAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Dictionary<long, int> { [7] = 2 });

		var cut = _context.RenderComponent<Constructions>();

		// Assert: строка первой конструкции показывает индикатор с числом живых
		// подсказок; вторая строка без индикатора.
		// Требование: индикатор показывает число живых подсказок конструкции.
		// Traceability: openspec:ui/screens#scenario-ui-badge-shows-live-count
		cut.WaitForAssertion(() =>
		{
			var badges = cut.FindAll(".hint-badge");
			Assert.That(badges, Has.Count.EqualTo(1));
			Assert.That(badges[0].TextContent, Is.EqualTo("2"));
		});
	}

	[TestMethod]
	[Description("Строка конструкции без живых подсказок не показывает индикатор")]
	public void TryIfBadgeHiddenWhenNoLiveHints()
	{
		// Arrange: у конструкции живых подсказок нет — словарь индикаторов её не содержит.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: индикатор в строке отсутствует — ноль живых подсказок не рисуется.
		// Требование: у конструкции без живых подсказок индикатор отсутствует.
		// Traceability: openspec:ui/screens#scenario-ui-badge-hidden-when-none
		cut.WaitForAssertion(() =>
			Assert.That(cut.FindAll(".hint-badge"), Has.Count.EqualTo(0)));
	}

	[TestMethod]
	[Description("Обзорный экран показывает панель подсказок журнала")]
	public void TryIfJournalPanelShowsOnOverview()
	{
		// Arrange: у журнала одна живая портфельная подсказка.
		var hint = Hint(31, HintSubject.ForJournal(), "risk-mode", "Лимит риска периода исчерпан");
		_hints
			.Setup(model => model.ReadJournalPanelAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(PanelOf(HintSubject.ForJournal(), sectionOf: ("risk-mode", "Риск-режим", [hint])));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: панель подсказок субъекта «журнал» показана на обзорном
		// экране с группой справочника и текстом подсказки.
		// Требование: портфельные подсказки показывает обзорный экран.
		// Traceability: openspec:ui/screens#requirement-ui-portfolio-hint-panel
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll(".hints-panel"), Has.Count.EqualTo(1));
			Assert.That(cut.Find(".hints-panel h3").TextContent, Is.EqualTo("Риск-режим"));
			Assert.That(cut.Find(".hints-panel .hint-text").TextContent, Does.Contain("Лимит риска периода исчерпан"));
		});
	}

	[TestMethod]
	[Description("Кнопка прохода запускает агент и показывает итог с перечитыванием панелей")]
	public void TryIfManualPassRunsEngine()
	{
		// Arrange: проход создаёт две подсказки и гасит одну.
		_passRunner
			.Setup(runner => runner.RunAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPassResult
			{
				Outcome = HintPassOutcome.Completed,
				AsOf = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
				CreatedHints = 2,
				ExpiredHints = 1,
			});

		var cut = _context.RenderComponent<Constructions>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tr.clickable"), Has.Count.EqualTo(0)));

		// Act: пользователь нажимает кнопку запуска прохода подсказок.
		FindButton(cut, "Запустить проход подсказок").Click();

		// Assert: итог прохода показан под кнопкой, панели и индикаторы
		// перечитаны после прохода.
		// Требование: ручной проход выполняет тот же код агента, что и
		// плановый, и обновляет подсказки на экране.
		// Traceability: openspec:ui/screens#scenario-ui-manual-pass-runs-engine
		// Traceability: openspec:ui/screens#requirement-ui-manual-pass-button
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".note-ok").TextContent, Does.Contain("Проход завершён: создано 2, погашено 1")));
		_hints.Verify(model => model.ReadLiveCountsByConstructionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
		_hints.Verify(model => model.ReadJournalPanelAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
	}

	[TestMethod]
	[Description("Невалидный корпус правил показывает агрегированный список проблем")]
	public void TryIfManualPassCorpusError()
	{
		// Arrange: проход сломан невалидным корпусом — проблема в одной карточке,
		// проход до чтения журнала и рынка не дошёл.
		_passRunner
			.Setup(runner => runner.RunAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPassResult
			{
				Outcome = HintPassOutcome.CorpusInvalid,
				AsOf = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
				Diagnostics = ["карточка risk-window: пустой шаблон текста"],
			});

		var cut = _context.RenderComponent<Constructions>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tr.clickable"), Has.Count.EqualTo(0)));

		// Act: пользователь нажимает кнопку запуска прохода подсказок.
		FindButton(cut, "Запустить проход подсказок").Click();

		// Assert: экран показывает ошибку корпуса со списком проблем карточек.
		// Требование: ошибка корпуса показывается агрегированным списком
		// проблем, записи подсказок при этом не создаются.
		// Traceability: openspec:ui/screens#scenario-ui-manual-pass-corpus-error
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".note-error").TextContent, Does.Contain("Корпус правил невалиден"));
			var problems = cut.FindAll(".corpus-problems li");
			Assert.That(problems, Has.Count.EqualTo(1));
			Assert.That(problems[0].TextContent, Does.Contain("карточка risk-window: пустой шаблон текста"));
		});
	}

	#region Помощники

	/// <summary>Пустые данные списка: нулевые счётчики и отсутствие строк.</summary>
	private static ConstructionListData EmptyData() => new(0m, 0m, 0m, null, false, 0, 0, []);

	/// <summary>Данные списка со строками и счётчиком открытых по статусам строк.</summary>
	private static ConstructionListData CreateData(params ConstructionListItem[] items) => new(
		0m,
		0m,
		0m,
		null,
		false,
		items.Length,
		items.Count(item => item.Status == ConstructionStatus.Open),
		items);

	/// <summary>Строка конструкции с заполненными по умолчанию метриками; сбойная нереализованная оценка оставляет итог null.</summary>
	private static ConstructionListItem Item(
		long id,
		string name,
		ConstructionStatus status,
		decimal? capital = 1000m,
		decimal? riskPercent = null,
		decimal? riskUsdt = null,
		decimal? profitPercent = null,
		decimal? profitUsdt = null,
		decimal realized = 0m,
		decimal? unrealized = 0m,
		decimal adjustments = 0m,
		decimal? total = null,
		decimal? percent = null) => new(
		id,
		name,
		status,
		capital,
		riskPercent,
		riskUsdt,
		profitPercent,
		profitUsdt,
		realized,
		unrealized,
		adjustments,
		total ?? (unrealized is null ? null : realized + unrealized.Value + adjustments),
		percent,
		new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero),
		status == ConstructionStatus.Closed ? new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero) : null);

	/// <summary>Завершённый запуск синхронизации: backfill со счётчиками новых записей.</summary>
	private static JournalSyncResult CreateCompletedResult() => new()
	{
		Mode = SyncRunMode.Backfill,
		Run = new SyncRun
		{
			StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
			FinishedAt = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero),
			Mode = SyncRunMode.Backfill,
			Status = SyncRunStatus.Succeeded,
			NewExecutions = 2,
			NewDeliveries = 1,
			NewInstruments = 1,
		},
		Executions = new Dictionary<string, ExecutionCategorySyncResult>(StringComparer.Ordinal),
		Deliveries = new Dictionary<string, DeliveryCategorySyncResult>(StringComparer.Ordinal),
	};

	/// <summary>Пустые данные панели подсказок: ни живых групп, ни истории.</summary>
	private static HintPanelData EmptyPanel(HintSubject? subject = null) => new()
	{
		Subject = subject ?? HintSubject.ForJournal(),
		LiveGroups = [],
		History = [],
	};

	/// <summary>Панель подсказок с одной живой группой; история не задана.</summary>
	private static HintPanelData PanelOf(HintSubject subject, (string Id, string Title, IReadOnlyList<HintRecord> Hints) sectionOf)
	{
		var definition = HintSectionGroups.V1.Single(group => group.Id == sectionOf.Id);
		return new HintPanelData
		{
			Subject = subject,
			LiveGroups = [new HintSection { Group = definition, Hints = sectionOf.Hints }],
			History = [],
		};
	}

	/// <summary>Живая подсказка с денормализованным снимком момента генерации.</summary>
	private static HintRecord Hint(
		long id,
		HintSubject subject,
		string character,
		string text,
		HintStatus status = HintStatus.New) => new()
	{
		Id = id,
		RuleId = "rule-" + character,
		Subject = subject,
		Character = character,
		Clarity = "crisp",
		Sources = [],
		Text = text,
		Facts = new Dictionary<string, string>(StringComparer.Ordinal),
		AsOf = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
		Status = status,
		FirstSeenAt = new DateTimeOffset(2026, 9, 19, 12, 5, 0, TimeSpan.Zero),
	};

	/// <summary>Завершённый исход прохода без созданных записей.</summary>
	private static HintPassResult CompletedPass() => new()
	{
		Outcome = HintPassOutcome.Completed,
		AsOf = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
	};

	/// <summary>Кнопка тулбара по видимому тексту.</summary>
	private static IElement FindButton(IRenderedComponent<IComponent> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

	#endregion
}
