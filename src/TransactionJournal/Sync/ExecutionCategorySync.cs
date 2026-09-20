using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Синхронизация истории исполнения одной торговой категории: по водяному знаку
/// состояния выбирает режим — без отметки об успешном синке выполняется первичный
/// backfill окнами назад до исчерпания данных биржи, иначе инкрементальная догрузка
/// от водяного знака с перекрытием назад. Успешный проход фиксирует в состоянии
/// категории водяной знак, а backfill — ещё и достигнутую границу доступной истории.
/// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
/// Traceability: change:add-bybit-sync/design#d4
/// </summary>
public sealed class ExecutionCategorySync
{
	private readonly ExecutionWindowPass _windowPass;
	private readonly IExecutionSyncStateStore _stateStore;
	private readonly TimeProvider _timeProvider;
		private readonly IRawExecutionBatchWriter? _rawWriter;

		/// <summary>Создаёт движок над проходом окна и хранилищем состояния категории.</summary>
		/// <param name="windowPass">Проход одного 7-дневного окна с курсорной пагинацией.</param>
		/// <param name="stateStore">Хранилище состояния синхронизации категории.</param>
		/// <param name="timeProvider">Поставщик времени; по умолчанию системные часы.</param>
		/// <param name="rawWriter">Писатель сырых записей пачками по окнам; null — записи не сохраняются, движок только собирает их в памяти.</param>
		/// <exception cref="ArgumentNullException">Проход или хранилище не заданы.</exception>
		public ExecutionCategorySync(
			ExecutionWindowPass windowPass,
			IExecutionSyncStateStore stateStore,
			TimeProvider? timeProvider = null,
			IRawExecutionBatchWriter? rawWriter = null)
		{
			_windowPass = windowPass ?? throw new ArgumentNullException(nameof(windowPass));
			_stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
			_timeProvider = timeProvider ?? TimeProvider.System;
			_rawWriter = rawWriter;
		}

	/// <summary>
	/// Выполняет синхронизацию категории: определяет режим по водяному знаку состояния,
	/// обходит 7-дневные окна выбранным режимом и фиксирует состояние успешного прохода.
	/// </summary>
	/// <param name="category">Торговая категория: linear или option.</param>
	/// <param name="options">Параметры синхронизации: размер страницы и перекрытие инкремента.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых записей продвигается пачками по окнам; null — прогресс запуска не ведётся.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Категория не задана либо запуск прогресса передан без писателя сырых записей.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Перекрытие инкрементального окна отрицательно.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов; состояние категории не меняется.</exception>
	public async Task<ExecutionCategorySyncResult> RunAsync(
		string category,
		ExecutionCategorySyncOptions? options = null,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		options ??= new ExecutionCategorySyncOptions();
		if (options.IncrementalOverlapMs < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options), options.IncrementalOverlapMs, "Перекрытие инкрементального окна не может быть отрицательным.");
		}

		if (progressRun is not null && _rawWriter is null)
		{
			throw new ArgumentException(
				"Ведение прогресса запуска требует писателя сырых записей; передайте его в конструктор движка.",
				nameof(progressRun));
		}

		var state = await _stateStore.FindAsync(category, cancellationToken).ConfigureAwait(false);
		var startedAt = _timeProvider.GetUtcNow();
		var startedAtMs = startedAt.ToUnixTimeMilliseconds();

		// Режим определяется по водяному знаку: отсутствие отметки об успешном синке
		// означает первый запуск категории — выполняется первичный backfill всей доступной истории.
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		var watermarkMs = state?.ExecWatermarkMs;
		var mode = watermarkMs is null ? SyncRunMode.Backfill : SyncRunMode.Incremental;

		var newExecutions = new List<BybitExecution>();
		var allSeenExecutions = new List<BybitExecution>();
		var newExecutionsPersisted = 0;
		var windowsProcessed = 0;
		var historyExhausted = false;
		var earlyStopped = false;

		if (mode == SyncRunMode.Backfill)
		{
			// Backfill идёт окнами назад от момента запуска без ранней остановки: известные
			// страницы не означают, что хвост окна уже сохранён. Зафиксированная ранее граница
			// ограничивает перебор снизу — старше границы биржа данных не отдаёт.
			var boundaryMs = state?.BackfillBoundaryMs;
			var windowEndMs = startedAtMs;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var desiredStartMs = windowEndMs - ExecutionWindowPass.MaxWindowMs;
				var windowStartMs = boundaryMs is { } boundary && desiredStartMs < boundary
					? boundary
					: desiredStartMs;
				if (windowStartMs >= windowEndMs)
				{
					break;
				}

				var passResult = await RunWindowAsync(category, windowStartMs, windowEndMs, options, earlyStopOnKnownPage: false, cancellationToken).ConfigureAwait(false);
				windowsProcessed++;
				newExecutions.AddRange(passResult.NewExecutions);
				allSeenExecutions.AddRange(passResult.AllExecutions);
					newExecutionsPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

				// Пустое окно при пролистывании назад — биржа исчерпала данные категории:
				// проход завершён, достигнута граница доступной истории.
				// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
				if (passResult.AllExecutions.Count == 0)
				{
					historyExhausted = true;
					break;
				}

				windowEndMs = windowStartMs;
			}
		}
		else
		{
			// Инкрементальная догрузка идёт окнами назад от момента запуска до водяного знака
			// минус перекрытие: перечитывается только хвост истории, а не вся она. Ранняя
			// остановка на целиком известной странице завершает проход — старше лежат только
			// уже сохранённые записи.
			// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
			var targetStartMs = watermarkMs.GetValueOrDefault() - options.IncrementalOverlapMs;
			var windowEndMs = startedAtMs;
			while (windowEndMs > targetStartMs)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var windowStartMs = Math.Max(windowEndMs - ExecutionWindowPass.MaxWindowMs, targetStartMs);
				var passResult = await RunWindowAsync(category, windowStartMs, windowEndMs, options, earlyStopOnKnownPage: true, cancellationToken).ConfigureAwait(false);
				windowsProcessed++;
				newExecutions.AddRange(passResult.NewExecutions);
				allSeenExecutions.AddRange(passResult.AllExecutions);
					newExecutionsPersisted += await PersistWindowBatchAsync(category, passResult, progressRun, cancellationToken).ConfigureAwait(false);

				if (passResult.EarlyStopped)
				{
					earlyStopped = true;
					break;
				}

				windowEndMs = windowStartMs;
			}
		}

		// Фиксация фактов успешного прохода. Водяной знак монотонен: покрывает всю историю
		// до момента запуска и не откатывается назад даже при сдвиге локальных часов.
		state ??= new SyncState { Category = category };
		var fixedWatermarkMs = Math.Max(state.ExecWatermarkMs ?? long.MinValue, startedAtMs);
		state.ExecWatermarkMs = fixedWatermarkMs;

		// Граница backfill — самая ранняя достигнутая запись: глубже биржа историю не отдаёт,
		// поэтому повторные полные проходы не запрашивают окна старее границы.
		// Traceability: openspec:sync/bybit-history#scenario-backfill-depth-boundary
		if (mode == SyncRunMode.Backfill && allSeenExecutions.Count > 0)
		{
			var earliestSeenMs = allSeenExecutions.Min(execution => execution.ExecTimeMs);
			state.BackfillBoundaryMs = state.BackfillBoundaryMs is { } fixedBoundary
				? Math.Min(fixedBoundary, earliestSeenMs)
				: earliestSeenMs;
		}

		state.LastSuccessAt = startedAt;
		await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);

		return new ExecutionCategorySyncResult
		{
			Mode = mode,
			NewExecutions = newExecutions,
			WindowsProcessed = windowsProcessed,
			HistoryExhausted = historyExhausted,
			EarlyStopped = earlyStopped,
			BackfillBoundaryMs = state.BackfillBoundaryMs,
			ExecWatermarkMs = fixedWatermarkMs,
			NewExecutionsPersisted = newExecutionsPersisted,
		};
	}

	#region Вспомогательные методы

	/// <summary>
	/// Сохраняет пачку новых записей одного окна в хранилище сырых записей и возвращает
	/// число фактически вставленных строк. Пачка пишется сразу после прохождения окна,
	/// поэтому обрыв на следующем окне не теряет уже прочитанные записи, а повторный
	/// прогон продолжает с места остановки без дублей.
	/// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
	/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
	/// </summary>
	private async Task<int> PersistWindowBatchAsync(
		string category,
		ExecutionWindowPassResult passResult,
		SyncRun? progressRun,
		CancellationToken cancellationToken)
	{
		// Пустая пачка не адресуется писателю: пустое окно не создаёт вызовов хранилища.
		if (_rawWriter is null || passResult.NewExecutions.Count == 0)
		{
			return 0;
		}

		var writeResult = await _rawWriter
			.WriteAsync(category, passResult.NewExecutions, progressRun, cancellationToken)
			.ConfigureAwait(false);
		return writeResult.InsertedCount;
	}

	private async Task<ExecutionWindowPassResult> RunWindowAsync(
		string category,
		long windowStartMs,
		long windowEndMs,
		ExecutionCategorySyncOptions options,
		bool earlyStopOnKnownPage,
		CancellationToken cancellationToken)
	{
		var window = new ExecutionWindow
		{
			Category = category,
			StartMs = windowStartMs,
			EndMs = windowEndMs,
		};
		var passOptions = new ExecutionWindowPassOptions
		{
			PageSize = options.PageSize,
			EarlyStopOnKnownPage = earlyStopOnKnownPage,
		};
		return await _windowPass.RunAsync(window, passOptions, cancellationToken).ConfigureAwait(false);
	}

	#endregion
}
