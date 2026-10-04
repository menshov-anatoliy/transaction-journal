using Microsoft.Extensions.Configuration;
using TransactionJournal.Application.Bybit;

namespace TransactionJournal.Infrastructure.Bybit;

/// <summary>
/// Поставщик учётных данных Bybit: читает ключ и секрет из конфигурации приложения,
/// куда они попадают из локального файла секретов appsettings.Local.json.
/// </summary>
// Секреты Bybit переезжают из переменных окружения в единый локальный файл секретов:
// значения читаются из IConfiguration по ключам Bybit:ApiKey / Bybit:ApiSecret,
// а их отсутствие обнаруживается только потребителем в момент обращения.
// Traceability: openspec:config/local-secrets#requirement-local-secrets-single-file
public sealed class ConfigurationBybitCredentialsProvider : IBybitCredentialsProvider
{
	private readonly IConfiguration _configuration;

	/// <summary>Создаёт поставщика поверх корневой конфигурации приложения.</summary>
	/// <param name="configuration">Корневая конфигурация приложения.</param>
	/// <exception cref="ArgumentNullException">Конфигурация не задана.</exception>
	public ConfigurationBybitCredentialsProvider(IConfiguration configuration)
	{
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	}

	/// <summary>
	/// Возвращает учётные данные из конфигурации Bybit:ApiKey и Bybit:ApiSecret.
	/// Отсутствие или пустота любого из значений — ошибка конфигурации.
	/// </summary>
	/// <exception cref="InvalidOperationException">Ключ или секрет не заданы в конфигурации.</exception>
	// Текст ошибки называет и файл, и ключ: владелец сразу видит, куда вписать
	// значение, без обращения к документации.
	// Traceability: openspec:config/local-secrets#scenario-local-secrets-missing-file-normal
	public BybitCredentials GetCredentials()
	{
		var apiKey = _configuration[BybitCredentialsConfig.ApiKeyConfigKey];
		var apiSecret = _configuration[BybitCredentialsConfig.ApiSecretConfigKey];

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new InvalidOperationException(
				$"Не задан API-ключ Bybit: заполните {BybitCredentialsConfig.ApiKeyConfigKey} в файле {BybitCredentialsConfig.LocalFileName}.");
		}

		if (string.IsNullOrWhiteSpace(apiSecret))
		{
			throw new InvalidOperationException(
				$"Не задан секрет API-ключа Bybit: заполните {BybitCredentialsConfig.ApiSecretConfigKey} в файле {BybitCredentialsConfig.LocalFileName}.");
		}

		return new BybitCredentials(apiKey, apiSecret);
	}
}
