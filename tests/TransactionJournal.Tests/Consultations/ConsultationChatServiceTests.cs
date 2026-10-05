namespace TransactionJournal.Tests.Consultations;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Consultations;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Infrastructure.Consultations;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Сквозные проверки конвейера сообщения консультаций: сообщение владельца
/// создаёт диалог первым сообщением, ответ ассистента фиксируется по завершении
/// стрима с as-of и рыночным следом использованных инструментов, история
/// диалога передаётся агенту до вопроса.
/// </summary>
[TestClass]
public sealed class ConsultationChatServiceTests
{
	/// <summary>Фиксированный момент as-of сообщений и рыночных данных.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private string _directory = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой папкой per-construction баз.
		_directory = Path.Combine(Path.GetTempPath(), $"consultation-chat-tests-{Guid.NewGuid():N}");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файлы баз открытыми — сбрасываем перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[TestMethod]
	[Description("Ответ ассистента с рыночным тулом фиксируется в диалоге с рыночным следом и as-of")]
	// Рыночный след сохраняется в самом сообщении: какие инструменты вызывались
	// и с какими as-of их данные — происхождение рекомендаций проверяемо постфактум.
	// Traceability: openspec:consultations/history#scenario-history-market-trace-persisted
	public async Task TryIfAssistantUsedMarketTool_TracePersistedInMessage()
	{
		// Arrange: модель сначала вызывает снимок рынка, затем отвечает текстом;
		// биржа отдаёт марку с фиксированным as-of.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ConsultationTools.GetMarketSnapshotToolName, "BTC")],
			[
				() => new ChatResponseUpdate(ChatRole.Assistant, "Марка BTC "),
				() => new ChatResponseUpdate(ChatRole.Assistant, "108975.4 USDT."),
			],
		];
		var market = new Mock<IConsultationMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConsultationMarketSnapshot
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = true,
				Symbol = "BTCUSDT",
				MarkPrice = 108975.4m,
			});
		var service = CreateService(chatClient, market);

		// Act: первое сообщение создаёт диалог, ответ стримится до конца.
		var userMessage = await service.AppendUserMessageAsync(7, null, "Что с маркой BTC?");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(7, userMessage));

		// Assert: сообщение владельца без следа, ответ ассистента — с следом тула.
		var messages = await CreateStore().ListMessagesAsync(7, userMessage.DialogueId);
		Assert.That(messages, Has.Count.EqualTo(2));
		var storedUser = messages.Single(message => message.Role == ConsultationMessageRole.User);
		Assert.That(storedUser.MarketTrace, Is.Null);
		var storedAssistant = messages.Single(message => message.Role == ConsultationMessageRole.Assistant);
		Assert.That(storedAssistant.Text, Is.EqualTo("Марка BTC 108975.4 USDT."));
		Assert.That(storedAssistant.MarketTrace, Is.Not.Null);
		var invocations = storedAssistant.MarketTrace!.Invocations;
		Assert.That(invocations, Has.Count.EqualTo(1));
		Assert.That(invocations[0].ToolName, Is.EqualTo(ConsultationTools.GetMarketSnapshotToolName));
		Assert.That(invocations[0].Arguments, Is.EqualTo("{\"baseCoin\":\"BTC\"}"));
		Assert.That(invocations[0].DataAsOf, Is.EqualTo(FixedNow));
	}

	[TestMethod]
	[Description("История диалога передаётся агенту до вопроса, ответ без тулов следа не получает")]
	// Ассистенту видна история только своего диалога; ответ по журналу и корпусу
	// без инструментов рыночного следа не создаёт.
	// Traceability: openspec:consultations/history#scenario-history-dialogues-isolated
	// Traceability: openspec:consultations/history#scenario-history-market-trace-persisted
	public async Task TryIfFollowUpWithoutTools_HistoryPrecedesQuestionAndTraceStaysNull()
	{
		// Arrange: в диалоге уже был обмен вопросом и ответом с следом.
		var chatClient = new FakeChatClient();
		chatClient.Script = [[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ по журналу.")]];
		var market = new Mock<IConsultationMarketReader>(MockBehavior.Strict);
		var service = CreateService(chatClient, market);
		var first = await service.AppendUserMessageAsync(7, null, "Первый вопрос.");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(7, first));

		// Act: продолжение диалога без рыночных инструментов.
		var second = await service.AppendUserMessageAsync(7, first.DialogueId, "Второй вопрос.");
		_ = await CollectAsync(service.StreamAssistantAnswerAsync(7, second));

		// Assert: конвейер передал агенту истории первого обмена и вопрос последним —
		// проверяется второй запрос: ответ на продолжение диалога.
		var request = chatClient.Requests[1];
		Assert.That(request[1].Text, Is.EqualTo("Первый вопрос."));
		Assert.That(request[2].Text, Is.EqualTo("Ответ по журналу."));
		Assert.That(request[3].Text, Does.Contain("Второй вопрос."));

		// Assert: ответ без тулов зафиксирован без рыночного следа.
		var messages = await CreateStore().ListMessagesAsync(7, first.DialogueId);
		Assert.That(messages, Has.Count.EqualTo(4));
		var lastAssistant = messages.Last(message => message.Role == ConsultationMessageRole.Assistant);
		Assert.That(lastAssistant.Text, Is.EqualTo("Ответ по журналу."));
		Assert.That(lastAssistant.MarketTrace, Is.Null);
	}

	[TestMethod]
	[Description("Сообщение владельца без вопроса отвергается исключением")]
	// Пустой вопрос не создаёт диалог: сообщение без текста сохранять нечего.
	// Traceability: openspec:consultations/history#scenario-history-created-by-first-message
	[DataRow("")]
	[DataRow("   ")]
	public void ThrowOnEmptyQuestion(string question)
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IConsultationMarketReader>(MockBehavior.Loose));

		// Act — Assert
		Assert.ThrowsAsync<ArgumentException>(async () => await service.AppendUserMessageAsync(7, null, question));
	}

	[TestMethod]
	[Description("Стрим ответа без зафиксированного сообщения владельца отвергается исключением")]
	// Ответу нужен якорь в диалоге: вопрос владельца обязан быть зафиксирован
	// до старта генерации, иначе историю и след не на что ссылать.
	// Traceability: openspec:consultations/history#scenario-history-market-trace-persisted
	public void ThrowOnNullUserMessage()
	{
		// Arrange
		var service = CreateService(new FakeChatClient(), new Mock<IConsultationMarketReader>(MockBehavior.Loose));

		// Act — Assert: перечисление стрима бросает исключение до первого кадра.
		Assert.ThrowsAsync<ArgumentNullException>(async () => await CollectAsync(service.StreamAssistantAnswerAsync(7, null!)));
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

	/// <summary>Хранилище над папкой проверки: каждое обращение создаёт независимый контекст.</summary>
	private ConsultationStore CreateStore() => new(_directory);

	/// <summary>Конвейер над настоящим хранилищем и подменённым клиентом модели.</summary>
	private ConsultationChatService CreateService(FakeChatClient chatClient, Mock<IConsultationMarketReader> market) => new(
		CreateStore(),
		new StubContextReader(),
		new ConsultationAgent(
			chatClient,
			new ConsultationTools(new Mock<IRuleCorpusReader>(MockBehavior.Loose).Object, market.Object),
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

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Агентный цикл обязан использовать стриминг.");

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose()
		{
		}
	}
}
