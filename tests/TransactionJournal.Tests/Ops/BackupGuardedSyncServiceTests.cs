using TransactionJournal.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Ops;
using TransactionJournal.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ops;

/// <summary>
/// Проверки декоратора синхронизации с защитной копией: при включённой политике копия
/// с причиной «sync» создаётся строго до запуска внутреннего сервиса, неудача копии
/// проходит наружу исключением, не запуская синхронизацию (строка SyncRun не пишется),
/// а выключенная политика вызывает внутренний сервис без копии.
/// </summary>
[TestClass]
public class BackupGuardedSyncServiceTests
{
	[TestMethod]
	[Description("Включённая политика создаёт копию «sync» до запуска синхронизации")]
	public async Task TryIfEnabledPolicyBacksUpBeforeSyncStarts()
	{
		// Требование: при включённой опции копирования синхронизация не начинается,
		// пока копия не создана — порядок доказывается общим журналом событий.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		var backup = new RecordingBackupService();
		var inner = new RecordingSyncService();
		var service = new BackupGuardedSyncService(
			backup,
			new StubPolicyStore(enabled: true),
			inner);

		// Act
		await service.SyncAsync();

		// Assert: копия с причиной «sync» стоит в журнале раньше старта синхронизации.
		Assert.That(backup.Events, Is.EqualTo(new[] { "backup:sync" }));
		Assert.That(inner.Events, Is.EqualTo(new[] { "sync-started" }));
		Assert.That(backup.Events.Concat(inner.Events), Is.Ordered);
	}

	[TestMethod]
	[Description("Неудача копии проходит наружу исключением и не запускает синхронизацию")]
	public void ThrowOnBackupFailureWithoutStartingSync()
	{
		// Требование: неудача копии блокирует запуск — внутренний сервис не вызывается,
		// поэтому строка SyncRun не создаётся: запуск ещё не начинался.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		var failure = new IOException("каталог копий недоступен");
		var backup = new RecordingBackupService { Failure = failure };
		var inner = new RecordingSyncService();
		var service = new BackupGuardedSyncService(
			backup,
			new StubPolicyStore(enabled: true),
			inner);

		// Act
		var thrown = Assert.ThrowsAsync<IOException>(() => service.SyncAsync());

		// Assert: исключение — именно причина неудавшейся копии; синхронизация не стартовала.
		Assert.That(thrown, Is.SameAs(failure));
		Assert.That(inner.Events, Is.Empty);
	}

	[TestMethod]
	[Description("Выключенная политика запускает синхронизацию без копии")]
	public async Task TryIfDisabledPolicySyncsWithoutBackup()
	{
		// Требование: при выключенной опции операция выполняется без создания копии.
		// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
		var backup = new RecordingBackupService();
		var inner = new RecordingSyncService();
		var service = new BackupGuardedSyncService(
			backup,
			new StubPolicyStore(enabled: false),
			inner);

		// Act
		var result = await service.SyncAsync();

		// Assert: копий нет, внутренний сервис вернул свой итог без изменений.
		Assert.That(backup.Events, Is.Empty);
		Assert.That(inner.Events, Is.EqualTo(new[] { "sync-started" }));
		Assert.That(result, Is.SameAs(inner.Result));
	}

	#region Помощники

	/// <summary>Записывающая заглушка копирования: события с причиной и имитация неудачи.</summary>
	private sealed class RecordingBackupService : IJournalBackupService
	{
		public List<string> Events { get; } = [];

		public Exception? Failure { get; set; }

		public Task<JournalBackupResult> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
		{
			Events.Add($"backup:{reason}");

			if (Failure is not null)
			{
				throw Failure;
			}

			return Task.FromResult(new JournalBackupResult { FileName = $"journal-{reason}.db" });
		}
	}

	/// <summary>Записывающая заглушка синхронизации: фиксирует сам факт запуска.</summary>
	private sealed class RecordingSyncService : IJournalSyncService
	{
		public List<string> Events { get; } = [];

		public JournalSyncResult Result { get; } = new()
		{
			Mode = SyncRunMode.Incremental,
			Run = new SyncRun { StartedAt = DateTimeOffset.UtcNow },
			Executions = new Dictionary<string, ExecutionCategorySyncResult>(),
			Deliveries = new Dictionary<string, DeliveryCategorySyncResult>(),
		};

		public Task<JournalSyncResult> SyncAsync(CancellationToken cancellationToken = default)
		{
			Events.Add("sync-started");
			return Task.FromResult(Result);
		}
	}

	/// <summary>Заглушка хранилища политики с фиксированным состоянием переключателя.</summary>
	private sealed class StubPolicyStore(bool enabled) : IBackupPolicyStore
	{
		public Task<bool> IsBackupBeforeSyncEnabledAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(enabled);

		public Task SetBackupBeforeSyncEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	#endregion
}
