namespace TransactionJournal.Consultations.Ports;

/// <summary>
/// Порт хранения консультаций: совокупность диалогов «один вопрос — один
/// диалог» в контексте одной конструкции. Диалог создаётся первым сообщением
/// владельца и удаляется им целиком — полное удаление без корзины; ассистент
/// видит историю только собственного диалога. Консультация — запись
/// окружения: домен журнала порт не видит, пересбор конструкции стирает её
/// консультации вместе со старой записью.
// Traceability: openspec:consultations/history#requirement-history-dialogue-per-question
// Traceability: openspec:consultations/history#requirement-history-environment-record
/// </summary>
public interface IConsultationStore
{
	/// <summary>Диалоги конструкции в порядке создания.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<ConsultationDialogue>> ListDialoguesAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Сохраняет сообщение и возвращает его с присвоенными идентификаторами.
	/// Отдельной команды создания диалога нет: диалог без идентификатора
	/// создаётся этим сообщением.
	// Traceability: openspec:consultations/history#scenario-history-created-by-first-message
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="dialogueId">Идентификатор диалога; null — создаётся новый диалог этим сообщением.</param>
	/// <param name="message">Черновик сообщения с незаполненным идентификатором.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ConsultationMessage> AppendMessageAsync(
		long constructionId,
		long? dialogueId,
		ConsultationMessageDraft message,
		CancellationToken cancellationToken = default);

	/// <summary>Сообщения диалога в порядке следования — история видна только этому диалогу.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="dialogueId">Идентификатор диалога.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<ConsultationMessage>> ListMessagesAsync(
		long constructionId,
		long dialogueId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Удаляет диалог со всеми его сообщениями без возможности восстановления;
	/// false — диалога нет или он принадлежит другой конструкции.
	// Traceability: openspec:consultations/history#scenario-history-hard-delete-dialogue
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="dialogueId">Идентификатор диалога.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<bool> DeleteDialogueAsync(long constructionId, long dialogueId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Стирает все консультации конструкции вместе со старой записью — история
	/// чата живёт и умирает вместе с конструкцией при её пересборе.
	// Traceability: openspec:consultations/history#scenario-history-rebuild-wipes
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task DeleteForConstructionAsync(long constructionId, CancellationToken cancellationToken = default);
}

/// <summary>Роль автора сообщения диалога.</summary>
public enum ConsultationMessageRole
{
	/// <summary>Сообщение владельца.</summary>
	User,

	/// <summary>Ответ ИИ-помощника.</summary>
	Assistant,
}

/// <summary>Диалог консультации — единичный обмен «один вопрос — один диалог».</summary>
public sealed record ConsultationDialogue
{
	/// <summary>Суррогатный ключ диалога; 0 у несохранённого диалога.</summary>
	public long Id { get; init; }

	/// <summary>Идентификатор конструкции, в контексте которой ведётся диалог.</summary>
	public long ConstructionId { get; init; }

	/// <summary>Момент создания диалога первым сообщением владельца.</summary>
	public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Черновик сообщения диалога с незаполненным идентификатором.</summary>
public sealed record ConsultationMessageDraft
{
	/// <summary>Роль автора сообщения.</summary>
	public required ConsultationMessageRole Role { get; init; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; init; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Рыночный след ответа ассистента; у сообщений владельца null.</summary>
	public ConsultationMarketTrace? MarketTrace { get; init; }
}

/// <summary>Сообщение диалога: роль, текст, as-of; у ассистента дополнительно рыночный след.</summary>
public sealed record ConsultationMessage
{
	/// <summary>Суррогатный ключ сообщения.</summary>
	public long Id { get; init; }

	/// <summary>Идентификатор диалога, в котором оставлено сообщение.</summary>
	public long DialogueId { get; init; }

	/// <summary>Роль автора сообщения.</summary>
	public required ConsultationMessageRole Role { get; init; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; init; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Рыночный след ответа ассистента; у сообщений владельца null.</summary>
	public ConsultationMarketTrace? MarketTrace { get; init; }
}

/// <summary>
/// Рыночный след ответа ассистента: какие инструменты вызывались и с какими
/// as-of их данные — происхождение рыночных рекомендаций проверяемо постфактум.
// Traceability: openspec:consultations/history#requirement-history-message-composition
/// </summary>
public sealed record ConsultationMarketTrace
{
	/// <summary>Вызовы инструментов в порядке следования.</summary>
	public required IReadOnlyList<ConsultationToolInvocation> Invocations { get; init; }
}

/// <summary>Запись вызова инструмента в рыночном следе ответа.</summary>
public sealed record ConsultationToolInvocation
{
	/// <summary>Имя инструмента (read_rule_card, get_market_snapshot, get_option_board).</summary>
	public required string ToolName { get; init; }

	/// <summary>Аргументы вызова в компактном виде.</summary>
	public required string Arguments { get; init; }

	/// <summary>
	/// As-of данных, отданных инструментом вызову; у кэшированной проекции
	/// при недоступном рынке это as-of кэша, null — данных нет вовсе.
	/// </summary>
	public DateTimeOffset? DataAsOf { get; init; }
}
