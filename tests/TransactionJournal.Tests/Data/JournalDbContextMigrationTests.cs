using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Data;

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
