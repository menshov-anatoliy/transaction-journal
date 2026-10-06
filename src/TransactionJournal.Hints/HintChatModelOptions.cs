namespace TransactionJournal.Hints;

/// <summary>
/// Модель изложения сводок подсказок: общие параметры провайдера
/// (провайдер, OpenAI-совместимый эндпоинт, ключ доступа) задаёт секция
/// <c>Llm</c> через резолвер composition root <c>LlmProviderSettings</c>,
/// рабочая модель — подсекция <c>Llm:Hint</c>; дефолт модели —
/// glm-5.3-flash. Потребитель — изложение сводок подсказок (change
/// add-summary-channels, задача 4.3): контракт фиксируется заранее, изложение
/// внедряет готовую зависимость без правки конфигурации или резолва.
/// Traceability: openspec:config/llm-provider#requirement-llm-model-subsections
/// </summary>
public sealed record HintChatModelOptions
{
	/// <summary>Модель по умолчанию: glm-5.3-flash.</summary>
	public const string DefaultModel = "glm-5.3-flash";

	/// <summary>Имя провайдера; задаётся общими настройками секции Llm.</summary>
	public string Provider { get; init; } = string.Empty;

	/// <summary>Идентификатор модели изложения в API провайдера.</summary>
	public string Model { get; init; } = DefaultModel;

	/// <summary>Базовый OpenAI-совместимый эндпоинт провайдера; задаётся общими настройками секции Llm.</summary>
	public string BaseUrl { get; init; } = string.Empty;

	/// <summary>Ключ доступа к API провайдера; задаётся общими настройками секции Llm, пусто — ключ не задан.</summary>
	public string ApiKey { get; init; } = string.Empty;

	/// <summary>
	/// Собирает опции из уже разрешённых общих настроек провайдера и значения
	/// подсекции Llm:Hint: пустая подсекция или пустое значение модели
	/// откатываются к дефолту, модель триммируется; правило стороннего
	/// провайдера и нормализация эндпоинта живут в LlmProviderSettings
	/// composition root и здесь не дублируются.
	/// Traceability: openspec:config/llm-provider#requirement-llm-model-subsections
	/// </summary>
	/// <param name="provider">Общий провайдер из Llm:Provider (уже разрешён резолвером).</param>
	/// <param name="baseUrl">Общий эндпоинт из Llm:BaseUrl (уже нормализован резолвером).</param>
	/// <param name="apiKey">Общий ключ из Llm:ApiKey.</param>
	/// <param name="model">Значение Llm:Hint:Model.</param>
	/// <returns>Разрешённые опции модели изложения.</returns>
	public static HintChatModelOptions Resolve(string provider, string baseUrl, string apiKey, string? model)
	{
		return new HintChatModelOptions
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
