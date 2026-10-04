using TransactionJournal.Application.Ops;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TransactionJournal.Infrastructure.Ops;

/// <summary>
/// Читает конфигурацию подсистемы резервного копирования при старте приложения:
/// каталог копий <c>Backup:Directory</c> и лимит хранения <c>Backup:RetentionLimit</c>
/// — в стиле существующих секций Sync. Приложение работает без секции Backup —
/// дефолты зашиты в код: каталог <c>App_Data/backups</c> (рядом с базой журнала)
/// и 15 копий (порядок недель истории при ежедневных пересборах); пустой каталог
/// или нечисловой, неположительный лимит откатывается к дефолту.
/// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
/// </summary>
public static class BackupOptionsReader
{
	/// <summary>Ключ каталога резервных копий.</summary>
	public const string DirectoryKey = "Backup:Directory";

	/// <summary>Ключ лимита одновременно хранимых копий.</summary>
	public const string RetentionLimitKey = "Backup:RetentionLimit";

	/// <summary>Дефолтный каталог резервных копий — рядом с базой журнала.</summary>
	public const string DefaultDirectory = "App_Data/backups";

	/// <summary>Дефолтный лимит одновременно хранимых копий.</summary>
	public const int DefaultRetentionLimit = 15;

	/// <summary>Собирает опции копирования из конфигурации; отсутствующая секция даёт дефолты.</summary>
	/// <param name="configuration">Конфигурация приложения.</param>
	/// <exception cref="ArgumentNullException">Конфигурация не задана.</exception>
	public static JournalBackupOptions Read(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		return new JournalBackupOptions
		{
			Directory = ReadDirectory(configuration),
			RetentionLimit = ReadRetentionLimit(configuration),
		};
	}

	#region Вспомогательные методы

	/// <summary>Читает каталог копий; пустое или пробельное значение — дефолт.</summary>
	private static string ReadDirectory(IConfiguration configuration)
	{
		var raw = configuration[DirectoryKey];
		return string.IsNullOrWhiteSpace(raw) ? DefaultDirectory : raw.Trim();
	}

	/// <summary>Читает лимит хранения; нечисловое или неположительное значение — дефолт.</summary>
	private static int ReadRetentionLimit(IConfiguration configuration)
	{
		var raw = configuration[RetentionLimitKey];
		if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) && limit > 0)
		{
			return limit;
		}

		return DefaultRetentionLimit;
	}

	#endregion
}
