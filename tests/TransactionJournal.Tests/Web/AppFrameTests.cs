using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Infrastructure.Ops;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application;
using TransactionJournal.Application.Ops;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using ConstructionsPage = TransactionJournal.Components.Pages.Constructions;
using InboxPage = TransactionJournal.Components.Pages.Inbox;
using SettingsPage = TransactionJournal.Components.Pages.Settings;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Web;

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
			.ReturnsAsync(new ConstructionListData(0m, 0m, 0m, null, false, 0, 0, []));
		_context.Services.AddSingleton(list.Object);

		// Тулбар списка выполняет синхронизацию через сервис единственной ручной
		// команды: каркасным проверкам достаточно заглушки без запусков.
		_context.Services.AddSingleton(new Mock<IJournalSyncService>().Object);

		// Панель подсказок и кнопка прохода обзорного экрана читают подсказки
		// через собственную read-модель: каркасным проверкам достаточно пустой
		// панели и завершённого без записей прохода.
		var hints = new Mock<IHintDisplayReadModel>();
		hints
			.Setup(model => model.ReadJournalPanelAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPanelData
			{
				Subject = HintSubject.ForJournal(),
				LiveGroups = [],
				History = [],
			});
		hints
			.Setup(model => model.ReadLiveCountsByConstructionAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Dictionary<long, int>());
		_context.Services.AddSingleton(hints.Object);
		var passRunner = new Mock<IHintPassRunner>();
		passRunner
			.Setup(runner => runner.RunAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPassResult
			{
				Outcome = HintPassOutcome.Completed,
				AsOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
			});
		_context.Services.AddSingleton(passRunner.Object);

		// «Настройки» выполняют сборку конструкций через собственную команду:
		// каркасным проверкам достаточно заглушки без запусков.
		// Traceability: change:add-construction-auto-assembly/specs/ui/screens/spec#requirement-settings-assembly-action
		_context.Services.AddSingleton(new Mock<IConstructionAssemblyService>().Object);

		// Детали конструкции создают опциональную копию перед удалением через
		// сервис резервных копий: каркасным проверкам достаточно заглушки,
		// возвращающей успешную копию без обращения к файловой системе.
		var backups = new Mock<IJournalBackupService>();
		backups
			.Setup(service => service.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new JournalBackupResult { FileName = "journal-frame.db" });
		_context.Services.AddSingleton(backups.Object);

		// «Настройки» читают политику копирования перед синхронизацией из
		// собственного хранилища: каркасным проверкам достаточно заглушки
		// с включённым по умолчанию состоянием.
		var policy = new Mock<IBackupPolicyStore>();
		policy
			.Setup(store => store.IsBackupBeforeSyncEnabledAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_context.Services.AddSingleton(policy.Object);

		// Каркас подписывается на сигнал изменений журнала после мутаций экранов:
		// каркасным проверкам достаточно молчащего сигнала без подписчиков.
		_context.Services.AddScoped<JournalChangeSignal>();

		// Экран «Входящие» читает sealed-класс read-модели над своей базой:
		// каркасным проверкам содержимое «Входящих» безразлично — чтение пустой
		// in-memory базы экран обрабатывает явным состоянием недоступности.
		_context.Services.AddSingleton(new InboxReadModel(
			new DbContextOptionsBuilder<JournalDbContext>().UseSqlite("Data Source=:memory:").Options));

		// Разбор выбранных «Входящих» выполняется use-case сервисами домена:
		// каркасным проверкам достаточно заглушек без мутаций.
		_context.Services.AddSingleton(new Mock<IConstructionService>().Object);
		_context.Services.AddSingleton(new Mock<ITradeBindingService>().Object);

		// Блок подключения «Настроек» читает учётные данные через read-модель:
		// каркасным проверкам достаточно не настроенного ключа — экран показывает
		// явное состояние без маски, значения ключа и секрета каркасу безразличны.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(new UnconfiguredCredentials());
		_context.Services.AddSingleton<SettingsReadModel>();

		// Журнал синхронизаций каркасным проверкам не нужен — экран рендерит
		// явное пустое состояние поверх заглушки read-модели.
		var syncJournal = new Mock<ISyncJournalReadModel>();
		syncJournal
			.Setup(model => model.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);
		_context.Services.AddSingleton(syncJournal.Object);
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
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("итог по журналу: неполный (сбой котировок)")));
	}

	[TestMethod]
	[Description("Панель показывает рядом с итогом разбивку на реализованный и нереализованный P&L")]
	public void TryIfJournalTotalShowsPnlBreakdown()
	{
		// Arrange: итог 125.5 складывается из реализованного 100 и нереализованного 25.5.
		var metrics = new Mock<IJournalMetricsReadModel>();
		metrics
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateExplicitMetrics(125.5m, 100m, 25.5m));
		_context.Services.AddSingleton(metrics.Object);

		var cut = RenderFrame(Screen<ConstructionsPage>());

		// Assert: итог несёт разбивку в компактных подписях, формат знака общий
		// с самим итогом; признака сбоя марок в панели нет.
		// Требование: панель показывает итог с разбивкой на любом экране.
		// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("итог по журналу: +125.5 USDT (реализов. +100 / нереализов. +25.5)"));
			Assert.That(cut.Markup, Does.Not.Contain("сбой котировок"));
		});
	}

	[TestMethod]
	[Description("Сбой марок в панели гасит только нереализованную часть, реализованная остаётся видимой")]
	public void TryIfMarksFailureDegradesUnrealizedOnlyInPanel()
	{
		// Arrange: итог недоступен из-за сбоя марок, реализованный агрегат посчитан.
		var metrics = new Mock<IJournalMetricsReadModel>();
		metrics
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateExplicitMetrics(null, 214.32m, null));
		_context.Services.AddSingleton(metrics.Object);

		var cut = RenderFrame(Screen<ConstructionsPage>());

		// Assert: итог помечен неполным с признаком сбоя, нереализованная часть —
		// прочерком, реализованная часть осталась числом без подмены.
		// Требование: сбой марок в панели гасит только нереализованную часть.
		// Traceability: openspec:ui/screens#scenario-frame-marks-failure-degrades-unrealized-only
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("итог по журналу: неполный (сбой котировок), реализов. +214.32, нереализов. —"));
		});
	}

	[TestMethod]
	[Description("До завершения первого чтения метрик итог панели остаётся многоточием")]
	public void TryIfPanelKeepsLoadingState()
	{
		// Arrange: чтение метрик ещё не завершено — панель показывает многоточие.
		var pending = new Mock<IJournalMetricsReadModel>();
		pending
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.Returns(new TaskCompletionSource<JournalMetrics>().Task);
		_context.Services.AddSingleton(pending.Object);

		var loading = RenderFrame(Screen<ConstructionsPage>());

		// Assert: до завершения первого чтения итог остаётся «…» — без частичных чисел.
		// Требование: состояние загрузки панели сохранено при появлении разбивки.
		// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
		Assert.That(loading.Markup, Does.Contain("итог по журналу: …"));
	}

	[TestMethod]
	[Description("Недоступное чтение метрик показывает явную недоступность итога панели")]
	public void TryIfPanelKeepsUnavailableState()
	{
		// Arrange: чтение метрик падает — сырьё повреждено или база недоступна.
		var failing = new Mock<IJournalMetricsReadModel>();
		failing
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("база недоступна"));
		_context.Services.AddSingleton(failing.Object);

		var unavailable = RenderFrame(Screen<ConstructionsPage>());

		// Assert: недоступность панели показывается словами, разбивки нет.
		// Требование: состояние недоступности панели сохранено при появлении разбивки.
		// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
		unavailable.WaitForAssertion(() =>
			Assert.That(unavailable.Markup, Does.Contain("итог по журналу: недоступен")));
	}

	[TestMethod]
	[Description("Статические вкладки каркаса ведут на экраны «Конструкции», «Входящие», «Настройки»")]
	public void TryIfStaticTabsOfferNavigationBetweenScreens()
	{
		_context.Services.AddSingleton(new Mock<IJournalMetricsReadModel>().Object);

		var cut = RenderFrame(Screen<ConstructionsPage>());

		// Assert: четыре статические вкладки с адресами корня, «Входящих»,
		// «Подсказок» и «Настроек».
		// Требование: навигация состоит из статических вкладок экранов каркаса.
		// Traceability: openspec:ui/screens#requirement-app-frame-navigation
		var tabs = cut.FindAll("nav.tabs a");
		Assert.That(tabs, Has.Count.EqualTo(4));
		Assert.That(tabs[0].GetAttribute("href"), Is.EqualTo("/"));
		Assert.That(tabs[0].TextContent, Does.Contain("Конструкции"));
		Assert.That(tabs[1].GetAttribute("href"), Is.EqualTo("/inbox"));
		Assert.That(tabs[1].TextContent, Does.Contain("Входящие"));
		Assert.That(tabs[2].GetAttribute("href"), Is.EqualTo("/hints"));
		Assert.That(tabs[2].TextContent, Does.Contain("Подсказки"));
		Assert.That(tabs[3].GetAttribute("href"), Is.EqualTo("/settings"));
		Assert.That(tabs[3].TextContent, Does.Contain("Настройки"));
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
		Assert.That(tabs[3].ClassList, Does.Not.Contain("active"));

		// Act: навигация на «Конструкции» подсвечивает первую вкладку.
		navigation.NavigateTo("/");
		var constructions = RenderFrame(Screen<ConstructionsPage>());

		tabs = constructions.FindAll("nav.tabs a");
		Assert.That(tabs[0].ClassList, Does.Contain("active"));
		Assert.That(tabs[1].ClassList, Does.Not.Contain("active"));
		Assert.That(tabs[2].ClassList, Does.Not.Contain("active"));
		Assert.That(tabs[3].ClassList, Does.Not.Contain("active"));
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
		// Слагаемые согласованы с итогом: проверки одного итога не завязаны на разбивку.
		RealizedPnL = totalPnL ?? 0m,
		UnrealizedPnL = totalPnL is null ? null : 0m,
		MarksAsOf = null,
		HasMarkFailure = totalPnL is null,
	};

	/// <summary>Метрики журнала с явной разбивкой: панельные проверки задают слагаемые независимо от итога.</summary>
	private static JournalMetrics CreateExplicitMetrics(decimal? totalPnL, decimal realizedPnL, decimal? unrealizedPnL) => new()
	{
		Constructions = [],
		Positions = [],
		TotalPnL = totalPnL,
		RealizedPnL = realizedPnL,
		UnrealizedPnL = unrealizedPnL,
		MarksAsOf = null,
		HasMarkFailure = totalPnL is null,
	};

	/// <summary>Заглушка без настроенных переменных окружения: ключ и секрет недоступны.</summary>
	private sealed class UnconfiguredCredentials : IBybitCredentialsProvider
	{
		public BybitCredentials GetCredentials() =>
			throw new InvalidOperationException("Переменные окружения ключа не заданы.");
	}

	#endregion
}
