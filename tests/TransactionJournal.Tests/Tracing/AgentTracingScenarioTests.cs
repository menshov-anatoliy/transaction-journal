namespace TransactionJournal.Tests.Tracing;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Moq;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Chats;
using TransactionJournal.Tests.Hints.Corpus;
using TransactionJournal.Tests.Hints.Engine;
using Assert = NUnit.Framework.Assert;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Единый сценарий «обе агентские поверхности одним механизмом»: один
/// фейковый приёмник ActivityListener слушает оба доменных источника
/// (TransactionJournal.Chats и TransactionJournal.Hints) и встроенный
/// источник Microsoft.Extensions.AI; внутри одной проверки выполняются
/// проход подсказок и два хода чата — завершённый с вызовом тулов и
/// отменённый. Проверяется, что обе трассы попали в один приёмник, у каждой
/// трассы ровно один корневой спан, встроенные спаны чата используются как
/// есть без собственных дубликатов, на спанах прохода нет gen_ai.*, а trace
/// id корней добирается до доменных записей (итог прохода, ответ помощника).
/// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
/// </summary>
[TestClass]
public sealed class AgentTracingScenarioTests : ChatDatabaseTests
{
	/// <summary>Источник спанов хода чата — часть контракта единого механизма.</summary>
	private const string ChatsSourceName = "TransactionJournal.Chats";

	/// <summary>Источник спанов прохода подсказок — часть контракта единого механизма.</summary>
	private const string HintsSourceName = "TransactionJournal.Hints";

	/// <summary>Встроенный источник спанов Microsoft.Extensions.AI.</summary>
	private const string MeaiSourceName = "Experimental.Microsoft.Extensions.AI";

	/// <summary>Имена шести фаз прохода в порядке исполнения.</summary>
	private static readonly string[] PhaseNames =
	[
		"hint_pass.corpus_snapshot",
		"hint_pass.expire_withdrawn_rules",
		"hint_pass.journal_snapshot",
		"hint_pass.expire_closed_subjects",
		"hint_pass.quotes",
		"hint_pass.evaluate_triggers",
	];

	[TestMethod]
	[@Description("Проход подсказок и ходы чата в одном приёмнике: три трассы с единственными корнями, встроенные спаны без дубликатов, у прохода нет gen_ai.*")]
	// Обе поверхности трассируются одним механизмом поверх ActivitySource:
	// единый приёмник слушает оба доменных источника и встроенный; проход
	// подсказок и два хода чата (завершённый с тулом и отменённый) дают три
	// независимые трассы. Форма деревьев здесь проверена в объёме, закрывающем
	// сам контракт «одним механизмом»; детальные проверки форм отдельных
	// трасс живут в ChatTurnTracingTests и HintPassTracingTests.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-chat-turn-root
	// Traceability: openspec:ops/agent-tracing#scenario-trace-chat-cancel-not-error
	// Traceability: openspec:ops/agent-tracing#scenario-trace-hint-pass-phases
	// Traceability: openspec:ops/agent-tracing#scenario-trace-meai-spans-verbatim
	public async Task TryIfBothSurfacesLandInOneSinkWithDistinctRootsAndNoDuplicates()
	{
		// Arrange: единый приёмник на оба доменных источника и встроенный.
		using var collector = new SpanCollector(ChatsSourceName, HintsSourceName, MeaiSourceName);
		var dir = CorpusYaml.TempDir();
		try
		{
			// Корпус с одним срабатывающим правилом цели прибыли — для прохода.
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				clarity: "fuzzy",
				hintTemplate: "Результат конструкции {pnlPct}% — плановая прибыль достигнута.",
				character: "profit-target"));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(
						id: 7,
						capital: 1000m,
						profitValue: 10m,
						profitUnit: TargetUnit.Percent,
						totalPnL: 120m),
				],
			};

			// Чат с тул-сценарием: два запроса модели, между ними вызов тулов.
			var chatClient = new FakeChatClient();
			chatClient.Script =
			[
				[() => ToolCallFrame("call-1", ChatTools.GetMarketSnapshotToolName, "BTC")],
				[() => new ChatResponseUpdate(ChatRole.Assistant, "Котировка BTC доступна.")],
			];
			var service = CreateChatService(chatClient);
			var userMessage = await service.AppendUserMessageAsync(null, Start(7), "Что с котировкой BTC?");

			// Act: обе поверхности в одном тесте — проход подсказок и ход чата.
			var (hintResult, _) = await HintPassHarness.RunAsync(dir, snapshot);

			// В приложении каждый запрос стартует с чистым ambient-контекстом;
			// в одном тестовом потоке Activity.Current протекает сквозь границы
			// await, поэтому между независимыми поверхностями его сбрасываем —
			// иначе следующий корень унаследовал бы чужой trace id.
			Activity.Current = null;
			_ = await CollectAsync(service.StreamAssistantAnswerAsync(userMessage.ChatId, userMessage));

			// Отменённый ход — третья трасса того же механизма: клиент модели
			// бросает OperationCanceledException до первого кадра стрима.
			var cancelledService = CreateChatService(new FakeChatClient { CancelBeforeFirstFrame = true });
			var cancelledMessage = await cancelledService.AppendUserMessageAsync(null, Start(7), "Стоп-вопрос");
			using var cancellation = new CancellationTokenSource();
			await cancellation.CancelAsync();
			Activity.Current = null;
			Assert.CatchAsync<OperationCanceledException>(async () =>
				await CollectAsync(cancelledService.StreamAssistantAnswerAsync(cancelledMessage.ChatId, cancelledMessage, cancellation.Token)));

			// Assert: один приёмник поймал спаны всех трёх источников — обе
			// поверхности пишут трассы одним механизмом.
			var sources = collector.Started.Select(span => span.Source.Name).Distinct().ToList();
			Assert.That(sources, Is.EquivalentTo(new[] { ChatsSourceName, HintsSourceName, MeaiSourceName }));

			// Assert: три трассы по trace id, в каждой ровно один корень без
			// родителя — hint_pass, завершённый chat_turn, отменённый chat_turn.
			var byTrace = collector.Started
				.GroupBy(span => span.TraceId.ToHexString())
				.ToDictionary(group => group.Key, group => group.ToList());
			Assert.That(byTrace, Has.Count.EqualTo(3));
			var roots = collector.Started.Where(span => span.Parent is null).ToList();
			Assert.That(roots, Has.Count.EqualTo(3));
			var hintRoot = roots.Single(root => root.OperationName == "hint_pass");
			var completedTurn = roots.Single(root =>
				root.OperationName == "chat_turn" && root.GetTagItem("tj.chat.cancelled") is null);
			var cancelledTurn = roots.Single(root =>
				root.OperationName == "chat_turn" && root.GetTagItem("tj.chat.cancelled") is not null);
			Assert.That(
				roots.Select(root => root.TraceId.ToHexString()).Distinct().ToList(),
				Has.Count.EqualTo(3));

			// Трасса прохода: корень с исходом и счётчиками, затем шесть фаз в
			// порядке исполнения — и ни одного атрибута gen_ai.* на спанах
			// прохода (детерминированный проход модель не вызывает).
			var hintTrace = byTrace[hintRoot.TraceId.ToHexString()];
			Assert.That(hintTrace.Select(span => span.OperationName).ToArray(),
				Is.EqualTo(new[] { "hint_pass" }.Concat(PhaseNames).ToArray()));
			Assert.That(hintRoot.GetTagItem("tj.hints.outcome"), Is.EqualTo("completed"));
			Assert.That(hintRoot.GetTagItem("tj.hints.created"), Is.EqualTo(1));
			Assert.That(hintRoot.GetTagItem("tj.hints.expired"), Is.EqualTo(0));
			Assert.That(hintTrace.SelectMany(span => span.Tags).Any(tag =>
				tag.Key.StartsWith("gen_ai.", StringComparison.Ordinal)), Is.False);

			// Корреляция прохода: итог прохода несёт trace id корневого спана.
			Assert.That(hintResult.TraceId, Is.EqualTo(hintRoot.TraceId.ToHexString()));

			// Трасса завершённого хода: корень chat_turn, ребёнок
			// orchestrate_tools, по одному chat glm-5.3 на каждый запрос модели
			// и execute_tool на вызов тулов — других спанов в трассе нет, то
			// есть собственных дубликатов встроенных спанов не создано.
			var chatTrace = byTrace[completedTurn.TraceId.ToHexString()];
			Assert.That(chatTrace, Has.Count.EqualTo(5));
			var orchestrate = chatTrace.Single(span => span.OperationName == "orchestrate_tools");
			var modelChats = chatTrace.Where(span => span.OperationName == "chat glm-5.3").ToList();
			var executeTool = chatTrace.Single(span => span.OperationName == "execute_tool get_market_snapshot");
			Assert.That(modelChats, Has.Count.EqualTo(2));
			Assert.That(orchestrate.ParentSpanId, Is.EqualTo(completedTurn.SpanId));
			Assert.That(modelChats.All(span => span.ParentSpanId == orchestrate.SpanId), Is.True);
			Assert.That(executeTool.ParentSpanId, Is.EqualTo(orchestrate.SpanId));

			// Атрибуты корня и семантика gen_ai на встроенных спанах.
			Assert.That(completedTurn.GetTagItem("tj.chat.id"), Is.EqualTo(userMessage.ChatId));
			Assert.That(completedTurn.GetTagItem("tj.chat.message.id"), Is.EqualTo(userMessage.Id));
			Assert.That(completedTurn.GetTagItem("tj.chat.iterations"), Is.EqualTo(2));
			Assert.That(modelChats.All(span => span.GetTagItem("gen_ai.operation.name") as string == "chat"), Is.True);
			Assert.That(executeTool.GetTagItem("gen_ai.tool.name"), Is.EqualTo(ChatTools.GetMarketSnapshotToolName));

			// Отменённый ход в том же приёмнике: корень помечен отменой со
			// статусом не-ошибки, связь с чатом и вопросом — в атрибутах.
			Assert.That(cancelledTurn.GetTagItem("tj.chat.cancelled"), Is.EqualTo(true));
			Assert.That(cancelledTurn.Status, Is.EqualTo(ActivityStatusCode.Unset));
			Assert.That(cancelledTurn.GetTagItem("tj.chat.id"), Is.EqualTo(cancelledMessage.ChatId));
			Assert.That(cancelledTurn.GetTagItem("tj.chat.message.id"), Is.EqualTo(cancelledMessage.Id));

			// Корреляция чата: trace id ответа помощника равен trace id корня
			// завершённого хода, вопрос владельца записи-носителя не получил,
			// отменённый ход не создал ответа.
			var messages = await CreateStore().ListMessagesAsync(userMessage.ChatId);
			Assert.That(messages.Single(message => message.Role == ChatMessageRole.Assistant).TraceId,
				Is.EqualTo(completedTurn.TraceId.ToHexString()));
			Assert.That(messages.Single(message => message.Role == ChatMessageRole.User).TraceId, Is.Null);
			var cancelledMessages = await CreateStore().ListMessagesAsync(cancelledMessage.ChatId);
			Assert.That(cancelledMessages.Select(message => message.Role), Is.EqualTo(new[] { ChatMessageRole.User }));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
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

	/// <summary>Параметры создания чата: модель glm-5.3, привязка и полный набор источников.</summary>
	private static ChatStartParameters Start(long? constructionId) => new()
	{
		Model = "glm-5.3",
		ConstructionId = constructionId,
		Sources = ChatDataSourceCatalog.All,
	};

	/// <summary>Конвейер над настоящим хранилищем, заглушкой контекста и подменённым клиентом модели.</summary>
	private ChatService CreateChatService(FakeChatClient chatClient)
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
	/// собирает стартовавшие активности в порядке старта — единый приёмник
	/// обеих поверхностей в проверке «одним механизмом».
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
