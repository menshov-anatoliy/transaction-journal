using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Domain.Bybit;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Components.Pages;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using SyncPage = TransactionJournal.Components.Pages.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки минимальной страницы синхронизации: кнопка «Синхронизировать» запускает
/// единственную ручную команду, а по завершении синка страница показывает статус,
/// счётчики новых записей и предупреждения сверки с биржей. Полный экран синка —
/// отдельная capability UI и здесь не проверяется.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// </summary>
[TestClass]
public class SyncPageTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IExecutionSyncStateStore> _stateStore = null!;
	private Mock<ISyncJournalReadModel> _syncJournal = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();
		_stateStore = new Mock<IExecutionSyncStateStore>();
		_context.Services.AddSingleton(_stateStore.Object);
		// Страница требует и сервис синка: тестам сброса достаточно свободной заглушки.
		_context.Services.AddSingleton(new Mock<IJournalSyncService>().Object);
		// Журнал запусков читается при загрузке страницы: по умолчанию заглушка
		// отвечает отсутствием завершённых запусков — блок результата не показывается.
		_syncJournal = new Mock<ISyncJournalReadModel>();
		_syncJournal
			.Setup(journal => journal.ReadLastCompletedAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((SyncRunRow?)null);
		_context.Services.AddSingleton(_syncJournal.Object);
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Команда сброса состояния требует подтверждения и вызывает сброс выбранной категории")]
	public void TryIfResetCommandRequiresConfirmationAndResetsSelectedCategory()
	{
		// Arrange: блок обслуживания предлагает сброс по каждой категории.
		// Требование: сброс состояния — отдельная обслуживающая команда с подтверждением
		// пользователя; без подтверждения хранилище не вызывается.
		// Traceability: openspec:sync/bybit-history#requirement-manual-category-state-reset
		var cut = _context.RenderComponent<SyncPage>();

		// Act: выбор категории option — только показ запроса подтверждения.
		FindButton(cut, "Сбросить состояние option").Click();

		// Assert: хранилище ещё не вызывалось, на экране запрос подтверждения.
		Assert.That(_stateStore.Invocations, Is.Empty);
		Assert.That(cut.Markup, Does.Contain("Сбросить состояние синхронизации категории option?"));

		// Act: отмена возвращает блок к выбору категории без сброса.
		FindButton(cut, "Отмена").Click();

		// Assert: подтверждение скрыто, хранилище по-прежнему не вызывалось.
		Assert.That(cut.Markup, Does.Not.Contain("Подтвердить сброс"));
		Assert.That(_stateStore.Invocations, Is.Empty);

		// Act: повторный выбор option и явное подтверждение выполняют сброс.
		FindButton(cut, "Сбросить состояние option").Click();
		FindButton(cut, "Подтвердить сброс").Click();
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("категории option сброшено")));

		// Assert: сброс вызван ровно один раз и только для выбранной категории.
		_stateStore.Verify(store => store.ResetAsync("option", It.IsAny<CancellationToken>()), Times.Once);
		_stateStore.Verify(store => store.ResetAsync("linear", It.IsAny<CancellationToken>()), Times.Never);
	}

	[TestMethod]
	[Description("Команды сброса состояния заблокированы, пока выполняется синхронизация")]
	public void TryIfResetCommandsAreLockedWhileSyncIsRunning()
	{
		// Arrange: сервис синхронизации держит задачу незавершённой — синк «выполняется».
		// Требование: сброс состояния категории на время синка блокируется — сброс
		// в момент прохода исключён на уровне экрана.
		// Traceability: openspec:sync/bybit-history#requirement-manual-category-state-reset
		var service = new Mock<IJournalSyncService>();
		var syncGate = new TaskCompletionSource<JournalSyncResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.Returns(syncGate.Task);
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act: запуск синка.
		FindButton(cut, "Синхронизировать").Click();

		// Assert: кнопки сброса получили атрибут disabled на время синка.
		cut.WaitForAssertion(() =>
		{
			Assert.That(FindButton(cut, "Сбросить состояние option").HasAttribute("disabled"), Is.True);
			Assert.That(FindButton(cut, "Сбросить состояние linear").HasAttribute("disabled"), Is.True);
		});

		// Act: синк завершается — блок обслуживания разблокируется.
		syncGate.SetResult(CreateCompletedResult());
		cut.WaitForAssertion(() =>
		{
			Assert.That(FindButton(cut, "Сбросить состояние option").HasAttribute("disabled"), Is.False);
			Assert.That(FindButton(cut, "Сбросить состояние linear").HasAttribute("disabled"), Is.False);
		});
	}

	[TestMethod]
	[Description("Реакция страницы на завершение синка: статус, режим, счётчики новых записей и предупреждения сверки")]
	public void TryIfCompletedSyncRendersStatusCountersAndWarnings()
	{
		// Arrange: сервис синхронизации возвращает завершённый успешный запуск —
		// backfill с двумя записями исполнения, одной delivery-записью, новым инструментом
		// и одним предупреждением сверки экспирации.
		var service = new Mock<IJournalSyncService>();
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateCompletedResult());
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Assert (исходное состояние): страница предлагает единственную ручную команду.
		Assert.That(cut.Find("button").TextContent, Does.Contain("Синхронизировать"));

		// Act: нажатие «Синхронизировать» запускает синк.
		cut.Find("button").Click();

		// Assert: реакция на завершение — статус, режим, счётчики новых записей
		// и предупреждение сверки с символом и расхождением.
		// Требование: единственная ручная команда, по завершении — индикация результата.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		// Traceability: openspec:sync/bybit-history#scenario-delivery-reconciliation-warning
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация завершена"));
			Assert.That(cut.Markup, Does.Contain("первичная загрузка (backfill)"));
			Assert.That(cut.Markup, Does.Contain("Новых записей исполнения: 2"));
			Assert.That(cut.Markup, Does.Contain("Новых delivery-записей: 1"));
			Assert.That(cut.Markup, Does.Contain("Новых инструментов в справочнике: 1"));
			Assert.That(cut.Markup, Does.Contain("BTC-29DEC23-45000-C"));
			Assert.That(cut.Markup, Does.Contain("расхождение -0.07"));
		});
	}

	[TestMethod]
	[Description("Завершённый запуск с пропущенными областями показывает заметку с перечнем и не помечается ошибкой")]
	public void TryIfSkippedAreasNoteShownWhenRunSkippedUnavailableAreas()
	{
		// Arrange: запуск завершён успешно, но биржа отказала в истории двух областей:
		// option-области исполнения (категория плюс базовый актив) и безфильтровой
		// delivery-области (только категория).
		// Требование: перечень пропущенных областей виден пользователю рядом с
		// результатом запуска, при этом запуск не отображается как ошибка.
		var service = new Mock<IJournalSyncService>();
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateResultWithSkippedAreas());
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act: нажатие «Синхронизировать» запускает синк.
		cut.Find("button").Click();

		// Assert: заметка с перечнем меток пропущенных областей видна, статус запуска
		// остаётся успешным — без блока прерывания.
		// Traceability: openspec:sync/bybit-history#scenario-skipped-areas-reported-to-user
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация завершена"));
			Assert.That(cut.Markup, Does.Not.Contain("Синхронизация прервана"));
			Assert.That(cut.Find("p[role='note']").TextContent, Does.Contain("Пропущенные области"));
			Assert.That(cut.Find("p[role='note']").TextContent, Does.Contain("контракт недоступен"));
			Assert.That(cut.Find("p[role='note']").TextContent, Does.Contain("option:BTC"));
			Assert.That(cut.Find("p[role='note']").TextContent, Does.Contain("linear"));
		});
	}

	[TestMethod]
	[Description("Завершённый запуск без пропусков областей не показывает заметку о пропусках")]
	public void TryIfNoSkippedAreasNoteWhenNothingWasSkipped()
	{
		// Arrange: обычный успешный запуск — все области загружены, пропусков нет.
		var service = new Mock<IJournalSyncService>();
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateCompletedResult());
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act: нажатие «Синхронизировать» запускает синк.
		cut.Find("button").Click();

		// Assert: результат показан, но заметки о пропущенных областях нет —
		// пустой перечень не отображается.
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Синхронизация завершена")));
		Assert.That(cut.Markup, Does.Not.Contain("Пропущенные области"));
		Assert.That(cut.Markup, Does.Not.Contain("Синхронизация прервана"));
	}

	[TestMethod]
	[Description("Завершённый запуск с неразрешёнными инструментами показывает заметку с перечнем")]
	public void TryIfUnresolvedInstrumentsNoteShownWhenListIsNotEmpty()
	{
		// Arrange: запуск завершён успешно, но спецификации двух инструментов не
		// получены — одна отвергнута биржей в пополнении справочника, другая осталась
		// неразрешённой в перестроенной проекции.
		// Требование: перечень неразрешённых символов виден пользователю рядом
		// с результатом запуска, при этом запуск не отображается как ошибка.
		// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbols-reported-to-user
		var service = new Mock<IJournalSyncService>();
		var result = CreateCompletedResult() with
		{
			UnresolvedInstruments = ["ETH-29DEC23-2000-C"],
			Projection = CreateCompletedResult().Projection! with
			{
				UnresolvedInstruments = ["XAUT-30OCT26-4400-C"],
			},
		};
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(result);
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act: нажатие «Синхронизировать» запускает синк.
		cut.Find("button").Click();

		// Assert: заметка с объединённым перечнем видна, статус запуска остаётся
		// успешным — без блока прерывания.
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация завершена"));
			Assert.That(cut.Markup, Does.Not.Contain("Синхронизация прервана"));
			var unresolvedNote = cut.Find("p[role='note']");
			Assert.That(unresolvedNote.TextContent, Does.Contain("Неразрешённые инструменты"));
			Assert.That(unresolvedNote.TextContent, Does.Contain("ETH-29DEC23-2000-C"));
			Assert.That(unresolvedNote.TextContent, Does.Contain("XAUT-30OCT26-4400-C"));
		});
	}

	[TestMethod]
	[Description("Завершённый запуск с пустым перечнем неразрешённых инструментов не показывает заметку")]
	public void TryIfNoUnresolvedInstrumentsNoteWhenListIsEmpty()
	{
		// Arrange: обычный успешный запуск — все спецификации получены, отложенных
		// символов нет ни в пополнении справочника, ни в проекции.
		var service = new Mock<IJournalSyncService>();
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateCompletedResult());
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act: нажатие «Синхронизировать» запускает синк.
		cut.Find("button").Click();

		// Assert: результат показан, заметки о неразрешённых инструментах нет.
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Синхронизация завершена")));
		Assert.That(cut.Markup, Does.Not.Contain("Неразрешённые инструменты"));
	}

	[TestMethod]
	[Description("Завершённый запуск с непокрытыми активами доски показывает заметку с подсказкой лечения")]
	public void TryIfUncoveredBaseCoinsNoteShownWithTreatmentHintWhenListIsNotEmpty()
	{
		// Arrange: запуск завершён успешно, но записи ETH-опционов есть в журнале,
		// а область ETH не пройдена — актив непокрыт.
		// Требование: перечень непокрытых активов виден рядом с результатом запуска
		// с подсказкой о конфигурации дополнительных активов доски и сбросе состояния
		// категории.
		// Traceability: openspec:sync/bybit-history#scenario-uncovered-base-coin-reported
		var service = new Mock<IJournalSyncService>();
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateCompletedResult() with { UncoveredBaseCoins = ["ETH"] });
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act: нажатие «Синхронизировать» запускает синк.
		cut.Find("button").Click();

		// Assert: заметка с перечнем и подсказкой лечения видна, статус запуска
		// остаётся успешным — без блока прерывания.
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация завершена"));
			Assert.That(cut.Markup, Does.Not.Contain("Синхронизация прервана"));
			var uncoveredNote = cut.Find("p[role='note']");
			Assert.That(uncoveredNote.TextContent, Does.Contain("Непокрытые базовые активы"));
			Assert.That(uncoveredNote.TextContent, Does.Contain("ETH"));
			Assert.That(uncoveredNote.TextContent, Does.Contain("Sync:ExtraOptionBaseCoins"));
			Assert.That(uncoveredNote.TextContent, Does.Contain("сбросьте состояние категории"));
		});
	}

	[TestMethod]
	[Description("Завершённый запуск без непокрытых активов не показывает заметку о покрытии доски")]
	public void TryIfNoUncoveredBaseCoinsNoteWhenListIsEmpty()
	{
		// Arrange: обычный успешный запуск — все активы записей option покрыты
		// пройденными областями доски.
		var service = new Mock<IJournalSyncService>();
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateCompletedResult());
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act: нажатие «Синхронизировать» запускает синк.
		cut.Find("button").Click();

		// Assert: результат показан, заметки о непокрытых активах нет.
		// Traceability: openspec:sync/bybit-history#scenario-covered-board-no-warning
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Синхронизация завершена")));
		Assert.That(cut.Markup, Does.Not.Contain("Непокрытые базовые активы"));
	}

	[TestMethod]
	[Description("Ошибка синка показывается на странице, кнопка остаётся доступной для повторного запуска")]
	public void TryIfFailedSyncShowsErrorAndKeepsButtonForRetry()
	{
		// Arrange: биржа отвечает ошибкой лимитов после всех повторов — синк прерван.
		var service = new Mock<IJournalSyncService>();
		service
			.Setup(svc => svc.SyncAsync(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new BybitApiException(10006, "превышение частоты запросов"));
		_context.Services.AddSingleton(service.Object);

		var cut = _context.RenderComponent<SyncPage>();

		// Act
		cut.Find("button").Click();

		// Assert: страница показывает прерывание с текстом ошибки биржи; кнопка
		// возвращается — прерванный запуск продолжается повторным нажатием без дублей.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация прервана"));
			Assert.That(cut.Markup, Does.Contain("retCode=10006"));
			Assert.That(cut.Find("button").TextContent, Does.Contain("Синхронизировать"));
		});
	}

	[TestMethod]
	[Description("Предупреждения последнего завершённого запуска видны при загрузке страницы без нажатия кнопки")]
	public void TryIfLastRunWarningsShownOnPageLoad()
	{
		// Arrange: последний завершённый запуск успешен и несёт три перечня предупреждений.
		// Требование: предупреждения сохраняются с записью запуска и показываются
		// при открытии страницы в том же виде, что сразу после завершения запуска.
		// Traceability: openspec:sync/bybit-history#requirement-run-warnings-persisted
		// Traceability: openspec:sync/bybit-history#scenario-run-warnings-shown-on-page-load
		_syncJournal
			.Setup(journal => journal.ReadLastCompletedAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(PersistedRunWithWarnings());

		// Act: страница открывается без запуска синхронизации.
		var cut = _context.RenderComponent<SyncPage>();

		// Assert: блок последнего запуска со статусом, режимом, счётчиками и всеми
		// тремя заметками виден без нажатия кнопки «Синхронизировать».
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация завершена"));
			Assert.That(cut.Markup, Does.Contain("инкрементальная догрузка"));
			Assert.That(cut.Markup, Does.Contain("Новых записей исполнения: 3"));
			Assert.That(cut.FindAll("p[role='note']"), Has.Count.EqualTo(3));
			Assert.That(cut.Markup, Does.Contain("option:BTC"));
			Assert.That(cut.Markup, Does.Contain("XAUT-30OCT26-4400-C"));
			Assert.That(cut.Markup, Does.Contain("ETH"));
		});

		// Сервис синхронизации кнопкой не запускался.
		Assert.That(cut.FindAll("button").Single(button => button.TextContent.Contains("Синхронизировать")), Is.Not.Null);
	}

	[TestMethod]
	[Description("Чистый последний запуск не показывает предупреждающих заметок при загрузке страницы")]
	public void TryIfCleanRunShowsNoPersistedWarnings()
	{
		// Arrange: последний завершённый запуск успешен, его перечни предупреждений пусты.
		// Требование: предупреждения показываются только для запусков, у которых они
		// есть; чистый запуск заметок не отображает.
		// Traceability: openspec:sync/bybit-history#scenario-clean-run-no-persisted-warnings
		_syncJournal
			.Setup(journal => journal.ReadLastCompletedAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(PersistedCleanRun());

		// Act: страница открывается без запуска синхронизации.
		var cut = _context.RenderComponent<SyncPage>();

		// Assert: блок результата виден, предупреждающих заметок нет.
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Синхронизация завершена"));
			Assert.That(cut.FindAll("p[role='note']"), Is.Empty);
		});
		Assert.That(cut.Markup, Does.Not.Contain("Пропущенные области"));
		Assert.That(cut.Markup, Does.Not.Contain("Неразрешённые инструменты"));
		Assert.That(cut.Markup, Does.Not.Contain("Непокрытые базовые активы"));
	}

	#region Помощники

	/// <summary>Кнопка по подстроке текста: на странице несколько команд с кнопками.</summary>
	private static IElement FindButton(IRenderedComponent<SyncPage> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Contains(text));

	/// <summary>
	/// Последний завершённый успешный запуск с тремя непустыми перечнями предупреждений,
	/// сериализованными в колонку записи запуска.
	/// </summary>
	private static SyncRunRow PersistedRunWithWarnings() => new(
		7,
		new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
		new DateTimeOffset(2026, 1, 2, 0, 3, 0, TimeSpan.Zero),
		SyncRunMode.Incremental,
		SyncRunStatus.Succeeded,
		Error: null,
		NewExecutions: 3,
		NewDeliveries: 1,
		NewInstruments: 0,
		WarningsJson: new SyncRunWarnings
		{
			SkippedAreas = ["option:BTC"],
			UnresolvedInstruments = ["XAUT-30OCT26-4400-C"],
			UncoveredBaseCoins = ["ETH"],
		}.ToJson());

	/// <summary>Последний завершённый успешный запуск без предупреждений.</summary>
	private static SyncRunRow PersistedCleanRun() => new(
		8,
		new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
		new DateTimeOffset(2026, 1, 3, 0, 1, 0, TimeSpan.Zero),
		SyncRunMode.Incremental,
		SyncRunStatus.Succeeded,
		Error: null,
		NewExecutions: 0,
		NewDeliveries: 0,
		NewInstruments: 0,
		WarningsJson: new SyncRunWarnings().ToJson());

	/// <summary>Завершённый успешный запуск: счётчики новых записей и одно предупреждение сверки.</summary>
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
		Executions = new Dictionary<string, ExecutionCategorySyncResult>(StringComparer.Ordinal)
		{
			["linear"] = new ExecutionCategorySyncResult
			{
				Mode = SyncRunMode.Backfill,
				NewExecutions = [new BybitExecution { Symbol = "BTCUSDT", ExecId = "exec-1", Side = "Buy", ExecTimeMs = 0 }],
				WindowsProcessed = 2,
				HistoryExhausted = true,
				EarlyStopped = false,
				ExecWatermarkMs = 1,
				NewExecutionsPersisted = 2,
			},
		},
		Deliveries = new Dictionary<string, DeliveryCategorySyncResult>(StringComparer.Ordinal)
		{
			["option"] = new DeliveryCategorySyncResult
			{
				Mode = SyncRunMode.Backfill,
				NewDeliveries = [new BybitDeliveryRecord { Symbol = "BTC-29DEC23-45000-C", DeliveryTimeMs = 1 }],
				WindowsProcessed = 1,
				HistoryExhausted = true,
				DeliveryWatermarkMs = 1,
				NewDeliveriesPersisted = 1,
			},
		},
		Projection = new JournalMaterializationResult
		{
			InboxTrades = [],
			ExpiryClosingEntries = [],
			ReconciliationWarnings =
			[
				new ExpiryReconciliationWarning
				{
					Symbol = "BTC-29DEC23-45000-C",
					DeliveryTime = new DateTimeOffset(2023, 12, 29, 8, 0, 0, TimeSpan.Zero),
					DeliveryRpl = 0.2m,
					OwnResult = 0.27m,
					Difference = -0.07m,
					SourceKey = "BTC-29DEC23-45000-C|1703846400000",
				},
			],
		},
	};

	/// <summary>
	/// Завершённый успешный запуск с пропуском двух областей из-за отказа биржи
	/// «контракт недоступен для торговли»: option-область исполнения и безфильтровая
	/// delivery-область. Счётчики новых записей нулевые — заметка о пропуске должна
	/// быть видна и без новых записей.
	/// </summary>
	private static JournalSyncResult CreateResultWithSkippedAreas() => new()
	{
		Mode = SyncRunMode.Incremental,
		Run = new SyncRun
		{
			StartedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
			FinishedAt = new DateTimeOffset(2026, 1, 2, 0, 1, 0, TimeSpan.Zero),
			Mode = SyncRunMode.Incremental,
			Status = SyncRunStatus.Succeeded,
			NewExecutions = 0,
			NewDeliveries = 0,
			NewInstruments = 0,
		},
		Executions = new Dictionary<string, ExecutionCategorySyncResult>(StringComparer.Ordinal)
		{
			["linear"] = new ExecutionCategorySyncResult
			{
				Mode = SyncRunMode.Incremental,
				NewExecutions = [],
				WindowsProcessed = 1,
				HistoryExhausted = false,
				EarlyStopped = true,
				ExecWatermarkMs = 2,
				NewExecutionsPersisted = 0,
				SkippedAreas = ["option:BTC"],
			},
		},
		Deliveries = new Dictionary<string, DeliveryCategorySyncResult>(StringComparer.Ordinal)
		{
			["linear"] = new DeliveryCategorySyncResult
			{
				Mode = SyncRunMode.Incremental,
				NewDeliveries = [],
				WindowsProcessed = 0,
				HistoryExhausted = false,
				DeliveryWatermarkMs = 2,
				NewDeliveriesPersisted = 0,
				SkippedAreas = ["linear"],
			},
		},
		Projection = new JournalMaterializationResult
		{
			InboxTrades = [],
			ExpiryClosingEntries = [],
			ReconciliationWarnings = [],
		},
	};

	#endregion
}
