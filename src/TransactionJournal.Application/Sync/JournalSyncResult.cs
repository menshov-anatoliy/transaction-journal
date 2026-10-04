using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Application.Materialization;

namespace TransactionJournal.Application.Sync;

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

	/// <summary>
	/// Перечень неразрешённых символов запуска: инструменты, чьи спецификации биржа
	/// отвергла отказом «контракт недоступен для торговли» при пополнении справочника.
	/// Запуск при этом успешен; повторный запуск заново пробует запросить спецификации.
	/// Traceability: openspec:sync/bybit-history#scenario-unavailable-instrument-spec-skipped
	/// </summary>
	public IReadOnlyList<string> UnresolvedInstruments { get; init; } = [];

	/// <summary>
	/// Непокрытые базовые активы опционной доски: активы option-записей полного снимка
	/// сырья, чьи области не пройдены этим запуском. Средства лечения — конфигурация
	/// дополнительных активов доски и сброс состояния категории option; расчёт advisory,
	/// запуск не помечается ошибкой.
	/// Traceability: openspec:sync/bybit-history#requirement-uncovered-base-coin-visibility
	/// </summary>
	public IReadOnlyList<string> UncoveredBaseCoins { get; init; } = [];
}
