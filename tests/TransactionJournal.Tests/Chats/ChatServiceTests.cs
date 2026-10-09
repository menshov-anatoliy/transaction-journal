namespace TransactionJournal.Tests.Chats;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Infrastructure.Chats;
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
/// собирается по привязке чата.
/// </summary>
[TestClass]
public sealed class ChatServiceTests
{
	/// <summary>Фиксированный момент as-of сообщений и рыночных данных.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой чатов во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"chat-service-tests-{Guid.NewGuid():N}.db");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

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
				Sources = ["journal", "rules-corpus"],
			},
			"Первый вопрос.");

		// Assert: чат существует с выбранными параметрами и активным статусом,
		// сообщение зафиксировано в его истории.
		var chat = await CreateStore().FindChatAsync(message.ChatId);
		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Model, Is.EqualTo("glm-5.3"));
		Assert.That(chat.ConstructionId, Is.EqualTo(7));
		Assert.That(chat.Sources, Is.EqualTo(new[] { "journal", "rules-corpus" }));
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

	/// <summary>Параметры создания чата с дефолтной моделью и полным набором источников.</summary>
	private static ChatStartParameters Start(long? constructionId) => new()
	{
		Model = "glm-5.3",
		ConstructionId = constructionId,
		Sources = ["journal", "rules-corpus", "market"],
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

	/// <summary>Хранилище над файлом базы проверки: каждое обращение создаёт независимый контекст.</summary>
	private ChatStore CreateStore() => new(_databasePath);

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
	/// повторяет последнюю сцену, копии запросов сохраняет для проверок.
	/// Кадры создаются фабриками — один и тот же экземпляр обновления нельзя
	/// скармливать дважды.
	/// </summary>
	private sealed class FakeChatClient : IChatClient
	{
		/// <summary>Сцены сценария; при исчерпании повторяется последняя сцена.</summary>
		public IReadOnlyList<IReadOnlyList<Func<ChatResponseUpdate>>> Script { get; set; } = [[]];

		/// <summary>Запросы, полученные клиентом, в порядке поступления.</summary>
		public List<List<ChatMessage>> Requests { get; } = [];

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Агентный цикл обязан использовать стриминг.");

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Requests.Add([.. messages]);
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
