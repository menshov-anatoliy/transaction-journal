namespace TransactionJournal.Infrastructure.Consultations;

/// <summary>
/// Строка таблицы сообщений диалога в SQLite-базе конструкции: роль, текст,
/// as-of; рыночный след ответа ассистента хранится денормализованным JSON —
/// происхождение рыночных рекомендаций проверяемо постфактум и не зависит от
/// позднейших правок кода.
// Traceability: openspec:consultations/history#requirement-history-message-composition
/// </summary>
internal sealed class ConsultationMessageEntity
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Идентификатор диалога, в котором оставлено сообщение.</summary>
	public long DialogueId { get; set; }

	/// <summary>Навигация на диалог: ключ присваивается в одном сохранении с сообщением.</summary>
	public ConsultationDialogueEntity Dialogue { get; set; } = null!;

	/// <summary>Роль автора сообщения, хранится строкой.</summary>
	public required string Role { get; set; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; set; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; set; }

	/// <summary>Рыночный след ответа ассистента в JSON; null у сообщений владельца.</summary>
	public string? MarketTraceJson { get; set; }
}
