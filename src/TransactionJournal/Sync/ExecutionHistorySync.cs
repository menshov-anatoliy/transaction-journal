using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Оркестратор синхронизации истории исполнения под одним запуском (SyncRun): определяет
/// режим запуска по водяным знакам категорий, открывает строку запуска до обхода категорий,
/// пишет сырые записи пачками по ходу проходов и закрывает запуск успехом или ошибкой.
/// Прерванный запуск оставляет журнал согласованным: сохранённые пачки остаются в хранилище,
/// состояние категорий не фиксируется, повторный запуск продолжает с места остановки без дублей.
/// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// Traceability: change:add-bybit-sync/design#d5
/// </summary>
public sealed class ExecutionHistorySync
{
	/// <summary>Категории синхронизации истории исполнения по умолчанию: linear и option.</summary>
	public static readonly IReadOnlyList<string> DefaultCategories = ["linear", "option"];

	private readonly ExecutionCategorySync _categorySync;
	private readonly IExecutionSyncStateStore _stateStore;
	private readonly ISyncRunJournal _runJournal;

	/// <summary>Создаёт оркестратор над движком категории, хранилищем состояния и журналом запусков.</summary>
	/// <param name="categorySync">Движок синхронизации одной категории с писателем сырых записей.</param>
	/// <param name="stateStore">Хранилище состояния категорий для выбора режима запуска.</param>
	/// <param name="runJournal">Журнал запусков синхронизации.</param>
	/// <exception cref="ArgumentNullException">Любая зависимость не задана.</exception>
	public ExecutionHistorySync(
		ExecutionCategorySync categorySync,
		IExecutionSyncStateStore stateStore,
		ISyncRunJournal runJournal)
	{
		_categorySync = categorySync ?? throw new ArgumentNullException(nameof(categorySync));
		_stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
		_runJournal = runJournal ?? throw new ArgumentNullException(nameof(runJournal));
	}

	/// <summary>
	/// Выполняет запуск синхронизации: открывает строку SyncRun с режимом по водяным знакам
	/// категорий, обходит категории последовательно с записью сырых пачек и закрытием запуска.
	/// </summary>
	/// <param name="categories">Торговые категории запуска; по умолчанию linear и option.</param>
	/// <param name="options">Параметры синхронизации категорий.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Список категорий пуст или содержит повторы.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой; запуск закрывается со статусом Failed, ошибка проходит наружу.</exception>
	public async Task<ExecutionHistorySyncResult> RunAsync(
		IReadOnlyCollection<string>? categories = null,
		ExecutionCategorySyncOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		categories ??= DefaultCategories;
		if (categories.Count == 0)
		{
			throw new ArgumentException("Список категорий синхронизации пуст.", nameof(categories));
		}

		if (categories.Distinct(StringComparer.Ordinal).Count() != categories.Count)
		{
			throw new ArgumentException("Список категорий синхронизации содержит повторы.", nameof(categories));
		}

		// Режим запуска определяется до обхода: строка SyncRun открывается в начале и должна
		// знать режим. Backfill выбирается, пока хотя бы одна категория не отмечена успешным
		// синком — запуск дочитывает недостающую историю целиком.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		var mode = SyncRunMode.Incremental;
		foreach (var category in categories)
		{
			var state = await _stateStore.FindAsync(category, cancellationToken).ConfigureAwait(false);
			if (state?.ExecWatermarkMs is null)
			{
				mode = SyncRunMode.Backfill;
				break;
			}
		}

		// Строка запуска открывается до первого запроса биржи: даже мгновенный обрыв
		// оставляет след запуска со статусом и текстом ошибки.
		var run = await _runJournal.StartAsync(mode, cancellationToken).ConfigureAwait(false);

		var categoryResults = new Dictionary<string, ExecutionCategorySyncResult>(StringComparer.Ordinal);
		try
		{
			foreach (var category in categories)
			{
				var result = await _categorySync.RunAsync(category, options, run, cancellationToken).ConfigureAwait(false);
				categoryResults[category] = result;
			}
		}
		catch (Exception ex)
		{
			// Ошибка закрывает запуск со статусом Failed и текстом причины; состояние категорий
			// остаётся без изменений, поэтому повторный запуск продолжит с места остановки.
			// Пометка выполняется без токена отмены: отмена тоже должна оставлять след запуска.
			await _runJournal.MarkFailedAsync(run, ex.Message, CancellationToken.None).ConfigureAwait(false);
			throw;
		}

		await _runJournal.MarkSucceededAsync(run, cancellationToken).ConfigureAwait(false);
		return new ExecutionHistorySyncResult
		{
			Mode = mode,
			Run = run,
			Categories = categoryResults,
		};
	}
}
