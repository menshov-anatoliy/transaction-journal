using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using TransactionJournal.Domain.Data;

namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>
/// Проверки читателя живой базы журнала: SQL-выборки RawExecutions и RawDeliveries
/// в границах диапазона дат, разбор PayloadJson в атрибуты сопоставления,
/// счётчики записей вне диапазона и режим только для чтения. Базы создаются
/// миграцией EF во временных файлах и наполняются сырыми записями.
/// </summary>
[TestClass]
public class JournalDbReaderTests
{
	/// <summary>Базовое время записей в диапазоне: 2026-09-22 19:00:00 UTC.</summary>
	private static readonly long InRangeMs = new DateTimeOffset(2026, 9, 22, 19, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Время записи вне диапазона: 2026-10-01 00:00:00 UTC.</summary>
	private static readonly long OutsideMs = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Путь к временной базе текущего теста.</summary>
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-reader-tests-{Guid.NewGuid():N}.db");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		SqliteConnection.ClearAllPools();
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
	[Description("Записи исполнения в диапазоне читаются с атрибутами из PayloadJson, записи вне диапазона попадают в счётчик исключённых")]
	public void TryIfExecutionsInRangeParsedWithPayloadAttributes()
	{
		// Arrange: линейная и опционная записи исполнения в диапазоне и одна запись вне диапазона.
		CreateDatabase();
		InsertExecution(
			execId: "exec-linear-1",
			category: "linear",
			symbol: "BTCUSDT",
			execTimeMs: InRangeMs,
			payload: """
				{"symbol":"BTCUSDT","side":"Buy","orderQty":"0.01","execQty":"0.001","execPrice":"10969.5","execFee":"0.00000219","execTime":%MS%,"execType":"Trade","execId":"exec-linear-1"}
				""".Replace("%MS%", InRangeMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
		InsertExecution(
			execId: "exec-option-1",
			category: "option",
			symbol: "BTC-25SEP26-45000-C",
			execTimeMs: InRangeMs + 1500,
			payload: """
				{"symbol":"BTC-25SEP26-45000-C","side":"Sell","orderQty":"10","execQty":"0.5","execPrice":"310.5","execFee":"","execTime":%MS%,"execType":"Trade","execId":"exec-option-1"}
				""".Replace("%MS%", (InRangeMs + 1500).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
		InsertExecution(
			execId: "exec-outside",
			category: "linear",
			symbol: "ETHUSDT",
			execTimeMs: OutsideMs,
			payload: """
				{"symbol":"ETHUSDT","side":"Buy","orderQty":"1","execPrice":"2000","execFee":"0.1","execTime":%MS%,"execId":"exec-outside"}
				""".Replace("%MS%", OutsideMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
		var reader = new JournalDbReader(_databasePath);

		// Act
		var data = reader.ReadInRange(InRangeMs - 5000, InRangeMs + 5000);

		// Assert: атрибуты разобраны из PayloadJson, запись вне диапазона учтена счётчиком.
		// Количество берётся из execQty — исполненного количества фактического заполнения,
		// как в строке выгрузки: orderQty заявок дробится частями и ключу пары не служит.
		// Traceability: change:reconcile-bybit-statement/design#d2
		// Traceability: change:reconcile-bybit-statement/design#d9
		Assert.That(data.Executions, Has.Count.EqualTo(2));
		Assert.That(data.ExecutionsOutsideRange, Is.EqualTo(1));
		var linear = data.Executions.Single(execution => execution.ExecId == "exec-linear-1");
		Assert.That(linear.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(linear.Side, Is.EqualTo("BUY"));
		Assert.That(linear.ExecType, Is.EqualTo("Trade"));
		Assert.That(linear.Quantity, Is.EqualTo(0.001m), "количество берётся из execQty, а не из orderQty");
		Assert.That(linear.Price, Is.EqualTo(10969.5m));
		Assert.That(linear.Fee, Is.EqualTo(0.00000219m));
		Assert.That(linear.TimeUtc, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(InRangeMs).UtcDateTime));
		var option = data.Executions.Single(execution => execution.ExecId == "exec-option-1");
		Assert.That(option.Side, Is.EqualTo("SELL"));
		Assert.That(option.Quantity, Is.EqualTo(0.5m), "для опционов orderQty кратно parts, execQty соответствует строке выгрузки");
		Assert.That(option.Fee, Is.Null, "пустая строка комиссии трактуется как отсутствие значения");
	}

	[TestMethod]
	[Description("Delivery-записи в диапазоне читаются с инструментом, количеством из PayloadJson и временем delivery")]
	public void TryIfDeliveriesInRangeParsedWithPayloadAttributes()
	{
		// Arrange: delivery-запись опциона в диапазоне и одна вне диапазона.
		CreateDatabase();
		InsertDelivery(
			symbol: "BTC-25SEP26-45000-C",
			category: "option",
			deliveryTimeMs: InRangeMs,
			payload: """
				{"deliveryTime":%MS%,"symbol":"BTC-25SEP26-45000-C","side":"Buy","position":"0.01","deliveryPrice":"16541.86","fee":"0.00000000","deliveryRpl":"3.5"}
				""".Replace("%MS%", InRangeMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
		InsertDelivery(
			symbol: "BTC-25SEP26-40000-P",
			category: "option",
			deliveryTimeMs: OutsideMs,
			payload: """
				{"deliveryTime":%MS%,"symbol":"BTC-25SEP26-40000-P","side":"Sell","position":"0.02","deliveryPrice":"1500","fee":"0","deliveryRpl":"1"}
				""".Replace("%MS%", OutsideMs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
		var reader = new JournalDbReader(_databasePath);

		// Act
		var data = reader.ReadInRange(InRangeMs - 5000, InRangeMs + 5000);

		// Assert: количество разобрано из position, время — из колонки DeliveryTimeMs.
		// Traceability: change:reconcile-bybit-statement/design#d6
		Assert.That(data.Deliveries, Has.Count.EqualTo(1));
		Assert.That(data.DeliveriesOutsideRange, Is.EqualTo(1));
		var delivery = data.Deliveries[0];
		Assert.That(delivery.Symbol, Is.EqualTo("BTC-25SEP26-45000-C"));
		Assert.That(delivery.Quantity, Is.EqualTo(0.01m));
		Assert.That(delivery.TimeUtc, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(InRangeMs).UtcDateTime));
	}

	[TestMethod]
	[ExpectedException(typeof(FileNotFoundException))]
	[Description("Отсутствующий файл базы журнала даёт ошибку, а не пустой результат")]
	public void ThrowOnDatabaseFileMissing()
	{
		// Arrange: путь к базе, которой не существует.
		var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.db");

		// Act
		new JournalDbReader(path);

		// Assert: ожидается FileNotFoundException (атрибут ExpectedException).
	}

	[TestMethod]
	[ExpectedException(typeof(SqliteException))]
	[Description("Попытка записи в базу, открытую читателем в режиме только для чтения, даёт SqliteException")]
	public void ThrowOnWriteToReadOnlyConnection()
	{
		// Arrange: временная база с миграцией и read-only соединение читателя.
		// Требование: прогон не изменяет живую базу — запись через соединение читателя
		// обязана отклоняться на уровне SQLite.
		// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-one-off-diagnostic-run
		CreateDatabase();
		using var connection = new JournalDbReader(_databasePath).OpenReadOnlyConnection();
		using var command = connection.CreateCommand();
		command.CommandText = """
			INSERT INTO RawExecutions (ExecId, Category, Symbol, ExecTimeMs, PayloadJson, FetchedAt)
			VALUES ('x', 'linear', 'BTCUSDT', 0, '{}', '2026-01-01 00:00:00+00:00')
			""";

		// Act
		command.ExecuteNonQuery();

		// Assert: ожидается SqliteException (атрибут ExpectedException).
	}

	/// <summary>
	/// Создаёт временную базу, применяя миграции EF, и закрывает контекст.
	/// </summary>
	private void CreateDatabase()
	{
		var options = new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;
		using (var db = new JournalDbContext(options))
		{
			db.Database.Migrate();
		}

		SqliteConnection.ClearAllPools();
	}

	/// <summary>
	/// Вставляет сырую запись исполнения напрямую через EF.
	/// </summary>
	private void InsertExecution(string execId, string category, string symbol, long execTimeMs, string payload)
	{
		var options = new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;
		using var db = new JournalDbContext(options);
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = category,
			Symbol = symbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = payload,
			FetchedAt = DateTimeOffset.UtcNow,
		});
		db.SaveChanges();
		SqliteConnection.ClearAllPools();
	}

	/// <summary>
	/// Вставляет сырую delivery-запись напрямую через EF.
	/// </summary>
	private void InsertDelivery(string symbol, string category, long deliveryTimeMs, string payload)
	{
		var options = new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;
		using var db = new JournalDbContext(options);
		db.RawDeliveries.Add(new RawDelivery
		{
			Symbol = symbol,
			Category = category,
			DeliveryTimeMs = deliveryTimeMs,
			PayloadJson = payload,
			FetchedAt = DateTimeOffset.UtcNow,
		});
		db.SaveChanges();
		SqliteConnection.ClearAllPools();
	}
}
