namespace TransactionJournal.Tests.Infrastructure.Chats;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Tests.Chats;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using Does = NUnit.Framework.Does;

/// <summary>
/// Адаптер единого SQLite-хранилища чатов: чат создаётся первым сообщением
/// с выбранными параметрами, живёт без привязки к конструкции, истории
/// соседних чатов изолированы, привязка и параметры неизменяемы после
/// создания, след источников ответа ИИ-помощника сохраняется в сообщении.
/// Жизненный цикл управляется только ручными действиями владельца:
/// завершение скрывает чат в завершённые, продолжение возвращает в активные,
/// удаление стирает чат с историей целиком без корзины.
/// </summary>
[TestClass]
public class ChatStoreTests : ChatDatabaseTests
{

	/// <summary>Параметры создания чата: модель по умолчанию, привязка и набор источников; без набора — все три категории справочника.</summary>
	private static ChatStartParameters Start(long? constructionId = 7, params ChatDataSource[] sources) => new()
	{
		Model = "glm-5.3",
		ConstructionId = constructionId,
		Sources = sources.Length == 0 ? ChatDataSourceCatalog.All : sources,
	};

	/// <summary>Черновик сообщения владельца с фиксированным as-of.</summary>
	private static ChatMessageDraft UserDraft(string text = "Первый вопрос.") => new()
	{
		Role = ChatMessageRole.User,
		Text = text,
		AsOf = FixedNow,
	};

	[TestMethod]
	[Description("Первое сообщение создаёт чат с выбранными параметрами: модель, привязка, источники, статус активен")]
	// Чат создаётся самим сообщением владельца: параметры фиксируются в момент
	// создания, статус нового чата — активен, сообщение становится первым в
	// его истории.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	public async Task TryIfFirstMessage_CreatesChatWithChosenParameters()
	{
		// Arrange
		var store = CreateStore();
		var start = Start(7, ChatDataSource.Journal, ChatDataSource.RulesCorpus);

		// Act: сообщение без идентификатора чата создаёт новый чат.
		var message = await store.AppendMessageAsync(null, start, UserDraft());

		// Assert: чат существует с выбранными параметрами и активным статусом.
		Assert.That(message.ChatId, Is.GreaterThan(0));
		var chat = await store.FindChatAsync(message.ChatId);
		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Model, Is.EqualTo("glm-5.3"));
		Assert.That(chat.ConstructionId, Is.EqualTo(7));
		Assert.That(chat.Sources, Is.EqualTo(new[] { ChatDataSource.Journal, ChatDataSource.RulesCorpus }));
		Assert.That(chat.Status, Is.EqualTo(ChatStatus.Active));
		Assert.That(chat.CreatedAt, Is.EqualTo(FixedNow));
		var messages = await store.ListMessagesAsync(message.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Первый вопрос." }));
	}

	[TestMethod]
	[Description("Чат без явного выбора источников получает все три категории справочника")]
	// Дефолт набора источников — закрытый справочник целиком: параметры
	// создания без явного выбора означают все три категории.
	// Traceability: openspec:chats/sources#scenario-sources-three-categories
	public async Task TryIfChatCreatedWithoutSourcesParameter_DefaultsToAllThreeSources()
	{
		// Arrange: параметры без явного набора источников.
		var store = CreateStore();
		var start = new ChatStartParameters
		{
			Model = "glm-5.3",
			ConstructionId = 7,
		};

		// Act
		var message = await store.AppendMessageAsync(null, start, UserDraft());

		// Assert: набор источников чата — полный справочник.
		var chat = await store.FindChatAsync(message.ChatId);
		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Sources, Is.EqualTo(ChatDataSourceCatalog.All));
	}

	[TestMethod]
	[Description("Пустой набор источников отвергается при создании чата")]
	// Подмножество справочника не бывает пустым: чат обязан выбрать хотя бы
	// одну категорию источников.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	public void ThrowOnNewChatWithEmptySources()
	{
		// Arrange: параметры с пустым набором источников.
		var store = CreateStore();
		var start = new ChatStartParameters
		{
			Model = "glm-5.3",
			ConstructionId = 7,
			Sources = [],
		};

		// Act + Assert
		Assert.ThrowsAsync<ArgumentException>(async () =>
			await store.AppendMessageAsync(null, start, UserDraft()));
	}

	[TestMethod]
	[Description("Чат без привязки к конструкции создаётся и ведёт историю наравне с привязанными")]
	// Привязка опциональна: чат без конструкции существует, принимает
	// продолжение истории и хранит пустую привязку.
	// Traceability: openspec:chats/history#scenario-chat-without-construction-allowed
	public async Task TryIfChatWithoutConstruction_CreatedAndLives()
	{
		// Arrange
		var store = CreateStore();

		// Act: первый вопрос без привязки и продолжение тем же чатом.
		var first = await store.AppendMessageAsync(null, Start(constructionId: null), UserDraft("Вопрос по портфелю."));
		var second = await store.AppendMessageAsync(first.ChatId, null, UserDraft("Продолжение."));

		// Assert: чат живёт без привязки, оба сообщения — его история.
		var chat = await store.FindChatAsync(first.ChatId);
		Assert.That(chat!.ConstructionId, Is.Null);
		Assert.That(second.ChatId, Is.EqualTo(first.ChatId));
		var messages = await store.ListMessagesAsync(first.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Вопрос по портфелю.", "Продолжение." }));
	}

	[TestMethod]
	[Description("Истории соседних чатов изолированы: сообщения чата не видны другому чату")]
	// ИИ-помощник видит историю только собственного чата: выборка сообщений
	// фильтруется по чату, сообщения соседних чатов в неё не попадают.
	// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
	public async Task TryIfNeighbourChats_Isolated()
	{
		// Arrange: два чата с разными привязками и разными первыми вопросами.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft("Вопрос чата один."));
		var second = await store.AppendMessageAsync(null, Start(constructionId: null), UserDraft("Вопрос чата два."));

		// Act: каждый чат читает свою историю.
		var firstMessages = await store.ListMessagesAsync(first.ChatId);
		var secondMessages = await store.ListMessagesAsync(second.ChatId);

		// Assert: в выборке каждого чата — только его собственные сообщения.
		Assert.That(firstMessages, Has.Count.EqualTo(1));
		Assert.That(secondMessages, Has.Count.EqualTo(1));
		Assert.That(firstMessages.Select(stored => stored.Text), Does.Not.Contain("Вопрос чата два."));
		Assert.That(secondMessages.Select(stored => stored.Text), Does.Not.Contain("Вопрос чата один."));
	}

	[TestMethod]
	[Description("Попытка передать параметры существующему чату отвергается, привязка остаётся прежней")]
	// Привязка и параметры чата фиксируются при создании: повторная передача
	// параметров — попытка перепривязки — отвергается, сменить контекст
	// можно только создав новый чат.
	// Traceability: openspec:chats/history#scenario-chat-binding-cannot-change
	public async Task ThrowOnRebindAttempt_ParametersRejectedAndBindingStays()
	{
		// Arrange: чат привязан к конструкции 7.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(constructionId: 7), UserDraft());

		// Act — Assert: сообщение с параметрами другой привязки отвергается.
		Assert.ThrowsAsync<ArgumentException>(async () =>
			await store.AppendMessageAsync(first.ChatId, Start(constructionId: 9), UserDraft("Повторная привязка.")));

		// Assert: привязка чата осталась прежней, история не изменилась.
		var chat = await store.FindChatAsync(first.ChatId);
		Assert.That(chat!.ConstructionId, Is.EqualTo(7));
		var messages = await store.ListMessagesAsync(first.ChatId);
		Assert.That(messages, Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("След источников ответа ИИ-помощника сохраняется в сообщении целиком")]
	// Сообщение хранит роль, текст и as-of; ответ помощника дополнительно —
	// след источников: вызовы инструментов с as-of их данных, ссылки на
	// прочитанные карточки правил и as-of данных журнала; у сообщений
	// владельца следа нет.
	// Traceability: openspec:chats/history#requirement-chat-message-composition
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	public async Task TryIfAssistantMessage_KeepsSourceTrace()
	{
		// Arrange: чат с первым вопросом и ответом помощника с тул-вызовом,
		// ссылкой на карточку правила и as-of данных журнала.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());
		var trace = new ChatSourceTrace
		{
			Invocations =
			[
				new ChatToolInvocation
				{
					ToolName = "get_market_snapshot",
					Arguments = "{\"baseCoin\":\"BTC\"}",
					DataAsOf = FixedNow,
				},
			],
			RuleCards = ["ac-01"],
			JournalAsOf = FixedNow,
		};

		// Act
		var answer = await store.AppendMessageAsync(first.ChatId, null, new ChatMessageDraft
		{
			Role = ChatMessageRole.Assistant,
			Text = "Марка BTC 108975.4.",
			AsOf = FixedNow,
			SourceTrace = trace,
		});

		// Assert: след восстановлен из хранения целиком — вызовы с as-of,
		// ссылка на карточку и as-of данных журнала.
		var messages = await store.ListMessagesAsync(first.ChatId);
		var storedAssistant = messages.Single(stored => stored.Role == ChatMessageRole.Assistant);
		Assert.That(storedAssistant.Id, Is.EqualTo(answer.Id));
		Assert.That(storedAssistant.SourceTrace, Is.Not.Null);
		Assert.That(storedAssistant.SourceTrace!.Invocations, Has.Count.EqualTo(1));
		Assert.That(storedAssistant.SourceTrace.Invocations[0].ToolName, Is.EqualTo("get_market_snapshot"));
		Assert.That(storedAssistant.SourceTrace.Invocations[0].Arguments, Is.EqualTo("{\"baseCoin\":\"BTC\"}"));
		Assert.That(storedAssistant.SourceTrace.Invocations[0].DataAsOf, Is.EqualTo(FixedNow));
		Assert.That(storedAssistant.SourceTrace.RuleCards, Is.EqualTo(new[] { "ac-01" }));
		Assert.That(storedAssistant.SourceTrace.JournalAsOf, Is.EqualTo(FixedNow));
		var storedUser = messages.Single(stored => stored.Role == ChatMessageRole.User);
		Assert.That(storedUser.SourceTrace, Is.Null);
	}

	[TestMethod]
	[Description("Новому чату без параметров создания сообщение не сохраняется")]
	// Параметры обязательны в момент первого сообщения: без модели, привязки
	// и источников чат создать нельзя.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	public void ThrowOnNewChatWithoutStartParameters()
	{
		// Arrange
		var store = CreateStore();

		// Act — Assert
		Assert.ThrowsAsync<ArgumentException>(async () =>
			await store.AppendMessageAsync(null, null, UserDraft()));
	}

	[TestMethod]
	[Description("Сообщение несуществующего чата отвергается исключением")]
	// Продолжать историю можно только существующего чата: неизвестный
	// идентификатор — ошибка, а не молчаливое создание.
	public void ThrowOnMessageForUnknownChat()
	{
		// Arrange
		var store = CreateStore();

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await store.AppendMessageAsync(999, null, UserDraft()));
	}

	[TestMethod]
	[Description("Неизвестный чат отдаётся как null, его история — пустой список")]
	// Отсутствие чата и пустая история различимы: FindChatAsync отвечает
	// null, ListMessagesAsync неизвестного чата — пустой список.
	public async Task TryIfUnknownChat_FoundNullAndMessagesEmpty()
	{
		// Arrange
		var store = CreateStore();

		// Act
		var chat = await store.FindChatAsync(999);
		var messages = await store.ListMessagesAsync(999);

		// Assert
		Assert.That(chat, Is.Null);
		Assert.That(messages, Is.Empty);
	}

	[TestMethod]
	[Description("Ручное завершение скрывает чат в список завершённых, история сохраняется")]
	// Завершение — ручное действие владельца: активный чат исчезает из списка
	// активных и появляется в списке завершённых, полная история сохраняется.
	// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
	public async Task TryIfCompletion_HidesChatToCompletedListAndKeepsHistory()
	{
		// Arrange: активный чат с историей из двух сообщений.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());
		await store.AppendMessageAsync(first.ChatId, null, UserDraft("Второе сообщение."));

		// Act: владелец завершает чат вручную.
		await store.CompleteChatAsync(first.ChatId);

		// Assert: статус завершён, чат в списке завершённых и не в активных,
		// история сохранена целиком.
		var chat = await store.FindChatAsync(first.ChatId);
		Assert.That(chat!.Status, Is.EqualTo(ChatStatus.Completed));
		var active = await store.ListActiveChatsAsync();
		var completed = await store.ListCompletedChatsAsync();
		Assert.That(active.Select(stored => stored.Id), Does.Not.Contain(first.ChatId));
		Assert.That(completed.Select(stored => stored.Id), Does.Contain(first.ChatId));
		var messages = await store.ListMessagesAsync(first.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Первый вопрос.", "Второе сообщение." }));
	}

	[TestMethod]
	[Description("Сообщение владельца в завершённый чат возвращает его в активные")]
	// Продолжение завершённого чата — отправка сообщения: чат возвращается в
	// список активных, история ведётся дальше в том же чате.
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	public async Task TryIfMessageToCompletedChat_ReturnsChatToActiveList()
	{
		// Arrange: завершённый владельцем чат.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());
		await store.CompleteChatAsync(first.ChatId);

		// Act: владелец продолжает чат сообщением.
		var second = await store.AppendMessageAsync(first.ChatId, null, UserDraft("Продолжаю."));

		// Assert: тот же чат снова активен и в списке активных, из завершённых
		// исчез; продолжение не создало нового чата.
		Assert.That(second.ChatId, Is.EqualTo(first.ChatId));
		var chat = await store.FindChatAsync(first.ChatId);
		Assert.That(chat!.Status, Is.EqualTo(ChatStatus.Active));
		var active = await store.ListActiveChatsAsync();
		var completed = await store.ListCompletedChatsAsync();
		Assert.That(active.Select(stored => stored.Id), Does.Contain(first.ChatId));
		Assert.That(completed.Select(stored => stored.Id), Does.Not.Contain(first.ChatId));
	}

	[TestMethod]
	[Description("Ручное продолжение возвращает завершённый чат в активные без сообщения")]
	// Продолжение доступно и отдельным действием владельца: завершённый чат
	// возвращается в активные ещё до отправки следующего сообщения.
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	public async Task TryIfExplicitResume_ReturnsCompletedChatToActiveList()
	{
		// Arrange: завершённый владельцем чат.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());
		await store.CompleteChatAsync(first.ChatId);

		// Act: владелец продолжает чат без нового сообщения.
		await store.ResumeChatAsync(first.ChatId);

		// Assert: чат снова активен и в списке активных.
		var chat = await store.FindChatAsync(first.ChatId);
		Assert.That(chat!.Status, Is.EqualTo(ChatStatus.Active));
		var active = await store.ListActiveChatsAsync();
		Assert.That(active.Select(stored => stored.Id), Does.Contain(first.ChatId));
	}

	[TestMethod]
	[Description("Удаление убирает активный и завершённый чаты со всеми сообщениями без восстановления")]
	// Удаление — явное действие владельца: чат и вся его история исчезают
	// целиком, корзины нет — ни по идентификатору, ни в списках, ни в
	// сообщениях чата больше ничего не остаётся.
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	public async Task TryIfHardDelete_RemovesChatWithMessagesWithoutRecovery()
	{
		// Arrange: активный и завершённый чаты, у каждого своя история.
		var store = CreateStore();
		var activeChat = await store.AppendMessageAsync(null, Start(), UserDraft("Вопрос активного."));
		var completedChat = await store.AppendMessageAsync(null, Start(constructionId: null), UserDraft("Вопрос завершённого."));
		await store.CompleteChatAsync(completedChat.ChatId);

		// Act: владелец удаляет оба чата целиком.
		await store.DeleteChatAsync(activeChat.ChatId);
		await store.DeleteChatAsync(completedChat.ChatId);

		// Assert: ни чатов, ни сообщений — ни в выборках, ни в списках статусов.
		Assert.That(await store.FindChatAsync(activeChat.ChatId), Is.Null);
		Assert.That(await store.FindChatAsync(completedChat.ChatId), Is.Null);
		Assert.That(await store.ListMessagesAsync(activeChat.ChatId), Is.Empty);
		Assert.That(await store.ListMessagesAsync(completedChat.ChatId), Is.Empty);
		Assert.That(await store.ListActiveChatsAsync(), Is.Empty);
		Assert.That(await store.ListCompletedChatsAsync(), Is.Empty);
	}

	[TestMethod]
	[Description("Стирание чатов конструкции удаляет привязанные чаты с историей; непривязанные и чужие переживают")]
	// Чат — запись окружения: пересбор конструкции стирает чаты, привязанные
	// к ней, вместе с её старой записью; портфельные чаты без привязки и чаты
	// других конструкций пересбор переживают.
	// Traceability: openspec:chats/history#scenario-chat-rebuild-wipes-bound-chats
	// Traceability: openspec:chats/history#scenario-chat-unbound-chat-survives-rebuild
	public async Task TryIfDeleteForConstruction_WipesOnlyBoundChats()
	{
		// Arrange: активный и завершённый чаты конструкции 7, чат конструкции 9
		// и чат без привязки — у каждого своя история.
		var store = CreateStore();
		var boundActive = await store.AppendMessageAsync(null, Start(7), UserDraft("Вопрос конструкции 7."));
		var boundCompleted = await store.AppendMessageAsync(null, Start(7), UserDraft("Второй вопрос конструкции 7."));
		await store.CompleteChatAsync(boundCompleted.ChatId);
		var otherConstruction = await store.AppendMessageAsync(null, Start(9), UserDraft("Вопрос конструкции 9."));
		var unbound = await store.AppendMessageAsync(null, Start(null), UserDraft("Портфельный вопрос."));

		// Act: стирание пересбором чатов конструкции 7.
		await store.DeleteForConstructionAsync(7);

		// Assert: привязанные чаты исчезли целиком, остальные живы с историей.
		Assert.That(await store.FindChatAsync(boundActive.ChatId), Is.Null, "Чат привязанной конструкции стёрт");
		Assert.That(await store.FindChatAsync(boundCompleted.ChatId), Is.Null, "Завершённый чат привязанной конструкции стёрт");
		Assert.That(await store.ListMessagesAsync(boundActive.ChatId), Is.Empty, "История стёртого чата удалена");
		Assert.That(await store.ListMessagesAsync(boundCompleted.ChatId), Is.Empty, "История стёртого завершённого чата удалена");
		var survivingOther = await store.FindChatAsync(otherConstruction.ChatId);
		Assert.That(survivingOther, Is.Not.Null, "Чат другой конструкции переживает");
		Assert.That((await store.ListMessagesAsync(otherConstruction.ChatId)).Single().Text, Is.EqualTo("Вопрос конструкции 9."));
		var survivingUnbound = await store.FindChatAsync(unbound.ChatId);
		Assert.That(survivingUnbound, Is.Not.Null, "Непривязанный чат переживает");
		Assert.That(survivingUnbound!.ConstructionId, Is.Null, "Привязка непривязанного чата не изменилась");
		Assert.That((await store.ListMessagesAsync(unbound.ChatId)).Single().Text, Is.EqualTo("Портфельный вопрос."));
	}

	[TestMethod]
	[Description("Стирание чатов несуществующей конструкции ничего не меняет")]
	// Пересбор над журналом без конструкций стирает пустое множество: ни чаты,
	// ни их истории не затрагиваются.
	public async Task TryIfDeleteForUnknownConstruction_KeepsEverythingIntact()
	{
		// Arrange: один непривязанный чат.
		var store = CreateStore();
		var chat = await store.AppendMessageAsync(null, Start(null), UserDraft());

		// Act: стирание чатов отсутствующей конструкции.
		await store.DeleteForConstructionAsync(42);

		// Assert: чат и его история не тронуты.
		Assert.That(await store.FindChatAsync(chat.ChatId), Is.Not.Null);
		Assert.That(await store.ListMessagesAsync(chat.ChatId), Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("Сообщения сами по себе чат не завершают: автоматического завершения нет")]
	// Автоматического завершения нет: ни отправка сообщений, ни поздний as-of
	// не меняют активный статус — чат завершает только ручное действие
	// владельца.
	// Traceability: openspec:chats/history#requirement-chat-manual-completion-and-deletion
	public async Task TryIfMessagesAppended_ChatStaysActiveWithoutAutoCompletion()
	{
		// Arrange: активный чат с первым сообщением.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());

		// Act: продолжение с сильно поздним as-of — «прошло время», но ручного
		// завершения владелец не делал.
		await store.AppendMessageAsync(first.ChatId, null, new ChatMessageDraft
		{
			Role = ChatMessageRole.User,
			Text = "Поздний вопрос.",
			AsOf = FixedNow.AddDays(30),
		});

		// Assert: чат остался активным и в списке активных, в завершённых пусто.
		var chat = await store.FindChatAsync(first.ChatId);
		Assert.That(chat!.Status, Is.EqualTo(ChatStatus.Active));
		var active = await store.ListActiveChatsAsync();
		Assert.That(active.Select(stored => stored.Id), Does.Contain(first.ChatId));
		Assert.That(await store.ListCompletedChatsAsync(), Is.Empty);
	}

	[TestMethod]
	[Description("Завершение несуществующего чата отвергается исключением")]
	// Ручное завершение применяется только к существующему чату: неизвестный
	// идентификатор — ошибка состояния, а не молчаливый успех.
	public void ThrowOnCompleteUnknownChat()
	{
		// Arrange
		var store = CreateStore();

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await store.CompleteChatAsync(999));
	}

	[TestMethod]
	[Description("Продолжение несуществующего чата отвергается исключением")]
	// Ручное продолжение применяется только к существующему чату.
	public void ThrowOnResumeUnknownChat()
	{
		// Arrange
		var store = CreateStore();

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await store.ResumeChatAsync(999));
	}

	[TestMethod]
	[Description("Удаление несуществующего чата отвергается исключением")]
	// Удаление применяется только к существующему чату: неизвестный
	// идентификатор — ошибка состояния.
	public void ThrowOnDeleteUnknownChat()
	{
		// Arrange
		var store = CreateStore();

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await store.DeleteChatAsync(999));
	}

	[TestMethod]
	[Description("Смена модели на лету обновляет параметр чата, история остаётся как есть")]
	// Модель — параметр чата: владелец меняет её между сообщениями, значение
	// нормализуется обрезкой и сохраняется, ранее записанная история не
	// переписывается.
	// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
	public async Task TryIfModelChangedMidChat_OnlyParameterMovesAndHistoryUntouched()
	{
		// Arrange: активный чат на дефолтной модели с одним сообщением.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());

		// Act: владелец меняет модель чата между сообщениями.
		var chat = await store.ChangeChatModelAsync(first.ChatId, "  glm-5.2  ");

		// Assert: модель обновлена с обрезкой и видна новому экземпляру хранилища.
		Assert.That(chat.Model, Is.EqualTo("glm-5.2"));
		var reloaded = await CreateStore().FindChatAsync(first.ChatId);
		Assert.That(reloaded!.Model, Is.EqualTo("glm-5.2"));

		// Assert: история не переписана — единственное сообщение владельца на месте.
		var messages = await CreateStore().ListMessagesAsync(first.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Первый вопрос." }));
	}

	[TestMethod]
	[Description("Смена модели несуществующего чата отвергается исключением")]
	// Параметр меняется только у существующего чата: неизвестный идентификатор —
	// ошибка состояния, а не молчаливый успех.
	// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
	public void ThrowOnChangeModelUnknownChat()
	{
		// Arrange
		var store = CreateStore();

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await store.ChangeChatModelAsync(999, "glm-5.2"));
	}

	[TestMethod]
	[Description("Смена модели на пустое значение отвергается, прежняя модель сохраняется")]
	// Модель чата не бывает пустой: пустая или пробельная строка не проходит
	// валидацию параметра, чат продолжает работать на прежней модели.
	// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
	public async Task ThrowOnChangeModelBlank()
	{
		// Arrange: активный чат на дефолтной модели.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());

		// Act — Assert
		Assert.ThrowsAsync<ArgumentException>(async () =>
			await store.ChangeChatModelAsync(first.ChatId, "   "));

		// Assert: прежняя модель сохранилась.
		var chat = await store.FindChatAsync(first.ChatId);
		Assert.That(chat!.Model, Is.EqualTo("glm-5.3"));
	}
}
