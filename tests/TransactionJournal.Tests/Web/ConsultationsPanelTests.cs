namespace TransactionJournal.Tests.Web;

using System.Runtime.CompilerServices;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Components;
using TransactionJournal.Consultations;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Infrastructure.Consultations;
using TransactionJournal.Tests;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки панели «Консультации» в деталях конструкции: список диалогов,
/// новый диалог создаётся первым сообщением, удаление диалога целиком с
/// подтверждением, чат read-only — панель не предлагает записей в журнал и
/// сохранения планов; стриминг ответа с троттлингом перерисовок, статусные
/// строки tool-вызовов, отмена генерации и режим пост-мортема закрытой
/// конструкции.
/// Traceability: openspec:ui/screens#requirement-ui-consultation-panel
/// </summary>
[TestClass]
public sealed class ConsultationsPanelTests
{
	/// <summary>Фиксированный момент as-of сообщений в проверках.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private Bunit.TestContext _context = null!;

	private ConsultationStore _store = null!;

	private string _directory = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой папкой per-construction баз.
		_directory = Path.Combine(Path.GetTempPath(), $"consultations-panel-tests-{Guid.NewGuid():N}");
		_store = new ConsultationStore(_directory);
		_context = new Bunit.TestContext();
		_context.Services.AddSingleton<IConsultationStore>(_store);
		_context.Services.AddSingleton(CreateChatService(new FakeChatClient()));
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
		// Пул соединений SQLite держит файлы баз открытыми — сбрасываем перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[TestMethod]
	[Description("Первое сообщение создаёт диалог, он появляется в списке вместе с ответом помощника")]
	// Новый диалог начинается первым сообщением: отдельной команды «создать
	// диалог» в панели нет — отправка без выбранного диалога открывает новый.
	// Traceability: openspec:ui/screens#scenario-ui-dialogue-started-by-message
	public async Task TryIfFirstMessageSent_DialogueAppearsInListWithAnswer()
	{
		// Arrange: пустая панель без диалогов, вопрос набирается во вводе.
		var cut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));
		cut.Find("textarea").Input("Что с маркой BTC?");

		// Act: владелец отправляет первое сообщение панели.
		cut.Find(".consultation-send").Click();

		// Assert: диалог создан и показан в списке, переписка содержит вопрос
		// и дочитанный до конца ответ помощника.
		cut.WaitForState(() => cut.FindAll(".consultation-dialogue-item").Count == 1, TimeSpan.FromSeconds(10));
		// Диалог появляется в списке раньше фиксации ответа: рендер после
		// сохранения вопроса приходит до сохранения ответа помощника, поэтому
		// переписку дожидаемся отдельным состоянием.
		cut.WaitForState(() => cut.FindAll(".consultation-message").Count == 2, TimeSpan.FromSeconds(10));
		var messages = cut.FindAll(".consultation-message");
		Assert.That(messages, Has.Count.EqualTo(2));
		Assert.That(messages[0].QuerySelector(".consultation-message-role")!.TextContent, Is.EqualTo("Вы"));
		Assert.That(messages[0].QuerySelector(".consultation-message-text")!.TextContent, Is.EqualTo("Что с маркой BTC?"));
		Assert.That(messages[1].QuerySelector(".consultation-message-role")!.TextContent, Is.EqualTo("Помощник"));
		Assert.That(messages[1].QuerySelector(".consultation-message-text")!.TextContent, Is.EqualTo("Ответ помощника."));
	}

	[TestMethod]
	[Description("Ответ стримится по сегментам, между ними статусные строки тулов, фрагменты не рендерятся")]
	// Стриминг с рыночными инструментами: текстовые чанки показываются
	// постепенно, вызов тула виден статусной строкой с аргументами, сам
	// tool-call фрагмент модели текстом ответа не рендерится.
	// Traceability: openspec:ui/screens#scenario-ui-streaming-with-tool-status
	public async Task TryIfStreamHasToolCalls_StatusLinesShownAndFragmentsNotRendered()
	{
		// Arrange: клиент отдаёт сцену с текстом и вызовом тула, вторая сцена
		// удерживается до ручного выпуска — генерация не завершена.
		var releaseScene = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var chat = new FakeChatClient
		{
			Script =
			[
				[
					() => new ChatResponseUpdate(ChatRole.Assistant, "Марка BTC"),
					() => ToolCallFrame("call-1", ConsultationTools.GetMarketSnapshotToolName, "BTC"),
				],
				[
					() => new ChatResponseUpdate(ChatRole.Assistant, " 108975.4 USDT."),
				],
			],
			SceneGate = (sceneIndex, _) => sceneIndex == 1 ? releaseScene.Task : Task.CompletedTask,
		};
		_context.Services.AddSingleton(CreateChatService(chat));
		var cut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));
		cut.Find("textarea").Input("Что с маркой BTC?");

		// Act: владелец отправляет вопрос, модель стримит ответ с инструментом.
		cut.Find(".consultation-send").Click();

		// Assert: во время генерации стримящийся ответ показывает текстовый
		// сегмент и статусную строку вызова тула с аргументами.
		cut.WaitForState(() => cut.FindAll(".consultation-tool-status").Count == 1, TimeSpan.FromSeconds(10));
		var streaming = cut.Find(".consultation-message-streaming");
		Assert.That(streaming.QuerySelectorAll(".consultation-message-text").Single().TextContent, Is.EqualTo("Марка BTC"));
		var status = cut.Find(".consultation-tool-status");
		Assert.That(status.TextContent, Does.Contain("get_market_snapshot"));
		Assert.That(status.TextContent, Does.Contain("baseCoin=BTC"));

		// Assert: tool-call фрагмент текстом не рендерится — ни идентификатор
		// вызова, ни имя тула не попадают в текстовые сегменты ответа.
		var texts = cut.FindAll(".consultation-message-text").Select(text => text.TextContent).ToList();
		Assert.That(texts.Any(text => text.Contains("call-1") || text.Contains("get_market_snapshot")), Is.False);

		// Act: выпуск второй сцены — стрим завершается, ответ фиксируется.
		releaseScene.TrySetResult();
		cut.WaitForState(() => cut.FindAll(".consultation-message-streaming").Count == 0, TimeSpan.FromSeconds(10));

		// Assert: зафиксированный ответ склеен из сегментов, статусные строки
		// стрима исчезли вместе с представлением генерации.
		var answer = cut.FindAll(".consultation-message-text").Last();
		Assert.That(answer.TextContent, Is.EqualTo("Марка BTC 108975.4 USDT."));
		Assert.That(cut.FindAll(".consultation-tool-status"), Is.Empty);
		var dialogue = (await _store.ListDialoguesAsync(7)).Single();
		Assert.That(await _store.ListMessagesAsync(7, dialogue.Id), Has.Count.EqualTo(2));
	}

	[TestMethod]
	[Description("Отмена во время генерации останавливает стрим и возвращает панель в состояние ввода")]
	// Отмена кнопкой: вопрос остаётся в диалоге, ответ ассистента не
	// фиксируется, отмена не показывается как ошибка.
	// Traceability: openspec:ui/screens#scenario-ui-cancel-generation
	public async Task TryIfCancelClickedDuringGeneration_StreamStopsAndInputReturns()
	{
		// Arrange: клиент удерживает стрим до отмены токеном генерации.
		var chat = new FakeChatClient
		{
			SceneGate = (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
		};
		_context.Services.AddSingleton(CreateChatService(chat));
		var cut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));
		cut.Find("textarea").Input("Почему сработало правило?");
		cut.Find(".consultation-send").Click();
		cut.WaitForState(() => cut.FindAll(".consultation-cancel").Count == 1, TimeSpan.FromSeconds(10));

		// Act: владелец нажимает отмену во время генерации.
		cut.Find(".consultation-cancel").Click();

		// Assert: панель вернулась в состояние ввода — ввод разблокирован,
		// кнопка отмены исчезла, вопрос остался единственным сообщением.
		cut.WaitForState(() => cut.FindAll(".consultation-cancel").Count == 0, TimeSpan.FromSeconds(10));
		Assert.That(cut.Find("textarea").HasAttribute("disabled"), Is.False);
		Assert.That(cut.FindAll(".consultation-message"), Has.Count.EqualTo(1));
		Assert.That(cut.FindAll(".consultation-message-streaming"), Is.Empty);
		Assert.That(cut.FindAll(".note-error"), Is.Empty);
		Assert.That(cut.Find(".consultation-cancel-notice").TextContent, Does.Contain("отменена"));
		var dialogue = (await _store.ListDialoguesAsync(7)).Single();
		var messages = await _store.ListMessagesAsync(7, dialogue.Id);
		Assert.That(messages, Has.Count.EqualTo(1));
		Assert.That(messages[0].Role, Is.EqualTo(ConsultationMessageRole.User));
	}

	[TestMethod]
	[Description("Закрытая конструкция показывает режим пост-мортема, открытая — нет")]
	// Панель закрытой конструкции ведёт консультации в режиме работы над
	// ошибками: режим задан снимком контекста и инструкциями агента, панель
	// его визуализирует; у открытой конструкции пост-мортем не показывается.
	// Traceability: openspec:ui/screens#scenario-ui-closed-construction-postmortem
	public void TryIfConstructionClosed_PanelShowsPostmortemMode()
	{
		// Arrange: панель закрытой конструкции.
		var closedCut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters
			.Add(panel => panel.ConstructionId, 7)
			.Add(panel => panel.IsConstructionClosed, true));

		// Assert: режим пост-мортема виден владельцу.
		var note = closedCut.Find(".consultation-postmortem-note");
		Assert.That(note.TextContent, Does.Contain("пост-мортем"));
		// Заметка пост-мортема объясняет, что разбор ведётся по финальному состоянию журнала.
		Assert.That(note.TextContent, Does.Contain("работы над ошибками"));

		// Arrange: та же панель у открытой конструкции.
		var openCut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters
			.Add(panel => panel.ConstructionId, 8)
			.Add(panel => panel.IsConstructionClosed, false));

		// Assert: без признака закрытой конструкции пост-мортем не показывается.
		Assert.That(openCut.FindAll(".consultation-postmortem-note"), Is.Empty);
	}

	[TestMethod]
	[Description("Выбранный диалог показывает свою переписку: роли, тексты и as-of сообщений")]
	// Ассистенту и владельцу видна история только выбранного диалога —
	// соседние диалоги изолированы, панель читает переписку по одному диалогу.
	// Traceability: openspec:consultations/history#scenario-history-dialogues-isolated
	public async Task TryIfDialogueSelected_ThreadShowsRolesTextsAndAsOf()
	{
		// Arrange: в хранилище уже есть диалог с обменом вопрос-ответ.
		await SeedDialogueAsync(7, "Почему сработало правило?", "Правило покрыто карточкой rule-exit.");

		// Act: панель открывается, владелец выбирает диалог.
		var cut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));
		cut.Find(".consultation-dialogue-open").Click();

		// Assert: переписка выбранного диалога показана с ролями и as-of.
		var messages = cut.FindAll(".consultation-message");
		Assert.That(messages, Has.Count.EqualTo(2));
		Assert.That(messages[1].QuerySelector(".consultation-message-text")!.TextContent, Is.EqualTo("Правило покрыто карточкой rule-exit."));
		Assert.That(messages[0].QuerySelector(".consultation-message-asof")!.TextContent, Does.Contain("2030"));
	}

	[TestMethod]
	[Description("Удаление диалога требует подтверждения и стирает диалог со всеми сообщениями")]
	// Диалог удаляется целиком без корзины: подтверждение — отдельный шаг,
	// без него команда удаления не запускается.
	// Traceability: openspec:consultations/history#scenario-history-hard-delete-dialogue
	public async Task TryIfDialogueDeleted_ConfirmedHardDeleteRemovesThread()
	{
		// Arrange: панель с одним диалогом и выбранной перепиской.
		await SeedDialogueAsync(7, "Вопрос на удаление.", "Ответ на удаление.");
		var cut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));
		cut.Find(".consultation-dialogue-open").Click();

		// Act: владелец запрашивает удаление — сначала появляется подтверждение.
		cut.Find(".consultation-dialogue-delete").Click();

		// Assert: без подтверждения диалог ещё на месте, сообщение об удалении видно.
		Assert.That(cut.FindAll(".consultation-dialogue-item"), Has.Count.EqualTo(1));
		Assert.That(cut.Find(".consultation-dialogue-confirm .action-warning").TextContent, Does.Contain("Удалить диалог целиком?"));

		// Act: владелец подтверждает удаление.
		cut.Find(".consultation-dialogue-confirm .btn.danger").Click();

		// Assert: диалог и его переписка исчезли, список пуст.
		cut.WaitForState(() => cut.FindAll(".consultation-dialogue-item").Count == 0, TimeSpan.FromSeconds(10));
		Assert.That(cut.FindAll(".consultation-message"), Has.Count.EqualTo(0));
		Assert.That(await _store.ListDialoguesAsync(7), Is.Empty);
	}

	[TestMethod]
	[Description("Чат read-only: панель не предлагает записей в журнал и сохранения планов")]
	// План управления остаётся markdown-текстом ответа; из мутаций у панели
	// только управление собственными диалогами и ввод вопроса.
	// Traceability: openspec:ui/screens#scenario-ui-chat-read-only
	public async Task TryIfChatReadOnly_PanelOffersNoJournalWritesOrPlanSaving()
	{
		// Arrange: панель с диалогом, ответ помощника содержит план управления.
		await SeedDialogueAsync(7, "Какой план управления?", "План: выйти по тейку, стоп под маркой.");
		var cut = _context.RenderComponent<ConsultationsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));
		cut.Find(".consultation-dialogue-open").Click();

		// Assert: план показан текстом сообщения, а не формой сохранения.
		var planText = cut.FindAll(".consultation-message-text").Single(text => text.TextContent.Contains("План"));
		Assert.That(planText.TagName, Is.EqualTo("P"));

		// Assert: у панели нет форм и кнопок журнальных мутаций — только выбор
		// и удаление диалогов, ввод вопроса и начало нового диалога.
		Assert.That(cut.FindAll("form"), Is.Empty);
		var buttons = cut.FindAll("button").Select(button => button.TextContent.Trim()).ToList();
		Assert.That(buttons.Any(text => text.Contains("Сохранить") || text.Contains("журнал") || text.Contains("Применено")), Is.False);
		Assert.That(buttons.Count(text => text.StartsWith("Диалог")), Is.EqualTo(1));
		Assert.That(buttons, Does.Contain("удалить…"));
		Assert.That(buttons, Does.Contain("Отправить"));
		Assert.That(buttons, Does.Contain("новый вопрос"));
	}

	/// <summary>Создаёт диалог с обменом вопрос-ответ в хранилище проверки.</summary>
	private async Task<ConsultationMessage> SeedDialogueAsync(long constructionId, string question, string answer)
	{
		var user = await _store.AppendMessageAsync(constructionId, null, new ConsultationMessageDraft
		{
			Role = ConsultationMessageRole.User,
			Text = question,
			AsOf = FixedNow,
		});
		await _store.AppendMessageAsync(constructionId, user.DialogueId, new ConsultationMessageDraft
		{
			Role = ConsultationMessageRole.Assistant,
			Text = answer,
			AsOf = FixedNow,
		});
		return user;
	}

	/// <summary>Конвейер над настоящим хранилищем и подменённым клиентом модели.</summary>
	private ConsultationChatService CreateChatService(FakeChatClient chatClient) => new(
		_store,
		new StubContextReader(),
		new ConsultationAgent(
			chatClient,
			new ConsultationTools(new Mock<IRuleCorpusReader>(MockBehavior.Loose).Object, new Mock<IConsultationMarketReader>(MockBehavior.Loose).Object),
			new ConsultationInstructions(Path.Combine(Path.GetTempPath(), "no-such-consultation-prompt.md"))),
		new FixedTimeProvider(FixedNow));

	/// <summary>Кадр модели с вызовом рыночного инструмента.</summary>
	private static ChatResponseUpdate ToolCallFrame(string callId, string toolName, string baseCoin) => new()
	{
		Role = ChatRole.Assistant,
		Contents = { new FunctionCallContent(callId, toolName, new Dictionary<string, object?> { ["baseCoin"] = baseCoin }) },
	};

	/// <summary>Детерминированный читатель снимка контекста: markdown без обращения к журналу.</summary>
	private sealed class StubContextReader : IConsultationContextReader
	{
		public Task<ConsultationContextSnapshot> ReadAsync(long constructionId, CancellationToken cancellationToken = default) =>
			Task.FromResult(new ConsultationContextSnapshot
			{
				Markdown = "# Снимок конструкции",
				AsOf = FixedNow,
				IsConstructionClosed = false,
			});
	}

	/// <summary>
	/// Подмена клиента модели: сцены сценария — один ответ модели, кадры — его
	/// стрим-чанки; при исчерпании повторяется последняя сцена. Перед выдачей
	/// сцены выполняется задержка SceneGate (удержание стрима и отмена токеном).
	/// Кадры создаются фабриками — один и тот же экземпляр обновления нельзя
	/// скармливать дважды.
	/// </summary>
	private sealed class FakeChatClient : IChatClient
	{
		/// <summary>Сцены сценария; при исчерпании повторяется последняя сцена.</summary>
		public IReadOnlyList<IReadOnlyList<Func<ChatResponseUpdate>>> Script { get; set; } =
			[[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ помощника.")]];

		/// <summary>Задержка перед выдачей сцены: индекс сцены и токен отмены; по умолчанию — без задержки.</summary>
		public Func<int, CancellationToken, Task>? SceneGate { get; set; }

		/// <summary>Запросы, полученные клиентом, в порядке поступления.</summary>
		public List<List<ChatMessage>> Requests { get; } = [];

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Requests.Add([.. messages]);
			await Task.Yield();
			var sceneIndex = Math.Min(Requests.Count - 1, Script.Count - 1);
			if (SceneGate is not null)
			{
				await SceneGate(sceneIndex, cancellationToken);
			}

			foreach (var frame in Script[sceneIndex])
			{
				yield return frame();
			}
		}

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Агентный цикл обязан использовать стриминг.");

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose()
		{
		}
	}
}
