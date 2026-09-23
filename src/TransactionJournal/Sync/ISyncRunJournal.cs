using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Порт журнала запусков синхронизации: запуск открывается строкой SyncRun до обхода
/// категорий, по ходу прохода продвигается счётчиками новых записей и закрывается успехом
/// или ошибкой. Прерванный запуск остаётся в журнале со своим прогрессом — повторный
/// запуск продолжает загрузку с места остановки без дублей.
/// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
/// </summary>
public interface ISyncRunJournal
{
	/// <summary>Открывает новый запуск синхронизации со статусом Running и возвращает его дескриптор с заполненным ключом.</summary>
	/// <param name="mode">Режим запуска: backfill или инкрементальная догрузка.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	Task<SyncRun> StartAsync(SyncRunMode mode, CancellationToken cancellationToken = default);

	/// <summary>Закрывает запуск успехом: статус Succeeded и момент завершения.</summary>
	/// <param name="run">Дескриптор запуска, полученный из <see cref="StartAsync"/>.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentNullException">Запуск не задан.</exception>
	/// <exception cref="InvalidOperationException">Строка запуска не найдена в журнале.</exception>
	Task MarkSucceededAsync(SyncRun run, CancellationToken cancellationToken = default);

	/// <summary>Закрывает запуск ошибкой: статус Failed, текст ошибки и момент завершения.</summary>
	/// <param name="run">Дескриптор запуска, полученный из <see cref="StartAsync"/>.</param>
	/// <param name="error">Текст ошибки, прервавшей запуск.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentNullException">Запуск не задан.</exception>
	/// <exception cref="ArgumentException">Текст ошибки пуст или не задан.</exception>
	/// <exception cref="InvalidOperationException">Строка запуска не найдена в журнале.</exception>
	Task MarkFailedAsync(SyncRun run, string error, CancellationToken cancellationToken = default);

	/// <summary>
	/// Сохраняет предупреждения завершённого запуска в его строке: перечень пропущенных
	/// областей, неразрешённых инструментов и непокрытых активов доски. Отдельное
	/// обновление после закрытия запуска — предупреждения переживают перезагрузку страницы.
	/// </summary>
	/// <param name="run">Дескриптор закрытого запуска, полученный из <see cref="StartAsync"/>.</param>
	/// <param name="warnings">Предупреждения запуска; пустые перечни сохраняются как пустой перечень.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentNullException">Запуск или предупреждения не заданы.</exception>
	/// <exception cref="InvalidOperationException">Строка запуска не найдена в журнале.</exception>
	Task SaveWarningsAsync(SyncRun run, SyncRunWarnings warnings, CancellationToken cancellationToken = default);

	/// <summary>
	/// Читает предупреждения строки запуска. Запуски без сохранённого значения —
	/// в том числе строки до появления колонки — читаются пустым перечнем.
	/// </summary>
	/// <param name="runId">Ключ строки запуска в журнале.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="InvalidOperationException">Строка запуска не найдена в журнале.</exception>
	Task<SyncRunWarnings> ReadWarningsAsync(long runId, CancellationToken cancellationToken = default);
}
