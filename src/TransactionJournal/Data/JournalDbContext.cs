using Microsoft.EntityFrameworkCore;

namespace TransactionJournal.Data;

/// <summary>
/// Контекст журнала: сырые записи биржи, состояние синхронизации и доменные
/// пользовательские записи. Сырьё и пользовательские данные хранятся строками,
/// а доменные представления (позиции, результаты) строятся из них при чтении
/// и строками не материализуются.
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

	/// <summary>Конструкции — единственный мутируемый агрегат домена журнала.</summary>
	public DbSet<Construction> Constructions => Set<Construction>();

	/// <summary>Внешние корректировки PnL — слагаемые результата конструкции без сделки.</summary>
	public DbSet<PnLAdjustment> PnLAdjustments => Set<PnLAdjustment>();

	/// <summary>Пользовательские данные сделок по ключу execId: привязка и комментарий.</summary>
	public DbSet<TradeUserdata> TradeUserdata => Set<TradeUserdata>();

	/// <summary>Комментарии позиций по ключу «конструкция × инструмент».</summary>
	public DbSet<PositionComment> PositionComments => Set<PositionComment>();

	/// <summary>Ручные пометки закрытия позиций — пользовательские закрывающие записи.</summary>
	public DbSet<ManualCloseMark> ManualCloseMarks => Set<ManualCloseMark>();

	/// <summary>Кэш последних известных марок инструментов — провайдер марок аналитики.</summary>
	public DbSet<InstrumentMark> InstrumentMarks => Set<InstrumentMark>();

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

		#region Доменные пользовательские записи

		// Статус конструкции хранится строкой для читаемости сырой базы.
		modelBuilder.Entity<Construction>()
			.Property(construction => construction.Status)
			.HasConversion<string>();

		// Корректировка принадлежит конструкции, а удаление конструкции допустимо
		// только без корректировок: внешний ключ запрещает тихое каскадное удаление
		// слагаемых результата на уровне БД.
		// Traceability: openspec:domain/constructions#scenario-delete-only-when-empty
		modelBuilder.Entity<PnLAdjustment>()
			.HasOne(adjustment => adjustment.Construction)
			.WithMany()
			.HasForeignKey(adjustment => adjustment.ConstructionId)
			.OnDelete(DeleteBehavior.Restrict);

		// Источник корректировки хранится строкой: «робот» или «ручная».
		modelBuilder.Entity<PnLAdjustment>()
			.Property(adjustment => adjustment.Source)
			.HasConversion<string>();

		// execId уникален: одна строка пользовательских данных на сделку хранит
		// единственную привязку, пока сделка не во «Входящих» (ConstructionId = null).
		// Привязка запрещает удаление конструкции с привязанными сделками — как и
		// корректировки, сделки должны быть сняты заранее.
		// Traceability: openspec:domain/constructions#requirement-trade-single-binding
		modelBuilder.Entity<TradeUserdata>()
			.HasIndex(userdata => userdata.ExecId)
			.IsUnique();

		modelBuilder.Entity<TradeUserdata>()
			.HasOne(userdata => userdata.Construction)
			.WithMany()
			.HasForeignKey(userdata => userdata.ConstructionId)
			.OnDelete(DeleteBehavior.Restrict);

		// Комментарий позиции хранится по стабильному ключу «конструкция × инструмент»:
		// уникальный индекс не даёт завести вторую строку на тот же ключ.
		// Traceability: openspec:domain/constructions#requirement-entity-comments
		modelBuilder.Entity<PositionComment>()
			.HasIndex(comment => new { comment.ConstructionId, comment.Symbol })
			.IsUnique();

		// Осиротевшие комментарии позиций и пометки закрытия удаляются каскадом
		// вместе с конструкцией: условие удаления «без сделок и корректировок»
		// на практике исключает живые пометки.
		// Traceability: change:add-core-domain/design#d1
		modelBuilder.Entity<PositionComment>()
			.HasOne(comment => comment.Construction)
			.WithMany()
			.HasForeignKey(comment => comment.ConstructionId)
			.OnDelete(DeleteBehavior.Cascade);

		modelBuilder.Entity<ManualCloseMark>()
			.HasOne(mark => mark.Construction)
			.WithMany()
			.HasForeignKey(mark => mark.ConstructionId)
			.OnDelete(DeleteBehavior.Cascade);

		#endregion

		#region Кэш марок аналитики

		// Символ уникален: марка одного инструмента хранится одной строкой, новое
		// получение марки обновляет её, а не плодит историю — кэш хранит именно
		// последнюю известную марку со временем получения.
		// Traceability: openspec:analytics/performance#requirement-mark-provider
		modelBuilder.Entity<InstrumentMark>()
			.HasIndex(mark => mark.Symbol)
			.IsUnique();

		#endregion
	}
}
