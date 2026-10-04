namespace TransactionJournal.Hints.Display;

using TransactionJournal.Hints.Ports;

/// <summary>
/// Данные панели «Подсказки» одного субъекта: живые подсказки, сгруппированные
/// по группам справочника v1 (пустые группы отсутствуют), и свёрнутая
/// терминальная история; сортировка внутри — по времени генерации, свежие
/// сверху. Поля группировки выводятся из субъекта и характера записи при
/// чтении — сама запись их не хранит.
// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
// Traceability: openspec:ui/screens#requirement-ui-hint-section-groups
/// </summary>
public sealed record HintPanelData
{
	/// <summary>Субъект панели — открытая конструкция или журнал.</summary>
	public required HintSubject Subject { get; init; }

	/// <summary>Живые подсказки субъекта по группам справочника v1; группы без подсказок не входят.</summary>
	public required IReadOnlyList<HintSection> LiveGroups { get; init; }

	/// <summary>Терминальная история субъекта (applied/dismissed/expired) для свёрнутого блока.</summary>
	public required IReadOnlyList<HintRecord> History { get; init; }

	/// <summary>Число живых подсказок субъекта.</summary>
	public int LiveCount => LiveGroups.Sum(group => group.Hints.Count);
}

/// <summary>Группа живых подсказок панели: записи одного характера действия под заголовком группы справочника.</summary>
public sealed record HintSection
{
	/// <summary>Определение группы справочника v1.</summary>
	public required HintGroupDefinition Group { get; init; }

	/// <summary>Живые подсказки группы, свежие сверху.</summary>
	public required IReadOnlyList<HintRecord> Hints { get; init; }
}
