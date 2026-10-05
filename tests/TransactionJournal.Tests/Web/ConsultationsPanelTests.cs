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
/// сохранения планов.
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
		_context.Services.AddSingleton(CreateChatService());
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
		var messages = cut.FindAll(".consultation-message");
		Assert.That(messages, Has.Count.EqualTo(2));
		Assert.That(messages[0].QuerySelector(".consultation-message-role")!.TextContent, Is.EqualTo("Вы"));
		Assert.That(messages[0].QuerySelector(".consultation-message-text")!.TextContent, Is.EqualTo("Что с маркой BTC?"));
		Assert.That(messages[1].QuerySelector(".consultation-message-role")!.TextContent, Is.EqualTo("Помощник"));
		Assert.That(messages[1].QuerySelector(".consultation-message-text")!.TextContent, Is.EqualTo("Ответ помощника."));
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
	private ConsultationChatService CreateChatService() => new(
		_store,
		new StubContextReader(),
		new ConsultationAgent(
			new FakeChatClient(),
			new ConsultationTools(new Mock<IRuleCorpusReader>(MockBehavior.Loose).Object, new Mock<IConsultationMarketReader>(MockBehavior.Loose).Object),
			new ConsultationInstructions(Path.Combine(Path.GetTempPath(), "no-such-consultation-prompt.md"))),
		new FixedTimeProvider(FixedNow));

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
	/// Подмена клиента модели: на каждый запрос отдаёт один текстовый кадр
	/// ответа — панель проверяется на переписке, а не на механике стрима.
	/// </summary>
	private sealed class FakeChatClient : IChatClient
	{
		/// <summary>Запросы, полученные клиентом, в порядке поступления.</summary>
		public List<List<ChatMessage>> Requests { get; } = [];

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Requests.Add([.. messages]);
			await Task.Yield();
			yield return new ChatResponseUpdate(ChatRole.Assistant, "Ответ помощника.");
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
