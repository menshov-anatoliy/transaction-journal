namespace TransactionJournal.Sync;

/// <summary>
/// Порт чтения полного снимка сырых записей журнала: материализатор строит доменные
/// представления только из локального сырья, поэтому источником проекции служит
/// хранилище, а не биржа.
/// Traceability: openspec:sync/bybit-history#scenario-raw-records-persisted-for-reparse
/// </summary>
public interface IJournalRawSnapshotStore
{
	/// <summary>
	/// Загружает все сырые записи хранилища: справочник инструментов, записи исполнения
	/// и delivery-записи.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены чтения.</param>
	Task<JournalRawSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}
