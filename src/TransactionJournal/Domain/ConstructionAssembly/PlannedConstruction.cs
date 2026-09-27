namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Конструкция в плане сборки: детерминированное имя из открывающего окна,
/// период жизни (от первого исполнения до закрытия, у живых конец открыт),
/// ноги с итоговыми остатками. Идентификатор — порядковый номер создания в
/// прогоне: нумерация воспроизводима, потому что порядок записей и активов
/// зафиксирован правилами сборки.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
/// Traceability: change:add-construction-auto-assembly/design#d4
/// </summary>
public sealed record PlannedConstruction
{
	/// <summary>Порядковый номер конструкции в прогоне сборки, начиная с единицы.</summary>
	public required long Id { get; init; }

	/// <summary>Детерминированное имя из состава открывающих ног; переименование свободно и на сборку не влияет.</summary>
	public required string Name { get; init; }

	/// <summary>Базовый актив конструкции (например, ETH).</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Время первого исполнения открывающего окна, мс Unix-эпохи — начало периода жизни.</summary>
	public required long OpenedAtMs { get; init; }

	/// <summary>Время закрытия, мс Unix-эпохи; null — конструкция жива, конец периода жизни открыт.</summary>
	public long? ClosedAtMs { get; init; }

	/// <summary>Ноги конструкции с итоговыми остатками, в порядке первого появления символов.</summary>
	public required IReadOnlyList<PlannedLeg> Legs { get; init; }
}
