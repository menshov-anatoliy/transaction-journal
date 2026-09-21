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
using TransactionJournal.Sync;
using ConstructionDetailPage = TransactionJournal.Components.Pages.ConstructionDetail;
using ConstructionsPage = TransactionJournal.Components.Pages.Constructions;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки бейджа «Входящих» и транзитной именованной вкладки конструкции:
/// бейдж показывает число непривязанных сделок и исчезает на пустых «Входящих»;
/// вкладка открытой в деталях конструкции занимает фиксированное место сразу
/// после «Конструкций», несёт статусную точку, закрывается крестиком
/// с возвратом к списку и сохраняется при переходе на статические экраны.
/// Traceability: openspec:ui/screens#requirement-app-frame-navigation
/// </summary>
[TestClass]
public class FrameNavigationTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IFrameReadModel> _frame = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();
		_frame = new Mock<IFrameReadModel>();
		_frame
			.Setup(model => model.CountInboxAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(0);
		_context.Services.AddSingleton(_frame.Object);
		_context.Services.AddSingleton(new Mock<IJournalMetricsReadModel>().Object);

		// Экран «Конструкции» читает собственную read-модель списка: навигационным
		// проверкам достаточно пустого списка.
		var list = new Mock<IConstructionListReadModel>();
		list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConstructionListData(0m, null, false, 0, 0, []));
		_context.Services.AddSingleton(list.Object);

		// Тулбар списка выполняет синхронизацию через сервис единственной ручной
		// команды: навигационным проверкам достаточно заглушки без запусков.
		_context.Services.AddSingleton(new Mock<IJournalSyncService>().Object);

		// Экран деталей конструкции читает собственную read-модель: навигационным
		// проверкам достаточно ответа «конструкция не найдена» — содержимое экрана
		// проверяется собственными тестами деталей.
		var detail = new Mock<IConstructionDetailReadModel>();
		detail
			.Setup(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new ConstructionNotFoundException(7));
		_context.Services.AddSingleton(detail.Object);

		// Экран деталей выполняет действия конструкции через use-case сервис:
		// навигационным проверкам достаточно заглушки без мутаций.
		_context.Services.AddSingleton(new Mock<IConstructionService>().Object);

		// Комментарии деталей правятся через сервис комментариев домена:
		// навигационным проверкам достаточно заглушки без мутаций.
		_context.Services.AddSingleton(new Mock<ICommentService>().Object);
		_context.Services.AddSingleton(new Mock<ITradeBindingService>().Object);

		// Ручные пометки закрытия ставятся сервисом пометок домена с дефолтом
		// цены у источника последних марок: навигационным проверкам достаточно
		// заглушек без мутаций.
		_context.Services.AddSingleton(new Mock<IManualCloseMarkService>().Object);
		_context.Services.AddSingleton(new Mock<IInstrumentMarkSource>().Object);

		// Сигнал изменений журнала: экран оповещает каркас после действий,
		// каркас перечитывает панель без навигации.
		_context.Services.AddScoped<JournalChangeSignal>();
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Бейдж вкладки «Входящие» показывает число непривязанных сделок")]
	public void TryIfInboxBadgeCountsUnassignedTrades()
	{
		// Arrange: во «Входящих» три непривязанные сделки.
		_frame
			.Setup(model => model.CountInboxAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(3);

		// Act: пользователь открывает экран «Конструкции».
		var cut = RenderFrame(Screen<ConstructionsPage>());

		// Assert: вкладка «Входящие» несёт бейдж с числом непривязанных сделок.
		// Требование: вкладка «Входящие» показывает бейдж с числом непривязанных сделок.
		// Traceability: openspec:ui/screens#scenario-inbox-badge-counts-unassigned
		cut.WaitForAssertion(() => Assert.That(cut.Find(".tab-badge").TextContent, Is.EqualTo("3")));
	}

	[TestMethod]
	[Description("После привязки всех сделок бейдж «Входящих» исчезает")]
	public void TryIfInboxBadgeDisappearsWhenAllTradesBound()
	{
		// Arrange: сначала две непривязанные сделки, после привязки — ноль.
		_frame
			.SetupSequence(model => model.CountInboxAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(2)
			.ReturnsAsync(0);

		var cut = RenderFrame(Screen<ConstructionsPage>());
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".tab-badge"), Has.Count.EqualTo(1)));

		// Act: сделки привязаны, каркас перечитал счётчик при смене экрана.
		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/inbox");

		// Assert: бейдж исчезает, когда непривязанных сделок не осталось.
		// Traceability: openspec:ui/screens#scenario-inbox-badge-counts-unassigned
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".tab-badge"), Has.Count.EqualTo(0)));
	}

	[TestMethod]
	[Description("Открытая в деталях конструкция получает именованную вкладку со статусной точкой сразу после «Конструкций»")]
	public void TryIfOpenedConstructionGetsNamedTransitTabWithStatusDot()
	{
		// Arrange: в деталях открыта конструкция «Календарь сентябрь» со статусом «открыта».
		_frame
			.Setup(model => model.FindConstructionHeaderAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConstructionHeader(7, "Календарь сентябрь", ConstructionStatus.Open));

		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/constructions/7");
		var cut = RenderFrame(DetailScreen(7));

		// Assert: транзитная вкладка именована конструкцией, ведёт в её детали,
		// несёт точку ручного статуса и крестик закрытия; её фиксированное место —
		// сразу после вкладки «Конструкции».
		// Traceability: openspec:ui/screens#requirement-app-frame-navigation
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("nav.tabs a"), Has.Count.EqualTo(4)));
		var tabs = cut.FindAll("nav.tabs a");
		Assert.That(tabs[0].GetAttribute("href"), Is.EqualTo("/"));
		Assert.That(tabs[1].GetAttribute("href"), Is.EqualTo("/constructions/7"));
		Assert.That(tabs[1].TextContent, Does.Contain("Календарь сентябрь"));
		Assert.That(cut.Find(".transit-tab .status-dot").ClassList, Does.Contain("status-open"));
		Assert.That(cut.Find(".transit-tab .tab-close"), Is.Not.Null);
	}

	[TestMethod]
	[Description("Крестик транзитной вкладки убирает вкладку и возвращает к списку конструкций")]
	public void TryIfTransitTabCloseReturnsToConstructionList()
	{
		// Arrange: пользователь в деталях закрытой конструкции.
		_frame
			.Setup(model => model.FindConstructionHeaderAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConstructionHeader(7, "Календарь сентябрь", ConstructionStatus.Closed));

		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/constructions/7");
		var cut = RenderFrame(DetailScreen(7));

		// Assert до закрытия: точка вкладки отражает ручной статус «закрыта».
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".transit-tab .status-dot").ClassList, Does.Contain("status-closed")));

		// Act: пользователь нажимает крестик вкладки.
		cut.Find(".tab-close").Click();

		// Assert: приложение возвращает пользователя к списку конструкций
		// и убирает вкладку.
		// Traceability: openspec:ui/screens#scenario-transit-tab-closes-to-list
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("nav.tabs a"), Has.Count.EqualTo(3)));
		Assert.That(cut.FindAll(".transit-tab"), Has.Count.EqualTo(0));
		Assert.That(navigation.Uri, Does.EndWith("/"));
	}

	[TestMethod]
	[Description("Переход на статическую вкладку сохраняет транзитную вкладку конструкции до явного закрытия")]
	public void TryIfStaticNavigationKeepsTransitTabUntilClosed()
	{
		// Arrange: в деталях открыта конструкция.
		_frame
			.Setup(model => model.FindConstructionHeaderAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConstructionHeader(7, "Календарь сентябрь", ConstructionStatus.Open));

		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/constructions/7");
		var cut = RenderFrame(DetailScreen(7));
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("nav.tabs a"), Has.Count.EqualTo(4)));

		// Act: пользователь уходит на статический экран «Входящие».
		navigation.NavigateTo("/inbox");

		// Assert: транзитная вкладка сохраняется до явного закрытия.
		// Traceability: openspec:ui/screens#scenario-transit-tab-closes-to-list
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".transit-tab").TextContent, Does.Contain("Календарь сентябрь")));

		// Act: крестик закрывает вкладку и из статического экрана.
		cut.Find(".tab-close").Click();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("nav.tabs a"), Has.Count.EqualTo(3)));
		Assert.That(navigation.Uri, Does.EndWith("/"));
	}

	[TestMethod]
	[Description("Точка транзитной вкладки отражает новый статус сразу после смены в деталях")]
	public void TryIfTransitTabStatusDotReflectsStatusChangeFromDetail()
	{
		// Arrange: каркас читает заголовок конструкции «открыта», после действия —
		// «закрыта»; экран деталей возвращает те же статусы снимком, смена статуса
		// выполняется заглушкой use-case сервиса.
		_frame
			.SetupSequence(model => model.FindConstructionHeaderAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConstructionHeader(7, "Календарь сентябрь", ConstructionStatus.Open))
			.ReturnsAsync(new ConstructionHeader(7, "Календарь сентябрь", ConstructionStatus.Closed));
		var detail = new Mock<IConstructionDetailReadModel>();
		detail
			.SetupSequence(model => model.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(DetailDataOf(ConstructionStatus.Open))
			.ReturnsAsync(DetailDataOf(ConstructionStatus.Closed));
		_context.Services.AddSingleton(detail.Object);
		var constructions = new Mock<IConstructionService>();
		_context.Services.AddSingleton(constructions.Object);
		_context.Services.AddSingleton(new Mock<ICommentService>().Object);
		_context.Services.AddSingleton(new Mock<ITradeBindingService>().Object);

		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		navigation.NavigateTo("/constructions/7");
		var cut = RenderFrame(DetailScreen(7));
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".transit-tab .status-dot").ClassList, Does.Contain("status-open")));

		// Act: пользователь закрывает конструкцию командой статуса в деталях.
		cut.FindAll(".detail-actions button")
			.Single(button => button.TextContent.Trim() == "Закрыть")
			.Click();

		// Assert: статус сменён сервисом домена; точка транзитной вкладки
		// отражает «закрыта» без навигации — каркас перечитал заголовок по сигналу.
		// Требование: смена статуса меняет индикацию статуса — бейдж и точка вкладки.
		// Traceability: openspec:ui/screens#scenario-detail-status-change-indicated
		constructions.Verify(service =>
			service.ChangeStatusAsync(7, ConstructionStatus.Closed, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".transit-tab .status-dot").ClassList, Does.Contain("status-closed")));
	}

	#region Помощники

	/// <summary>Рендерит каркас с заданным экраном в теле страницы.</summary>
	private IRenderedComponent<MainLayout> RenderFrame(RenderFragment screen) =>
		_context.RenderComponent<MainLayout>(parameters => parameters.Add(layout => layout.Body, screen));

	/// <summary>
	/// Снимок деталей конструкции с ручным статусом и пустыми таблицами:
	/// навигационной проверке достаточно заголовка со статусом.
	/// </summary>
	private static ConstructionDetailData DetailDataOf(ConstructionStatus status) => new(
		7,
		"Календарь сентябрь",
		status,
		3000m,
		null,
		new ConstructionMetrics
		{
			ConstructionId = 7,
			AllocatedCapitalUsdt = 3000m,
			RealizedPnL = 0m,
			UnrealizedPnL = 0m,
			AdjustmentsPnL = 0m,
			TotalPnL = 0m,
			RealizedPnLPercent = null,
			UnrealizedPnLPercent = null,
			AdjustmentsPnLPercent = null,
			TotalPnLPercent = null,
			OpenedAt = null,
			ClosedAt = null,
			Duration = null,
		},
		false,
		false,
		null,
		[],
		[],
		[],
		[],
		[]);

	/// <summary>Фрагмент рендера экрана как тела каркаса.</summary>
	private static RenderFragment Screen<TScreen>() where TScreen : IComponent => builder =>
	{
		builder.OpenComponent<TScreen>(0);
		builder.CloseComponent();
	};

	/// <summary>Фрагмент рендера экрана деталей заданной конструкции.</summary>
	private static RenderFragment DetailScreen(long constructionId) => builder =>
	{
		builder.OpenComponent<ConstructionDetailPage>(0);
		builder.AddAttribute(1, nameof(ConstructionDetailPage.ConstructionId), constructionId);
		builder.CloseComponent();
	};

	#endregion
}
