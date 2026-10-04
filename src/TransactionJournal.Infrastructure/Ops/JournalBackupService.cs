using TransactionJournal.Application.Ops;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Infrastructure.Data;

namespace TransactionJournal.Infrastructure.Ops;

/// <summary>
/// Реализация резервного копирования журнала над SQLite: копия создаётся одним
/// SQL-оператором <c>VACUUM INTO</c> через короткоживущий контекст — SQLite строит
/// консистентный снапшот работающей базы с учётом WAL-данных, не блокируя параллельных
/// читателей, а целевой файл компактится и обязан не существовать. Путь подставляется
/// экранированным литералом (одинарные кавычки удваиваются); коллизия имён в ту же
/// секунду разрешается суффиксом-счётчиком. После копии выполняется ротация «лучшие
/// усилия»: старейшие за лимитом копии по <see cref="File.GetLastWriteTimeUtc"/>
/// удаляются, ошибки удаления собираются предупреждениями — неудача ротации не отменяет
/// созданную копию и защищаемую операцию. Сервис живёт singleton-ом над собственными
/// опциями контекста: каждый вызов создаёт короткоживущий контекст.
/// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
/// Traceability: openspec:ops/db-backup#requirement-backup-retention
/// </summary>
public sealed class JournalBackupService : IJournalBackupService
{
	/// <summary>Шаблон имён копий в каталоге резервных копий.</summary>
	private const string CopySearchPattern = "journal-*.db";

	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly JournalBackupOptions _backupOptions;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт сервис над опциями контекста журнала и опциями копирования.</summary>
	/// <param name="options">Опции EF-контекста; база уже развёрнута миграциями.</param>
	/// <param name="backupOptions">Каталог копий и лимит хранения.</param>
	/// <param name="timeProvider">Поставщик времени для метки в имени копии; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public JournalBackupService(
		DbContextOptions<JournalDbContext> options,
		JournalBackupOptions backupOptions,
		TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_backupOptions = backupOptions ?? throw new ArgumentNullException(nameof(backupOptions));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc cref="IJournalBackupService.CreateBackupAsync" />
	public async Task<JournalBackupResult> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new ArgumentException("Причина копии не задана.", nameof(reason));
		}

		if (reason.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			throw new ArgumentException($"Причина копии непригодна для имени файла: «{reason}».", nameof(reason));
		}

		// Каталог копий создаётся лени при первом бэкапе — установка не требует
		// подготовки папки заранее.
		Directory.CreateDirectory(_backupOptions.Directory);
		var fileName = ChooseFreeFileName(reason);
		var targetPath = Path.Combine(_backupOptions.Directory, fileName);

		// VACUUM INTO отказывается перезаписывать существующий файл, поэтому свободное
		// имя выбирается до оператора. Путь подставляется экранированным литералом:
		// одинарные кавычки удваиваются, посторонних подстановок в оператор нет —
		// интерполяция строк здесь не используется, чтобы не включать анализатор EF1002.
		await using (var db = new JournalDbContext(_options))
		{
			var escapedPath = targetPath.Replace("'", "''", StringComparison.Ordinal);
			await db.Database
				.ExecuteSqlRawAsync("VACUUM INTO '" + escapedPath + "'", cancellationToken)
				.ConfigureAwait(false);
		}

		var warnings = RotateOldCopies(createdFileName: fileName);
		return new JournalBackupResult { FileName = fileName, RotationWarnings = warnings };
	}

	#region Вспомогательные методы

	/// <summary>
	/// Выбирает свободное имя копии <c>journal-&lt;время&gt;-&lt;причина&gt;.db</c>:
	/// метка времени локальная — для читаемости пользователем, коллизия в ту же секунду
	/// разрешается суффиксом-счётчиком. Порядок ротации считается по времени файла,
	/// а не по имени, поэтому суффикс не обязан сохранять сортировку.
	/// </summary>
	private string ChooseFreeFileName(string reason)
	{
		var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		var baseName = $"journal-{stamp}-{reason}";
		var candidate = $"{baseName}.db";
		var counter = 2;
		while (File.Exists(Path.Combine(_backupOptions.Directory, candidate)))
		{
			candidate = $"{baseName}-{counter}.db";
			counter++;
		}

		return candidate;
	}

	/// <summary>
	/// Ротация «лучшие усилия»: перечисляет копии по возрастанию времени файла и
	/// удаляет старейшие за лимитом; свежайшая копия не удаляется никогда, ошибка
	/// удаления становится предупреждением, а не исключением.
	/// </summary>
	private List<string> RotateOldCopies(string createdFileName)
	{
		var warnings = new List<string>();
		var copyPaths = Directory
			.EnumerateFiles(_backupOptions.Directory, CopySearchPattern)
			.OrderBy(File.GetLastWriteTimeUtc)
			.ToList();

		var excess = copyPaths.Count - Math.Max(_backupOptions.RetentionLimit, 0);
		for (var index = 0; index < excess; index++)
		{
			var path = copyPaths[index];
			if (Path.GetFileName(path) == createdFileName)
			{
				// Свежайшая копия не удаляется даже при странном времени файла —
				// лимит хранения никогда не съедает только что созданную защиту.
				continue;
			}

			try
			{
				File.Delete(path);
			}
			catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException)
			{
				warnings.Add($"Не удалось удалить старую копию «{Path.GetFileName(path)}»: {failure.Message}");
			}
		}

		return warnings;
	}

	#endregion
}
