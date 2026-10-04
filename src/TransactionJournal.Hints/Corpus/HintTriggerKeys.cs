namespace TransactionJournal.Hints.Corpus;

/// <summary>
/// Машинные ключи триггеров корпуса v1 — явный набор в коде: новый ключ
/// добавляется кодом движка вместе с чистой функцией триггера, корпус сам
/// код не меняет. Ключ карточки вне набора схеме соответствует — карточка
/// валидна, но подсказок не порождает и попадает в чек-лист сводки.
/// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
/// </summary>
public static class HintTriggerKeys
{
	/// <summary>Ключи машинных триггеров корпуса v1 ([#27]).</summary>
	public static readonly IReadOnlyCollection<string> V1 = new[]
	{
		"risk-limit-period",
		"uncovered-sale-margin",
		"profit-target-reached",
		"edge-sale-cap",
		"roll-time-window",
		"roll-threshold",
		"atm-decay-window",
		"min-straddle-size",
		"flat-win-streak",
		"unfreeze-profit-ratio",
		"synthetic-close-itm",
	};

	/// <summary>Проверяет, покрыт ли ключ триггера кодом движка.</summary>
	/// <param name="implementation">Ключ реализации триггера из карточки.</param>
	public static bool IsKnown(string implementation) => V1.Contains(implementation, StringComparer.Ordinal);
}
