namespace TransactionJournal.Application.Ops;

/// <summary>
/// Хранилище политики опционального резервного копирования перед синхронизацией:
/// переключатель живёт в базе журнала (таблица AppSetting, ключ «backup-before-sync»)
/// и обязана переживать перезапуск приложения. Отсутствие строки трактуется как
/// «включён» — опция копирования по умолчанию действует без записи значения.
/// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
/// </summary>
public interface IBackupPolicyStore
{
	/// <summary>
	/// Читает переключатель копирования перед синхронизацией; строка в базе отсутствует
	/// или база чиста — «включён», значение по умолчанию записи не требует.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены чтения.</param>
	Task<bool> IsBackupBeforeSyncEnabledAsync(CancellationToken cancellationToken = default);

	/// <summary>Записывает переключатель копирования перед синхронизацией: создаёт или обновляет строку ключа.</summary>
	/// <param name="enabled">Включено ли копирование перед синхронизацией.</param>
	/// <param name="cancellationToken">Токен отмены записи.</param>
	Task SetBackupBeforeSyncEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
