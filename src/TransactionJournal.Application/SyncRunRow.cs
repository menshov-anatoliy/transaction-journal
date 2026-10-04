using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;

namespace TransactionJournal.Application;

/// <summary>
/// Строка журнала синхронизаций для экранов «Настройки» и «Синхронизация»: время запуска,
/// режим, выбранный системой, результат — счётчики новых записей или причина прерывания —
/// и статус завершённости запуска.
/// </summary>
/// <param name="Id">Идентификатор строки запуска.</param>
/// <param name="StartedAt">Момент запуска синхронизации.</param>
/// <param name="FinishedAt">Момент завершения; null у ещё выполняющегося запуска.</param>
/// <param name="Mode">Режим запуска, выбранный системой: backfill или инкремент.</param>
/// <param name="Status">Состояние запуска: выполняется, успех или ошибка.</param>
/// <param name="Error">Текст ошибки прерванного запуска; null у успешного.</param>
/// <param name="NewExecutions">Число новых записей исполнения запуска.</param>
/// <param name="NewDeliveries">Число новых delivery-записей запуска.</param>
/// <param name="NewInstruments">Число новых инструментов справочника запуска.</param>
/// <param name="WarningsJson">Сериализованные предупреждения запуска; null у запусков без сохранённого значения.</param>
public sealed record SyncRunRow(
	long Id,
	DateTimeOffset StartedAt,
	DateTimeOffset? FinishedAt,
	SyncRunMode Mode,
	SyncRunStatus Status,
	string? Error,
	int NewExecutions,
	int NewDeliveries,
	int NewInstruments,
	string? WarningsJson)
{
	/// <summary>
	/// Разобранные предупреждения запуска: пропущенные области, неразрешённые инструменты,
	/// непокрытые активы доски. Запуски без сохранённого значения читаются пустым перечнем.
	/// Traceability: openspec:sync/bybit-history#requirement-run-warnings-persisted
	/// </summary>
	public SyncRunWarnings Warnings => SyncRunWarnings.Parse(WarningsJson);
}
