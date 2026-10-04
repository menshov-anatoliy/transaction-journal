using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Sync;

namespace TransactionJournal.Components.Pages;

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

/// <summary>
/// Read-модель журнала синхронизаций экранов «Настройки» и «Синхронизация»: читает
/// строки запусков из хранилища новыми сверху и последний завершённый запуск.
/// Модель тонкая — строка SyncRun уже несёт режим, счётчики и статус, модель ничего
/// не пересчитывает и не фильтрует.
/// </summary>
// Журнал синхронизаций со временем, режимом, результатом и статусом определён
// требованием экрана «Настройки»; предупреждения сверки дополняют журнал на экране.
// Traceability: openspec:ui/screens#requirement-settings-screen
public interface ISyncJournalReadModel
{
	/// <summary>Читает последние запуски синхронизации новыми сверху.</summary>
	/// <param name="limit">Максимальное число строк журнала.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<SyncRunRow>> ReadAsync(int limit = 20, CancellationToken cancellationToken = default);

	/// <summary>
	/// Читает последний завершённый запуск — успешный или прерванный — с его
	/// предупреждениями. Выполняющийся запуск завершённым не считается; пока
	/// завершённых запусков нет, метод возвращает null.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<SyncRunRow?> ReadLastCompletedAsync(CancellationToken cancellationToken = default);
}

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
