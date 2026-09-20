namespace TransactionJournal.Sync;

/// <summary>
/// Сервис ручной команды «Синхронизировать»: единственная точка входа UI в подсистему
/// синхронизации с Bybit. Один вызов выполняет полный запуск — историю исполнения,
/// delivery-записи и пополнение справочника под общей строкой SyncRun — и возвращает
/// итог со счётчиками новых записей и предупреждениями сверки для отображения на странице.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// </summary>
public interface IJournalSyncService
{
	/// <summary>
	/// Выполняет один запуск синхронизации по категориям linear и option: определяет режим
	/// по водяным знакам, обходит историю исполнения и delivery-записи окнами, пополняет
	/// справочник неизвестных инструментов и перестраивает доменные проекции из сырых
	/// записей. Ошибка биржи закрывает запуск со статусом Failed и проходит наружу.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="Bybit.BybitApiException">Биржа ответила ошибкой после всех повторов; запуск закрыт со статусом Failed.</exception>
	Task<JournalSyncResult> SyncAsync(CancellationToken cancellationToken = default);
}
