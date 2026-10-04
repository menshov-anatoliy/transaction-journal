namespace TransactionJournal.Consultations.Ports;

/// <summary>
/// Порт снимка контекста консультации: перед сообщением владельца собирает
/// детерминированный markdown-снимок кодом поверх read-моделей журнала, без
/// участия LLM. Состав: конструкция с позициями и результатами, портфельные
/// агрегаты и лимиты, компактный индекс корпуса правил; живые подсказки
/// движка в снимок не входят, рыночные данные в снимок тоже — они доступны
/// только инструментами в момент сообщения.
// Traceability: openspec:consultations/context#requirement-context-deterministic-snapshot
/// </summary>
public interface IConsultationContextReader
{
	/// <summary>Собирает снимок контекста конструкции для сообщения консультации.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ConsultationContextSnapshot> ReadAsync(long constructionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Снимок контекста консультации: markdown-текст с отметкой as-of каждого
/// раздела. Признак закрытой конструкции переключает инструкции агента в
/// режим пост-мортема «работа над ошибками»; состав инструментов при этом
/// не меняется.
// Traceability: openspec:consultations/context#requirement-context-postmortem-mode
/// </summary>
public sealed record ConsultationContextSnapshot
{
	/// <summary>Markdown-текст снимка: конструкция, портфельные агрегаты, индекс корпуса.</summary>
	public required string Markdown { get; init; }

	/// <summary>Отметка as-of момента сборки снимка.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Конструкция закрыта — агент консультирует в пост-мортеме.</summary>
	public required bool IsConstructionClosed { get; init; }
}
