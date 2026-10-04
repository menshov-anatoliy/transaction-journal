namespace TransactionJournal.Application.Bybit;

/// <summary>
/// Конфигурационный контракт учётных данных Bybit: имя локального файла секретов,
/// имя секции конфигурации и ключи значений ключа/секрета. Константы живут рядом
/// с портом IBybitCredentialsProvider, потому что их читают и реализация поставщика
/// в Infrastructure, и потребители контракта в Application (текст места хранения
/// секрета на экране «Настроек»), и подключение файла секретов в точке входа.
/// Traceability: openspec:config/local-secrets#requirement-local-secrets-single-file
/// </summary>
public static class BybitCredentialsConfig
{
	/// <summary>Имя локального файла секретов рядом с приложением.</summary>
	public const string LocalFileName = "appsettings.Local.json";

	/// <summary>Имя секции конфигурации с настройками Bybit.</summary>
	public const string SectionName = "Bybit";

	/// <summary>Ключ конфигурации с API-ключом Bybit.</summary>
	public const string ApiKeyConfigKey = "Bybit:ApiKey";

	/// <summary>Ключ конфигурации с секретом API-ключа Bybit.</summary>
	public const string ApiSecretConfigKey = "Bybit:ApiSecret";
}
