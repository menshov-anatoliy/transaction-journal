using TransactionJournal.Application.Ops;
using TransactionJournal.Infrastructure.Ops;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;

namespace TransactionJournal.Tests;

/// <summary>
/// Заглушка сервиса резервного копирования для проверок, чья команда сама вызывает
/// бэкап (пересбор, синхронизация): запоминает причины запросов копий и умеет
/// имитировать неудачу копирования заданным исключением, не трогая реальную базу.
/// </summary>
public sealed class StubJournalBackupService : IJournalBackupService
{
	/// <summary>Причины запросов копий в порядке поступления — включая неудавшиеся попытки.</summary>
	public List<string> Reasons { get; } = [];

	/// <summary>Исключение, которым заглушка имитирует неудачу копирования; null — копия удаётся.</summary>
	public Exception? Failure { get; set; }

	/// <inheritdoc />
	public Task<JournalBackupResult> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
	{
		Reasons.Add(reason);

		if (Failure is not null)
		{
			throw Failure;
		}

		return Task.FromResult(new JournalBackupResult { FileName = $"journal-{reason}.db" });
	}
}
