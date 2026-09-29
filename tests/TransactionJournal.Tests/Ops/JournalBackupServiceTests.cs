using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Ops;
using TransactionJournal.Tests;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ops;

/// <summary>
/// Проверки сервиса резервного копирования журнала на временной SQLite-базе:
/// копия создаётся снапшотом работающей базы и воспроизводит данные, включая
/// записи, ещё не перенесённые из WAL в основной файл, имя различает причины
/// и переживает коллизию в одну секунду, а ротация удаляет старейшие копии за
/// лимитом, сообщая неудачи удаления предупреждениями без отмены операции.
/// </summary>
[TestClass]
public class JournalBackupServiceTests
{
	// Виртуальные часы фиксированы на 2026-01-01: метка времени в имени копии
	// воспроизводится тестом из того же источника, что и в сервисе.
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private string _backupDirectory = null!;
	private FixedTimeProvider _timeProvider = null!;
	private JournalBackupService _service = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой и своим каталогом копий.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-backup-tests-{Guid.NewGuid():N}.db");
		_backupDirectory = Path.Combine(Path.GetTempPath(), $"journal-backup-tests-{Guid.NewGuid():N}", "backups");
		using (var db = new JournalDbContext(CreateOptions(_databasePath)))
		{
			db.Database.Migrate();
			// Режим WAL включается как в приложении при старте — записи идут в -wal
			// файл, пока соединение открыто и чекпоинт не выполнен.
			db.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
		}

		_timeProvider = new FixedTimeProvider(Now);
		_service = CreateService(limit: 15);
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база, соседние WAL/SHM-файлы и каталог копий удаляются после проверки.
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}

		if (Directory.Exists(Path.GetDirectoryName(_backupDirectory)))
		{
			Directory.Delete(Path.GetDirectoryName(_backupDirectory)!, recursive: true);
		}
	}

	[TestMethod]
	[Description("Копия создаётся и воспроизводит данные с учётом незачекпоинченного WAL")]
	public async Task TryIfBackupSnapshotReproducesDataIncludingWal()
	{
		// Arrange: запись пишется при открытом соединении в WAL-режиме — она обязана
		// остаться в -wal файле, не попадая в основной файл базы до чекпоинта.
		// Требование: копия воспроизводит состояние базы на момент операции, включая
		// WAL-данные, не перенесённые в основной файл.
		// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
		var marker = "exec-wal-snapshot-marker";
		using (var writer = new JournalDbContext(CreateOptions(_databasePath)))
		{
			writer.RawExecutions.Add(CreateRawExecution("exec-wal-1", marker));
			writer.SaveChanges();

			// Предпосылка проверки: маркера ещё нет в основном файле — иначе тест
			// не доказывал бы попадание WAL-данных в копию. Файл открыт пулом
			// соединений SQLite, поэтому читается с общим доступом.
			Assert.That(ReadAllBytesShared(_databasePath).AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker)), Is.EqualTo(-1));
		}

		// Act
		var result = await _service.CreateBackupAsync("rebuild");

		// Assert: копия существует и содержит запись, жившую только в WAL.
		var copyPath = Path.Combine(_backupDirectory, result.FileName);
		Assert.That(File.Exists(copyPath), Is.True);
		using (var copy = new JournalDbContext(CreateOptions(copyPath)))
		{
			var stored = copy.RawExecutions.Single(execution => execution.ExecId == "exec-wal-1");
			Assert.That(stored.PayloadJson, Does.Contain(marker));
		}
	}

	[TestMethod]
	[Description("Имя копии различает причины и укладывается в шаблон с меткой времени")]
	public async Task TryIfBackupFileNameDistinguishesReasons()
	{
		// Требование: имена файлов различают операцию-причину и позволяют упорядочить
		// копии по времени создания.
		// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
		var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

		// Act: копии перед разными операциями — полный пересбор, синхронизация, удаление.
		var rebuild = await _service.CreateBackupAsync("rebuild");
		var sync = await _service.CreateBackupAsync("sync");
		var delete = await _service.CreateBackupAsync("delete-construction");

		// Assert: у каждой причины своё имя шаблона journal-<время>-<причина>.db,
		// все три файла появились в каталоге копий.
		Assert.That(rebuild.FileName, Is.EqualTo($"journal-{stamp}-rebuild.db"));
		Assert.That(sync.FileName, Is.EqualTo($"journal-{stamp}-sync.db"));
		Assert.That(delete.FileName, Is.EqualTo($"journal-{stamp}-delete-construction.db"));
		foreach (var fileName in new[] { rebuild.FileName, sync.FileName, delete.FileName })
		{
			Assert.That(File.Exists(Path.Combine(_backupDirectory, fileName)), Is.True, fileName);
		}
	}

	[TestMethod]
	[Description("Коллизия имени в ту же секунду разрешается суффиксом-счётчиком")]
	public async Task TryIfSameSecondCollisionGetsCounterSuffix()
	{
		// Act: две копии с одной причиной по одним виртуальным часам — одна секунда.
		var first = await _service.CreateBackupAsync("rebuild");
		var second = await _service.CreateBackupAsync("rebuild");

		// Assert: вторая копия получила суффикс-счётчик и не перезаписала первую.
		var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		Assert.That(first.FileName, Is.EqualTo($"journal-{stamp}-rebuild.db"));
		Assert.That(second.FileName, Is.EqualTo($"journal-{stamp}-rebuild-2.db"));
		Assert.That(File.Exists(Path.Combine(_backupDirectory, first.FileName)), Is.True);
		Assert.That(File.Exists(Path.Combine(_backupDirectory, second.FileName)), Is.True);
	}

	[TestMethod]
	[Description("Ротация удаляет старейшие копии за лимитом и хранит свежайшую")]
	public async Task TryIfRotationDeletesOldestCopiesBeyondLimitAndKeepsFresh()
	{
		// Arrange: три «старые» копии с нарастающим временем файла и лимит на три —
		// новая копия должна вытеснить только старейшую.
		// Требование: самые старые копии удаляются, число копий не превышает лимит,
		// только что созданная копия сохраняется.
		// Traceability: openspec:ops/db-backup#requirement-backup-retention
		var oldest = SeedCopy("journal-20250101-000001-rebuild.db", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		var middle = SeedCopy("journal-20250102-000001-sync.db", new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));
		var newest = SeedCopy("journal-20250103-000001-rebuild.db", new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));
		var service = CreateService(limit: 3);

		// Act
		var result = await service.CreateBackupAsync("rebuild");

		// Assert: удалена только старейшая копия, остальные и свежая на месте,
		// ротация прошла без предупреждений.
		Assert.That(File.Exists(Path.Combine(_backupDirectory, oldest)), Is.False, oldest);
		Assert.That(File.Exists(Path.Combine(_backupDirectory, middle)), Is.True, middle);
		Assert.That(File.Exists(Path.Combine(_backupDirectory, newest)), Is.True, newest);
		Assert.That(File.Exists(Path.Combine(_backupDirectory, result.FileName)), Is.True, result.FileName);
		Assert.That(result.RotationWarnings, Is.Empty);
	}

	[TestMethod]
	[Description("Неудача удаления при ротации даёт предупреждение и не отменяет копию")]
	public async Task TryIfRotationFailureYieldsWarningWithoutCancellingBackup()
	{
		// Arrange: старейшая копия заблокирована открытым файлом без общего доступа —
		// удаление упадёт, но не должно сорвать ни копию, ни защищаемую операцию.
		// Требование: при неудавшейся ротации операция выполняется, пользователь
		// видит предупреждение.
		// Traceability: openspec:ops/db-backup#requirement-backup-retention
		var locked = SeedCopy("journal-20250101-000001-rebuild.db", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		var service = CreateService(limit: 1);
		using var stream = new FileStream(
			Path.Combine(_backupDirectory, locked),
			FileMode.Open,
			FileAccess.Read,
			FileShare.None);

		// Act: создание копии завершается успешно несмотря на занятый старый файл.
		var result = await service.CreateBackupAsync("rebuild");

		// Assert: свежая копия создана, заблокированная осталась, предупреждение
		// называет неудавшееся удаление.
		Assert.That(File.Exists(Path.Combine(_backupDirectory, result.FileName)), Is.True, result.FileName);
		Assert.That(File.Exists(Path.Combine(_backupDirectory, locked)), Is.True, locked);
		Assert.That(result.RotationWarnings, Has.Count.EqualTo(1));
		Assert.That(result.RotationWarnings[0], Does.Contain(locked));
	}

	#region Помощники

	private JournalBackupService CreateService(int limit) => new(
		CreateOptions(_databasePath),
		new JournalBackupOptions { Directory = _backupDirectory, RetentionLimit = limit },
		_timeProvider);

	private static DbContextOptions<JournalDbContext> CreateOptions(string databasePath) =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={databasePath}")
			.Options;

	private static byte[] ReadAllBytesShared(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using var reader = new BinaryReader(stream);
		return reader.ReadBytes((int)stream.Length);
	}

	private string SeedCopy(string fileName, DateTime lastWriteTimeUtc)
	{
		var path = Path.Combine(_backupDirectory, fileName);
		Directory.CreateDirectory(_backupDirectory);
		File.WriteAllBytes(path, []);
		File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
		return fileName;
	}

	private static RawExecution CreateRawExecution(string execId, string marker) => new()
	{
		ExecId = execId,
		Category = "linear",
		Symbol = "BTCUSDT",
		ExecTimeMs = 1735296000000,
		PayloadJson = $"{{\"execId\":\"{marker}\"}}",
		FetchedAt = DateTimeOffset.UtcNow,
	};

	#endregion
}
