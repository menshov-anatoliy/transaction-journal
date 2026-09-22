using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Порт хранилища состояния синхронизации: движок читает водяной знак категории
/// для выбора режима и фиксирует по завершении успешного прохода водяной знак
/// и достигнутую границу backfill. Прерывание прохода оставляет состояние без
/// изменений, поэтому повторный запуск продолжает с прежней отметки. Сброс
/// состояния категории удаляет её водяные знаки — очередной запуск выполняет
/// первичный backfill категории.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// </summary>
public interface IExecutionSyncStateStore
{
	/// <summary>Возвращает состояние категории либо null, когда успешного синка ещё не было.</summary>
	/// <param name="category">Торговая категория: linear или option.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	Task<SyncState?> FindAsync(string category, CancellationToken cancellationToken = default);

	/// <summary>Сохраняет состояние категории после успешного прохода.</summary>
	/// <param name="state">Состояние с обновлёнными водяным знаком и границей backfill.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	Task SaveAsync(SyncState state, CancellationToken cancellationToken = default);

	/// <summary>
	/// Сбрасывает состояние синхронизации категории: водяные знаки и граница backfill
	/// категории удаляются, поэтому следующий запуск выполняет первичный backfill
	/// категории. Сырые записи, доменные сущности и журнал запусков не затрагиваются.
	/// </summary>
	/// <param name="category">Торговая категория, чьё состояние сбрасывается: linear или option.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <remarks>
	/// Ручная обслуживающая команда на странице «Синхронизация»: сброс возвращает
	/// категорию в первичную загрузку без удаления данных журнала.
	/// Traceability: openspec:sync/bybit-history#requirement-manual-category-state-reset
	/// </remarks>
	Task ResetAsync(string category, CancellationToken cancellationToken = default);
}
