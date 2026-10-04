using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>
/// Проверки разрешения путей живых данных сверки: поиск корня репозитория вверх
/// от сборки теста, признаки пропуска при отсутствии базы или каталога выгрузки,
/// переопределение каталога переменной окружения и чтение существующей базы
/// только в режиме только для чтения.
/// </summary>
[TestClass]
public class ReconciliationPathsTests
{
	/// <summary>Путь к корню временного дерева текущего теста.</summary>
	private string? _tempRoot;

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		SqliteConnection.ClearAllPools();
		if (_tempRoot != null && Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	[TestMethod]
	[Description("Отсутствующая база журнала и каталог выгрузки дают признаки пропуска прогона")]
	public void TryIfMissingLiveDataReportsSkipIndicators()
	{
		// Arrange: корень фейкового репозитория с маркером «.git», но без живых данных.
		var root = CreateTempRoot();
		Directory.CreateDirectory(Path.Combine(root, ".git"));
		var paths = new ReconciliationPaths(Path.Combine(root, "tests", "app", "bin"));

		// Act и Assert: оба признака отсутствия поднимаются, пропуск определён их парой.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-live-data-missing-run-skips
		Assert.That(paths.RepositoryRoot, Is.EqualTo(root));
		Assert.That(paths.JournalDbMissing, Is.True);
		Assert.That(paths.StatementDirectoryMissing, Is.True);
		Assert.That(paths.IsSkippable, Is.True);
	}

	[TestMethod]
	[Description("Существующая база журнала разрешается по каноническому пути и открывается только на чтение")]
	public void TryIfExistingDatabaseResolvedAndOpenedReadOnly()
	{
		// Arrange: корень фейкового репозитория с мигрированной базой и каталогом выгрузки.
		var root = CreateTempRoot();
		Directory.CreateDirectory(Path.Combine(root, ".git"));
		Directory.CreateDirectory(Path.Combine(root, "examples", "2026-01-01 до 2026-09-22"));
		var appData = Directory.CreateDirectory(Path.Combine(root, "src", "TransactionJournal", "App_Data")).FullName;
		var databasePath = Path.Combine(appData, "journal.db");
		var options = new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={databasePath}")
			.Options;
		using (var db = new JournalDbContext(options))
		{
			db.Database.Migrate();
		}

		SqliteConnection.ClearAllPools();
		var paths = new ReconciliationPaths(Path.Combine(root, "tests", "app", "bin", "Debug", "net9.0"));

		// Act: существующая база не даёт признака пропуска и открывается читателем.
		// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-one-off-diagnostic-run
		Assert.That(paths.IsSkippable, Is.False);
		Assert.That(paths.JournalDbPath, Is.EqualTo(databasePath));
		Assert.That(paths.StatementDirectory, Is.EqualTo(Path.Combine(root, "examples", "2026-01-01 до 2026-09-22")));
		using var connection = new JournalDbReader(paths.JournalDbPath).OpenReadOnlyConnection();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM RawExecutions";
		var count = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

		// Assert: выборка из живой базы работает без записи.
		Assert.That(count, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Переменная окружения BYBIT_STATEMENT_DIR переопределяет каталог выгрузки")]
	public void TryIfEnvironmentVariableOverridesStatementDirectory()
	{
		// Arrange: корень фейкового репозитория и временный каталог переопределения.
		var root = CreateTempRoot();
		Directory.CreateDirectory(Path.Combine(root, ".git"));
		var overrideDirectory = Path.Combine(root, "custom-statement");
		var originalValue = Environment.GetEnvironmentVariable(ReconciliationPaths.StatementDirectoryEnvironmentVariable);
		try
		{
			Environment.SetEnvironmentVariable(ReconciliationPaths.StatementDirectoryEnvironmentVariable, overrideDirectory);
			var paths = new ReconciliationPaths(root);

			// Act и Assert: каталог выгрузки берётся из переменной окружения.
			// Traceability: change:reconcile-bybit-statement/design#d2
			Assert.That(paths.StatementDirectory, Is.EqualTo(overrideDirectory));
			Assert.That(paths.StatementDirectoryMissing, Is.True, "переопределённый каталог ещё не создан и даёт признак пропуска");
		}
		finally
		{
			Environment.SetEnvironmentVariable(ReconciliationPaths.StatementDirectoryEnvironmentVariable, originalValue);
		}
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Отсутствие маркера «.git» во всех родительских каталогах даёт ошибку разрешения корня")]
	public void ThrowOnRepositoryRootNotFound()
	{
		// Arrange: вложенный временный каталог без «.git» в цепочке родителей.
		var directory = Path.Combine(Path.GetTempPath(), $"recon-paths-{Guid.NewGuid():N}", "nested");
		Directory.CreateDirectory(directory);
		try
		{
			// Act
			new ReconciliationPaths(directory);

			// Assert: ожидается InvalidOperationException (атрибут ExpectedException).
		}
		finally
		{
			Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
		}
	}

	/// <summary>
	/// Создаёт уникальный корень временного дерева и запоминает его для очистки.
	/// </summary>
	/// <returns>Путь к корню временного дерева.</returns>
	private string CreateTempRoot()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), $"recon-paths-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempRoot);
		return _tempRoot;
	}
}
