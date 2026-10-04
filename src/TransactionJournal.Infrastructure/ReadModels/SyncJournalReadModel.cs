using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application;

namespace TransactionJournal.Infrastructure.ReadModels;

/// <summary>
/// Реализация read-модели журнала синхронизаций: короткоживущий контекст над
/// хранилищем журнала, как у остальных read-моделей экранов.
/// </summary>
public sealed class SyncJournalReadModel : ISyncJournalReadModel
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт read-модель над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public SyncJournalReadModel(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<SyncRunRow>> ReadAsync(int limit = 20, CancellationToken cancellationToken = default)
	{
		using var db = new JournalDbContext(_options);
		var runs = await db.SyncRuns
			.AsNoTracking()
			// Порядок новыми сверху держится на монотонном суррогатном ключе вставки:
			// запуски открываются последовательно, а SQLite не упорядочивает по
			// DateTimeOffset.
			.OrderByDescending(run => run.Id)
			.Take(limit)
			.Select(run => new SyncRunRow(
				run.Id,
				run.StartedAt,
				run.FinishedAt,
				run.Mode,
				run.Status,
				run.Error,
				run.NewExecutions,
				run.NewDeliveries,
				run.NewInstruments,
				run.WarningsJson))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return runs;
	}

	/// <inheritdoc cref="ISyncJournalReadModel.ReadLastCompletedAsync" />
	public async Task<SyncRunRow?> ReadLastCompletedAsync(CancellationToken cancellationToken = default)
	{
		// Последний завершённый запуск ищется тем же монотонным порядком ключей, что и
		// весь журнал: предупреждения страницы синхронизации берутся у самой свежей
		// закрытой строки — успешной или прерванной.
		// Traceability: openspec:sync/bybit-history#scenario-run-warnings-shown-on-page-load
		using var db = new JournalDbContext(_options);
		var run = await db.SyncRuns
			.AsNoTracking()
			.Where(row => row.Status != SyncRunStatus.Running)
			.OrderByDescending(row => row.Id)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
		return run is null
			? null
			: new SyncRunRow(
				run.Id,
				run.StartedAt,
				run.FinishedAt,
				run.Mode,
				run.Status,
				run.Error,
				run.NewExecutions,
				run.NewDeliveries,
				run.NewInstruments,
				run.WarningsJson);
	}
}

