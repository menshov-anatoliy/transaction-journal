using TransactionJournal.Bybit;

namespace TransactionJournal.Components.Pages;

/// <summary>
/// Параметры подключения для блока «Настроек»: маска API-ключа, описание
/// аккаунта и место хранения секрета. Секрета в записи нет — модель принимает
/// учётные данные от поставщика и немедленно оставляет от ключа только маску.
/// </summary>
/// <param name="MaskedApiKey">Маска API-ключа вида «abcd······wxyz».</param>
/// <param name="AccountDescription">Тип аккаунта и права ключа.</param>
/// <param name="SecretStorage">Место хранения секрета с именами переменных окружения.</param>
public sealed record ConnectionInfo(string MaskedApiKey, string AccountDescription, string SecretStorage);

/// <summary>
/// Read-модель блока подключения экрана «Настройки»: обращается к поставщику
/// учётных данных вместо компонента и отдаёт экрану только маску ключа.
/// Секрет не проходит дальше модели, поэтому экран не может его отобразить
/// или ввести через интерфейс.
/// </summary>
// Секрет не доходит до состояния компонента: пользовательские правки ключа
// интерфейсом не предусматриваются, место хранения — переменные окружения.
// Traceability: openspec:ui/screens#requirement-settings-screen
public sealed class SettingsReadModel
{
	/// <summary>Описание аккаунта и прав ключа из контракта синхронизации: read-only доступ.</summary>
	// Ключ выдаётся только с правами на чтение — ограничение контракта sync/bybit-history.
	// Traceability: openspec:sync/bybit-history#requirement-read-only-access
	private const string AccountText = "Unified (UTA), субаккаунтов нет, ключ только на чтение";

	private readonly IBybitCredentialsProvider _credentials;

	/// <summary>Создаёт read-модель над поставщиком учётных данных.</summary>
	/// <param name="credentials">Поставщик ключа и секрета Bybit.</param>
	/// <exception cref="ArgumentNullException">Аргументы не заданы.</exception>
	public SettingsReadModel(IBybitCredentialsProvider credentials)
	{
		_credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
	}

	/// <summary>
	/// Возвращает параметры подключения с маскированным ключом или null,
	/// когда ключ не настроен: экран показывает явное состояние вместо маски.
	/// </summary>
	// Ключ и секрет существуют только внутри метода: наружу уходит одна маска.
	// Traceability: openspec:ui/screens#scenario-settings-secret-never-displayed
	public ConnectionInfo? GetConnection()
	{
		BybitCredentials credentials;
		try
		{
			credentials = _credentials.GetCredentials();
		}
		catch (InvalidOperationException)
		{
			// Переменные окружения с ключом или секретом не заданы —
			// конфигурация не готова, экран не показывает никаких значений.
			return null;
		}

		var storage = $"переменные окружения {EnvironmentBybitCredentialsProvider.ApiKeyVariableName}"
			+ $" / {EnvironmentBybitCredentialsProvider.ApiSecretVariableName} — внутри журнала не хранится";
		return new ConnectionInfo(MaskKey(credentials.ApiKey), AccountText, storage);
	}

	/// <summary>Маскирует ключ: первые и последние четыре знака, середина — точки; короткий ключ скрыт целиком.</summary>
	private static string MaskKey(string apiKey) =>
		apiKey.Length < 8
			? new string('·', 6)
			: $"{apiKey[..4]}······{apiKey[^4..]}";
}
