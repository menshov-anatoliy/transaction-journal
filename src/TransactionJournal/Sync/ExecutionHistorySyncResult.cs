using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>Итог синхронизации истории исполнения под одним запуском.</summary>
public sealed class ExecutionHistorySyncResult
{
	/// <summary>Режим запуска: backfill, пока хотя бы одна категория без водяного знака, иначе инкрементальная догрузка.</summary>
	public required SyncRunMode Mode { get; init; }

	/// <summary>Дескриптор строки запуска с итоговыми статусом и счётчиками новых записей.</summary>
	public required SyncRun Run { get; init; }

	/// <summary>Итоги синхронизации по категориям.</summary>
	public required IReadOnlyDictionary<string, ExecutionCategorySyncResult> Categories { get; init; }
}
