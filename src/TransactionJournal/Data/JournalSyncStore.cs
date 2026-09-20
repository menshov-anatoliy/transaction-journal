using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Bybit;
using TransactionJournal.Sync;

namespace TransactionJournal.Data;

/// <summary>
/// EF-адаптер сырого хранилища журнала над JournalDbContext: проверка известных execId
/// и delivery-ключей, идемпотентная пакетная вставка записей исполнения и delivery-записей
/// с продвижением счётчиков запуска, хранение состояния категорий и журнал запусков
/// синхронизации. Каждый вызов создаёт собственный короткоживущий контекст, поэтому
/// адаптер не держит соединений между вызовами и безопасен в длительных сессиях Blazor Server.
/// Идемпотентность вставки двойная: известные записи отфильтровываются запросом до
/// вставки, а уникальные индексы (execId; symbol + deliveryTime) отклоняют дубликат
/// на уровне БД.
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// Traceability: change:add-bybit-sync/design#d2
/// </summary>
public sealed class JournalSyncStore :
	IExecutionKnownIdProbe,
	IExecutionSyncStateStore,
	IRawExecutionBatchWriter,
	IDeliveryKnownKeyProbe,
	IRawDeliveryBatchWriter,
	ISyncRunJournal
{
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт адаптер над опциями контекста журнала.</summary>
	/// <param name="options">Опции EF-контекста; база уже развёрнута миграциями.</param>
	/// <param name="timeProvider">Поставщик времени для отметок загрузки и запусков; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public JournalSyncStore(DbContextOptions<JournalDbContext> options, TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	#region IExecutionKnownIdProbe

	/// <inheritdoc cref="IExecutionKnownIdProbe.FindKnownAsync" />
	public async Task<IReadOnlySet<string>> FindKnownAsync(
		IReadOnlyCollection<string> execIds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(execIds);
		if (execIds.Count == 0)
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		using var db = CreateContext();
		var knownExecIds = await db.RawExecutions
			.Where(execution => execIds.Contains(execution.ExecId))
			.Select(execution => execution.ExecId)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return new HashSet<string>(knownExecIds, StringComparer.Ordinal);
	}

	#endregion

	#region IRawExecutionBatchWriter

	/// <inheritdoc cref="IRawExecutionBatchWriter.WriteAsync" />
	public async Task<RawExecutionBatchResult> WriteAsync(
		string category,
		IReadOnlyCollection<BybitExecution> executions,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		ArgumentNullException.ThrowIfNull(executions);
		if (executions.Count == 0)
		{
			return new RawExecutionBatchResult { InsertedExecIds = [], SkippedKnownCount = 0 };
		}

		// Внутри пачки один execId вставляется один раз: биржа не повторяет идентификаторы
		// в одной выдаче, но защита дешевле расследования дублей.
		var uniqueExecutions = new List<BybitExecution>(executions.Count);
		var uniqueExecIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var execution in executions)
		{
			if (uniqueExecIds.Add(execution.ExecId))
			{
				uniqueExecutions.Add(execution);
			}
		}

		// Известность спрашиваем у хранилища до вставки: повторный прогон после обрыва
		// пропускает уже сохранённые записи, а уникальный индекс остаётся страховкой
		// от гонок и дублей на уровне БД.
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		var knownExecIds = await FindKnownAsync(uniqueExecIds.ToArray(), cancellationToken).ConfigureAwait(false);
		var fetchedAt = _timeProvider.GetUtcNow();

		using var db = CreateContext();
		var insertedExecIds = new List<string>();
		foreach (var execution in uniqueExecutions)
		{
			if (knownExecIds.Contains(execution.ExecId))
			{
				continue;
			}

			// Запись сохраняется целиком в сыром виде: JSON хранит все поля биржи,
			// идентификатор источника и время загрузки — этого достаточно для полного
			// переразбора доменных представлений без обращения к API.
			// Traceability: openspec:sync/bybit-history#scenario-raw-records-persisted-for-reparse
			db.RawExecutions.Add(new RawExecution
			{
				ExecId = execution.ExecId,
				Category = category,
				Symbol = execution.Symbol,
				ExecTimeMs = execution.ExecTimeMs,
				PayloadJson = JsonSerializer.Serialize(execution, BybitJson.Options),
				FetchedAt = fetchedAt,
			});
			insertedExecIds.Add(execution.ExecId);
		}

		// Счётчик запуска продвигается той же транзакцией SaveChanges, что и вставка:
		// прогресс в SyncRun не расходится с фактически сохранёнными записями даже
		// при обрыве на середине запуска.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		if (progressRun is not null && insertedExecIds.Count > 0)
		{
			var runRow = await FindRunRowAsync(db, progressRun.Id, cancellationToken).ConfigureAwait(false);
			runRow.NewExecutions += insertedExecIds.Count;
			progressRun.NewExecutions = runRow.NewExecutions;
		}

		if (insertedExecIds.Count > 0)
		{
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return new RawExecutionBatchResult
		{
			InsertedExecIds = insertedExecIds,
			SkippedKnownCount = knownExecIds.Count,
		};
	}

	#endregion

	#region IDeliveryKnownKeyProbe

	/// <inheritdoc cref="IDeliveryKnownKeyProbe.FindKnownAsync" />
	public async Task<IReadOnlySet<DeliveryRecordKey>> FindKnownAsync(
		IReadOnlyCollection<DeliveryRecordKey> keys,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(keys);
		if (keys.Count == 0)
		{
			return new HashSet<DeliveryRecordKey>();
		}

		// Один запрос по множеству символов пачки: пары symbol + deliveryTime сверяются
		// в памяти после чтения строк кандидатов, чтобы результат содержал только
		// запрошенные ключи, а не все строки найденных символов.
		using var db = CreateContext();
		var symbols = keys.Select(key => key.Symbol).Distinct(StringComparer.Ordinal).ToArray();
		var candidateRows = await db.RawDeliveries
			.Where(delivery => symbols.Contains(delivery.Symbol))
			.Select(delivery => new { delivery.Symbol, delivery.DeliveryTimeMs })
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var requestedKeys = keys.ToHashSet();
		return candidateRows
			.Select(row => new DeliveryRecordKey(row.Symbol, row.DeliveryTimeMs))
			.Where(requestedKeys.Contains)
			.ToHashSet();
	}

	#endregion

	#region IRawDeliveryBatchWriter

	/// <inheritdoc cref="IRawDeliveryBatchWriter.WriteAsync" />
	public async Task<RawDeliveryBatchResult> WriteAsync(
		string category,
		IReadOnlyCollection<BybitDeliveryRecord> deliveries,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		ArgumentNullException.ThrowIfNull(deliveries);
		if (deliveries.Count == 0)
		{
			return new RawDeliveryBatchResult { InsertedKeys = [], SkippedKnownCount = 0 };
		}

		// Внутри пачки один ключ symbol + deliveryTime вставляется один раз: защита
		// дешевле расследования дублей.
		var uniqueDeliveries = new List<BybitDeliveryRecord>(deliveries.Count);
		var uniqueKeys = new HashSet<DeliveryRecordKey>();
		foreach (var delivery in deliveries)
		{
			if (uniqueKeys.Add(new DeliveryRecordKey(delivery.Symbol, delivery.DeliveryTimeMs)))
			{
				uniqueDeliveries.Add(delivery);
			}
		}

		// Известность спрашиваем у хранилища до вставки: повторный прогон после обрыва
		// и пересекающиеся окна пропускают уже сохранённые записи, а уникальный индекс
		// по symbol + deliveryTime остаётся страховкой от гонок на уровне БД.
		// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
		var knownKeys = await FindKnownAsync(uniqueKeys.ToArray(), cancellationToken).ConfigureAwait(false);
		var fetchedAt = _timeProvider.GetUtcNow();

		using var db = CreateContext();
		var insertedKeys = new List<DeliveryRecordKey>();
		foreach (var delivery in uniqueDeliveries)
		{
			var key = new DeliveryRecordKey(delivery.Symbol, delivery.DeliveryTimeMs);
			if (knownKeys.Contains(key))
			{
				continue;
			}

			// Запись сохраняется целиком в сыром виде: JSON хранит все поля биржи,
			// идентификатор источника и время загрузки — этого достаточно для полного
			// переразбора закрывающих записей без обращения к API.
			// Traceability: openspec:sync/bybit-history#scenario-raw-records-persisted-for-reparse
			db.RawDeliveries.Add(new RawDelivery
			{
				Symbol = delivery.Symbol,
				DeliveryTimeMs = delivery.DeliveryTimeMs,
				Category = category,
				PayloadJson = JsonSerializer.Serialize(delivery, BybitJson.Options),
				FetchedAt = fetchedAt,
			});
			insertedKeys.Add(key);
		}

		// Счётчик запуска продвигается той же транзакцией SaveChanges, что и вставка:
		// прогресс в SyncRun не расходится с фактически сохранёнными записями даже
		// при обрыве на середине запуска.
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		if (progressRun is not null && insertedKeys.Count > 0)
		{
			var runRow = await FindRunRowAsync(db, progressRun.Id, cancellationToken).ConfigureAwait(false);
			runRow.NewDeliveries += insertedKeys.Count;
			progressRun.NewDeliveries = runRow.NewDeliveries;
		}

		if (insertedKeys.Count > 0)
		{
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return new RawDeliveryBatchResult
		{
			InsertedKeys = insertedKeys,
			SkippedKnownCount = knownKeys.Count,
		};
	}

	#endregion

	#region IExecutionSyncStateStore

	/// <inheritdoc cref="IExecutionSyncStateStore.FindAsync" />
	public async Task<SyncState?> FindAsync(string category, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		using var db = CreateContext();
		return await db.SyncStates
			.FirstOrDefaultAsync(state => state.Category == category, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <inheritdoc cref="IExecutionSyncStateStore.SaveAsync" />
	public async Task SaveAsync(SyncState state, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentException.ThrowIfNullOrWhiteSpace(state.Category);

		using var db = CreateContext();
		var existing = await db.SyncStates
			.FirstOrDefaultAsync(current => current.Category == state.Category, cancellationToken)
			.ConfigureAwait(false);
		if (existing is null)
		{
			db.SyncStates.Add(state);
		}
		else
		{
			// Строка состояния одна на категорию: обновляются водяные знаки и граница,
			// суррогатный ключ строки остаётся на месте.
			existing.ExecWatermarkMs = state.ExecWatermarkMs;
			existing.DeliveryWatermarkMs = state.DeliveryWatermarkMs;
			existing.BackfillBoundaryMs = state.BackfillBoundaryMs;
			existing.LastSuccessAt = state.LastSuccessAt;
		}

		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region ISyncRunJournal

	/// <inheritdoc cref="ISyncRunJournal.StartAsync" />
	public async Task<SyncRun> StartAsync(SyncRunMode mode, CancellationToken cancellationToken = default)
	{
		var run = new SyncRun
		{
			StartedAt = _timeProvider.GetUtcNow(),
			Mode = mode,
			Status = SyncRunStatus.Running,
		};

		using var db = CreateContext();
		db.SyncRuns.Add(run);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return run;
	}

	/// <inheritdoc cref="ISyncRunJournal.MarkSucceededAsync" />
	public async Task MarkSucceededAsync(SyncRun run, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(run);
		var finishedAt = _timeProvider.GetUtcNow();

		using var db = CreateContext();
		var runRow = await FindRunRowAsync(db, run.Id, cancellationToken).ConfigureAwait(false);
		runRow.Status = SyncRunStatus.Succeeded;
		runRow.FinishedAt = finishedAt;
		runRow.Error = null;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		SyncHandle(run, runRow);
	}

	/// <inheritdoc cref="ISyncRunJournal.MarkFailedAsync" />
	public async Task MarkFailedAsync(SyncRun run, string error, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(run);
		ArgumentException.ThrowIfNullOrWhiteSpace(error);
		var finishedAt = _timeProvider.GetUtcNow();

		using var db = CreateContext();
		var runRow = await FindRunRowAsync(db, run.Id, cancellationToken).ConfigureAwait(false);
		runRow.Status = SyncRunStatus.Failed;
		runRow.FinishedAt = finishedAt;
		runRow.Error = error;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		SyncHandle(run, runRow);
	}

	#endregion

	#region Вспомогательные методы

	private JournalDbContext CreateContext()
	{
		return new JournalDbContext(_options);
	}

	private static async Task<SyncRun> FindRunRowAsync(
		JournalDbContext db,
		long runId,
		CancellationToken cancellationToken)
	{
		var runRow = await db.SyncRuns.FindAsync(new object[] { runId }, cancellationToken).ConfigureAwait(false);
		return runRow ?? throw new InvalidOperationException(
			$"Запуск синхронизации с ключом {runId} не найден в журнале запусков.");
	}

	// Дескриптор запуска в памяти синхронизируется со строкой хранилища, чтобы вызывающая
	// сторона видела статус и счётчики без повторного чтения базы.
	private static void SyncHandle(SyncRun handle, SyncRun runRow)
	{
		handle.Status = runRow.Status;
		handle.FinishedAt = runRow.FinishedAt;
		handle.Error = runRow.Error;
		handle.NewExecutions = runRow.NewExecutions;
		handle.NewDeliveries = runRow.NewDeliveries;
		handle.NewInstruments = runRow.NewInstruments;
	}

	#endregion
}
