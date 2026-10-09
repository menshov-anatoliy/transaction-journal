namespace TransactionJournal.Chats.Ports;

/// <summary>
/// Порт хранения чатов агента: единое хранилище плоских чатов — каждый чат
/// ведётся единой полной историей сообщений без вложенных диалогов. Чат
/// создаётся первым сообщением владельца вместе с параметрами — ИИ-модель,
/// опциональная привязка к конструкции, набор источников; параметры
/// фиксируются при создании и повторно не передаются, сменить контекст
/// можно только новым чатом. ИИ-помощник видит историю только собственного
/// чата. Чат — запись окружения: домен журнала порт не видит.
// Traceability: openspec:chats/history#requirement-chat-flat-full-history
// Traceability: openspec:chats/history#requirement-chat-environment-record
/// </summary>
public interface IChatStore
{
	/// <summary>
	/// Сохраняет сообщение и возвращает его с присвоенными идентификаторами.
	/// Отдельной команды создания чата нет: сообщение без идентификатора чата
	/// создаёт новый чат с переданными параметрами; сообщение существующего
	/// чата параметры не принимает — привязка и набор источников неизменяемы,
	/// перепривязка означает начало нового чата.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	// Traceability: openspec:chats/history#scenario-chat-binding-cannot-change
	/// </summary>
	/// <param name="chatId">Идентификатор чата; null — создаётся новый чат этим сообщением.</param>
	/// <param name="start">Параметры создания чата: обязательны при новом чате, запрещены для существующего.</param>
	/// <param name="message">Черновик сообщения с незаполненным идентификатором.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatMessage> AppendMessageAsync(
		long? chatId,
		ChatStartParameters? start,
		ChatMessageDraft message,
		CancellationToken cancellationToken = default);

	/// <summary>Чат по идентификатору: параметры и статус; null — чата нет.</summary>
	/// <param name="chatId">Идентификатор чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatRecord?> FindChatAsync(long chatId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Сообщения чата в порядке следования — полная история видна только
	/// этому чату, истории соседних чатов в выборку не попадают.
	// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
	/// </summary>
	/// <param name="chatId">Идентификатор чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(long chatId, CancellationToken cancellationToken = default);
}

/// <summary>Статус жизненного цикла чата: активен или завершён владельцем.</summary>
public enum ChatStatus
{
	/// <summary>Активный чат.</summary>
	Active,

	/// <summary>Завершён владельцем; продолжение возвращает в активные.</summary>
	Completed,
}

/// <summary>
/// Параметры создания чата: ИИ-модель, опциональная привязка к конструкции
/// и набор источников данных. Фиксируются первым сообщением владельца и
/// дальше не меняются.
// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
/// </summary>
public sealed record ChatStartParameters
{
	/// <summary>ИИ-модель чата: новые сообщения чата уходят ей.</summary>
	public required string Model { get; init; }

	/// <summary>Идентификатор конструкции привязки; null — чат без привязки, портфельный уровень журнала.</summary>
	public long? ConstructionId { get; init; }

	/// <summary>Набор источников данных чата — ключи закрытого справочника источников.</summary>
	public required IReadOnlyList<string> Sources { get; init; }
}

/// <summary>Чат агента: плоская полная история обмена с параметрами и статусом.</summary>
public sealed record ChatRecord
{
	/// <summary>Суррогатный ключ чата; 0 у несохранённого чата.</summary>
	public long Id { get; init; }

	/// <summary>ИИ-модель чата: новые сообщения чата уходят ей.</summary>
	public required string Model { get; init; }

	/// <summary>Идентификатор конструкции привязки; null — чат без привязки.</summary>
	public long? ConstructionId { get; init; }

	/// <summary>Набор источников данных чата — ключи закрытого справочника источников.</summary>
	public required IReadOnlyList<string> Sources { get; init; }

	/// <summary>Статус жизненного цикла: активен или завершён.</summary>
	public ChatStatus Status { get; init; }

	/// <summary>Момент создания чата первым сообщением владельца.</summary>
	public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Роль автора сообщения чата.</summary>
public enum ChatMessageRole
{
	/// <summary>Сообщение владельца.</summary>
	User,

	/// <summary>Ответ ИИ-помощника.</summary>
	Assistant,
}

/// <summary>Черновик сообщения чата с незаполненным идентификатором.</summary>
public sealed record ChatMessageDraft
{
	/// <summary>Роль автора сообщения.</summary>
	public required ChatMessageRole Role { get; init; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; init; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>След источников ответа ИИ-помощника; у сообщений владельца null.</summary>
	public ChatMarketTrace? MarketTrace { get; init; }
}

/// <summary>Сообщение чата: роль, текст, as-of; у помощника дополнительно след источников.</summary>
public sealed record ChatMessage
{
	/// <summary>Суррогатный ключ сообщения.</summary>
	public long Id { get; init; }

	/// <summary>Идентификатор чата, в котором оставлено сообщение.</summary>
	public long ChatId { get; init; }

	/// <summary>Роль автора сообщения.</summary>
	public required ChatMessageRole Role { get; init; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; init; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>След источников ответа ИИ-помощника; у сообщений владельца null.</summary>
	public ChatMarketTrace? MarketTrace { get; init; }
}

/// <summary>
/// Рыночный след ответа ассистента: какие инструменты вызывались и с какими
/// as-of их данные — происхождение рыночных рекомендаций проверяемо постфактум.
// Traceability: openspec:chats/history#requirement-chat-message-composition
/// </summary>
public sealed record ChatMarketTrace
{
	/// <summary>Вызовы инструментов в порядке следования.</summary>
	public required IReadOnlyList<ChatToolInvocation> Invocations { get; init; }
}

/// <summary>Запись вызова инструмента в рыночном следе ответа.</summary>
public sealed record ChatToolInvocation
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
