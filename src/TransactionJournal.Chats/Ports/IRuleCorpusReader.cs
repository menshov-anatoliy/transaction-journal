namespace TransactionJournal.Chats.Ports;

/// <summary>
/// Порт корпуса правил для чата: индекс отдаёт компактный список всех
/// карточек (id и краткое содержание), полный текст карточки читается только
/// по id — инструментом чтения в момент сообщения. Адаптер живёт в
/// composition root поверх загрузчика корпуса подсказок: проект Chats
/// на Hints не ссылается, окружения не связаны друг с другом.
// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
// Traceability: openspec:architecture/solution-structure#scenario-environments-not-linked
/// </summary>
public interface IRuleCorpusReader
{
	/// <summary>Компактный индекс всех карточек корпуса, включая retired с их статусом.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<RuleCardSummary>> ListIndexAsync(CancellationToken cancellationToken = default);

	/// <summary>Полный текст карточки по идентификатору; null — карточки с таким id нет.</summary>
	/// <param name="cardId">Идентификатор карточки — имя файла без расширения.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<RuleCardContent?> ReadCardAsync(string cardId, CancellationToken cancellationToken = default);
}

/// <summary>Запись компактного индекса корпуса: id карточки и её краткое содержание.</summary>
public sealed record RuleCardSummary
{
	/// <summary>Идентификатор карточки — равен имени файла без расширения.</summary>
	public required string Id { get; init; }

	/// <summary>Название правила.</summary>
	public required string Title { get; init; }

	/// <summary>Краткое содержание карточки одной строкой.</summary>
	public required string Summary { get; init; }

	/// <summary>Статус карточки в каноне: active — правило действует, retired — выведено из канона.</summary>
	public required string Status { get; init; }
}

/// <summary>Полный текст карточки правила, читаемый инструментом по идентификатору.</summary>
public sealed record RuleCardContent
{
	/// <summary>Идентификатор карточки.</summary>
	public required string Id { get; init; }

	/// <summary>Полный текст карточки.</summary>
	public required string Text { get; init; }
}
