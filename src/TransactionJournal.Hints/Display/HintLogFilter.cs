namespace TransactionJournal.Hints.Display;

using TransactionJournal.Hints.Ports;

/// <summary>
/// Фильтр общего журнала подсказок: null — ограничение не выбрано, показываются
/// записи всех статусов, характеров и групп. Фильтр по группе ограничивает
/// журнал характерами этой группы справочника v1.
// Traceability: openspec:ui/screens#requirement-ui-hint-log
/// </summary>
public sealed record HintLogFilter
{
	/// <summary>Статус жизненного цикла; null — записи всех статусов.</summary>
	public HintStatus? Status { get; init; }

	/// <summary>Характер действия; null — записи всех характеров.</summary>
	public string? Character { get; init; }

	/// <summary>Идентификатор группы справочника v1; null — записи всех групп.</summary>
	public string? GroupId { get; init; }

	/// <summary>Фильтр без ограничений — весь журнал.</summary>
	public static HintLogFilter All { get; } = new();

	/// <summary>Характеры, допустимые фильтром группы: набор характеров группы; null-группа — null.</summary>
	public IReadOnlyList<string>? GroupCharacters => GroupId is null
		? null
		: HintSectionGroups.V1.FirstOrDefault(group => group.Id == GroupId)?.Characters;
}
