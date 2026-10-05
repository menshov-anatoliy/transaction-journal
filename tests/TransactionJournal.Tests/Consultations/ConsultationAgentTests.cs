namespace TransactionJournal.Tests.Consultations;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Consultations;
using TransactionJournal.Consultations.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки агентного цикла консультаций: ответ стримится обновлениями чата,
/// конвейер сообщения собирается из инструкций, истории диалога и вопроса со
/// снимком контекста, рыночный инструмент реально исполняется внутри цикла и
/// его результат возвращается модели, а зацикливание тул-вызовов останавливается
/// потолком итераций без исключения.
/// </summary>
[TestClass]
public sealed class ConsultationAgentTests
{
	/// <summary>Фиксированный момент as-of для детерминированных снимков и ответов.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Ответ стримится текстовыми обновлениями, вопрос уходит вместе со снимком контекста")]
	public async Task TryIfStreamAnswerYieldsTextChunksAndComposesQuestion()
	{
		// Arrange: модель стримит ответ двумя кадрами одного запроса; рыночный
		// порт под строгим моком — агент без инструментов не имеет права его трогать.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[
				() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ "),
				() => new ChatResponseUpdate(ChatRole.Assistant, "начинается."),
			],
		];
		var market = new Mock<IConsultationMarketReader>(MockBehavior.Strict);
		var agent = CreateAgent(chatClient, market);

		// Act: задаём вопрос со снимком пустой истории.
		var updates = await CollectAsync(agent.StreamAnswerAsync(Snapshot(), [], "Как выглядит структура?"));

		// Assert: оба кадра стрима дошли до вызывающего без склейки — текстовые
		// обновления агент отдаёт один к одному.
		Assert.That(updates, Has.Count.EqualTo(2));
		Assert.That(string.Concat(updates.Select(update => update.Text)), Is.EqualTo("Ответ начинается."));

		// Assert: конвейер сообщения — инструкции системой, вопрос со снимком последним.
		var request = chatClient.Requests.Single();
		Assert.That(request[0].Role, Is.EqualTo(ChatRole.System));
		Assert.That(request[0].Text, Is.Not.Empty);
		Assert.That(request[^1].Role, Is.EqualTo(ChatRole.User));
		Assert.That(request[^1].Text, Does.Contain("# Снимок конструкции"));
		Assert.That(request[^1].Text, Does.Contain("Как выглядит структура?"));
		Assert.That(market.Invocations, Is.Empty);
	}

	[TestMethod]
	[Description("История диалога передаётся перед вопросом с ролями авторов")]
	public async Task TryIfHistoryPrecedesQuestion()
	{
		// Arrange: в диалоге уже был обмен вопросом и ответом.
		var chatClient = new FakeChatClient();
		chatClient.Script = [[() => new ChatResponseUpdate(ChatRole.Assistant, "Новый ответ.")]];
		var market = new Mock<IConsultationMarketReader>(MockBehavior.Strict);
		var agent = CreateAgent(chatClient, market);
		var history = new List<ConsultationMessage>
		{
			new() { Role = ConsultationMessageRole.User, Text = "Прошлый вопрос", AsOf = FixedNow },
			new() { Role = ConsultationMessageRole.Assistant, Text = "Прошлый ответ", AsOf = FixedNow },
		};

		// Act: задаём следующий вопрос диалога.
		_ = await CollectAsync(agent.StreamAnswerAsync(Snapshot(), history, "Следующий вопрос"));

		// Assert: порядок — инструкции, история владельца и ассистента, вопрос.
		var roles = chatClient.Requests.Single().Select(message => message.Role).ToList();
		Assert.That(roles, Is.EqualTo(new[] { ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User }));
		var request = chatClient.Requests.Single();
		Assert.That(request[1].Text, Is.EqualTo("Прошлый вопрос"));
		Assert.That(request[2].Text, Is.EqualTo("Прошлый ответ"));
		Assert.That(request[3].Text, Does.Contain("Следующий вопрос"));
	}

	[TestMethod]
	[Description("Рыночный инструмент исполняется в цикле, результат возвращается модели следующим запросом")]
	public async Task TryIfToolLoopExecutesMarketTool()
	{
		// Arrange: сцена 1 — модель вызывает снимок рынка, сцена 2 — отвечает текстом.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ConsultationTools.GetMarketSnapshotToolName, "BTC")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Марка 108975.4.")],
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
		var agent = CreateAgent(chatClient, market);

		// Act: вопрос провоцирует модель обратиться к инструменту.
		var updates = await CollectAsync(agent.StreamAnswerAsync(Snapshot(), [], "Что с маркой BTC?"));

		// Assert: биржевой запрос выполнен ровно один раз на один вызов инструмента.
		// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
		market.Verify(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()), Times.Once);
		market.VerifyNoOtherCalls();

		// Assert: результат инструмента ушёл модели вторым запросом сообщением роли Tool.
		Assert.That(chatClient.Requests, Has.Count.EqualTo(2));
		var secondRequest = chatClient.Requests[1];
		Assert.That(secondRequest, Has.Count.EqualTo(4));
		Assert.That(secondRequest[^1].Role, Is.EqualTo(ChatRole.Tool));

		// Assert: текстовый ответ модели после тула дошёл до вызывающего.
		Assert.That(updates.Select(update => update.Text), Does.Contain("Марка 108975.4."));
	}

	[TestMethod]
	[Description("Модель, упорствующая с тул-вызовами, останавливается потолком итераций без исключения")]
	public async Task TryIfNeverEndingToolCallsStopAtIterationCap()
	{
		// Arrange: модель в каждой сцене требует новый вызов того же тула —
		// сценарий зацикливания; id вызовов уникальны, как у настоящей модели.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame($"call-{chatClient.Requests.Count}", ConsultationTools.GetMarketSnapshotToolName, "BTC")],
		];
		var market = new Mock<IConsultationMarketReader>(MockBehavior.Loose);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ConsultationMarketSnapshot { BaseCoin = "BTC", AsOf = FixedNow, IsAvailable = true });
		var agent = CreateAgent(chatClient, market);

		// Act: стримим ответ — исключения быть не должно, цикл обрывается потолком.
		var updates = await CollectAsync(agent.StreamAnswerAsync(Snapshot(), [], "Вопрос без конца"));

		// Assert: биржевых запросов ровно столько, сколько допускает потолок итераций.
		// Traceability: openspec:consultations/tools#scenario-tools-iteration-cap
		market.Verify(
			reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()),
			Times.Exactly(ConsultationAgent.MaximumIterationsPerRequest));

		// Assert: последний запрос к модели ушёл без инструментов — цикл завершён
		// возвратом последнего ответа, незакрытый тул-вызов проходит вызывающему как есть.
		Assert.That(chatClient.Requests.Count, Is.EqualTo(ConsultationAgent.MaximumIterationsPerRequest + 1));
		Assert.That(updates, Is.Not.Empty);
	}

	[TestMethod]
	[Description("Тул-вызов агентного цикла записывается в рыночный след с as-of отданных данных")]
	// Traceability: openspec:consultations/history#scenario-history-market-trace-persisted
	public async Task TryIfMarketToolInvoked_TraceRecordsInvocationWithAsOf()
	{
		// Arrange: сцена 1 — модель вызывает снимок рынка, сцена 2 — отвечает текстом;
		// биржа отвечает маркой с фиксированным as-of.
		var chatClient = new FakeChatClient();
		chatClient.Script =
		[
			[() => ToolCallFrame("call-1", ConsultationTools.GetMarketSnapshotToolName, "BTC")],
			[() => new ChatResponseUpdate(ChatRole.Assistant, "Марка 108975.4.")],
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
		var agent = CreateAgent(chatClient, market);
		var traceRecorder = new ConsultationMarketTraceRecorder();

		// Act: вопрос провоцирует модель обратиться к рыночному инструменту.
		_ = await CollectAsync(agent.StreamAnswerAsync(Snapshot(), [], "Что с маркой BTC?", traceRecorder));

		// Assert: вызов записан в момент исполнения — имя, компактные аргументы
		// и as-of данных, отданных инструментом.
		var trace = traceRecorder.Build();
		Assert.That(trace, Is.Not.Null);
		Assert.That(trace!.Invocations, Has.Count.EqualTo(1));
		Assert.That(trace.Invocations[0].ToolName, Is.EqualTo(ConsultationTools.GetMarketSnapshotToolName));
		Assert.That(trace.Invocations[0].Arguments, Is.EqualTo("{\"baseCoin\":\"BTC\"}"));
		Assert.That(trace.Invocations[0].DataAsOf, Is.EqualTo(FixedNow));
	}

	[TestMethod]
	[Description("Ответ без инструментальных вызовов рыночного следа не создаёт")]
	// След в сообщении появляется только у ответов, использовавших инструменты:
	// у «чистого» ответа по журналу и корпусу рыночных данных нет.
	// Traceability: openspec:consultations/history#scenario-history-market-trace-persisted
	public async Task TryIfAnswerWithoutTools_TraceStaysEmpty()
	{
		// Arrange: модель отвечает текстом без тул-вызовов.
		var chatClient = new FakeChatClient();
		chatClient.Script = [[() => new ChatResponseUpdate(ChatRole.Assistant, "Ответ по журналу.")]];
		var market = new Mock<IConsultationMarketReader>(MockBehavior.Strict);
		var agent = CreateAgent(chatClient, market);
		var traceRecorder = new ConsultationMarketTraceRecorder();

		// Act
		_ = await CollectAsync(agent.StreamAnswerAsync(Snapshot(), [], "Как структура?", traceRecorder));

		// Assert
		Assert.That(traceRecorder.Build(), Is.Null);
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

	/// <summary>Кадр модели с вызовом рыночного инструмента.</summary>
	private static ChatResponseUpdate ToolCallFrame(string callId, string toolName, string baseCoin) => new()
	{
		Role = ChatRole.Assistant,
		Contents = { new FunctionCallContent(callId, toolName, new Dictionary<string, object?> { ["baseCoin"] = baseCoin }) },
	};

	/// <summary>Детерминированный снимок контекста конструкции.</summary>
	private static ConsultationContextSnapshot Snapshot() => new()
	{
		Markdown = "# Снимок конструкции",
		AsOf = FixedNow,
		IsConstructionClosed = false,
	};

	/// <summary>Агент над подменённым клиентом модели: инструкции берутся из несуществующего файла — встроенный дефолт.</summary>
	private static ConsultationAgent CreateAgent(IChatClient chatClient, Mock<IConsultationMarketReader> market) =>
		new(
			chatClient,
			new ConsultationTools(new Mock<IRuleCorpusReader>(MockBehavior.Loose).Object, market.Object),
			new ConsultationInstructions(Path.Combine(Path.GetTempPath(), "no-such-consultation-prompt.md")));

	/// <summary>
	/// Подмена клиента модели: на каждый запрос отдаёт кадры текущей сцены
	/// (сцена — один ответ модели, кадры — его стрим-чанки), при исчерпании
	/// повторяет последнюю сцену (модель упорствует), копии запросов сохраняет
	/// для проверок конвейера сообщений. Кадры создаются фабриками — один и
	/// тот же экземпляр обновления нельзя скармливать дважды.
	/// </summary>
	private sealed class FakeChatClient : IChatClient
	{
		/// <summary>Сцены сценария; при исчерпании повторяется последняя сцена.</summary>
		public IReadOnlyList<IReadOnlyList<Func<ChatResponseUpdate>>> Script { get; set; } = [[]];

		/// <summary>Запросы, полученные клиентом, в порядке поступления.</summary>
		public List<List<ChatMessage>> Requests { get; } = [];

		/// <summary>Число запросов к клиенту модели.</summary>
		public int RequestCount => Requests.Count;

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
			// Сценарий исчерпан — повторяем последнюю сцену: модель упорствует с тул-коллами.
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
