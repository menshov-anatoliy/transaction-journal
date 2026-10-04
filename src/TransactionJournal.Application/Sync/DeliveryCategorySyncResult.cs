using TransactionJournal.Domain.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application.Sync;

/// <summary>Итог синхронизации delivery-истории одной торговой категории.</summary>
public sealed class DeliveryCategorySyncResult
{
	/// <summary>Выбранный по водяному знаку режим запуска: backfill или инкрементальная догрузка.</summary>
	public required SyncRunMode Mode { get; init; }

	/// <summary>Новые delivery-записи категории в порядке обхода окон от текущего момента назад.</summary>
	public required IReadOnlyList<BybitDeliveryRecord> NewDeliveries { get; init; }

	/// <summary>Сколько 30-дневных окон обработано запуском.</summary>
	public required int WindowsProcessed { get; init; }

	/// <summary>Backfill дошёл до исчерпания данных биржи: очередное окно вернулось пустым.</summary>
	public required bool HistoryExhausted { get; init; }

	/// <summary>Зафиксированный водяной знак успешного delivery-прохода: момент запуска, мс.</summary>
	public required long DeliveryWatermarkMs { get; init; }

	/// <summary>
	/// Сколько новых delivery-записей запуска сохранено в хранилище сырых записей пачками
	/// по окнам. Ноль, когда писатель сырых записей не подключён и записи собираются
	/// только в памяти.
	/// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
	/// </summary>
	public required int NewDeliveriesPersisted { get; init; }

	/// <summary>
	/// Метки областей, пропущенных из-за отказа биржи «контракт недоступен для торговли»:
	/// для безфильтровой области delivery-прохода — только категория. Пуст, когда
	/// пропусков не было; исчерпание истории и пограничное зажатие пропуск не отмечают.
	/// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
	/// </summary>
	public IReadOnlyList<string> SkippedAreas { get; init; } = [];
}
