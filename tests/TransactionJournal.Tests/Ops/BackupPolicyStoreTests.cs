using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Ops;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ops;

/// <summary>
/// Проверки хранилища политики опционального копирования перед синхронизацией:
/// чистая база без строки читается как «включён» — дефолт записи не требует, а
/// запись переключателя создаёт и обновляет строку ключа, переживая перечитывание
/// новым короткоживущим контекстом.
/// </summary>
[TestClass]
public class BackupPolicyStoreTests
{
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-policy-tests-{Guid.NewGuid():N}.db");
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();
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
	public async Task TryIfMissingRowReadsAsEnabled()
	{
		// Требование: опция копирования включена по умолчанию; отсутствие строки в
		// базе трактуется как «включён» и не требует записи значения.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		var store = new BackupPolicyStore(CreateOptions());

		// Act: переключатель на чистой базе ещё ни разу не записывался.
		var enabled = await store.IsBackupBeforeSyncEnabledAsync();

		// Assert
		Assert.That(enabled, Is.True);
	}

	[TestMethod]
	public async Task TryIfWrittenToggleIsReadBackByFreshStore()
	{
		// Требование: пользователь выключает и включает опцию — переключатель обязан
		// переживать перечитывание, поэтому чтение идёт новым экземпляром стора,
		// как это делает singleton-стор приложения на каждом вызове.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		var writer = new BackupPolicyStore(CreateOptions());
		var reader = new BackupPolicyStore(CreateOptions());

		// Act: выключаем опцию и читаем обратно.
		await writer.SetBackupBeforeSyncEnabledAsync(false);
		var afterDisable = await reader.IsBackupBeforeSyncEnabledAsync();

		// Assert
		Assert.That(afterDisable, Is.False);

		// Act: включаем опцию обратно и читаем снова.
		await writer.SetBackupBeforeSyncEnabledAsync(true);
		var afterEnable = await reader.IsBackupBeforeSyncEnabledAsync();

		// Assert: повторная запись обновила существующую строку, а не задвоила её.
		Assert.That(afterEnable, Is.True);
		Assert.That(CountKeyRows(), Is.EqualTo(1));
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private int CountKeyRows()
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.AppSettings.Count(setting => setting.Key == BackupPolicyStore.BackupBeforeSyncKey);
	}

	#endregion
}
