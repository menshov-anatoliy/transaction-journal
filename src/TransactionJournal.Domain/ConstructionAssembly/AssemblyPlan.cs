namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// План сборки — результат чистого прогона алгоритма над снимком сырых записей:
/// конструкции с именами, периодами и ногами; привязки execId → конструкция;
/// счётчик «Входящих». План не зависит от хранилища: use-case пересбора
/// применяет его к базе одной транзакцией.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-full-rebuild-semantics
/// Traceability: change:add-construction-auto-assembly/design#d5
/// </summary>
public sealed record AssemblyPlan
{
	/// <summary>Построенные конструкции в порядке создания (по порядковому номеру).</summary>
	public required IReadOnlyList<PlannedConstruction> Constructions { get; init; }

	/// <summary>Привязки execId → порядковый номер конструкции; сделка не привязки остаётся во «Входящих».</summary>
	public required IReadOnlyDictionary<string, long> Bindings { get; init; }

	/// <summary>Число исполнений, оставшихся непривязанными во «Входящих».</summary>
	public required int InboxCount { get; init; }
}
