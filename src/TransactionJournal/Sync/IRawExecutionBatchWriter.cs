using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Порт пакетной записи сырых записей исполнения: движок сохраняет пачками новые записи
/// каждого пройденного окна, уже известные журналу записи пропускаются по execId, поэтому
/// повторный прогон после обрыва не создаёт дублей. Уникальный индекс по execId остаётся
/// последней линией защиты от дублей на уровне БД.
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public interface IRawExecutionBatchWriter
{
	/// <summary>
	/// Сохраняет пачку записей исполнения одной категории: известные по execId записи
	/// пропускаются, новые вставляются целиком в сыром виде с отметкой времени загрузки.
	/// </summary>
	/// <param name="category">Торговая категория записей: linear или option.</param>
	/// <param name="executions">Записи исполнения из одного окна синхронизации.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых записей продвигается на размер вставки; null — счётчик не трогается.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Категория пуста или не задана.</exception>
	/// <exception cref="ArgumentNullException">Записи не заданы.</exception>
	Task<RawExecutionBatchResult> WriteAsync(
		string category,
		IReadOnlyCollection<BybitExecution> executions,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default);
}
