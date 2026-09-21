using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Sync;
using ConstructionsPage = TransactionJournal.Components.Pages.Constructions;
using InboxPage = TransactionJournal.Components.Pages.Inbox;
using SettingsPage = TransactionJournal.Components.Pages.Settings;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки каркаса «Терминал»: верхняя панель показывает бренд, текущий экран
/// и итог по журналу на любом экране, статические вкладки «Конструкции»,
/// «Входящие», «Настройки» ведут на свои экраны и подсвечивают активный.
/// Содержимое самих экранов и транзитная вкладка конструкции — задачи экранов,
/// здесь не проверяются.
/// Traceability: openspec:ui/screens#requirement-app-frame-navigation
/// </summary>
[TestClass]
public class AppFrameTests
{
	private Bunit.TestContext _context = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();

		// Каркас дополнительно читает счётчик «Входящих» и заголовки конструкций
		// через собственную read-модель: базовым проверкам каркаса достаточно
		// пустых значений по умолчанию.
		var frame = new Mock<IFrameReadModel>();
		frame
			.Setup(model => model.CountInboxAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(0);
		_context.Services.AddSingleton(frame.Object);

		// Экран «Конструкции» читает собственную read-модель списка: каркасным
		// проверкам достаточно пустого списка.
		var list = new Mock<IConstructionListReadModel>();
		list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConstructionListData(0m, null, false, 0, 0, []));
		_context.Services.AddSingleton(list.Object);

		// Тулбар списка выполняет синхронизацию через сервис единственной ручной
		// команды: каркасным проверкам достаточно заглушки без запусков.
		_context.Services.AddSingleton(new Mock<IJournalSyncService>().Object);

		// Каркас подписывается на сигнал изменений журнала после мутаций экранов:
		// каркасным проверкам достаточно молчащего сигнала без подписчиков.
		_context.Services.AddScoped<JournalChangeSignal>();

		// Экран «Входящие» читает sealed-класс read-модели над своей базой:
		// каркасным проверкам содержимое «Входящих» безразлично — чтение пустой
		// in-memory базы экран обрабатывает явным состоянием недоступности.
		_context.Services.AddSingleton(new InboxReadModel(
			new DbContextOptionsBuilder<JournalDbContext>().UseSqlite("Data Source=:memory:").Options));
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Итог по журналу виден в верхней панели на любом экране и вычислен по текущим данным")]
	public void TryIfJournalTotalIsVisibleOnEveryScreen()
	{
		// Arrange: read-модель журнала возвращает итог 125.5 USDT по текущим данным.
		var metrics = new Mock<IJournalMetricsReadModel>();
		metrics
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateMetrics(125.5m));
		_context.Services.AddSingleton(metrics.Object);

		// Act/Assert: каждый экран приложения несёт верхнюю панель с итогом журнала.
		// Требование: верхняя панель показывает итог по журналу, видимый на любом экране.
		// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
		var constructions = RenderFrame(Screen<ConstructionsPage>());
		constructions.WaitForAssertion(() =>
		{
			Assert.That(constructions.Markup, Does.Contain("итог по журналу: +125.5 USDT"));
			Assert.That(constructions.Markup, Does.Contain("<h1>Конструкции</h1>"));
		});

		var inbox = RenderFrame(Screen<InboxPage>());
		inbox.WaitForAssertion(() =>
		{
			Assert.That(inbox.Markup, Does.Contain("итог по журналу: +125.5 USDT"));
			Assert.That(inbox.Markup, Does.Contain("<h1>Входящие</h1>"));
		});

		var settings = RenderFrame(Screen<SettingsPage>());
		settings.WaitForAssertion(() =>
		{
			Assert.That(settings.Markup, Does.Contain("итог по журналу: +125.5 USDT"));
			Assert.That(settings.Markup, Does.Contain("<h1>Настройки</h1>"));
		});
	}

	[TestMethod]
	[Description("Сбой марок помечает итог панели неполным вместо подмены частичной суммой")]
	public void TryIfMarkFailureMarksJournalTotalIncomplete()
	{
		// Arrange: нереализованная оценка недоступна — итог журнала null с признаком сбоя.
		var metrics = new Mock<IJournalMetricsReadModel>();
		metrics
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateMetrics(null));
		_context.Services.AddSingleton(metrics.Object);

		var cut = RenderFrame(Screen<ConstructionsPage>());

		// Assert: панель помечает итог неполным, не показывая частичную сумму.
		// Traceability: change:add-ui-screens/design#d4
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("итог по журналу: неполный (сбой марок)")));
	}

	[TestMethod]
	[Description("Статические вкладки каркаса ведут на экраны «Конструкции», «Входящие», «Настройки»")]
	public void TryIfStaticTabsOfferNavigationBetweenScreens()
	{
		_context.Services.AddSingleton(new Mock<IJournalMetricsReadModel>().Object);

		var cut = RenderFrame(Screen<ConstructionsPage>());

		// Assert: три статические вкладки с адресами корня, «Входящих» и «Настроек».
		// Требование: навигация состоит из статических вкладок трёх экранов.
		// Traceability: openspec:ui/screens#requirement-app-frame-navigation
		var tabs = cut.FindAll("nav.tabs a");
		Assert.That(tabs, Has.Count.EqualTo(3));
		Assert.That(tabs[0].GetAttribute("href"), Is.EqualTo("/"));
		Assert.That(tabs[0].TextContent, Does.Contain("Конструкции"));
		Assert.That(tabs[1].GetAttribute("href"), Is.EqualTo("/inbox"));
		Assert.That(tabs[1].TextContent, Does.Contain("Входящие"));
		Assert.That(tabs[2].GetAttribute("href"), Is.EqualTo("/settings"));
		Assert.That(tabs[2].TextContent, Does.Contain("Настройки"));
	}

	[TestMethod]
	[Description("Активная вкладка подсвечивает текущий экран навигации")]
	public void TryIfActiveTabHighlightsCurrentScreen()
	{
		_context.Services.AddSingleton(new Mock<IJournalMetricsReadModel>().Object);
		var navigation = _context.Services.GetRequiredService<NavigationManager>();

		// Act: пользователь на экране «Входящие».
		navigation.NavigateTo("/inbox");
		var inbox = RenderFrame(Screen<InboxPage>());

		// Assert: подсвечена вкладка «Входящие», остальные вкладки не активны.
		var tabs = inbox.FindAll("nav.tabs a");
		Assert.That(tabs[1].ClassList, Does.Contain("active"));
		Assert.That(tabs[0].ClassList, Does.Not.Contain("active"));
		Assert.That(tabs[2].ClassList, Does.Not.Contain("active"));

		// Act: навигация на «Конструкции» подсвечивает первую вкладку.
		navigation.NavigateTo("/");
		var constructions = RenderFrame(Screen<ConstructionsPage>());

		tabs = constructions.FindAll("nav.tabs a");
		Assert.That(tabs[0].ClassList, Does.Contain("active"));
		Assert.That(tabs[1].ClassList, Does.Not.Contain("active"));
		Assert.That(tabs[2].ClassList, Does.Not.Contain("active"));
	}

	#region Помощники

	/// <summary>Рендерит каркас с заданным экраном в теле страницы.</summary>
	private IRenderedComponent<MainLayout> RenderFrame(RenderFragment screen) =>
		_context.RenderComponent<MainLayout>(parameters => parameters.Add(layout => layout.Body, screen));

	/// <summary>Фрагмент рендера экрана как тела каркаса.</summary>
	private static RenderFragment Screen<TScreen>() where TScreen : IComponent => builder =>
	{
		builder.OpenComponent<TScreen>(0);
		builder.CloseComponent();
	};

	/// <summary>Метрики журнала с одним итогом: панели достаточно итога и признака сбоя марок.</summary>
	private static JournalMetrics CreateMetrics(decimal? totalPnL) => new()
	{
		Constructions = [],
		Positions = [],
		TotalPnL = totalPnL,
		MarksAsOf = null,
		HasMarkFailure = totalPnL is null,
	};

	#endregion
}
