namespace TransactionJournal.Consultations;

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using TransactionJournal.Consultations.Ports;

/// <summary>
/// Агентный цикл консультаций: keyed <see cref="IChatClient"/> из конфигурации
/// оборачивается FunctionInvokingChatClient с потолком
/// <see cref="MaximumIterationsPerRequest"/> — глубина инструментальных вызовов
/// ограничена ~6 биржевыми запросами на сообщение. На каждое сообщение
/// владельца собирается конвейер: инструкции агента (system), история диалога,
/// вопрос вместе со снимком контекста; ответ стримится
/// <see cref="ChatResponseUpdate"/> — текстовые чанки идут в UI, tool-вызовы
/// проходят через поток для рыночного следа ответа.
/// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
/// </summary>
public sealed class ConsultationAgent
{
	/// <summary>Ключ keyed-регистрации IChatClient модели чата в composition root.</summary>
	public const string ChatClientServiceKey = "consultations";

	/// <summary>Потолок итераций агентного цикла на одно сообщение: ~6 биржевых запросов.</summary>
	public const int MaximumIterationsPerRequest = 6;

	private readonly IChatClient _chatClient;

	private readonly ConsultationTools _tools;

	private readonly ConsultationInstructions _instructions;

	/// <summary>Создаёт агентный цикл над keyed клиентом модели и реестром инструментов.</summary>
	/// <param name="chatClient">Клиент модели чата, разрешаемый по ключу из конфигурации.</param>
	/// <param name="tools">Реестр read-only инструментов консультаций.</param>
	/// <param name="instructions">Источник инструкций агента.</param>
	public ConsultationAgent(
		[FromKeyedServices(ChatClientServiceKey)] IChatClient chatClient,
		ConsultationTools tools,
		ConsultationInstructions instructions)
	{
		ArgumentNullException.ThrowIfNull(chatClient);
		_tools = tools ?? throw new ArgumentNullException(nameof(tools));
		_instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));

		// Цикл с функциями и его потолок — механика проекта Consultations, а не
		// composition root: контракт потолка проверяется тестом на зацикливание.
		// Traceability: openspec:consultations/tools#scenario-tools-iteration-cap
		_chatClient = chatClient
			.AsBuilder()
			.UseFunctionInvocation(configure: static options => options.MaximumIterationsPerRequest = MaximumIterationsPerRequest)
			.Build();
	}

	/// <summary>
	/// Стримит ответ ассистента на вопрос владельца: инструкции задают
	/// поведение, история диалога передаётся как есть, вопрос отправляется
	/// вместе со снимком контекста; инструменты агентного цикла — ровно три
	/// read-only функции реестра. Накопитель следа заполняется тул-вызовами
	/// в момент их исполнения — по завершении стрима из него строится
	/// рыночный след сообщения ассистента.
	// Traceability: openspec:consultations/tools#requirement-tools-read-only-registry
	// Traceability: openspec:consultations/history#scenario-history-market-trace-persisted
	/// </summary>
	/// <param name="context">Снимок контекста конструкции, собранный кодом без LLM.</param>
	/// <param name="history">Предыдущие сообщения текущего диалога в порядке следования.</param>
	/// <param name="question">Вопрос владельца.</param>
	/// <param name="traceRecorder">Накопитель рыночного следа ответа; null — вызовы не записываются.</param>
	/// <param name="cancellationToken">Токен отмены генерации.</param>
	/// <returns>Поток обновлений ответа: текстовые чанки и tool-вызовы.</returns>
	public async IAsyncEnumerable<ChatResponseUpdate> StreamAnswerAsync(
		ConsultationContextSnapshot context,
		IReadOnlyList<ConsultationMessage> history,
		string question,
		ConsultationMarketTraceRecorder? traceRecorder = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(history);
		ArgumentException.ThrowIfNullOrWhiteSpace(question);

		var chatMessages = new List<ChatMessage>
		{
			// Инструкции перечитываются на каждое сообщение: правки владельца
			// действуют со следующего вопроса без перезапуска приложения.
			new(ChatRole.System, _instructions.Read()),
		};
		chatMessages.AddRange(history.Select(ToChatMessage));
		chatMessages.Add(new ChatMessage(ChatRole.User, ComposeUserMessage(context, question)));

		var options = new ChatOptions
		{
			// Реестр фиксирован контрактоном: три read-only инструмента, пишущих нет.
			Tools = [.. _tools.CreateTools(traceRecorder)],
			ToolMode = ChatToolMode.Auto,
		};

		await foreach (var update in _chatClient
			.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
			.ConfigureAwait(false))
		{
			yield return update;
		}
	}

	/// <summary>Сообщение диалога отображается в сообщение чата по роли автора.</summary>
	private static ChatMessage ToChatMessage(ConsultationMessage message) => new(
		message.Role == ConsultationMessageRole.User ? ChatRole.User : ChatRole.Assistant,
		message.Text);

	/// <summary>Вопрос владельца уходит вместе со снимком контекста: модель видит факты журнала в момент вопроса.</summary>
	private static string ComposeUserMessage(ConsultationContextSnapshot context, string question)
	{
		var builder = new StringBuilder();
		builder.AppendLine(context.Markdown.TrimEnd());
		builder.AppendLine();
		builder.AppendLine("## Вопрос владельца");
		builder.AppendLine();
		builder.Append(question.Trim());
		return builder.ToString();
	}
}
