namespace TransactionJournal.Tests.Infrastructure.Chats;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Tests.Chats;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Колонка trace_id сообщений чата: W3C TraceId корневого спана chat_turn
/// пишется только в ответ помощника в момент фиксации, у сообщений владельца
/// остаётся null; схема базы чатов разворачивается EnsureCreated, значение
/// переживает переоткрытие хранилища. Метаданные отладки: доменные правила
/// колонку не читают.
/// </summary>
[TestClass]
public class ChatStoreTraceIdTests : ChatDatabaseTests
{
	/// <summary>TraceId ответа помощника для проверок — как у корневого chat_turn, 32 hex.</summary>
	private const string AssistantTraceId = "57355d120ff34deb22f5b3f80c80a5f9";

	[TestMethod]
	[Description("TraceId ответа помощника сохраняется и читается после переоткрытия хранилища")]
	// Roundtrip через адаптер: колонка маппится в обе стороны, значение
	// переживает переоткрытие файла базы, как при перезапуске приложения.
	// Traceability: openspec:chats/history#requirement-chat-trace-id-on-answer
	// Traceability: openspec:chats/history#scenario-chat-answer-carries-trace-id
	public async Task TryIfAssistantTraceIdRoundTripsThroughStore()
	{
		// Arrange: чат с вопросом владельца и ответом помощника с trace id.
		var store = CreateStore();
		var start = new ChatStartParameters { Model = "glm-5.3", ConstructionId = 7 };
		var user = await store.AppendMessageAsync(null, start, new ChatMessageDraft
		{
			Role = ChatMessageRole.User,
			Text = "Что с котировкой BTC?",
			AsOf = FixedNow,
		});
		await store.AppendMessageAsync(user.ChatId, null, new ChatMessageDraft
		{
			Role = ChatMessageRole.Assistant,
			Text = "Котировка доступна.",
			AsOf = FixedNow,
			TraceId = AssistantTraceId,
		});

		// Act: переоткрытие хранилища над тем же файлом базы и чтение истории.
		var reloaded = CreateStore();
		var messages = await reloaded.ListMessagesAsync(user.ChatId);

		// Assert: trace id ответа сохранён, сообщение владельца — null.
		var storedUser = messages.Single(message => message.Role == ChatMessageRole.User);
		var storedAssistant = messages.Single(message => message.Role == ChatMessageRole.Assistant);
		Assert.That(storedAssistant.TraceId, Is.EqualTo(AssistantTraceId));
		Assert.That(storedUser.TraceId, Is.Null);
	}

	[TestMethod]
	[Description("Ответ без trace id и сообщение владельца остаются null без ошибки")]
	// Ответы до внедрения и сообщения владельца читаются как null — колонка
	// nullable-метаданные, её отсутствие не влияет на чтение истории.
	// Traceability: openspec:chats/history#scenario-chat-owner-message-no-trace-id
	public async Task TryIfMessagesWithoutTraceIdReadAsNull()
	{
		// Arrange: оба сообщения фиксируются без trace id.
		var store = CreateStore();
		var start = new ChatStartParameters { Model = "glm-5.3", ConstructionId = 7 };
		var user = await store.AppendMessageAsync(null, start, new ChatMessageDraft
		{
			Role = ChatMessageRole.User,
			Text = "Первый вопрос.",
			AsOf = FixedNow,
		});
		await store.AppendMessageAsync(user.ChatId, null, new ChatMessageDraft
		{
			Role = ChatMessageRole.Assistant,
			Text = "Ответ.",
			AsOf = FixedNow,
		});

		// Act
		var messages = await store.ListMessagesAsync(user.ChatId);

		// Assert: у обоих сообщений trace id null.
		Assert.That(messages.Select(message => message.TraceId), Has.All.Null);
	}

	[TestMethod]
	[Description("Сообщение владельца с trace id не появляется: порт хранит то, что передал черновик")]
	// Хранилище не расставляет trace id само: конвейер передаёт его только
	// в черновике ответа помощника — владелец не может получить чужой ход.
	// Traceability: openspec:chats/history#scenario-chat-owner-message-no-trace-id
	// Traceability: openspec:chats/history#scenario-chat-trace-id-not-domain
	public async Task TryIfOwnerDraftTraceIdStaysNullByDefault()
	{
		// Arrange: черновик владельца без trace id — дефолтный путь конвейера.
		var store = CreateStore();
		var start = new ChatStartParameters { Model = "glm-5.3", ConstructionId = 7 };

		// Act
		var user = await store.AppendMessageAsync(null, start, new ChatMessageDraft
		{
			Role = ChatMessageRole.User,
			Text = "Первый вопрос.",
			AsOf = FixedNow,
		});

		// Assert
		Assert.That(user.TraceId, Is.Null);
	}
}
