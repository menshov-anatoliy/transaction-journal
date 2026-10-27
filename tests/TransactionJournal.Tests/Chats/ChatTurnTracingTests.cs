namespace TransactionJournal.Tests.Chats;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

/// <summary>
/// Проверки операционной трассы хода чата: корневой спан chat_turn несёт
/// связь с чатом и вопросом, число запросов модели и признак отмены со
/// статусом не-ошибки; встроенные спаны Microsoft.Extensions.AI используются
/// как есть — по одному chat на запрос модели и execute_tool на вызов тула,
/// без собственных дубликатов и без полных текстов сообщений модели; trace id
/// корневого спана пишется только в зафиксированный ответ помощника.
/// </summary>
[TestClass]
public sealed class ChatTurnTracingTests : ChatDatabaseTests
{
	/// <summary>Источник спанов хода чата — часть контракта трассировки.</summary>
	private const string ChatsSourceName = "TransactionJournal.Chats";

	/// <summary>Встроенный источник спанов Microsoft.Extensions.AI.</summary>
	private const string MeaiSourceName = "Experimental.Microsoft.Extensions.AI";

	[TestMethod]
	[Description("Корневой спан chat_turn несёт id чата, id вопроса и число запросов модели")]
	// Связь хода с чатом и вопросом известна до старта стрима; итерации —
	// запросы модели, дошедшие до клиента внутри конвейера (тул-сценарий
	// делает два запроса), отдельного события итераций не создаётся.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-chat-turn-root
	// Traceability: openspec:chats/history#requirement-trace-otel-two-surfaces
	public async Task TryIfChatTurnSpanCarriesChatAndMessageIdsAndIterationCount()
	{
		// Arrange: слушатель источника чатов ставится до хода; тул-сценарий
		// заставляет цикл сделать два запроса модели.
		using var collector = new SpanCollector(ChatsSourceName);
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ChatTools.GetMarketSnapshotToolName, "BTC")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Котировка BTC доступна.")],
		];
		var service = CreateService(chatClient, MarketWithQuote());
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Что с котировкой BTC?");

		// Act: ход выполняется целиком — от вопроса до фиксации ответа.
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: ровно один корневой спан chat_turn, без дубликатов.
		var turns = collector.Named("chat_turn");
		Assert.That(turns, Has.Count.EqualTo(1));
		var turn = turns.Single();

		// Assert: атрибуты связи и числа итераций.
		Assert.That(turn.GetTagItem("tj.chat.id"), Is.EqualTo(userMessage.ChatId));
		Assert.That(turn.GetTagItem("tj.chat.message.id"), Is.EqualTo(userMessage.Id));
		Assert.That(turn.GetTagItem("tj.chat.iterations"), Is.EqualTo(2));

		// Assert: ход без отмены — признака отмены нет, статус не-ошибка.
		Assert.That(turn.GetTagItem("tj.chat.cancelled"), Is.Null);
		Assert.That(turn.Status, Is.EqualTo(ActivityStatusCode.Unset));
	}

	[TestMethod]
	[Description("Trace id корневого спана пишется в ответ помощника, у владельца null")]
	// Корреляция «доменная запись → строка трассы»: W3C TraceId (32 hex)
	// корневого chat_turn фиксируется только в ответе помощника.
	// Traceability: openspec:chats/history#scenario-chat-answer-carries-trace-id
	// Traceability: openspec:chats/history#scenario-chat-owner-message-no-trace-id
	public async Task TryIfAssistantAnswerCarriesTurnTraceIdAndOwnerStaysNull()
	{
		// Arrange: обычный текстовый ход без тулов.
		using var collector = new SpanCollector(ChatsSourceName);
		var chatClient = new FakeChatClient();
		chatClient.Script = [[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ по журналу.")]];
		var service = CreateService(chatClient, new Mock<IChatMarketReader>(MockBehavior.Strict));
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Как выглядит структура?");

		// Act
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: trace id ответа равен W3C TraceId корневого chat_turn.
		var turn = collector.Named("chat_turn").Single();
		var messages = await CreateStore().ListMessagesAsync(userMessage.ChatId);
		var storedUser = messages.Single(message => message.Role == ChatMessageRole.User);
		var storedAssistant = messages.Single(message => message.Role == ChatMessageRole.Assistant);
		Assert.That(storedAssistant.TraceId, Is.EqualTo(turn.TraceId.ToHexString()));
		Assert.That(storedAssistant.TraceId, Does.Match(new Regex("^[0-9a-f]{32}$")));

		// Assert: сообщение владельца trace id не получает вовсе.
		Assert.That(storedUser.TraceId, Is.Null);
	}

	[TestMethod]
	[Description("Отменённый ход помечается tj.chat.cancelled=true со статусом не-ошибки и без записи ответа")]
	// Отмена владельцем — не ошибка: спан помечается признаком отмены, статус
	// остаётся не-ошибкой, ответ не фиксируется, а связь хода с чатом и
	// вопросом живёт в атрибутах строки трассы.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-chat-cancel-not-error
	// Traceability: openspec:chats/history#scenario-chat-cancelled-turn-trace-only
	public async Task ThrowOnCancelledTurnMarksCancelledWithNonErrorStatus()
	{
		// Arrange: клиент модели получает уже отменённый токен и бросает
		// OperationCanceledException до первого кадра стрима.
		using var collector = new SpanCollector(ChatsSourceName);
		var chatClient = new FakeChatClient { CancelBeforeFirstFrame = true };
		var service = CreateService(chatClient, new Mock<IChatMarketReader>(MockBehavior.Strict));
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Что с котировкой BTC?");
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		// Act: отмена пробрасывается вызывающему как есть; EF и инфраструктура
		// могут доставить её наследником OperationCanceledException.
		Assert.CatchAsync<OperationCanceledException>(async () =>
			await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage, cancellation.Token)));

		// Assert: спан хода существует, помечен отменой, статус не-ошибка.
		var turn = collector.Named("chat_turn").Single();
		Assert.That(turn.GetTagItem("tj.chat.cancelled"), Is.EqualTo(true));
		Assert.That(turn.Status, Is.EqualTo(ActivityStatusCode.Unset));

		// Assert: связь с чатом и вопросом восстанавливается по атрибутам.
		Assert.That(turn.GetTagItem("tj.chat.id"), Is.EqualTo(userMessage.ChatId));
		Assert.That(turn.GetTagItem("tj.chat.message.id"), Is.EqualTo(userMessage.Id));

		// Assert: запись ответа не создана — в истории только вопрос владельца.
		var messages = await CreateStore().ListMessagesAsync(userMessage.ChatId);
		Assert.That(messages.Select(message => message.Role), Is.EqualTo(new[] { ChatMessageRole.User }));
	}

	[TestMethod]
	[Description("Встроенные спаны идут как есть: один orchestrate_tools, chat на запрос модели, execute_tool на вызов тула")]
	// Собственные дубликаты встроенных спанов запрещены: состав и родственные
	// связи проверяются на фейковом приёмнике ActivityListener — chat_turn
	// родитель orchestrate_tools, chat и execute_tool — его дети.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-meai-spans-verbatim
	public async Task TryIfMeaiSpansEmittedVerbatimWithoutDuplicates()
	{
		// Arrange: тул-сценарий из двух запросов модели с моделью glm-5.3.
		using var collector = new SpanCollector(ChatsSourceName, MeaiSourceName);
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ChatTools.GetMarketSnapshotToolName, "BTC")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Котировка BTC доступна.")],
		];
		var service = CreateService(chatClient, MarketWithQuote());
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Что с котировкой BTC?");

		// Act
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: ровно по одному спану orchestrate_tools и execute_tool,
		// по одному chat glm-5.3 на каждый запрос модели — без дубликатов.
		var turn = collector.Named("chat_turn").Single();
		var orchestrate = collector.Named("orchestrate_tools");
		var chats = collector.Named("chat glm-5.3");
		var executeTool = collector.Named("execute_tool get_market_snapshot");
		Assert.That(orchestrate, Has.Count.EqualTo(1));
		Assert.That(chats, Has.Count.EqualTo(2));
		Assert.That(executeTool, Has.Count.EqualTo(1));

		// Assert: другой встроенной инструментации в источнике не появилось.
		var unexpected = collector.Started
			.Where(activity => activity.Source.Name == MeaiSourceName)
			.Select(activity => activity.OperationName)
			.Except(new[] { "orchestrate_tools", "chat glm-5.3", "execute_tool get_market_snapshot" })
			.ToList();
		Assert.That(unexpected, Is.Empty);

		// Assert: родственные связи — chat_turn корень, orchestrate_tools его
		// ребёнок, chat и execute_tool — дети orchestrate_tools; trace id общий.
		Assert.That(orchestrate[0].ParentSpanId, Is.EqualTo(turn.SpanId));
		Assert.That(chats.All(span => span.ParentSpanId == orchestrate[0].SpanId), Is.True);
		Assert.That(executeTool[0].ParentSpanId, Is.EqualTo(orchestrate[0].SpanId));
		Assert.That(collector.Started.All(span => span.TraceId == turn.TraceId), Is.True);

		// Assert: семантика gen_ai на встроенных спанах сохранена.
		Assert.That(chats.All(span => span.GetTagItem("gen_ai.operation.name") as string == "chat"), Is.True);
		Assert.That(executeTool[0].GetTagItem("gen_ai.tool.name"), Is.EqualTo(ChatTools.GetMarketSnapshotToolName));
	}

	[TestMethod]
	[Description("Событие tj.tool.args ложится на спан execute_tool с JSON аргументов вызова")]
	// Аргументы тулов — событие в момент исполнения на встроенном спане
	// execute_tool; дефолтный уровень детализации detailed.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-detailed-with-tool-args
	// Traceability: openspec:ops/agent-tracing#requirement-trace-metadata-only
	public async Task TryIfToolArgsEventLandsOnExecuteToolSpanWithJson()
	{
		// Arrange: тул-сценарий с вызовом снимка рынка по BTC.
		using var collector = new SpanCollector(ChatsSourceName, MeaiSourceName);
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ChatTools.GetMarketSnapshotToolName, "BTC")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Котировка BTC доступна.")],
		];
		var service = CreateService(chatClient, MarketWithQuote());
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Что с котировкой BTC?");

		// Act
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: событие на спане execute_tool, JSON аргументов без усечения.
		var executeTool = collector.Named("execute_tool get_market_snapshot").Single();
		var argsEvent = executeTool.Events.Single(e => e.Name == "tj.tool.args");
		Assert.That(argsEvent.Tags.Single(tag => tag.Key == "tj.tool.args.json").Value, Is.EqualTo("{\"baseCoin\":\"BTC\"}"));
		Assert.That(argsEvent.Tags.Single(tag => tag.Key == "tj.tool.args.truncated").Value, Is.EqualTo(false));
	}

	[TestMethod]
	[Description("Полные тексты вопроса и ответа модели не попадают ни в атрибуты, ни в события спанов")]
	// EnableSensitiveData всегда false: metadata-only — ни вопрос владельца,
	// ни текст ответа, ни результат тула в трассе не присутствуют.
	// Traceability: openspec:ops/agent-tracing#requirement-trace-metadata-only
	// Traceability: openspec:ops/agent-tracing#scenario-trace-no-prompt-completion-texts
	public async Task TryIfPromptAndCompletionTextsNeverReachTrace()
	{
		// Arrange: тул-сценарий с текстом результата, совпадающим с ответом.
		using var collector = new SpanCollector(ChatsSourceName, MeaiSourceName);
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ChatTools.GetMarketSnapshotToolName, "BTC")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Котировка BTC 108975.4 USDT.")],
		];
		var service = CreateService(chatClient, MarketWithQuote());
		var question = "Что с котировкой BTC?";
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), question);

		// Act
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: ни один атрибут или событие ни одного спана не содержит
		// вопрос, ответ модели или объём данных результата тула.
		var texts = CollectTagTexts(collector.Started);
		Assert.That(texts.Any(text => text.Contains(question, StringComparison.Ordinal)), Is.False);
		Assert.That(texts.Any(text => text.Contains("108975.4", StringComparison.Ordinal)), Is.False);
		Assert.That(texts.Any(text => text.Contains("Котировка BTC 108975.4", StringComparison.Ordinal)), Is.False);
	}

	[TestMethod]
	[Description("Ход без тулов считает одну итерацию — один запрос модели")]
	// Число итераций — запросы модели, дошедшие до клиента: текстовый ответ
	// делает ровно один запрос, события итераций не создаются.
	// Traceability: openspec:ops/agent-tracing#requirement-trace-detail-levels
	public async Task TryIfTurnWithoutToolsCountsSingleModelRequest()
	{
		// Arrange: модель отвечает текстом без тул-вызовов.
		using var collector = new SpanCollector(ChatsSourceName, MeaiSourceName);
		var chatClient = new FakeChatClient();
		chatClient.Script = [[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ.")]];
		var service = CreateService(chatClient, new Mock<IChatMarketReader>(MockBehavior.Strict));
		var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Как структура?");

		// Act
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

		// Assert: одна итерация и один спан chat glm-5.3 — задержки читаются
		// из длительности встроенного спана, отдельных событий итераций нет.
		var turn = collector.Named("chat_turn").Single();
		Assert.That(turn.GetTagItem("tj.chat.iterations"), Is.EqualTo(1));
		Assert.That(collector.Named("chat glm-5.3"), Has.Count.EqualTo(1));
		Assert.That(collector.Named("execute_tool get_market_snapshot"), Is.Empty);
	}

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

	/// <summary>Тексты всех атрибутов и событий спанов — для проверки metadata-only.</summary>
	private static List<string> CollectTagTexts(List<Activity> spans)
	{
		var texts = new List<string>();
		foreach (var span in spans)
		{
			texts.AddRange(span.Tags.Select(tag => tag.Value?.ToString() ?? string.Empty));
			foreach (var e in span.Events)
			{
				texts.Add(e.Name);
				texts.AddRange(e.Tags.Select(tag => tag.Value?.ToString() ?? string.Empty));
			}
		}

		return texts;
	}

	/// <summary>Кадр модели с вызовом рыночного инструмента.</summary>
	private static ChatResponseUpdate ToolCallFrame(string callId, string toolName, string baseCoin) => new()
	{
		Role = ChatRole.Assistant,
		Contents = { new FunctionCallContent(callId, toolName, new Dictionary<string, object?> { ["baseCoin"] = baseCoin }) },
	};

	/// <summary>Параметры создания чата: модель glm-5.3, привязка и полный набор источников.</summary>
	private static ChatStartParameters Start(long? constructionId) => new()
	{
		Model = "glm-5.3",
		ConstructionId = constructionId,
		Sources = ChatDataSourceCatalog.All,
	};

	/// <summary>Строгий порт биржи, отдающий снимок с фиксированной котировкой.</summary>
	private static Mock<IChatMarketReader> MarketWithQuote()
	{
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatMarketSnapshot
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = true,
				Symbol = "BTCUSDT",
				QuotePrice = 108975.4m,
			});
		return market;
	}

	/// <summary>Конвейер над настоящим хранилищем, заглушкой контекста и подменённым клиентом модели.</summary>
	private ChatService CreateService(FakeChatClient chatClient, Mock<IChatMarketReader> market)
	{
		return new ChatService(
			CreateStore(),
			new RecordingContextReader(),
			new ChatAgent(
				chatClient,
				new ChatTools(new Mock<IRuleCorpusReader>(MockBehavior.Loose).Object, market.Object),
				new ChatInstructions(Path.Combine(Path.GetTempPath(), "no-such-agent-prompt.md"))),
			new FixedTimeProvider(FixedNow));
	}

	/// <summary>
	/// Заглушка читателя снимка контекста: отдаёт фиксированный снимок,
	/// история чата в проверках трассы не участвует.
	/// </summary>
	private sealed class RecordingContextReader : IChatContextReader
	{
		public Task<ChatContextSnapshot> ReadAsync(
			long? constructionId,
			IReadOnlyList<ChatDataSource>? sources = null,
			CancellationToken cancellationToken = default) => Task.FromResult(new ChatContextSnapshot
			{
				Markdown = "# Снимок контекста",
				AsOf = FixedNow,
				IsConstructionClosed = false,
			});
	}

	/// <summary>
	/// Фейковый приёмник спанов: подписывается на перечисленные источники и
	/// собирает стартовавшие активности в порядке старта — заменяет sink
	/// трассы в проверках состава спанов.
	/// </summary>
	private sealed class SpanCollector : IDisposable
	{
		private readonly ActivityListener _listener;

		public SpanCollector(params string[] sourceNames)
		{
			var names = sourceNames.ToHashSet();
			_listener = new ActivityListener
			{
				ShouldListenTo = source => names.Contains(source.Name),
				Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
				ActivityStarted = activity => Started.Add(activity),
			};
			ActivitySource.AddActivityListener(_listener);
		}

		/// <summary>Стартовавшие активности в порядке старта.</summary>
		public List<Activity> Started { get; } = [];

		/// <summary>Активности с заданным именем операции.</summary>
		public List<Activity> Named(string name) => Started.Where(activity => activity.OperationName == name).ToList();

		public void Dispose() => _listener.Dispose();
	}

	/// <summary>
	/// Подмена клиента модели: на каждый запрос отдаёт кадры текущей сцены,
	/// при исчерпании повторяет последнюю сцену; режим отмены бросает
	/// OperationCanceledException по токену до первого кадра.
	/// </summary>
	private sealed class FakeChatClient : IChatClient
	{
		/// <summary>Сцены сценария; при исчерпании повторяется последняя сцена.</summary>
		public IReadOnlyList<IReadOnlyList<Func<ChatResponseUpdate>>> Script { get; set; } = [[]];

		/// <summary>Запросы, полученные клиентом, в порядке поступления.</summary>
		public List<List<ChatMessage>> Requests { get; } = [];

		/// <summary>Режим отмены: токен проверяется до отдачи первого кадра.</summary>
		public bool CancelBeforeFirstFrame { get; set; }

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
			if (CancelBeforeFirstFrame)
			{
				cancellationToken.ThrowIfCancellationRequested();
				yield break;
			}

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
