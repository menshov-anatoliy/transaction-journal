using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;
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

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();
		_stateStore = new Mock<IExecutionSyncStateStore>();
		_context.Services.AddSingleton(_stateStore.Object);
		// Страница требует и сервис синка: тестам сброса достаточно свободной заглушки.
		_context.Services.AddSingleton(new Mock<IJournalSyncService>().Object);
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

	#region Помощники

	/// <summary>Кнопка по подстроке текста: на странице несколько команд с кнопками.</summary>
	private static IElement FindButton(IRenderedComponent<SyncPage> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Contains(text));

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

	#endregion
}
