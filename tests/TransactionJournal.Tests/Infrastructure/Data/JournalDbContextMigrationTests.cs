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
			Assert.That(tables, Does.Contain("ManualCloseMarks"));

			// Кэш последних известных марок — единственное новое хранилище аналитики:
			// развернулся вместе с остальной схемой журнала.
			// Traceability: openspec:analytics/performance#requirement-mark-provider
			Assert.That(tables, Does.Contain("InstrumentMarks"));

			// История рыночных снимков разворачивается штатным Migrate() на пустой
			// базе: провайдер марок дописывает строки с первой же успешной выборки,
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

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
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

	#endregion
}
