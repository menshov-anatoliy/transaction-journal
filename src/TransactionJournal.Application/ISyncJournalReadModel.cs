using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;

namespace TransactionJournal.Application;

/// <summary>
/// Read-модель журнала синхронизаций экранов «Настройки» и «Синхронизация»: читает
/// строки запусков из хранилища новыми сверху и последний завершённый запуск.
/// Модель тонкая — строка SyncRun уже несёт режим, счётчики и статус, модель ничего
/// не пересчитывает и не фильтрует.
/// </summary>
// Журнал синхронизаций со временем, режимом, результатом и статусом определён
// требованием экрана «Настройки»; предупреждения сверки дополняют журнал на экране.
// Traceability: openspec:ui/screens#requirement-settings-screen
public interface ISyncJournalReadModel
{
	/// <summary>Читает последние запуски синхронизации новыми сверху.</summary>
	/// <param name="limit">Максимальное число строк журнала.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<SyncRunRow>> ReadAsync(int limit = 20, CancellationToken cancellationToken = default);

	/// <summary>
	/// Читает последний завершённый запуск — успешный или прерванный — с его
	/// предупреждениями. Выполняющийся запуск завершённым не считается; пока
	/// завершённых запусков нет, метод возвращает null.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<SyncRunRow?> ReadLastCompletedAsync(CancellationToken cancellationToken = default);
}
