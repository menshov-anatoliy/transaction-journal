using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки экрана деталей конструкции: сводка метрик с периодом и отметкой
/// марок, комментарий рядом со сводкой, четыре таблицы записей — позиции,
/// сделки, закрывающие записи и корректировки PnL; действия конструкции —
/// переименование, смена статуса с архивацией, изменение капитала и удаление
/// с подтверждением и причиной отказа; действия сделок — возврат во «Входящие»
/// и перенос в другую конструкцию с выбором цели; пустые таблицы показывают
/// явное сообщение об отсутствии, сбой марок — признак на месте нереализованных
/// величин, недоступный журнал и отсутствующая конструкция — явные состояния.
/// Traceability: openspec:ui/screens#requirement-construction-detail-screen
/// Traceability: openspec:ui/screens#requirement-construction-actions
/// Traceability: openspec:ui/screens#requirement-comments-inline-editing
/// Traceability: openspec:ui/screens#requirement-trade-actions-in-detail
/// </summary>
[TestClass]
public class ConstructionDetailScreenTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IConstructionDetailReadModel> _detail = null!;
	private Mock<IConstructionService> _constructions = null!;
	private Mock<ICommentService> _comments = null!;
	private Mock<ITradeBindingService> _bindings = null!;

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

		// Сигнал изменений журнала оповещает каркас после действий экрана;
		// без подписчиков в изолированном рендере он безопасно бездействует.
		_context.Services.AddScoped<JournalChangeSignal>();
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
		tradesTable.QuerySelectorAll(".cell-input").Single().Change("вход половиной");
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
				new ConstructionPositionRow("BTCUSDT", 0.1m, 42000m, 42100m, 10m, true, "наблюдение"),
			]));

		var cut = RenderDetail();
		cut.WaitForAssertion(() => Assert.That(
			cut.FindAll("table")[0].TextContent, Does.Contain("BTCUSDT")));

		// Act: пользователь начинает правку комментария позиции.
		var positionsTable = cut.FindAll("table")[0];
		FindRowButton(positionsTable, "изменить").Click();

		// Assert: в строке появилось ровно одно текстовое поле — поле комментария
		// в своей колонке; остаток и прочие величины позиции остаются текстом.
		// Требование: доступно только текстовое поле комментария, остаток позиции
		// не редактируется.
		// Traceability: openspec:ui/screens#scenario-position-comment-without-residual-edit
		var row = cut.FindAll("table")[0].QuerySelectorAll("tbody tr").Single();
		Assert.That(row.QuerySelectorAll("input"), Has.Length.EqualTo(1));
		Assert.That(row.QuerySelectorAll("td")[6].QuerySelectorAll("input"), Has.Length.EqualTo(1));
		Assert.That(row.QuerySelectorAll("td")[1].QuerySelectorAll("input"), Is.Empty);
		Assert.That(row.QuerySelectorAll("td")[1].TextContent.Trim(), Is.EqualTo("+0.1"));

		// Act: пользователь сохраняет новый текст комментария позиции.
		cut.FindAll("table")[0].QuerySelectorAll(".cell-input").Single().Change("переворот ближе к экспирации");
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
		cut.Find(".detail-comment .cell-input").Change("стратегия календаря");
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

	#region Помощники

	/// <summary>Рендерит экран деталей конструкции с идентификатором 7.</summary>
	private IRenderedComponent<ConstructionDetail> RenderDetail() =>
		_context.RenderComponent<ConstructionDetail>(parameters => parameters.Add(detail => detail.ConstructionId, 7L));

	/// <summary>Находит кнопку экрана по точному тексту подписи.</summary>
	private static IElement FindButton(IRenderedComponent<ConstructionDetail> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

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
