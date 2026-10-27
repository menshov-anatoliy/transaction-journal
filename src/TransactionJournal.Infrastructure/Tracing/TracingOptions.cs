namespace TransactionJournal.Infrastructure.Tracing;

/// <summary>
/// Параметры журнала трасс агентского харнеса из секции <c>Tracing</c>:
/// уровень детализации, каталог трасс, предел размера файла до ротации,
/// число хранимых архивов и опциональный приёмник OTLP (dev-опция Phoenix).
/// Отсутствие секции конфигурации даёт дефолты этого типа. Экземпляр
/// регистрируется в DI singleton-ом при старте и читается потребителями
/// захвата без повторного обращения к конфигурации.
/// Traceability: openspec:config/tracing#requirement-tracing-config-section
/// </summary>
public sealed record TracingOptions
{
	/// <summary>Уровень детализации трассы; дефолт — <see cref="TracingDetail.Detailed"/>.</summary>
	public TracingDetail Detail { get; init; } = TracingDetail.Detailed;

	/// <summary>
	/// Каталог трасс; дефолт <c>App_Data/traces</c>. Относительный путь
	/// разрешается от корня содержимого приложения при регистрации.
	/// </summary>
	public string Dir { get; init; } = "App_Data/traces";

	/// <summary>Предел размера файла трасс до ротации, байты; дефолт — 10 МБ.</summary>
	public long MaxFileBytes { get; init; } = 10_485_760;

	/// <summary>Число хранимых архивов <c>traces-{timestamp}.jsonl</c>; дефолт — 5.</summary>
	public int KeepFiles { get; init; } = 5;

	/// <summary>
	/// Приёмник OTLP (dev-просмотр трасс в Phoenix); <c>null</c> или пустое
	/// значение — экспорт выключен, трассы живут только в локальном JSONL.
	/// Traceability: openspec:ops/agent-tracing#scenario-trace-phoenix-off-by-default
	/// </summary>
	public string? OtlpEndpoint { get; init; }
}
