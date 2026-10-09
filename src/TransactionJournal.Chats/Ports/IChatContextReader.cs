namespace TransactionJournal.Chats.Ports;

using TransactionJournal.Chats;

/// <summary>
/// Порт снимка контекста чата: перед сообщением владельца собирает
/// детерминированный markdown-снимок кодом поверх read-моделей журнала, без
/// участия LLM. Состав: конструкция с позициями и результатами, портфельные
/// агрегаты и лимиты, компактный индекс корпуса правил; живые подсказки
/// движка в снимок не входят, рыночные данные в снимок тоже — они доступны
/// только инструментами в момент сообщения.
// Traceability: openspec:chats/context#requirement-chat-context-deterministic-snapshot
/// </summary>
public interface IChatContextReader
{
	/// <summary>
	/// Собирает снимок контекста для сообщения чата: у чата с привязкой —
	/// конструкция с позициями и результатами, у чата без привязки —
	/// портфельный уровень журнала. Набор источников чата режет состав
	/// снимка: журнальные секции — только с источником «журнал», индекс
	/// корпуса правил — только с источником «корпус правил».
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции привязки; null — чат без привязки, портфельный уровень журнала.</param>
	/// <param name="sources">Набор источников чата; null — дефолт, все три категории справочника.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatContextSnapshot> ReadAsync(
		long? constructionId,
		IReadOnlyList<ChatDataSource>? sources = null,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Снимок контекста чата: markdown-текст с отметкой as-of каждого
/// раздела. Признак закрытой конструкции переключает инструкции агента в
/// режим пост-мортема «работа над ошибками»; состав инструментов при этом
/// не меняется.
// Traceability: openspec:chats/context#requirement-chat-context-postmortem-mode
/// </summary>
public sealed record ChatContextSnapshot
{
	/// <summary>Markdown-текст снимка: конструкция, портфельные агрегаты, индекс корпуса.</summary>
	public required string Markdown { get; init; }

	/// <summary>Отметка as-of момента сборки снимка.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Конструкция закрыта — агент консультирует в пост-мортеме.</summary>
	public required bool IsConstructionClosed { get; init; }
}
