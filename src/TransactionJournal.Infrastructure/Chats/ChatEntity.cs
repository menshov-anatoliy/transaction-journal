namespace TransactionJournal.Infrastructure.Chats;

/// <summary>
/// Строка таблицы чатов в единой SQLite-базе — хранение записи окружения
/// агента. Сущность живёт в Infrastructure: домен о чатах не знает, а проект
/// Chats не ссылается на EF; в запись порта строка отображается адаптером
/// хранилища. Привязка к конструкции хранится значением, без внешнего ключа
/// в доменные таблицы: чат может жить и без привязки, а база чатов не
/// привязана к жизненному циклу журнала.
// Traceability: openspec:chats/history#requirement-chat-environment-record
/// </summary>
internal sealed class ChatEntity
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>ИИ-модель чата: новые сообщения чата уходят ей.</summary>
	public required string Model { get; set; }

	/// <summary>Идентификатор конструкции привязки значением; null — чат без привязки.</summary>
	public long? ConstructionId { get; set; }

	/// <summary>Набор источников данных чата в JSON — имена категорий закрытого справочника источников.</summary>
	public required string SourcesJson { get; set; }

	/// <summary>Статус жизненного цикла, хранится строкой: Active или Completed.</summary>
	public required string Status { get; set; }

	/// <summary>Момент создания чата первым сообщением владельца.</summary>
	public required DateTimeOffset CreatedAt { get; set; }
}
