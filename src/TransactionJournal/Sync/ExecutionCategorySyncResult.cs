using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>Итог синхронизации истории исполнения одной торговой категории.</summary>
public sealed class ExecutionCategorySyncResult
{
	/// <summary>Выбранный по водяному знаку режим запуска: backfill или инкрементальная догрузка.</summary>
	public required SyncRunMode Mode { get; init; }

	/// <summary>Новые записи категории в порядке обхода окон от текущего момента назад.</summary>
	public required IReadOnlyList<BybitExecution> NewExecutions { get; init; }

	/// <summary>Сколько 7-дневных окон обработано запуском.</summary>
	public required int WindowsProcessed { get; init; }

	/// <summary>Backfill дошёл до исчерпания данных биржи: очередное окно вернулось пустым.</summary>
	public required bool HistoryExhausted { get; init; }

	/// <summary>Инкрементальная догрузка остановилась на целиком известной странице.</summary>
	public required bool EarlyStopped { get; init; }

	/// <summary>
	/// Зафиксированная граница backfill: самая ранняя достигнутая запись категории, мс.
	/// Null, когда границы ещё нет — история не читалась или записей не найдено.
	/// </summary>
	public long? BackfillBoundaryMs { get; init; }

	/// <summary>Зафиксированный водяной знак успешного прохода: момент запуска, мс.</summary>
	public required long ExecWatermarkMs { get; init; }

	/// <summary>
	/// Сколько новых записей запуска сохранено в хранилище сырых записей пачками по окнам.
	/// Ноль, когда писатель сырых записей не подключён и записи собираются только в памяти.
	/// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
	/// </summary>
	public required int NewExecutionsPersisted { get; init; }
}
