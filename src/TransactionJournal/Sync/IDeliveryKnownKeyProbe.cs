namespace TransactionJournal.Sync;

/// <summary>
/// Порт проверки известных журналу delivery-записей по ключу symbol + deliveryTime:
/// проход окна спрашивает хранилище пачкой по каждой странице, чтобы отфильтровать
/// уже сохранённые записи. Дубликаты исключаются и на уровне БД уникальным индексом
/// по symbol + deliveryTime — порт служит оптимизацией вставки.
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// </summary>
public interface IDeliveryKnownKeyProbe
{
	/// <summary>Возвращает подмножество переданных ключей, уже сохранённых в журнале.</summary>
	/// <param name="keys">Ключи symbol + deliveryTime одной страницы ответа биржи.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	Task<IReadOnlySet<DeliveryRecordKey>> FindKnownAsync(
		IReadOnlyCollection<DeliveryRecordKey> keys,
		CancellationToken cancellationToken = default);
}
