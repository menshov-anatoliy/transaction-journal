using TransactionJournal.Data;
using TransactionJournal.Materialization;

namespace TransactionJournal.Sync;

/// <summary>
/// Итог одного запуска синхронизации для страницы «Синхронизировать»: режим и строка
/// запуска со счётчиками новых записей, результаты проходов по категориям и перестроенная
/// проекция журнала с предупреждениями сверки экспираций.
// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// </summary>
public sealed record JournalSyncResult
{
	/// <summary>Режим запуска: backfill, пока хотя бы один водяной знак категории не зафиксирован, иначе инкрементальная догрузка.</summary>
	public required SyncRunMode Mode { get; init; }

	/// <summary>Дескриптор строки запуска с итоговыми статусом и счётчиками новых записей.</summary>
	public required SyncRun Run { get; init; }

	/// <summary>Итоги синхронизации истории исполнения по категориям.</summary>
	public required IReadOnlyDictionary<string, ExecutionCategorySyncResult> Executions { get; init; }

	/// <summary>Итоги синхронизации delivery-истории по категориям.</summary>
	public required IReadOnlyDictionary<string, DeliveryCategorySyncResult> Deliveries { get; init; }

	/// <summary>
	/// Перестроенная проекция журнала: сделки «Входящих», закрывающие записи экспираций
	/// и предупреждения сверки с deliveryRpl. Null, когда проекция не построилась —
	/// текст причины в <see cref="ProjectionError"/>; сырые записи при этом сохранены.
	/// </summary>
	public JournalMaterializationResult? Projection { get; init; }

	/// <summary>Текст ошибки построения проекции; null при успешной материализации.</summary>
	public string? ProjectionError { get; init; }
}
