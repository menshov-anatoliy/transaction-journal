namespace TransactionJournal.Chats;

using TransactionJournal.Chats.Ports;

/// <summary>
/// Накопитель следа источников одного ответа ассистента: инструменты исполняются
/// внутри агентного цикла, запись вызова ведётся в момент исполнения тула —
/// когда as-of отданных данных известен точно. Ссылки на карточки правил
/// записывает инструмент чтения корпуса, as-of данных журнала — конвейер
/// при сборке снимка контекста. По завершении стрима след фиксируется в
/// сообщении ассистента; ни одного источника ответ не использовал — следа нет.
// Traceability: openspec:chats/history#requirement-chat-message-composition
/// </summary>
public sealed class ChatSourceTraceRecorder
{
	private readonly List<ChatToolInvocation> _invocations = [];

	private readonly List<string> _ruleCardIds = [];

	private DateTimeOffset? _journalAsOf;

	/// <summary>Записывает один вызов инструмента: имя, компактные аргументы и as-of отданных данных.</summary>
	/// <param name="toolName">Имя инструмента реестра read-only функций.</param>
	/// <param name="arguments">Аргументы вызова в компактном виде.</param>
	/// <param name="dataAsOf">As-of данных тула; null — данных с as-of у вызова нет.</param>
	public void Record(string toolName, string arguments, DateTimeOffset? dataAsOf)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
		ArgumentException.ThrowIfNullOrWhiteSpace(arguments);
		_invocations.Add(new ChatToolInvocation
		{
			ToolName = toolName,
			Arguments = arguments,
			DataAsOf = dataAsOf,
		});
	}

	/// <summary>Записывает ссылку на прочитанную карточку правила; повторные чтения дают одну запись.</summary>
	/// <param name="cardId">Идентификатор карточки из индекса корпуса, например ac-01.</param>
	public void RecordRuleCard(string cardId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cardId);

		// Ссылка без повторов: ответ, дважды читавший одну карточку, отдаёт
		// в след одну запись — UI рендерит из списка ссылки без дедупликации.
		if (_ruleCardIds.Contains(cardId) == false)
		{
			_ruleCardIds.Add(cardId);
		}
	}

	/// <summary>Записывает as-of данных журнала, отданных снимком контекста.</summary>
	/// <param name="asOf">Отметка as-of данных журнала в снимке.</param>
	public void RecordJournal(DateTimeOffset asOf) => _journalAsOf = asOf;

	/// <summary>Строит след источников сообщения; null — ни один источник ответ не использовал.</summary>
	public ChatSourceTrace? Build() => _invocations.Count == 0 && _ruleCardIds.Count == 0 && _journalAsOf is null
		? null
		: new ChatSourceTrace
		{
			Invocations = [.. _invocations],
			RuleCards = [.. _ruleCardIds],
			JournalAsOf = _journalAsOf,
		};
}
