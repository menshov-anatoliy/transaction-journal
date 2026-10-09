namespace TransactionJournal.Infrastructure.Chats;

/// <summary>
/// Строка таблицы сообщений чата: роль, текст, as-of; след источников ответа
/// ИИ-помощника хранится денормализованным JSON — происхождение рекомендаций
/// проверяемо постфактум и не зависит от позднейших правок кода. История
/// чата плоская: сообщения одного чата упорядочены суррогатным ключом, без
/// вложенных диалогов.
// Traceability: openspec:chats/history#requirement-chat-flat-full-history
// Traceability: openspec:chats/history#requirement-chat-message-composition
/// </summary>
internal sealed class ChatMessageEntity
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Идентификатор чата, в котором оставлено сообщение.</summary>
	public long ChatId { get; set; }

	/// <summary>Навигация на чат: ключ присваивается в одном сохранении с сообщением.</summary>
	public ChatEntity Chat { get; set; } = null!;

	/// <summary>Роль автора сообщения, хранится строкой: User или Assistant.</summary>
	public required string Role { get; set; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; set; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; set; }

	/// <summary>След источников ответа ИИ-помощника в JSON; null у сообщений владельца.</summary>
	public string? MarketTraceJson { get; set; }
}
