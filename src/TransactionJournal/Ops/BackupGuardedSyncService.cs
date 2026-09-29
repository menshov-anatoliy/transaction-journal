using TransactionJournal.Sync;

namespace TransactionJournal.Ops;

/// <summary>
/// Декоратор команды синхронизации: перед запуском внутреннего сервиса читает политику
/// опционального копирования и, пока опция включена, создаёт копию базы с причиной
/// «sync». Неудача копии проходит наружу исключением до запуска синхронизации — строка
/// SyncRun не создаётся, потому что запуск ещё не начинался; при выключенной опции
/// внутренний сервис вызывается без копии. Одна точка встраивания закрывает все кнопки
/// запуска синхронизации: экраны продолжают зависеть от <see cref="IJournalSyncService"/>.
/// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
/// </summary>
public sealed class BackupGuardedSyncService(
	IJournalBackupService backupService,
	IBackupPolicyStore policyStore,
	IJournalSyncService inner) : IJournalSyncService
{
	/// <summary>Причина копии, попадающая в имя файла резервной копии синхронизации.</summary>
	public const string SyncBackupReason = "sync";

	private readonly IJournalBackupService _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
	private readonly IBackupPolicyStore _policyStore = policyStore ?? throw new ArgumentNullException(nameof(policyStore));
	private readonly IJournalSyncService _inner = inner ?? throw new ArgumentNullException(nameof(inner));

	/// <inheritdoc cref="IJournalSyncService.SyncAsync" />
	public async Task<JournalSyncResult> SyncAsync(CancellationToken cancellationToken = default)
	{
		// Копия делается до любого обращения к внутреннему сервису: неудача копии
		// блокирует запуск синхронизации, журнал SyncRun остаётся без новой строки.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		if (await _policyStore.IsBackupBeforeSyncEnabledAsync(cancellationToken).ConfigureAwait(false))
		{
			await _backupService.CreateBackupAsync(SyncBackupReason, cancellationToken).ConfigureAwait(false);
		}

		return await _inner.SyncAsync(cancellationToken).ConfigureAwait(false);
	}
}
