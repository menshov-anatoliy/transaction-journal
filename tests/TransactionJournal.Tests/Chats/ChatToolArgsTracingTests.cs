namespace TransactionJournal.Tests.Chats;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки захвата аргументов тулов: на уровне detailed вызов тула несёт
/// событие tj.tool.args с JSON аргументов на текущей активности — в конвейере
/// это спан execute_tool; аргументы больше 2 КБ усекаются с маркером;
/// несериализуемое значение деградирует и не ломает вызов; на уровне basic
/// событий аргументов нет вовсе.
/// </summary>
[TestClass]
public sealed class ChatToolArgsTracingTests
{
	/// <summary>Имя события трассы с аргументами вызова тула.</summary>
	private const string EventName = "tj.tool.args";

	/// <summary>Предел длины JSON аргументов — 2 КБ, 2048 символов.</summary>
	private const int MaxJsonLength = 2048;

	/// <summary>Маркер усечения, дописываемый к обрезанному JSON.</summary>
	private const string TruncationMarker = "…[truncated]";

	[TestMethod]
	[Description("Detailed-вызов тула пишет событие tj.tool.args с JSON аргументов на текущую активность")]
	// Событие добавляется в момент исполнения тула на ближайшую активность —
	// в конвейере ChatAgent это спан execute_tool; JSON — компактный.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-detailed-with-tool-args
	public async Task TryIfDetailedToolCallAddsArgsEventWithJson()
	{
		// Arrange: реестр на дефолтных (detailed) опциях; слушатель ставит
		// активность-родителя, на которую ложится событие.
		using var harness = new ActivityHarness();
		var function = CreateRuleCardTool(new ChatTracingOptions());

		// Act: вызов тула под активностью слушателя.
		var result = await harness.InvokeAsync(function, new AIFunctionArguments { ["cardId"] = "ac-01" });

		// Assert: тул исполнился, событие одно, JSON компактный без усечения.
		Assert.That(result, Is.Not.Null);
		var activity = harness.Activity!;
		var argsEvent = activity.Events.Single(e => e.Name == EventName);
		Assert.That(argsEvent.Tags.Single(tag => tag.Key == "tj.tool.args.json").Value, Is.EqualTo("{\"cardId\":\"ac-01\"}"));
		Assert.That(argsEvent.Tags.Single(tag => tag.Key == "tj.tool.args.truncated").Value, Is.EqualTo(false));
	}

	[TestMethod]
	[Description("Обёртка сохраняет контракт тула: имя, описание и схему аргументов")]
	// Декоратор обязан быть прозрачным для модели: реестр инструментов после
	// обёртки объявляет те же имена и схемы, что и исходные функции.
	// Traceability: openspec:ops/agent-tracing#requirement-trace-detail-levels
	public void TryIfWrappedToolKeepsNameAndSchema()
	{
		// Arrange: реестр на дефолтных (detailed) опциях.
		var wrapped = CreateRuleCardTool(new ChatTracingOptions());

		// Assert: имя, описание и схема совпадают с контрактом реестра.
		Assert.That(wrapped.Name, Is.EqualTo(ChatTools.ReadRuleCardToolName));
		Assert.That(wrapped.Description, Is.Not.Empty);
		Assert.That(wrapped.JsonSchema.ValueKind, Is.EqualTo(JsonValueKind.Object));
		Assert.That(wrapped.JsonSchema.ToString(), Does.Contain("cardId"));
	}

	[TestMethod]
	[Description("Аргументы больше 2 КБ усекаются до 2048 символов с маркером усечения")]
	// Кап 2 КБ защищает файл трасс от раздувания: JSON обрезается, маркер
	// делает усечение явным, признак дублируется атрибутом.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-tool-args-capped
	public async Task TryIfOversizedToolArgsCappedWithMarker()
	{
		// Arrange: идентификатор карточки в 3000 символов — JSON заведомо
		// превышает предел.
		using var harness = new ActivityHarness();
		var function = CreateRuleCardTool(new ChatTracingOptions());

		// Act: вызов тула с длинным аргументом.
		var result = await harness.InvokeAsync(function, new AIFunctionArguments { ["cardId"] = new string('x', 3000) });

		// Assert: JSON в пределах 2 КБ, оканчивается маркером, признак
		// усечения выставлен; вызов тула при этом состоялся.
		Assert.That(result, Is.Not.Null);
		var activity = harness.Activity!;
		var argsEvent = activity.Events.Single(e => e.Name == EventName);
		var json = (string)argsEvent.Tags.Single(tag => tag.Key == "tj.tool.args.json").Value!;
		Assert.That(json.Length, Is.LessThanOrEqualTo(MaxJsonLength));
		Assert.That(json, Does.EndWith(TruncationMarker));
		Assert.That(argsEvent.Tags.Single(tag => tag.Key == "tj.tool.args.truncated").Value, Is.EqualTo(true));
	}

	[TestMethod]
	[Description("Basic-уровень не добавляет события аргументов вовсе")]
	// basic отсекает события tj.tool.args целиком: JSONL и OTLP видят один
	// и тот же состав трассы без зависящих от приёмника деталей.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-basic-without-tool-args
	public async Task TryIfBasicLevelToolCallHasNoArgsEvent()
	{
		// Arrange: реестр на уровне basic.
		using var harness = new ActivityHarness();
		var function = CreateRuleCardTool(new ChatTracingOptions { Detail = ChatTracingDetail.Basic });

		// Act: вызов тула под активностью слушателя.
		var result = await harness.InvokeAsync(function, new AIFunctionArguments { ["cardId"] = "ac-01" });

		// Assert: тул исполнился, событий на активности нет.
		Assert.That(result, Is.Not.Null);
		Assert.That(harness.Activity!.Events, Is.Empty);
	}

	[TestMethod]
	[Description("Несериализуемый аргумент деградирует в ToString и не ломает вызов тула")]
	// Сериализация устойчива: сбой сериализации значения уходит в ToString,
	// полный сбой сборки JSON только опускает событие — вызов тула живёт.
	// Traceability: openspec:ops/agent-tracing#requirement-trace-metadata-only
	public async Task TryIfUnserializableToolArgsDoNotBreakToolCall()
	{
		// Arrange: лишний аргумент с зацикленным объектом — JsonSerializer
		// падает на цикле, значение деградирует в ToString.
		using var harness = new ActivityHarness();
		var function = CreateRuleCardTool(new ChatTracingOptions());
		var cycle = new SelfReferencingNode();
		cycle.Self = cycle;

		// Act: вызов тула с обычным и несериализуемым аргументами.
		var result = await harness.InvokeAsync(function, new AIFunctionArguments
		{
			["cardId"] = "ac-01",
			["junk"] = cycle,
		});

		// Assert: вызов тула дошёл до корпуса и вернул ответ об отсутствии;
		// событие написано, в JSON аргумент — деградировавший в строку объект.
		Assert.That(result, Is.Not.Null);
		var activity = harness.Activity!;
		var argsEvent = activity.Events.Single(e => e.Name == EventName);
		var json = (string)argsEvent.Tags.Single(tag => tag.Key == "tj.tool.args.json").Value!;
		Assert.That(json, Does.Contain("\"cardId\":\"ac-01\""));
		Assert.That(json, Does.Contain(typeof(SelfReferencingNode).Name));
	}

	/// <summary>
	/// Инструмент чтения карточки из реестра чата: корпус подменён Moq-заглушкой
	/// без карточек — вызов безопасен и не требует рыночных данных.
	/// </summary>
	private static AIFunction CreateRuleCardTool(ChatTracingOptions tracingOptions)
	{
		var tools = new ChatTools(
			new Mock<IRuleCorpusReader>(MockBehavior.Loose).Object,
			new Mock<IChatMarketReader>(MockBehavior.Strict).Object,
			tracingOptions);
		return tools.CreateTools([ChatDataSource.RulesCorpus]).Single();
	}

	/// <summary>Зацикленный объект: сериализация падает с обнаружением цикла.</summary>
	private sealed class SelfReferencingNode
	{
		public SelfReferencingNode Self { get; set; } = null!;
	}

	/// <summary>
	/// Каркас проверки обёртки: слушатель-приёмник ставит активность-родителя,
	/// под которую вызывается тул. Источник и слушатель — на экземпляр с
	/// уникальным именем: глобальный список слушателей ActivitySource живёт до
	/// конца процесса, и лямбда не должна читать статические поля, чья
	/// инициализация могла ещё не завершиться к моменту вызова.
	/// </summary>
	private sealed class ActivityHarness : IDisposable
	{
		private readonly ActivitySource _source = new($"chat-tool-args-{Guid.NewGuid():N}");

		private readonly ActivityListener _listener;

		public ActivityHarness()
		{
			var sourceName = _source.Name;
			_listener = new ActivityListener
			{
				ShouldListenTo = source => source.Name == sourceName,
				Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
			};
			ActivitySource.AddActivityListener(_listener);
			Activity = _source.StartActivity("probe-tool-call");
		}

		/// <summary>Активность-родитель проверки; события тула ложатся на неё.</summary>
		public Activity? Activity { get; private set; }

		/// <summary>Вызывает тул под активностью каркаса.</summary>
		public async Task<object?> InvokeAsync(AIFunction function, AIFunctionArguments arguments)
		{
			var result = await function.InvokeAsync(arguments).ConfigureAwait(false);
			Activity?.Stop();
			return result;
		}

		public void Dispose()
		{
			Activity?.Dispose();
			_source.Dispose();
			_listener.Dispose();
		}
	}
}
