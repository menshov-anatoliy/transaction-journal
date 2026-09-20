using Microsoft.EntityFrameworkCore;

namespace TransactionJournal.Data;

/// <summary>
/// Контекст журнала: сырые записи биржи и состояние синхронизации.
/// Хранит только сырьё и служебные таблицы — доменные представления строятся
/// из сырья при чтении и строками не материализуются.
/// </summary>
public sealed class JournalDbContext(DbContextOptions<JournalDbContext> options) : DbContext(options)
{
	/// <summary>Сырые записи исполнения сделок.</summary>
	public DbSet<RawExecution> RawExecutions => Set<RawExecution>();

	/// <summary>Сырые delivery-записи экспираций.</summary>
	public DbSet<RawDelivery> RawDeliveries => Set<RawDelivery>();

	/// <summary>Сырые спецификации инструментов (справочник).</summary>
	public DbSet<RawInstrument> RawInstruments => Set<RawInstrument>();

	/// <summary>Запуски синхронизации с прогрессом и счётчиками.</summary>
	public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

	/// <summary>Состояние синхронизации по категориям (водяные знаки, границы).</summary>
	public DbSet<SyncState> SyncStates => Set<SyncState>();

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		#region Записи исполнения

		// Уникальный индекс по execId гарантирует идемпотентность вставки на уровне БД:
		// повторный синк не может задвоить уже известную запись исполнения.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		modelBuilder.Entity<RawExecution>()
			.HasIndex(execution => execution.ExecId)
			.IsUnique();

		#endregion

		#region Delivery-записи

		// Идемпотентный ключ delivery-записи — пара symbol + deliveryTime:
		// пересекающиеся окна инкрементальной догрузки не создают дублей.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		modelBuilder.Entity<RawDelivery>()
			.HasIndex(delivery => new { delivery.Symbol, delivery.DeliveryTimeMs })
			.IsUnique();

		#endregion

		#region Справочник инструментов

		// Символ уникален: повторная встреча инструмента в записях обновляет справочник,
		// а не плодит дубли спецификации.
		// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
		modelBuilder.Entity<RawInstrument>()
			.HasIndex(instrument => instrument.Symbol)
			.IsUnique();

		#endregion

		#region Служебные таблицы синхронизации

		modelBuilder.Entity<SyncRun>()
			.Property(run => run.Mode)
			.HasConversion<string>();

		modelBuilder.Entity<SyncRun>()
			.Property(run => run.Status)
			.HasConversion<string>();

		// Строка состояния одна на торговую категорию (linear, option).
		modelBuilder.Entity<SyncState>()
			.HasIndex(state => state.Category)
			.IsUnique();

		#endregion
	}
}
