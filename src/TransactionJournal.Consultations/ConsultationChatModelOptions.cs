namespace TransactionJournal.Consultations;

/// <summary>
/// Конфигурация модели чата консультаций: отдельная секция
/// <c>Consultations:ChatModel</c> задаёт провайдера, модель, OpenAI-совместимый
/// эндпоинт и ключ доступа; дефолт — провайдер z.ai и модель GLM-5.3 в
/// OpenAI-совместимом доступе. Смена провайдера или модели выполняется правкой
/// конфигурации без правки кода, без секции в конфигурации чат работает на
/// дефолтах кода.
/// Traceability: openspec:consultations/tools#requirement-tools-chat-model-configurable
/// </summary>
public sealed record ConsultationChatModelOptions
{
	/// <summary>Провайдер модели чата по умолчанию: z.ai.</summary>
	public const string DefaultProvider = "zai";

	/// <summary>Модель по умолчанию: GLM-5.3 в OpenAI-совместимом доступе.</summary>
	public const string DefaultModel = "glm-5.3";

	/// <summary>OpenAI-совместимый эндпоинт z.ai по умолчанию.</summary>
	public const string ZaiBaseUrl = "https://api.z.ai/api/paas/v4/";

	/// <summary>Имя провайдера модели чата.</summary>
	public string Provider { get; init; } = DefaultProvider;

	/// <summary>Идентификатор модели чата в API провайдера.</summary>
	public string Model { get; init; } = DefaultModel;

	/// <summary>Базовый OpenAI-совместимый эндпоинт провайдера.</summary>
	public string BaseUrl { get; init; } = ZaiBaseUrl;

	/// <summary>Ключ доступа к API провайдера; пусто — ключ не задан.</summary>
	public string ApiKey { get; init; } = string.Empty;

	/// <summary>
	/// Разрешает опции из значений секции Consultations:ChatModel: пустые
	/// значения откатываются к дефолтам, у провайдера, отличного от z.ai,
	/// эндпоинт обязателен — тихий откат на адрес z.ai замаскировал бы ошибку
	/// настройки и уводил бы запросы к чужому сервису.
	/// Traceability: openspec:consultations/tools#scenario-tools-model-switch-config
	/// </summary>
	/// <param name="provider">Значение Consultations:ChatModel:Provider.</param>
	/// <param name="model">Значение Consultations:ChatModel:Model.</param>
	/// <param name="baseUrl">Значение Consultations:ChatModel:BaseUrl.</param>
	/// <param name="apiKey">Значение Consultations:ChatModel:ApiKey (или его запасной источник).</param>
	/// <returns>Разрешённые опции модели чата.</returns>
	/// <exception cref="InvalidOperationException">Провайдер отличен от дефолтного, а эндпоинт не задан.</exception>
	public static ConsultationChatModelOptions Resolve(string? provider, string? model, string? baseUrl, string? apiKey)
	{
		var resolvedProvider = BlankToNull(provider) ?? DefaultProvider;
		var resolvedModel = BlankToNull(model) ?? DefaultModel;
		var resolvedBaseUrl = BlankToNull(baseUrl)
			?? (resolvedProvider == DefaultProvider
				? ZaiBaseUrl
				: throw new InvalidOperationException(
					$"Для провайдера «{resolvedProvider}» требуется явный эндпоинт Consultations:ChatModel:BaseUrl: " +
					$"адрес по умолчанию {ZaiBaseUrl} применим только к провайдеру «{DefaultProvider}»."));
		return new ConsultationChatModelOptions
		{
			Provider = resolvedProvider,
			Model = resolvedModel,
			BaseUrl = resolvedBaseUrl,
			ApiKey = BlankToNull(apiKey) ?? string.Empty,
		};
	}

	/// <summary>Пустая или состоящая из пробелов строка сворачивается в null, остальные триммируются.</summary>
	private static string? BlankToNull(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
