namespace TransactionJournal.Tests.Infrastructure.Chats;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Infrastructure.Chats;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using Does = NUnit.Framework.Does;

/// <summary>
/// Адаптер единого SQLite-хранилища чатов: чат создаётся первым сообщением
/// с выбранными параметрами, живёт без привязки к конструкции, истории
/// соседних чатов изолированы, привязка и параметры неизменяемы после
/// создания, след источников ответа ИИ-помощника сохраняется в сообщении.
/// </summary>
[TestClass]
public class ChatStoreTests
{
	/// <summary>Фиксированный момент as-of сообщений проверок.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"chat-store-tests-{Guid.NewGuid():N}.db");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
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

	/// <summary>Хранилище над файлом базы проверки: каждое обращение создаёт независимый контекст.</summary>
	private ChatStore CreateStore() => new(_databasePath);

	/// <summary>Параметры создания чата: модель по умолчанию, привязка и набор источников.</summary>
	private static ChatStartParameters Start(long? constructionId = 7, params string[] sources) => new()
	{
		Model = "glm-5.3",
		ConstructionId = constructionId,
		Sources = sources.Length == 0 ? ["journal", "rules-corpus", "market"] : sources,
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
		var start = Start(7, "journal", "rules-corpus");

		// Act: сообщение без идентификатора чата создаёт новый чат.
		var message = await store.AppendMessageAsync(null, start, UserDraft());

		// Assert: чат существует с выбранными параметрами и активным статусом.
		Assert.That(message.ChatId, Is.GreaterThan(0));
		var chat = await store.FindChatAsync(message.ChatId);
		Assert.That(chat, Is.Not.Null);
		Assert.That(chat!.Model, Is.EqualTo("glm-5.3"));
		Assert.That(chat.ConstructionId, Is.EqualTo(7));
		Assert.That(chat.Sources, Is.EqualTo(new[] { "journal", "rules-corpus" }));
		Assert.That(chat.Status, Is.EqualTo(ChatStatus.Active));
		Assert.That(chat.CreatedAt, Is.EqualTo(FixedNow));
		var messages = await store.ListMessagesAsync(message.ChatId);
		Assert.That(messages.Select(stored => stored.Text), Is.EqualTo(new[] { "Первый вопрос." }));
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
	// след источников: вызовы инструментов с as-of их данных, у сообщений
	// владельца следа нет.
	// Traceability: openspec:chats/history#requirement-chat-message-composition
	public async Task TryIfAssistantMessage_KeepsSourceTrace()
	{
		// Arrange: чат с первым вопросом и ответом помощника с одним тул-вызовом.
		var store = CreateStore();
		var first = await store.AppendMessageAsync(null, Start(), UserDraft());
		var trace = new ChatMarketTrace
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
		};

		// Act
		var answer = await store.AppendMessageAsync(first.ChatId, null, new ChatMessageDraft
		{
			Role = ChatMessageRole.Assistant,
			Text = "Марка BTC 108975.4.",
			AsOf = FixedNow,
			MarketTrace = trace,
		});

		// Assert: след восстановлен из хранения с аргументами и as-of данных.
		var messages = await store.ListMessagesAsync(first.ChatId);
		var storedAssistant = messages.Single(stored => stored.Role == ChatMessageRole.Assistant);
		Assert.That(storedAssistant.Id, Is.EqualTo(answer.Id));
		Assert.That(storedAssistant.MarketTrace, Is.Not.Null);
		Assert.That(storedAssistant.MarketTrace!.Invocations, Has.Count.EqualTo(1));
		Assert.That(storedAssistant.MarketTrace.Invocations[0].ToolName, Is.EqualTo("get_market_snapshot"));
		Assert.That(storedAssistant.MarketTrace.Invocations[0].Arguments, Is.EqualTo("{\"baseCoin\":\"BTC\"}"));
		Assert.That(storedAssistant.MarketTrace.Invocations[0].DataAsOf, Is.EqualTo(FixedNow));
		var storedUser = messages.Single(stored => stored.Role == ChatMessageRole.User);
		Assert.That(storedUser.MarketTrace, Is.Null);
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
}
