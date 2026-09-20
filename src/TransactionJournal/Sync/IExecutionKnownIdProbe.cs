namespace TransactionJournal.Sync;

/// <summary>
/// Порт проверки известных журналу идентификаторов исполнения: проход окна спрашивает
/// хранилище пачкой по каждой странице, чтобы отфильтровать уже сохранённые записи
/// и вовремя остановиться. Дубликаты при этом исключаются и на уровне БД уникальным
/// индексом по execId — порт служит ранней оптимизацией и критерием остановки.
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// </summary>
public interface IExecutionKnownIdProbe
{
	/// <summary>Возвращает подмножество переданных execId, уже сохранённых в журнале.</summary>
	/// <param name="execIds">Идентификаторы исполнения одной страницы ответа биржи.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	Task<IReadOnlySet<string>> FindKnownAsync(
		IReadOnlyCollection<string> execIds,
		CancellationToken cancellationToken = default);
}
