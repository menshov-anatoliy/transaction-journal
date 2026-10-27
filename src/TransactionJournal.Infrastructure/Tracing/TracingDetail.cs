namespace TransactionJournal.Infrastructure.Tracing;

/// <summary>
/// Уровень детализации операционной трассы агентского харнеса: <see cref="Basic"/>
/// записывает имена, длительности, статусы и метрики <c>gen_ai.*</c>,
/// <see cref="Detailed"/> дополнительно захватывает события аргументов тулов
/// <c>tj.tool.args</c>. Уровень читается потребителями захвата из DI
/// (<see cref="TracingOptions.Detail"/>).
/// Traceability: openspec:ops/agent-tracing#requirement-trace-detail-levels
/// </summary>
public enum TracingDetail
{
	/// <summary>Базовый уровень: имена, длительности, статусы, метрики <c>gen_ai.*</c>.</summary>
	Basic,

	/// <summary>Подробный уровень (дефолт): базовый плюс события <c>tj.tool.args</c>.</summary>
	Detailed,
}
