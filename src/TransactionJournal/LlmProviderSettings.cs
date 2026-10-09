namespace TransactionJournal;

/// <summary>
/// Общие параметры LLM-провайдера: единая секция <c>Llm</c> задаёт провайдера,
/// OpenAI-совместимый эндпоинт и ключ доступа для всех потребителей журнала
/// (чат агента, изложение сводок подсказок). Дефолт — провайдер z.ai на
/// адресе <c>https://api.z.ai/api/paas/v4</c>; провайдер, отличный от z.ai,
/// обязан указать явный эндпоинт; незастроенный ключ обнаруживается
/// потребителем лениво, в момент обращения к модели.
/// Traceability: openspec:config/llm-provider#requirement-llm-provider-shared-section
/// </summary>
public sealed record LlmProviderSettings
{
	/// <summary>Провайдер по умолчанию: z.ai.</summary>
	public const string DefaultProvider = "zai";

	/// <summary>Общий OpenAI-совместимый эндпоинт z.ai по умолчанию (без слэша).</summary>
	public const string ZaiBaseUrl = "https://api.z.ai/api/paas/v4";

	/// <summary>Имя провайдера.</summary>
	public string Provider { get; init; } = DefaultProvider;

	/// <summary>Базовый OpenAI-совместимый эндпоинт провайдера, нормализованный со слэшем на конце.</summary>
	public string BaseUrl { get; init; } = ZaiBaseUrl + "/";

	/// <summary>Ключ доступа к API провайдера; пусто — ключ не задан.</summary>
	public string ApiKey { get; init; } = string.Empty;

	/// <summary>
	/// Разрешает общие параметры из значений секции Llm: пустые значения
	/// откатываются к дефолтам; у провайдера, отличного от z.ai, эндпоинт
	/// обязателен — тихий откат на адрес z.ai замаскировал бы ошибку настройки
	/// и уводил бы запросы к чужому сервису; эндпоинт без завершающего слэша
	/// дополняется им, так что обе формы значения конфигурации эквивалентны.
	/// Traceability: openspec:config/llm-provider#requirement-llm-provider-shared-section
	/// </summary>
	/// <param name="provider">Значение Llm:Provider.</param>
	/// <param name="baseUrl">Значение Llm:BaseUrl.</param>
	/// <param name="apiKey">Значение Llm:ApiKey.</param>
	/// <returns>Разрешённые общие параметры провайдера.</returns>
	/// <exception cref="InvalidOperationException">Провайдер отличен от z.ai, а эндпоинт не задан.</exception>
	public static LlmProviderSettings Resolve(string? provider, string? baseUrl, string? apiKey)
	{
		var resolvedProvider = BlankToNull(provider) ?? DefaultProvider;
		var resolvedBaseUrl = BlankToNull(baseUrl)
			?? (resolvedProvider == DefaultProvider
				? ZaiBaseUrl
				: throw new InvalidOperationException(
					$"Для провайдера «{resolvedProvider}» требуется явный эндпоинт Llm:BaseUrl: " +
					$"адрес по умолчанию {ZaiBaseUrl} применим только к провайдеру «{DefaultProvider}»."));
		return new LlmProviderSettings
		{
			Provider = resolvedProvider,
			// Нормализация trailing slash: SDK строит URL запроса от эндпоинта,
			// поэтому обе формы значения конфигурации обязаны вести на один и
			// тот же адрес независимо от склейки путей внутри адаптера.
			// Traceability: openspec:config/llm-provider#scenario-llm-provider-base-url-slash-normalized
			BaseUrl = resolvedBaseUrl.EndsWith('/') ? resolvedBaseUrl : resolvedBaseUrl + "/",
			ApiKey = BlankToNull(apiKey) ?? string.Empty,
		};
	}

	/// <summary>Пустая или состоящая из пробелов строка сворачивается в null, остальные триммируются.</summary>
	private static string? BlankToNull(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
