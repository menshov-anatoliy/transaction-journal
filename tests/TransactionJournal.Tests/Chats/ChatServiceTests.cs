namespace TransactionJournal.Tests.Chats;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Tests;
using Assert = NUnit.Framework.Assert;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using Does = NUnit.Framework.Does;

/// <summary>
/// Сквозные проверки конвейера сообщения чата агента: первое сообщение
/// владельца создаёт новый чат с выбранными параметрами, ответ ИИ-помощника
/// фиксируется по завершении стрима с as-of и следом источников, агенту
/// передаётся история только собственного чата, а снимок контекста
/// собирается по привязке чата. Жизненный цикл — ручные действия владельца:
/// завершение скрывает чат в завершённые, продолжение возвращает в активные,
/// удаление стирает чат с историей целиком.
/// </summary>
[TestClass]
public sealed class ChatServiceTests : ChatDatabaseTests
{

	[TestMethod]
	[Description("Первое сообщение владельца создаёт новый чат с выбранными параметрами")]
	// Чат создаётся самим сообщением: модель, привязка и набор источников
	// фиксируются в момент первого вопроса, статус нового чата — активен.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	public async Task TryIfFirstUserMessage_CreatesChatWithChosenParameters()
	{
		// Arrange: конвейер над настоящим хранилищем и подменённым клиентом модели.
		var chatClient = new FakeChatClient();
		var market = new Mock<IChatMarketReader>(MockBehavior.Loose);
		var service = CreateService(chatClient, market, out var contextReader);

		// Act: сообщение без идентификатора чата создаёт новый чат.
		var message = await service.AppendUserMessageAsync(
			null,
			new ChatStartParameters
			{
				Model = "glm-5.3",
				ConstructionId = 7,
				Sources = [ChatDataSource.Journal, ChatDataSource.RulesCorpus],
			},
			"Первый вопрос.");

		// Assert: чат существует с выбранными параметрами и активным статусом,
		// сообщение зафиксировано в его истории.
		var chat = await CreateStore().FindChatAsync(message.ChatId);
		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Model, Is.EqualTo("glm-5.3"));
		Assert.That(chat.ConstructionId, Is.EqualTo(7));
		Assert.That(chat.Sources, Is.EqualTo(new[] { ChatDataSource.Journal, ChatDataSource.RulesCorpus }));
		Assert.That(chat.Status, Is.EqualTo(ChatStatus.Active));
		var messages = await CreateStore().ListMessagesAsync(message.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Первый вопрос." }));
		Assert.That(contextReader.RequestedConstructionIds, Is.Empty);
	}

	[TestMethod]
	[Description("Ответ помощника с инструментом фиксируется в чате со следом источников и as-of")]
	// След источников сохраняется в самом сообщении: какие инструменты
	// вызывались и с какими as-of их данные — происхождение рекомендаций
	// проверяемо постфактум.
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	public async Task TryIfAssistantUsedMarketTool_TracePersistedInMessage()
	{
		// Arrange: модель сначала вызывает снимок рынка, затем отвечает текстом;
		// биржа отдаёт марку с фиксированным as-of.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ChatTools.GetMarketSnapshotToolName, "BTC")],
			[
				() => new ChatResponseUpdate(ChatRole.Assistant, "Марка BTC "),
				() => new ChatResponseUpdate(ChatRole.Assistant, "108975.4 USDT."),
			],
		];
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatMarketSnapshot
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = true,
				Symbol = "BTCUSDT",
				MarkPrice = 108975.4m,
			});
		var service = CreateService(chatClient, market, out _);

		// Act: первое сообщение создаёт чат, ответ стримится до конца.
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Что с маркой BTC?");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: сообщение владельца без следа, ответ помощника — со следом тула.
		var messages = await CreateStore().ListMessagesAsync(userMessage.ChatId);
		Assert.That(messages, Has.Count.EqualTo(2));
		var storedUser = messages.Single(message => message.Role == ChatMessageRole.User);
		Assert.That(storedUser.MarketTrace, Is.Null);
		var storedAssistant = messages.Single(message => message.Role == ChatMessageRole.Assistant);
		Assert.That(storedAssistant.Text, Is.EqualTo("Марка BTC 108975.4 USDT."));
		Assert.That(storedAssistant.MarketTrace, Is.Not.Null);
		var invocations = storedAssistant.MarketTrace!.Invocations;
		Assert.That(invocations, Has.Count.EqualTo(1));
		Assert.That(invocations[0].ToolName, Is.EqualTo(ChatTools.GetMarketSnapshotToolName));
		Assert.That(invocations[0].Arguments, Is.EqualTo("{\"baseCoin\":\"BTC\"}"));
		Assert.That(invocations[0].DataAsOf, Is.EqualTo(FixedNow));
	}

	[TestMethod]
	[Description("Агенту видна история только собственного чата, снимок собирается по привязке чата")]
	// Соседние чаты изолированы: в запрос модели попадает история только
	// отвечаемого чата; снимок контекста собирается по привязке чата — у
	// привязанного по конструкции, у чата без привязки — портфельная ветка
	// читателя контекста.
	// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
	public async Task TryIfNeighbourChatHistory_InvisibleToAssistant()
	{
		// Arrange: два чата с обменами — привязанный к конструкции 7 и чат
		// без привязки; затем продолжение второго чата.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ А1.")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ Б1.")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ Б2.")],
		];
		var market = new Mock<IChatMarketReader>(MockBehavior.Loose);
		var service = CreateService(chatClient, market, out var contextReader);

		// Act: обмен в чате A (конструкция 7), обмен в чате B (без привязки)
		// и продолжение чата B.
		var questionA = await service.AppendUserMessageAsync(null, Start(7), "Вопрос А1.");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(questionA.ChatId, questionA));
		var questionB1 = await service.AppendUserMessageAsync(null, Start(null), "Вопрос Б1.");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(questionB1.ChatId, questionB1));
		var questionB2 = await service.AppendUserMessageAsync(questionB1.ChatId, null, "Вопрос Б2.");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(questionB1.ChatId, questionB2));

		// Assert: последний запрос модели — только история чата B и его вопрос,
		// сообщения чата A в конвейер не попали.
		var request = chatClient.Requests[^1];
		Assert.That(request.Select(message => message.Text), Does.Contain("Вопрос Б1."));
		Assert.That(request.Select(message => message.Text), Does.Contain("Ответ Б1."));
		Assert.That(request[^1].Text, Does.Contain("Вопрос Б2."));
		Assert.That(request.Select(message => message.Text), Does.Not.Contain("Вопрос А1."));
		Assert.That(request.Select(message => message.Text), Does.Not.Contain("Ответ А1."));

		// Assert: снимок контекста собирался по привязке каждого чата —
		// конструкция 7 у чата A, портфельная ветка (null) у чата B.
		Assert.That(contextReader.RequestedConstructionIds, Is.EqualTo(new long?[] { 7, null, null }));
	}

	[TestMethod]
	[Description("Сообщение владельца без вопроса отвергается исключением")]
	// Пустой вопрос не создаёт чат: сообщение без текста сохранять нечего.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	[DataRow("")]
	[DataRow("   ")]
	public void ThrowOnEmptyQuestion(string question)
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);

		// Act — Assert
		Assert.ThrowsAsync<ArgumentException>(async () =>
			await service.AppendUserMessageAsync(null, Start(7), question));
	}

	[TestMethod]
	[Description("Стрим ответа без зафиксированного сообщения владельца отвергается исключением")]
	// Ответу нужен якорь в чате: вопрос владельца обязан быть зафиксирован
	// до старта генерации, иначе истории и следу не на что ссылаться.
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	public void ThrowOnNullUserMessage()
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);

		// Act — Assert: перечисление стрима бросает исключение до первого кадра.
		Assert.ThrowsAsync<ArgumentNullException>(async () =>
			await CollectAsync(service.StreamAssistantAnswerAsync(1, null!)));
	}

	[TestMethod]
	[Description("Стрим ответа для несуществующего чата отвергается исключением")]
	// Отвечать можно только в существующий чат: идентификатор без чата —
	// ошибка конвейера, а не молчаливый ответ в никуда.
	public void ThrowOnStreamForUnknownChat()
	{
		// Arrange: сообщение-якорь с идентификатором несуществующего чата.
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);
		var orphan = new TransactionJournal.Chats.Ports.ChatMessage
		{
			Id = 1,
			ChatId = 999,
			Role = ChatMessageRole.User,
			Text = "Вопрос в никуда.",
			AsOf = FixedNow,
		};

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await CollectAsync(service.StreamAssistantAnswerAsync(orphan.ChatId, orphan)));
	}

	[TestMethod]
	[Description("Стрим ответа с сообщением-якорем чужого чата отвергается исключением")]
	// Сообщение-якорь обязано принадлежать отвечаемому чату: рассинхрон пары
	// (чат, сообщение) не должен молча подмешивать чужой вопрос в контекст
	// чата и дописывать в него ответ.
	// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
	public async Task ThrowOnUserMessageOfAnotherChat()
	{
		// Arrange: два чата с вопросами; сообщение-якорь — вопрос первого чата,
		// ответ запрашивается во второй.
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);
		var first = await service.AppendUserMessageAsync(null, Start(7), "Вопрос чата один.");
		var second = await service.AppendUserMessageAsync(null, Start(9), "Вопрос чата два.");

		// Act — Assert: нарушение контракта отвергается до генерации ответа.
		Assert.ThrowsAsync<ArgumentException>(async () =>
			await CollectAsync(service.StreamAssistantAnswerAsync(second.ChatId, first)));

		// Assert: история чата-получателя не изменилась — чужой вопрос и ответ
		// в неё не попали.
		var messages = await CreateStore().ListMessagesAsync(second.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Вопрос чата два." }));
	}

	[TestMethod]
	[Description("Ручное завершение через конвейер скрывает чат в список завершённых, история сохраняется")]
	// Завершение — ручное действие владельца на уровне приложения: чат уходит
	// из активных в завершённые, история остаётся доступной.
	// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
	public async Task TryIfCompleteAsync_HidesChatToCompletedListAndKeepsHistory()
	{
		// Arrange: активный чат с вопросом владельца.
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);
		var question = await service.AppendUserMessageAsync(null, Start(7), "Вопрос по конструкции.");

		// Act: владелец завершает чат.
		await service.CompleteAsync(question.ChatId);

		// Assert: чат в списке завершённых, из активных исчез, история цела.
		var active = await service.ListActiveAsync();
		var completed = await service.ListCompletedAsync();
		Assert.That(active.Select(stored => stored.Id), Does.Not.Contain(question.ChatId));
		Assert.That(completed.Select(stored => stored.Id), Does.Contain(question.ChatId));
		var messages = await CreateStore().ListMessagesAsync(question.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Вопрос по конструкции." }));
	}

	[TestMethod]
	[Description("Ручное продолжение через конвейер возвращает завершённый чат в активные")]
	// Продолжение — ручное действие владельца: завершённый чат возвращается
	// в активные ещё до отправки нового сообщения.
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	public async Task TryIfResumeAsync_ReturnsCompletedChatToActiveList()
	{
		// Arrange: завершённый владельцем чат.
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);
		var question = await service.AppendUserMessageAsync(null, Start(7), "Вопрос по конструкции.");
		await service.CompleteAsync(question.ChatId);

		// Act: владелец продолжает чат.
		await service.ResumeAsync(question.ChatId);

		// Assert: чат снова в списке активных и не в завершённых.
		var active = await service.ListActiveAsync();
		var completed = await service.ListCompletedAsync();
		Assert.That(active.Select(stored => stored.Id), Does.Contain(question.ChatId));
		Assert.That(completed.Select(stored => stored.Id), Does.Not.Contain(question.ChatId));
	}

	[TestMethod]
	[Description("Сообщение владельца в завершённый чат продолжает его и возвращает в активные")]
	// Продолжение завершённого чата — отправка сообщения через конвейер:
	// тот же чат возвращается в активные, вопрос дописан в его историю.
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	public async Task TryIfUserMessageToCompletedChat_ReturnsChatToActiveList()
	{
		// Arrange: завершённый владельцем чат.
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);
		var question = await service.AppendUserMessageAsync(null, Start(7), "Вопрос по конструкции.");
		await service.CompleteAsync(question.ChatId);

		// Act: владелец продолжает чат новым сообщением.
		var continuation = await service.AppendUserMessageAsync(question.ChatId, null, "Продолжаю разговор.");

		// Assert: тот же чат снова активен, в активных списках, вопрос в истории.
		Assert.That(continuation.ChatId, Is.EqualTo(question.ChatId));
		var chat = await CreateStore().FindChatAsync(question.ChatId);
		Assert.That(chat!.Status, Is.EqualTo(ChatStatus.Active));
		var active = await service.ListActiveAsync();
		var completed = await service.ListCompletedAsync();
		Assert.That(active.Select(stored => stored.Id), Does.Contain(question.ChatId));
		Assert.That(completed.Select(stored => stored.Id), Does.Not.Contain(question.ChatId));
	}

	[TestMethod]
	[Description("Удаление через конвейер стирает чат с историей целиком без восстановления")]
	// Удаление — явное действие владельца: чат и все его сообщения исчезают
	// целиком, корзины нет.
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	public async Task TryIfDeleteAsync_RemovesChatWithHistoryEntirely()
	{
		// Arrange: активный чат с вопросом владельца.
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);
		var question = await service.AppendUserMessageAsync(null, Start(7), "Вопрос по конструкции.");

		// Act: владелец удаляет чат целиком.
		await service.DeleteAsync(question.ChatId);

		// Assert: ни чата, ни истории, ни в списках статусов.
		Assert.That(await CreateStore().FindChatAsync(question.ChatId), Is.Null);
		Assert.That(await CreateStore().ListMessagesAsync(question.ChatId), Is.Empty);
		Assert.That(await service.ListActiveAsync(), Is.Empty);
		Assert.That(await service.ListCompletedAsync(), Is.Empty);
	}

	[TestMethod]
	[Description("Списки активных и завершённых чатов не пересекаются")]
	// Разделение чатов по статусу: активные и завершённые попадают только в
	// свои списки, порядок — по созданию.
	// Traceability: openspec:chats/history#requirement-chat-manual-completion-and-deletion
	public async Task TryIfLists_SplitActiveAndCompletedChats()
	{
		// Arrange: активный и завершённый чаты.
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);
		var activeQuestion = await service.AppendUserMessageAsync(null, Start(7), "Вопрос активного.");
		var completedQuestion = await service.AppendUserMessageAsync(null, Start(null), "Вопрос завершённого.");
		await service.CompleteAsync(completedQuestion.ChatId);

		// Act: читаются оба списка.
		var active = await service.ListActiveAsync();
		var completed = await service.ListCompletedAsync();

		// Assert: каждый чат ровно в своём списке, статусы согласованы.
		Assert.That(active.Select(stored => stored.Id), Is.EqualTo(new[] { activeQuestion.ChatId }));
		Assert.That(completed.Select(stored => stored.Id), Is.EqualTo(new[] { completedQuestion.ChatId }));
		Assert.That(active.Single().Status, Is.EqualTo(ChatStatus.Active));
		Assert.That(completed.Single().Status, Is.EqualTo(ChatStatus.Completed));
	}

	[TestMethod]
	[Description("Завершение несуществующего чата через конвейер отвергается исключением")]
	// Ручное завершение применяется только к существующему чату.
	public void ThrowOnCompleteUnknownChat()
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await service.CompleteAsync(999));
	}

	[TestMethod]
	[Description("Продолжение несуществующего чата через конвейер отвергается исключением")]
	// Ручное продолжение применяется только к существующему чату.
	public void ThrowOnResumeUnknownChat()
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await service.ResumeAsync(999));
	}

	[TestMethod]
	[Description("Удаление несуществующего чата через конвейер отвергается исключением")]
	// Удаление применяется только к существующему чату.
	public void ThrowOnDeleteUnknownChat()
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await service.DeleteAsync(999));
	}

	[TestMethod]
	[Description("Чат без источника «рынок Bybit» не отправляет модели рыночных инструментов")]
	// Реестр, уходящий модели, собирается из набора источников чата: без
	// рыночной категории в опциях запроса нет ни снимка фьючерсов, ни доски
	// опционов — модель физически не может читать рынок мимо набора.
	// Traceability: openspec:chats/sources#scenario-sources-registry-matches-chat-sources
	public async Task TryIfChatCreatedWithoutMarketSource_RegistryHasNoMarketTools()
	{
		// Arrange: конвейер и чат с журналом и корпусом правил.
		var chatClient = new FakeChatClient();
		chatClient.Script = [[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ по журналу.")]];
		var market = new Mock<IChatMarketReader>(MockBehavior.Loose);
		var service = CreateService(chatClient, market, out _);
		var userMessage = await service.AppendUserMessageAsync(
			null,
			new ChatStartParameters
			{
				Model = "glm-5.3",
				ConstructionId = 7,
				Sources = [ChatDataSource.Journal, ChatDataSource.RulesCorpus],
			},
			"Первый вопрос.");

		// Act: ответ помощника проходит конвейер.
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: в опциях запроса только чтение карточки правила.
		Assert.That(chatClient.Options, Has.Count.EqualTo(1));
		var toolNames = chatClient.Options[0]!.Tools!.Select(tool => tool.Name).ToList();
		Assert.That(toolNames, Is.EqualTo([ChatTools.ReadRuleCardToolName]));
	}

	[TestMethod]
	[Description("Подмножество источников чата задаёт состав реестра инструментов запроса")]
	// Набор источников — параметр чата: чат только с рынком Bybit получает
	// только рыночные инструменты, и ничего кроме них.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	// Traceability: openspec:chats/sources#scenario-sources-registry-matches-chat-sources
	public async Task TryIfChatCreatedWithSubset_RegistryContainsOnlySelectedSourceTools()
	{
		// Arrange: конвейер и чат только с источником «рынок Bybit».
		var chatClient = new FakeChatClient();
		chatClient.Script = [[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ по рынку.")]];
		var market = new Mock<IChatMarketReader>(MockBehavior.Loose);
		var service = CreateService(chatClient, market, out _);
		var userMessage = await service.AppendUserMessageAsync(
			null,
			new ChatStartParameters
			{
				Model = "glm-5.3",
				ConstructionId = 7,
				Sources = [ChatDataSource.BybitMarket],
			},
			"Что с маркой BTC?");

		// Act: ответ помощника проходит конвейер.
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: в опциях запроса ровно рыночные инструменты без чтения правил.
		Assert.That(chatClient.Options, Has.Count.EqualTo(1));
		var toolNames = chatClient.Options[0]!.Tools!.Select(tool => tool.Name).ToList();
		Assert.That(toolNames, Is.EqualTo([ChatTools.GetMarketSnapshotToolName, ChatTools.GetOptionBoardToolName]));
	}

	[TestMethod]
	[Description("Смена модели на лету переключает модель следующих сообщений, история не переписывается")]
	// Модель — параметр чата: первый обмен уходит модели из параметров создания,
	// после смены следующий вопрос с историей уходит новой модели, а записанная
	// история сохраняется как есть.
	// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
	// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
	public async Task TryIfModelChangedMidChat_NewQuestionUsesNewModelAndHistoryUntouched()
	{
		// Arrange: конвейер, чат на дефолтной модели, первый обмен завершён.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ на старой модели.")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ на новой модели.")],
		];
		var market = new Mock<IChatMarketReader>(MockBehavior.Loose);
		var service = CreateService(chatClient, market, out _);
		var first = await service.AppendUserMessageAsync(null, Start(7), "Первый вопрос.");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(first.ChatId, first));

		// Act: владелец меняет модель и задаёт следующий вопрос.
		await service.ChangeModelAsync(first.ChatId, "glm-5.2");
		var second = await service.AppendUserMessageAsync(first.ChatId, null, "Второй вопрос.");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(second.ChatId, second));

		// Assert: каждый запрос ушёл своей модели — первая glm-5.3, вторая glm-5.2.
		Assert.That(chatClient.Options, Has.Count.EqualTo(2));
		Assert.That(chatClient.Options[0]!.ModelId, Is.EqualTo("glm-5.3"));
		Assert.That(chatClient.Options[1]!.ModelId, Is.EqualTo("glm-5.2"));

		// Assert: история не переписана — четыре записи обмена в исходном порядке.
		var messages = await CreateStore().ListMessagesAsync(first.ChatId);
		Assert.That(
			messages.Select(stored => (Role: stored.Role, Text: stored.Text)),
			Is.EqualTo(new[]
			{
				(ChatMessageRole.User, "Первый вопрос."),
				(ChatMessageRole.Assistant, "Ответ на старой модели."),
				(ChatMessageRole.User, "Второй вопрос."),
				(ChatMessageRole.Assistant, "Ответ на новой модели."),
			}));
	}

	[TestMethod]
	[Description("Смена модели несуществующего чата через конвейер отвергается исключением")]
	// Параметр меняется только у существующего чата.
	public void ThrowOnChangeModelUnknownChat()
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IChatMarketReader>(MockBehavior.Loose), out _);

		// Act — Assert
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await service.ChangeModelAsync(999, "glm-5.2"));
	}

	/// <summary>Параметры создания чата с дефолтной моделью и полным набором источников.</summary>
	private static ChatStartParameters Start(long? constructionId) => new()
	{
		Model = "glm-5.3",
		ConstructionId = constructionId,
		Sources = ChatDataSourceCatalog.All,
	};

	/// <summary>Собирает стрим обновлений в список для проверок.</summary>
	private static async Task<List<ChatResponseUpdate>> CollectAsync(IAsyncEnumerable<ChatResponseUpdate> stream)
	{
		var updates = new List<ChatResponseUpdate>();
		await foreach (var update in stream)
		{
			updates.Add(update);
		}

		return updates;
	}

	/// <summary>Конвейер над настоящим хранилищем, заглушкой читателя контекста и подменённым клиентом модели.</summary>
	private ChatService CreateService(
		FakeChatClient chatClient,
		Mock<IChatMarketReader> market,
		out RecordingContextReader contextReader)
	{
		contextReader = new RecordingContextReader();
		return new ChatService(
			CreateStore(),
			contextReader,
			new ChatAgent(
				chatClient,
				new ChatTools(new Mock<IRuleCorpusReader>(MockBehavior.Loose).Object, market.Object),
				new ChatInstructions(Path.Combine(Path.GetTempPath(), "no-such-agent-prompt.md"))),
			new FixedTimeProvider(FixedNow));
	}

	/// <summary>Кадр модели с вызовом рыночного инструмента.</summary>
	private static ChatResponseUpdate ToolCallFrame(string callId, string toolName, string baseCoin) => new()
	{
		Role = ChatRole.Assistant,
		Contents = { new FunctionCallContent(callId, toolName, new Dictionary<string, object?> { ["baseCoin"] = baseCoin }) },
	};

	/// <summary>
	/// Заглушка читателя снимка контекста: фиксирует запрошенные привязки,
	/// чтобы проверки видели, по какой ветке чата собирался снимок.
	/// </summary>
	private sealed class RecordingContextReader : IChatContextReader
	{
		/// <summary>Привязки, по которым запрашивался снимок, в порядке обращений.</summary>
		public List<long?> RequestedConstructionIds { get; } = [];

		public Task<ChatContextSnapshot> ReadAsync(long? constructionId, CancellationToken cancellationToken = default)
		{
			RequestedConstructionIds.Add(constructionId);
			return Task.FromResult(new ChatContextSnapshot
			{
				Markdown = "# Снимок контекста",
				AsOf = FixedNow,
				IsConstructionClosed = false,
			});
		}
	}

	/// <summary>
	/// Подмена клиента модели: на каждый запрос отдаёт кадры текущей сцены
	/// (сцена — один ответ модели, кадры — его стрим-чанки), при исчерпании
	/// повторяет последнюю сцену, копии запросов и опций с реестром
	/// инструментов сохраняет для проверок.
	/// Кадры создаются фабриками — один и тот же экземпляр обновления нельзя
	/// скармливать дважды.
	/// </summary>
	private sealed class FakeChatClient : IChatClient
	{
		/// <summary>Сцены сценария; при исчерпании повторяется последняя сцена.</summary>
		public IReadOnlyList<IReadOnlyList<Func<ChatResponseUpdate>>> Script { get; set; } = [[]];

		/// <summary>Запросы, полученные клиентом, в порядке поступления.</summary>
		public List<List<ChatMessage>> Requests { get; } = [];

		/// <summary>Опции запросов с реестром инструментов, в порядке поступления.</summary>
		public List<ChatOptions?> Options { get; } = [];

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Агентный цикл обязан использовать стриминг.");

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Requests.Add([.. messages]);
			Options.Add(options);
			await Task.Yield();
			var scene = Script[Math.Min(Requests.Count - 1, Script.Count - 1)];
			foreach (var frame in scene)
			{
				yield return frame();
			}
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose()
		{
		}
	}
}
