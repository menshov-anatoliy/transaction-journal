namespace TransactionJournal.Data;

/// <summary>Режим запуска синхронизации: первичный backfill или инкрементальная догрузка.</summary>
public enum SyncRunMode
{
	/// <summary>Первичный проход всей доступной истории (первый запуск журнала).</summary>
	Backfill,

	/// <summary>Догрузка от водяного знака последнего успешного синка с перекрытием назад.</summary>
	Incremental,
}

/// <summary>Состояние запуска синхронизации.</summary>
public enum SyncRunStatus
{
	/// <summary>Запуск выполняется.</summary>
	Running,

	/// <summary>Запуск завершился успешно.</summary>
	Succeeded,

	/// <summary>Запуск прерван ошибкой; журнал остаётся согласованным, запуск можно повторить.</summary>
	Failed,
}

/// <summary>
/// Один запуск синхронизации: режим, счётчики новых записей и результат.
/// Прогресс пишется поэтапно, поэтому прерванный запуск продолжается повторным
/// запуском без дублей.
// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
/// </summary>
public sealed class SyncRun
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Момент запуска синхронизации.</summary>
	public required DateTimeOffset StartedAt { get; set; }

	/// <summary>Момент завершения; заполнен только для завершённых запусков.</summary>
	public DateTimeOffset? FinishedAt { get; set; }

	/// <summary>Режим запуска: backfill или инкрементальная догрузка.</summary>
	public SyncRunMode Mode { get; set; }

	/// <summary>Состояние запуска.</summary>
	public SyncRunStatus Status { get; set; }

	/// <summary>Текст ошибки для прерванного запуска; пуст для успешного.</summary>
	public string? Error { get; set; }

	/// <summary>Количество новых записей исполнения, сохранённых этим запуском.</summary>
	public int NewExecutions { get; set; }

	/// <summary>Количество новых delivery-записей, сохранённых этим запуском.</summary>
	public int NewDeliveries { get; set; }

	/// <summary>Количество новых инструментов, попавших в справочник этим запуском.</summary>
	public int NewInstruments { get; set; }
}
