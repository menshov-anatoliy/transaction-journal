namespace TransactionJournal.Chats;

/// <summary>
/// Модель чата агента: общие параметры провайдера (провайдер,
/// OpenAI-совместимый эндпоинт, ключ доступа, правило стороннего провайдера)
/// задаёт секция <c>Llm</c> через резолвер composition root
/// <c>LlmProviderSettings</c>, рабочая модель — подсекция <c>Llm:Chat</c>;
/// дефолт модели — GLM-5.3. Смена провайдера или модели выполняется правкой
/// конфигурации без правки кода.
/// Traceability: openspec:chats/sources#requirement-sources-model-is-chat-parameter
/// Traceability: openspec:config/llm-provider#requirement-llm-model-subsections
/// </summary>
public sealed record ChatModelOptions
{
	/// <summary>Модель по умолчанию: GLM-5.3 в OpenAI-совместимом доступе.</summary>
	public const string DefaultModel = "glm-5.3";

	/// <summary>Имя провайдера модели чата; задаётся общими настройками секции Llm.</summary>
	public string Provider { get; init; } = string.Empty;

	/// <summary>Идентификатор модели чата в API провайдера.</summary>
	public string Model { get; init; } = DefaultModel;

	/// <summary>Базовый OpenAI-совместимый эндпоинт провайдера; задаётся общими настройками секции Llm.</summary>
	public string BaseUrl { get; init; } = string.Empty;

	/// <summary>Ключ доступа к API провайдера; задаётся общими настройками секции Llm, пусто — ключ не задан.</summary>
	public string ApiKey { get; init; } = string.Empty;

	/// <summary>
	/// Собирает опции из уже разрешённых общих настроек провайдера и значения
	/// подсекции Llm:Chat: пустая подсекция или пустое значение модели
	/// откатываются к дефолту, модель триммируется; правило стороннего
	/// провайдера и нормализация эндпоинта живут в LlmProviderSettings
	/// composition root и здесь не дублируются.
	/// Traceability: openspec:config/llm-provider#requirement-llm-model-subsections
	/// </summary>
	/// <param name="provider">Общий провайдер из Llm:Provider (уже разрешён резолвером).</param>
	/// <param name="baseUrl">Общий эндпоинт из Llm:BaseUrl (уже нормализован резолвером).</param>
	/// <param name="apiKey">Общий ключ из Llm:ApiKey.</param>
	/// <param name="model">Значение Llm:Chat:Model.</param>
	/// <returns>Разрешённые опции модели чата.</returns>
	public static ChatModelOptions Resolve(string provider, string baseUrl, string apiKey, string? model)
	{
		return new ChatModelOptions
		{
			Provider = provider,
			Model = BlankToNull(model) ?? DefaultModel,
			BaseUrl = baseUrl,
			ApiKey = apiKey,
		};
	}

	/// <summary>Пустая или состоящая из пробелов строка сворачивается в null, остальные триммируются.</summary>
	private static string? BlankToNull(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

