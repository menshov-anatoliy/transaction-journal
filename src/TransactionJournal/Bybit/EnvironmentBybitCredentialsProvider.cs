namespace TransactionJournal.Bybit;

/// <summary>
/// Dev-реализация поставщика ключа Bybit: читает ключ и секрет из переменных окружения.
/// Это временная реализация до решения тикета #4 о постоянном месте хранения секрета.
// Traceability: change:add-bybit-sync/design#d6
/// </summary>
public sealed class EnvironmentBybitCredentialsProvider : IBybitCredentialsProvider
{
	/// <summary>Имя переменной окружения с API-ключом.</summary>
	public const string ApiKeyVariableName = "BYBIT_API_KEY";

	/// <summary>Имя переменной окружения с секретом API-ключа.</summary>
	public const string ApiSecretVariableName = "BYBIT_API_SECRET";

	/// <summary>
	/// Возвращает учётные данные из переменных окружения BYBIT_API_KEY и BYBIT_API_SECRET.
	/// Отсутствие или пустота любой из переменных — ошибка конфигурации.
	/// </summary>
	/// <exception cref="InvalidOperationException">Одна или обе переменные окружения не заданы.</exception>
	public BybitCredentials GetCredentials()
	{
		var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariableName);
		var apiSecret = Environment.GetEnvironmentVariable(ApiSecretVariableName);

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new InvalidOperationException(
				$"Не задана переменная окружения {ApiKeyVariableName} с API-ключом Bybit.");
		}

		if (string.IsNullOrWhiteSpace(apiSecret))
		{
			throw new InvalidOperationException(
				$"Не задана переменная окружения {ApiSecretVariableName} с секретом API-ключа Bybit.");
		}

		return new BybitCredentials(apiKey, apiSecret);
	}
}
