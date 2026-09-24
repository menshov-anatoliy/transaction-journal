namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>
/// Разрешение путей живых данных сверки: корень репозитория ищется вверх
/// от сборки теста по маркеру «.git», база журнала лежит в
/// «src\TransactionJournal\App_Data\journal.db», каталог выгрузки —
/// «examples\2026-01-01 до 2026-09-22» с переопределением переменной
/// окружения BYBIT_STATEMENT_DIR.
/// Traceability: change:reconcile-bybit-statement/design#d2
/// </summary>
public sealed class ReconciliationPaths
{
	/// <summary>Переменная окружения, переопределяющая каталог выгрузки.</summary>
	public const string StatementDirectoryEnvironmentVariable = "BYBIT_STATEMENT_DIR";

	/// <summary>Путь к базе журнала относительно корня репозитория.</summary>
	private const string JournalDbRelativePath = @"src\TransactionJournal\App_Data\journal.db";

	/// <summary>Путь к каталогу выгрузки относительно корня репозитория.</summary>
	private const string StatementDirectoryRelativePath = @"examples\2026-01-01 до 2026-09-22";

	/// <summary>Каталог, от которого начинается поиск корня репозитория.</summary>
	private readonly string _startDirectory;

	/// <summary>
	/// Создаёт решатель путей, сразу находя корень репозитория вверх по каталогам.
	/// </summary>
	/// <param name="startDirectory">Каталог сборки теста или любой вложенный каталог репозитория.</param>
	public ReconciliationPaths(string startDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
		_startDirectory = startDirectory;
		RepositoryRoot = FindRepositoryRoot(startDirectory);
	}

	/// <summary>Корень репозитория, найденный по маркеру «.git».</summary>
	public string RepositoryRoot { get; }

	/// <summary>Полный путь к живой базе журнала.</summary>
	public string JournalDbPath => Path.Combine(RepositoryRoot, JournalDbRelativePath);

	/// <summary>
	/// Полный путь к каталогу выгрузки: переменная окружения BYBIT_STATEMENT_DIR
	/// переопределяет каталог по умолчанию.
	/// </summary>
	public string StatementDirectory =>
		Environment.GetEnvironmentVariable(StatementDirectoryEnvironmentVariable) is { Length: > 0 } overrideDirectory
			? overrideDirectory
			: Path.Combine(RepositoryRoot, StatementDirectoryRelativePath);

	/// <summary>Признак отсутствия живой базы журнала: прогон уходит в пропуск.</summary>
	public bool JournalDbMissing => File.Exists(JournalDbPath) == false;

	/// <summary>Признак отсутствия каталога выгрузки: прогон уходит в пропуск.</summary>
	public bool StatementDirectoryMissing => Directory.Exists(StatementDirectory) == false;

	/// <summary>
	/// Признак пропуска прогона: нет живой базы или каталога выгрузки —
	/// сверка предназначена для локального диагностического запуска.
	/// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-live-data-missing-run-skips
	/// </summary>
	public bool IsSkippable => JournalDbMissing || StatementDirectoryMissing;

	/// <summary>
	/// Разрешает пути от каталога сборки выполняющегося теста.
	/// </summary>
	/// <returns>Решённые пути живых данных.</returns>
	public static ReconciliationPaths ResolveFromTestAssembly() => new(AppContext.BaseDirectory);

	/// <summary>
	/// Ищет корень репозитория вверх по каталогам: корнем считается каталог,
	/// содержащий «.git» (каталог обычного клона или файл worktree).
	/// </summary>
	/// <param name="startDirectory">Каталог, от которого начинается поиск.</param>
	/// <returns>Путь к корню репозитория.</returns>
	private static string FindRepositoryRoot(string startDirectory)
	{
		var candidate = Path.GetFullPath(startDirectory);
		while (true)
		{
			if (Directory.Exists(Path.Combine(candidate, ".git")) || File.Exists(Path.Combine(candidate, ".git")))
			{
				return candidate;
			}

			var parent = Path.GetDirectoryName(candidate);
			if (string.IsNullOrEmpty(parent) || parent == candidate)
			{
				throw new InvalidOperationException(
					$"Корень репозитория не найден вверх от каталога {startDirectory}: прогон живых данных возможен только внутри клона репозитория.");
			}

			candidate = parent;
		}
	}
}
