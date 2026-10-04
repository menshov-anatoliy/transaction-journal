namespace TransactionJournal.Hints.Display;

/// <summary>
/// Подписи характеров действия — закрытого справочника таксономии корпуса:
/// карточка подсказки и фильтр журнала показывают характер словами. Набор
/// значений каноничен загрузчику корпуса, подписи — только отображение.
/// </summary>
public static class HintCharacterLabels
{
	/// <summary>Подпись характера действия словами; неизвестный характер показывается значением как есть.</summary>
	/// <param name="character">Значение характера записи.</param>
	public static string LabelOf(string character) => Labels.TryGetValue(character, out var label)
		? label
		: character;

	/// <summary>Подписи всех десяти характеров справочника таксономии v1.</summary>
	public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["risk-mode"] = "лимиты и режим риска",
		["profit-target"] = "цель по прибыли",
		["profit-protection"] = "защита прибыли",
		["risk-reduction"] = "снижение риска",
		["rolling"] = "роллирование",
		["entry"] = "возможность входа",
		["exit"] = "возможность выхода",
		["futures-leg"] = "фьючерсная нога",
		["rebuild-dismantle"] = "перестройка и разборка",
		["other"] = "прочее",
	};
}
