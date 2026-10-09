namespace TransactionJournal.Chats.Ports;

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
	/// <summary>Собирает снимок контекста конструкции для сообщения чата.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatContextSnapshot> ReadAsync(long constructionId, CancellationToken cancellationToken = default);
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
