namespace TransactionJournal.Chats;

/// <summary>
/// Уровень детализации операционной трассы хода чата: <see cref="Basic"/>
/// записывает имена, длительности, статусы и метрики gen_ai.*, а
/// <see cref="Detailed"/> дополнительно захватывает события аргументов тулов
/// tj.tool.args на спанах execute_tool. Уровень по умолчанию — detailed;
/// маппинг из секции Tracing конфигурации выполняет composition root, сам
/// проект Chats на Infrastructure не ссылается.
// Traceability: openspec:ops/agent-tracing#requirement-trace-detail-levels
/// </summary>
public enum ChatTracingDetail
{
	/// <summary>Базовый уровень: спаны, статусы и метрики gen_ai.* без событий аргументов тулов.</summary>
	Basic,

	/// <summary>Подробный уровень (дефолт): базовый плюс события tj.tool.args.</summary>
	Detailed,
}

/// <summary>
/// Опции инструментации хода чата: единственный параметр — уровень
/// детализации трассы. Дефолт — detailed: инструментация включена сразу,
/// без настройки; composition root маппит секцию Tracing в эти опции при
/// регистрации реестра инструментов.
// Traceability: openspec:ops/agent-tracing#requirement-trace-detail-levels
/// </summary>
public sealed record ChatTracingOptions
{
	/// <summary>Уровень детализации трассы хода чата; дефолт — <see cref="ChatTracingDetail.Detailed"/>.</summary>
	public ChatTracingDetail Detail { get; init; } = ChatTracingDetail.Detailed;
}
