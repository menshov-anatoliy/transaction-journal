using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using TransactionJournal.Domain.Data;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;

namespace TransactionJournal.Tests.Infrastructure.Data;

/// <summary>
/// Проверки развёртывания сырой схемы журнала: миграция применяется к пустой
/// SQLite-базе, а биржевые идентификаторы дают идемпотентность вставки на уровне БД.
/// </summary>
[TestClass]
public class JournalDbContextMigrationTests
{
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-tests-{Guid.NewGuid():N}.db");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база и соседние WAL/SHM-файлы удаляются после каждой проверки.
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

	[TestMethod]
	[Description("Миграция разворачивает все таблицы сырого хранилища на пустой SQLite-базе")]
	public void TryIfMigrationCreatesRawStorageTablesOnEmptyDatabase()
	{
		// Act: применяем миграции к пустому файлу базы.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		// Assert: все таблицы сырого хранилища и служебные таблицы синка появились в базе.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var tables = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
				.ToList();

			Assert.That(tables, Does.Contain("RawExecutions"));
			Assert.That(tables, Does.Contain("RawDeliveries"));
			Assert.That(tables, Does.Contain("RawInstruments"));
			Assert.That(tables, Does.Contain("SyncRuns"));
			Assert.That(tables, Does.Contain("SyncStates"));
			Assert.That(tables, Does.Contain("Constructions"));
			Assert.That(tables, Does.Contain("ManualCloseAnnotations"));

			// Кэш последних известных котировок — единственное новое хранилище аналитики:
			// развернулся вместе с остальной схемой журнала.
			// Traceability: openspec:analytics/performance#requirement-mark-provider
			Assert.That(tables, Does.Contain("InstrumentQuotes"));

			// История рыночных снимков разворачивается штатным Migrate() на пустой
			// базе: провайдер котировок дописывает строки с первой же успешной выборки,
			// историю невозможно восстановить задним числом.
			// Traceability: openspec:analytics/option-lifetime#requirement-market-snapshot-history
			Assert.That(tables, Does.Contain("InstrumentMarketSnapshots"));

			var snapshotIndexes = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'InstrumentMarketSnapshots' ORDER BY name")
				.ToList();

			// Индекс «символ + момент получения» развёрнут: поиск ближайшего снимка
			// суточной давности прироста распада остаётся точечным запросом.
			Assert.That(snapshotIndexes, Does.Contain("IX_InstrumentMarketSnapshots_Symbol_ReceivedAt"));

			// Таблица настроек приложения уровня рантайма разворачивается штатным
			// Migrate() на чистой базе — переключатель бэкапа синхронизации получает
			// своё хранилище при старте без ручных шагов установки.
			// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
			Assert.That(tables, Does.Contain("AppSettings"));
		}
	}

	[TestMethod]
	[Description("Повторное применение миграций к развёрнутой базе проходит без ошибок")]
	public void TryIfMigrationIsRepeatableOnMigratedDatabase()
	{
		using var db = new JournalDbContext(CreateOptions());

		// Act: повторный запуск миграций должен быть безвредным.
		db.Database.Migrate();
		db.Database.Migrate();

		// Assert: база остаётся доступной для работы.
		Assert.That(db.Database.CanConnect(), Is.True);
	}

	[TestMethod]
	[Description("Уникальный индекс по execId отклоняет дубликат записи исполнения")]
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnDuplicateExecIdInsert()
	{
		// Повторная загрузка известной бирже записи не должна создавать дублей:
		// идемпотентность обеспечивает уникальный индекс по execId.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();

		// Act: вставляем две разные строки с одним биржевым execId.
		db.RawExecutions.Add(CreateRawExecution("exec-duplicate"));
		db.SaveChanges();

		db.RawExecutions.Add(CreateRawExecution("exec-duplicate"));
		db.SaveChanges();
	}

	[TestMethod]
	[Description("Уникальный индекс по symbol + deliveryTime отклоняет дубликат delivery-записи")]
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnDuplicateDeliveryKeyInsert()
	{
		// Delivery-запись узнаётся по паре symbol + deliveryTime: пересекающиеся окна
		// догрузки не должны задваивать экспирацию.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();

		// Act: вставляем две delivery-записи одного инструмента с одним временем поставки.
		db.RawDeliveries.Add(CreateRawDelivery("BTC-27DEC24-2800-C", 1735296000000));
		db.SaveChanges();

		db.RawDeliveries.Add(CreateRawDelivery("BTC-27DEC24-2800-C", 1735296000000));
		db.SaveChanges();
	}

	[TestMethod]
	[Description("Миграция разворачивает таблицу оспариваний синтетических связок с уникальным индексом")]
	// Таблица ручных данных логического слоя разворачивается штатным Migrate()
	// на пустой базе вместе с остальной схемой журнала.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-dispute
	public void TryIfMigrationCreatesSyntheticCloseDisputesTable()
	{
		// Act: применяем миграции к пустому файлу базы.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		// Assert: таблица и уникальный индекс по «конструкция + пара execId» на месте.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var tables = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
				.ToList();
			Assert.That(tables, Does.Contain("SyntheticCloseDisputes"));

			var indexes = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'SyntheticCloseDisputes' ORDER BY name")
				.ToList();
			Assert.That(indexes, Does.Contain("IX_SyntheticCloseDisputes_ConstructionId_LinearExecId_CounterOptionExecId"));
		}
	}

	[TestMethod]
	[Description("Уникальный индекс по «конструкция + пара execId» отклоняет второй спор одной связки")]
	// Спор связки один: повторное оспаривание той же пары исполнений в той же
	// конструкции отклоняется на уровне БД, а не только сервисом.
	// Traceability: change:synthetic-close-logical-layer/design#d3
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnDuplicateDisputePairInsert()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();
		db.Constructions.Add(new Construction { Name = "XAUT стреддл 30OCT26 4400" });
		db.SaveChanges();

		// Act: вставляем второй спор с той же парой execId той же конструкции.
		db.SyntheticCloseDisputes.Add(new SyntheticCloseDispute
		{
			ConstructionId = db.Constructions.Single().Id,
			LinearExecId = "case-perp",
			CounterOptionExecId = "case-call",
			DisputedAt = DateTimeOffset.UtcNow,
		});
		db.SaveChanges();

		db.SyntheticCloseDisputes.Add(new SyntheticCloseDispute
		{
			ConstructionId = db.Constructions.Single().Id,
			LinearExecId = "case-perp",
			CounterOptionExecId = "case-call",
			DisputedAt = DateTimeOffset.UtcNow,
		});
		db.SaveChanges();
	}

	[TestMethod]
	[Description("Откат миграции удаляет таблицу оспариваний, повторное применение возвращает её")]
	// Миграция симметрична: Down удаляет таблицу с записями споров, Up
	// разворачивает её снова — откат и накат проходят штатным Migrate().
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-dispute
	public void TryIfMigrationRollbackDropsAndRollforwardRestoresSyntheticCloseDisputesTable()
	{
		// Act: применяем все миграции и пишем один спор.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			db.Constructions.Add(new Construction { Name = "XAUT стреддл 30OCT26 4400" });
			db.SaveChanges();
			db.SyntheticCloseDisputes.Add(new SyntheticCloseDispute
			{
				ConstructionId = db.Constructions.Single().Id,
				LinearExecId = "case-perp",
				CounterOptionExecId = "case-call",
				DisputedAt = DateTimeOffset.UtcNow,
			});
			db.SaveChanges();
		}

		// Act: откатываем миграцию к предыдущей (AddHints).
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate("20261004093503_AddHints");
		}

		// Assert: таблицы больше нет вместе с записью спора.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var tables = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
				.ToList();
			Assert.That(tables, Does.Not.Contain("SyntheticCloseDisputes"));
		}

		// Act: повторный накат всех миграций.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		// Assert: таблица развёрнута снова и доступна для записей.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var tables = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
				.ToList();
			Assert.That(tables, Does.Contain("SyntheticCloseDisputes"));
			Assert.That(db.SyntheticCloseDisputes, Is.Empty, "Запись спора не пережила откат");
		}
	}

	[TestMethod]
	[Description("Миграция разворачивает таблицы ручных сделок, ручных связок и nullable количество пометки")]
	// Сырьё ручных сделок, ручные связки и nullable-колонка количества пометки
	// разворачиваются штатным Migrate() на пустой базе вместе с остальной схемой.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
	public void TryIfMigrationCreatesManualTradesAndManualLinksTables()
	{
		// Act: применяем миграции к пустому файлу базы.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		// Assert: обе новые таблицы появились в базе.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var tables = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
				.ToList();
			Assert.That(tables, Does.Contain("ManualTrades"));
			Assert.That(tables, Does.Contain("SyntheticCloseManualLinks"));

			// ConstructionId nullable: null означает «Входящие», значение — привязку к конструкции.
			// notnull — зарезервированное слово SQLite, в фильтре экранируется кавычками.
			var manualTradeNullableColumns = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ManualTrades') WHERE \"notnull\" = 0 ORDER BY name")
				.ToList();
			Assert.That(manualTradeNullableColumns, Does.Contain("ConstructionId"));

			// Quantity nullable: null — весь остаток, заданное значение — частичное закрытие.
			var manualCloseAnnotationNullableColumns = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ManualCloseAnnotations') WHERE \"notnull\" = 0 ORDER BY name")
				.ToList();
			Assert.That(manualCloseAnnotationNullableColumns, Does.Contain("Quantity"));
		}
	}

	[TestMethod]
	[Description("Откат миграции удаляет ручные таблицы и колонку количества, повторное применение возвращает их")]
	// Миграция аддитивна и симметрична: Down удаляет новые таблицы и колонку
	// Quantity, Up разворачивает их снова — откат и накат проходят штатным Migrate().
	// Traceability: change:manual-journal-management/design#migration-plan
	public void TryIfMigrationRollbackDropsAndRollforwardRestoresManualTables()
	{
		// Arrange: применяем все миграции и пишем одну ручную сделку и одну связку.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			db.Constructions.Add(new Construction { Name = "XAUT стреддл 30OCT26 4400" });
			db.SaveChanges();
			var constructionId = db.Constructions.Single().Id;
			db.ManualTrades.Add(CreateManualTrade(constructionId));
			db.SyntheticCloseManualLinks.Add(new SyntheticCloseManualLink
			{
				ConstructionId = constructionId,
				LinearTradeId = "exec-perp",
				OptionTradeId = "exec-call",
				Quantity = 2m,
			});
			db.SaveChanges();
		}

		// Act: откатываем миграцию к предыдущей (AddInstrumentMarketSnapshots).
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate("20261018163720_AddInstrumentMarketSnapshots");
		}

		// Assert: новые таблицы удалены, колонка количества у пометки снята.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var tables = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
				.ToList();
			Assert.That(tables, Does.Not.Contain("ManualTrades"));
			Assert.That(tables, Does.Not.Contain("SyntheticCloseManualLinks"));

			var manualCloseAnnotationColumns = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ManualCloseAnnotations') ORDER BY name")
				.ToList();
			Assert.That(manualCloseAnnotationColumns, Does.Not.Contain("Quantity"));
		}

		// Act: повторный накат всех миграций.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		// Assert: таблицы развёрнуты снова и пусты — записи не пережили откат.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.ManualTrades, Is.Empty, "Ручная сделка не пережила откат");
			Assert.That(db.SyntheticCloseManualLinks, Is.Empty, "Ручная связка не пережила откат");
		}
	}

	[TestMethod]
	[Description("Удаление конструкции с привязанной ручной сделкой отклоняется")]
	// Ручная сделка — сырьё журнала: привязка к конструкции запрещает удаление
	// конструкции, сделка снимается заранее — инвариант единственной принадлежности
	// общий для биржевых и ручных сделок.
	// Traceability: openspec:domain/constructions#requirement-trade-single-binding
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnConstructionDeleteWithBoundManualTrade()
	{
		// Arrange: создаём конструкцию с привязанной ручной сделкой и закрываем
		// контекст, чтобы EF не обнулял FK отслеживаемой зависимости — проверяется
		// именно DDL-ограничение, а не клиентское поведение EF.
		using (var db = new JournalDbContext(CreateEnforcedOptions()))
		{
			db.Database.Migrate();
			db.Constructions.Add(new Construction { Name = "XAUT стреддл 30OCT26 4400" });
			db.SaveChanges();
			db.ManualTrades.Add(CreateManualTrade(db.Constructions.Single().Id));
			db.SaveChanges();
		}

		// Act: в новом контексте удаляем конструкцию вместе с привязанной сделкой.
		using (var db = new JournalDbContext(CreateEnforcedOptions()))
		{
			db.Constructions.Remove(db.Constructions.Single());
			db.SaveChanges();
		}
	}

	[TestMethod]
	[Description("Удаление конструкции стирает её ручные синтетические связки")]
	// Ручная связка — ручные данные конструкции: умирает вместе с ней, полный
	// пересбор вычищает таблицу связок целиком, симметрично оспариваниям.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	public void TryIfConstructionDeleteCascadesManualLinks()
	{
		// Arrange: создаём конструкцию со связкой и закрываем контекст, чтобы
		// каскад сработал на уровне БД, а не клиентским каскадом EF.
		using (var db = new JournalDbContext(CreateEnforcedOptions()))
		{
			db.Database.Migrate();
			db.Constructions.Add(new Construction { Name = "XAUT стреддл 30OCT26 4400" });
			db.SaveChanges();
			db.SyntheticCloseManualLinks.Add(new SyntheticCloseManualLink
			{
				ConstructionId = db.Constructions.Single().Id,
				LinearTradeId = "exec-perp",
				OptionTradeId = "exec-call",
				Quantity = 2m,
			});
			db.SaveChanges();
		}

		// Act: в новом контексте удаляем конструкцию.
		using (var db = new JournalDbContext(CreateEnforcedOptions()))
		{
			db.Constructions.Remove(db.Constructions.Single());
			db.SaveChanges();

			// Assert: связка стёрта вместе с конструкцией.
			Assert.That(db.SyntheticCloseManualLinks, Is.Empty);
		}
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private DbContextOptions<JournalDbContext> CreateEnforcedOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath};Foreign Keys=True")
			.Options;

	private static RawExecution CreateRawExecution(string execId) => new()
	{
		ExecId = execId,
		Category = "linear",
		Symbol = "BTCUSDT",
		ExecTimeMs = 1735296000000,
		PayloadJson = """{"execId":"placeholder"}""",
		FetchedAt = DateTimeOffset.UtcNow,
	};

	private static RawDelivery CreateRawDelivery(string symbol, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		DeliveryTimeMs = deliveryTimeMs,
		Category = "option",
		PayloadJson = """{"symbol":"placeholder"}""",
		FetchedAt = DateTimeOffset.UtcNow,
	};

	private static ManualTrade CreateManualTrade(long constructionId) => new()
	{
		ExecutedAt = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero),
		Symbol = "XAUTUSDT",
		Side = "Buy",
		Quantity = 2m,
		Price = 4400m,
		FeeUsdt = 1.5m,
		ConstructionId = constructionId,
		CreatedAt = DateTimeOffset.UtcNow,
		UpdatedAt = DateTimeOffset.UtcNow,
	};

	#endregion
}
