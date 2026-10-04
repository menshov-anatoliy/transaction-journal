using TransactionJournal.Application.Ops;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;

namespace TransactionJournal.Infrastructure.Ops;

/// <summary>
/// Реализация хранилища политики опционального копирования над SQLite: переключатель
/// «копия перед синхронизацией» хранится строкой таблицы AppSetting с ключом
/// «backup-before-sync». Отсутствие строки означает «включён» — дефолт опции не
/// требует записи, чистая база сразу работает с копированием. Запись — upsert:
/// повторное переключение обновляет существующую строку по первичному ключу.
/// Стор живёт singleton-ом над собственными опциями контекста: каждый вызов создаёт
/// короткоживущий контекст.
/// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
/// </summary>
public sealed class BackupPolicyStore(DbContextOptions<JournalDbContext> options) : IBackupPolicyStore
{
	/// <summary>Ключ строки переключателя копирования перед синхронизацией.</summary>
	public const string BackupBeforeSyncKey = "backup-before-sync";

	private readonly DbContextOptions<JournalDbContext> _options = options ?? throw new ArgumentNullException(nameof(options));

	/// <inheritdoc cref="IBackupPolicyStore.IsBackupBeforeSyncEnabledAsync" />
	public async Task<bool> IsBackupBeforeSyncEnabledAsync(CancellationToken cancellationToken = default)
	{
		await using var db = new JournalDbContext(_options);
		var setting = await db.AppSettings
			.AsNoTracking()
			.SingleOrDefaultAsync(s => s.Key == BackupBeforeSyncKey, cancellationToken)
			.ConfigureAwait(false);

		return setting is null || setting.Value == BooleanValue.True;
	}

	/// <inheritdoc cref="IBackupPolicyStore.SetBackupBeforeSyncEnabledAsync" />
	public async Task SetBackupBeforeSyncEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
	{
		await using var db = new JournalDbContext(_options);
		var setting = await db.AppSettings
			.SingleOrDefaultAsync(s => s.Key == BackupBeforeSyncKey, cancellationToken)
			.ConfigureAwait(false);

		if (setting is null)
		{
			db.AppSettings.Add(new AppSetting
			{
				Key = BackupBeforeSyncKey,
				Value = enabled ? BooleanValue.True : BooleanValue.False,
			});
		}
		else
		{
			setting.Value = enabled ? BooleanValue.True : BooleanValue.False;
		}

		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Строковые представления булевых значений переключателя в AppSetting.</summary>
	private static class BooleanValue
	{
		public const string True = "true";
		public const string False = "false";
	}
}
