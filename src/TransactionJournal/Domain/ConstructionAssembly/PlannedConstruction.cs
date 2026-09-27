using TransactionJournal.Data;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Конструкция в плане сборки: имя, период жизни (от первого исполнения до
/// закрытия, у живых конец открыт), ноги с итоговыми остатками и производные
/// атрибуты — статус, следующий за опционным прикрытием, и признак ручного
/// имени. Идентификатор — порядковый номер создания в прогоне для новых
/// конструкций или ключ БД для существующих, переданных в seed'е; нумерация
/// воспроизводима, потому что порядок записей, активов и seed'а зафиксирован
/// правилами сборки.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
/// Traceability: change:add-construction-auto-assembly/design#d4
/// Traceability: change:refine-construction-assembly/design#d7
/// </summary>
public sealed record PlannedConstruction
{
	/// <summary>Порядковый номер конструкции в прогоне или ключ существующей конструкции из seed'а.</summary>
	public required long Id { get; init; }

	/// <summary>Имя конструкции: производное от живых ног либо зафиксированное вручную.</summary>
	public required string Name { get; init; }

	/// <summary>
	/// Признак вручную зафиксированного имени: автогенерация имени для этой
	/// конструкции отключена, сборка имя не пересчитывает.
	/// </summary>
	public bool NameIsManual { get; init; }

	/// <summary>
	/// Статус, следующий за опционным прикрытием: «открыта» при ненулевой
	/// опционной ноге, «закрыта» при полном обнулении ног; «архив» — строго
	/// ручной статус, сборкой не изменяется.
	/// </summary>
	public required ConstructionStatus Status { get; init; }

	/// <summary>Признак существующей конструкции из seed'а: план её не создаёт, а лишь обновляет производные атрибуты.</summary>
	public bool IsSeeded { get; init; }


	/// <summary>Базовый актив конструкции (например, ETH).</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Время первого исполнения открывающего окна, мс Unix-эпохи — начало периода жизни.</summary>
	public required long OpenedAtMs { get; init; }

	/// <summary>Время закрытия, мс Unix-эпохи; null — конструкция жива, конец периода жизни открыт.</summary>
	public long? ClosedAtMs { get; init; }

	/// <summary>Ноги конструкции с итоговыми остатками, в порядке первого появления символов.</summary>
	public required IReadOnlyList<PlannedLeg> Legs { get; init; }
}
