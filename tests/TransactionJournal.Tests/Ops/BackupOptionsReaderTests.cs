using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Ops;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ops;

/// <summary>
/// Проверки чтения конфигурации подсистемы резервного копирования при старте:
/// без секции Backup приложение работает на дефолтах кода (каталог App_Data/backups
/// и лимит 15 копий), заданные значения передаются в опции, а пустой каталог или
/// нечисловой, неположительный лимит откатывается к дефолту.
/// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
/// </summary>
[TestClass]
public class BackupOptionsReaderTests
{
	[TestMethod]
	[Description("Отсутствие секции Backup даёт опциям дефолты кода")]
	public void TryIfMissingBackupSectionFallsBackToDefaults()
	{
		// Arrange: конфигурация без секции Backup — обычный случай приложения по умолчанию.
		// Требование: каталог копий и лимит хранения настраиваются конфигурацией
		// с безопасными значениями по умолчанию.
		// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
		var configuration = new ConfigurationBuilder().Build();

		// Act
		var options = BackupOptionsReader.Read(configuration);

		// Assert: дефолтный каталог рядом с базой журнала и лимит 15 копий.
		Assert.That(options.Directory, Is.EqualTo("App_Data/backups"));
		Assert.That(options.RetentionLimit, Is.EqualTo(15));
	}

	[TestMethod]
	[Description("Заданные в конфигурации значения передаются в опции копирования")]
	public void TryIfConfiguredValuesReachBackupOptions()
	{
		// Arrange: пользовательский каталог и лимит — переопределение обоих значений.
		// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[BackupOptionsReader.DirectoryKey] = @"D:\journal-backups",
				[BackupOptionsReader.RetentionLimitKey] = "7",
			})
			.Build();

		// Act
		var options = BackupOptionsReader.Read(configuration);

		// Assert: оба значения дошли до опций без изменений.
		Assert.That(options.Directory, Is.EqualTo(@"D:\journal-backups"));
		Assert.That(options.RetentionLimit, Is.EqualTo(7));
	}

	[TestMethod]
	[Description("Пустой каталог или некорректный лимит откатывается к дефолту")]
	public void TryIfInvalidValuesFallBackToDefaults()
	{
		foreach (var rawLimit in new[] { "abc", "0", "-5" })
		{
			// Arrange: каждое некорректное значение лимита проверяется отдельной конфигурацией.
			var configuration = new ConfigurationBuilder()
				.AddInMemoryCollection(new Dictionary<string, string?>
				{
					[BackupOptionsReader.RetentionLimitKey] = rawLimit,
				})
				.Build();

			// Act
			var options = BackupOptionsReader.Read(configuration);

			// Assert: неверный лимит не способен отключить ротацию — работает дефолт.
			Assert.That(options.RetentionLimit, Is.EqualTo(15), rawLimit);
			Assert.That(options.Directory, Is.EqualTo("App_Data/backups"));
		}

		// Arrange: пустой и пробельный каталог тоже проверяются отдельно.
		var emptyDirectoryConfiguration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[BackupOptionsReader.DirectoryKey] = "   ",
			})
			.Build();

		// Act
		var emptyDirectoryOptions = BackupOptionsReader.Read(emptyDirectoryConfiguration);

		// Assert: копии без каталога невозможны — работает дефолтный каталог.
		Assert.That(emptyDirectoryOptions.Directory, Is.EqualTo("App_Data/backups"));
	}
}
