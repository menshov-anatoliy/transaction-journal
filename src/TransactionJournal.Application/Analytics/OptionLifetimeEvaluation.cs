namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Результат вычислителя метрик времени жизни одного прохода чтения: метрики
/// открытых опционных ног, саммари конструкции и предупреждения об экспирации.
/// Саммари отсутствует у конструкции без открытых опционных ног; предупреждения
/// детерминированно упорядочены по символу, по одной записи на инструмент.
// Traceability: change:add-option-lifetime-metrics/design#d2
/// </summary>
public sealed record OptionLifetimeEvaluation
{
	/// <summary>Метрики открытых опционных ног в порядке их перечисления; закрытые ноги не возвращаются.</summary>
	public required IReadOnlyList<OptionLegLifetimeMetrics> Legs { get; init; }

	/// <summary>Саммари времени жизни конструкции; null без открытых опционных ног.</summary>
	public ConstructionLifetimeSummary? Summary { get; init; }

	/// <summary>Предупреждения об экспирации открытых опционных ног, по одной записи на инструмент.</summary>
	public IReadOnlyList<OptionExpiryWarning> ExpiryWarnings { get; init; } = [];
}
