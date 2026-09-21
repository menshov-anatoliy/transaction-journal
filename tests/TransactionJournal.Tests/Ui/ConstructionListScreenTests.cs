using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

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
				MarksAsOf = new DateTimeOffset(2026, 9, 19, 12, 34, 0, TimeSpan.Zero),
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
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("2026-09-19 12:34"));
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
				null,
				true,
				1,
				1,
				[Item(7, "Календарь сентябрь", ConstructionStatus.Open, realized: 214.32m, unrealized: null, adjustments: 87.4m, total: null)]));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: нереализованный PnL и итог строки заняты признаком сбоя марок,
		// сводка помечает отметку марок и итог; реализованный результат и
		// корректировки остаются видимыми.
		// Требование: сбой марок показывается признаком, реализованные величины
		// и проценты остаются видимыми.
		// Traceability: openspec:ui/screens#scenario-list-marks-failure-indicated
		cut.WaitForAssertion(() =>
		{
			var row = cut.Find("tr.clickable");
			Assert.That(row.QuerySelectorAll(".markfail").Length, Is.EqualTo(2));
			Assert.That(row.TextContent, Does.Contain("сбой марок"));
			Assert.That(row.TextContent, Does.Contain("неполный"));
			Assert.That(row.TextContent, Does.Contain("+214.32"));
			Assert.That(row.TextContent, Does.Contain("+87.4"));
			Assert.That(cut.FindAll(".kstrip .markfail").Count, Is.EqualTo(1));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("неполный (сбой марок)"));
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

	#region Помощники

	/// <summary>Пустые данные списка: нулевые счётчики и отсутствие строк.</summary>
	private static ConstructionListData EmptyData() => new(0m, null, false, 0, 0, []);

	/// <summary>Данные списка со строками и счётчиком открытых по статусам строк.</summary>
	private static ConstructionListData CreateData(params ConstructionListItem[] items) => new(
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
		decimal capital = 1000m,
		decimal realized = 0m,
		decimal? unrealized = 0m,
		decimal adjustments = 0m,
		decimal? total = null,
		decimal? percent = null) => new(
		id,
		name,
		status,
		capital,
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

	#endregion
}
