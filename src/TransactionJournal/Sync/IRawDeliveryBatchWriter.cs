using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Порт пакетной записи сырых delivery-записей: движок сохраняет пачками новые записи
/// каждого пройденного окна, уже известные журналу записи пропускаются по ключу
/// symbol + deliveryTime, поэтому повторный прогон после обрыва и пересекающиеся окна
/// не создают дублей. Уникальный индекс по symbol + deliveryTime остаётся последней
/// линией защиты от дублей на уровне БД.
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public interface IRawDeliveryBatchWriter
{
	/// <summary>
	/// Сохраняет пачку delivery-записей одной категории: известные по symbol + deliveryTime
	/// записи пропускаются, новые вставляются целиком в сыром виде с отметкой времени загрузки.
	/// </summary>
	/// <param name="category">Торговая категория записей: option или linear.</param>
	/// <param name="deliveries">Delivery-записи из одного окна синхронизации.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых записей продвигается на размер вставки; null — счётчик не трогается.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Категория пуста или не задана.</exception>
	/// <exception cref="ArgumentNullException">Записи не заданы.</exception>
	Task<RawDeliveryBatchResult> WriteAsync(
		string category,
		IReadOnlyCollection<BybitDeliveryRecord> deliveries,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default);
}
