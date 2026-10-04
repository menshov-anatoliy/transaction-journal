namespace TransactionJournal.Hints.Display;

/// <summary>
/// Закрытый справочник групп v1 — общий слой отображения подсказок: панель
/// «Подсказки» и журнал подсказок группируют записи по характерам действия,
/// пустые группы не показываются. Тот же справочник переиспользуют секции
/// сводки следующего change.
// Traceability: openspec:ui/screens#requirement-ui-hint-section-groups
/// </summary>
public static class HintSectionGroups
{
	/// <summary>Группы v1 в порядке показа: «Риск-режим», «Управление конструкцией», «Фьючерсная нога».</summary>
	public static readonly IReadOnlyList<HintGroupDefinition> V1 =
	[
		new HintGroupDefinition(
			"risk-mode",
			"Риск-режим",
			// Портфельные лимиты и режим риска.
			["risk-mode"]),
		new HintGroupDefinition(
			"construction-management",
			"Управление конструкцией",
			// Выход, защита прибыли, цель по прибыли, снижение риска, роллирование,
			// перестройка/разборка, прочее.
			["exit", "profit-protection", "profit-target", "risk-reduction", "rolling", "rebuild-dismantle", "other"]),
		new HintGroupDefinition(
			"futures-leg",
			"Фьючерсная нога",
			// Управление фьючерсной ногой.
			["futures-leg"]),
	];

	/// <summary>Идентификатор группы «Управление конструкцией» — запасной группы характеров вне набора v1.</summary>
	public const string ConstructionManagementGroupId = "construction-management";

	/// <summary>
	/// Группа характера действия. Справочник закрыт, но запись подсказки
	/// денормализована и может нести характер вне набора v1 (характер снятой
	/// карточки): такие записи показывает группа «Управление конструкцией» —
	/// группа прочих управленческих характеров.
	/// </summary>
	public static HintGroupDefinition Resolve(string character)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(character);

		return V1.FirstOrDefault(group => group.Characters.Contains(character, StringComparer.Ordinal))
			?? V1.Single(group => group.Id == ConstructionManagementGroupId);
	}
}

/// <summary>Группа отображения подсказок: имя, заголовок и закрытый набор характеров.</summary>
public sealed record HintGroupDefinition
{
	/// <summary>Создаёт определение группы.</summary>
	/// <param name="id">Стабильный идентификатор группы — фильтры журнала ссылаются на него.</param>
	/// <param name="title">Заголовок группы в панели и журнале.</param>
	/// <param name="characters">Характеры действия, входящие в группу.</param>
	/// <exception cref="ArgumentException">Идентификатор или заголовок пусты.</exception>
	public HintGroupDefinition(string id, string title, IReadOnlyList<string> characters)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		Id = id;
		Title = title;
		Characters = characters ?? throw new ArgumentNullException(nameof(characters));
	}

	/// <summary>Стабильный идентификатор группы.</summary>
	public string Id { get; }

	/// <summary>Заголовок группы.</summary>
	public string Title { get; }

	/// <summary>Характеры действия, входящие в группу.</summary>
	public IReadOnlyList<string> Characters { get; }
}
