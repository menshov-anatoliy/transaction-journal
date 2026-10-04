namespace TransactionJournal.Hints.Engine;

using TransactionJournal.Hints.Engine.Triggers;

/// <summary>
/// Реестр триггеров — явный словарь «ключ trigger.implementation → чистая
/// функция» в коде движка. Набор v1 собран по умолчанию; новый ключ добавляется
/// реализацией IHintTrigger и регистрацией в словаре, карточки корпуса код не
/// меняют. Ключ вне реестра карточке не ломает: правило уходит в чек-лист.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
/// </summary>
public sealed class HintTriggerRegistry
{
	private readonly IReadOnlyDictionary<string, IHintTrigger> _triggers;

	/// <summary>Создаёт реестр с набором триггеров v1 либо с явной подменой набора.</summary>
	/// <param name="triggers">Триггеры реестра; null — полный набор машинных ключей v1.</param>
	/// <exception cref="ArgumentException">Два триггера объявили один ключ.</exception>
	public HintTriggerRegistry(IEnumerable<IHintTrigger>? triggers = null)
	{
		var items = (triggers ?? DefaultTriggers()).ToArray();
		_triggers = items.ToDictionary(trigger => trigger.Key, StringComparer.Ordinal);
	}

	/// <summary>Разрешает ключ триггера карточки в реализацию движка.</summary>
	/// <param name="implementation">Ключ trigger.implementation карточки.</param>
	/// <param name="trigger">Реализация триггера; null — ключа нет в движке.</param>
	/// <returns>true — ключ покрыт кодом движка.</returns>
	public bool TryResolve(string implementation, out IHintTrigger trigger)
		=> _triggers.TryGetValue(implementation, out trigger!);

	/// <summary>Полный набор машинных триггеров v1 — 11 ключей корпуса ([#27]).</summary>
	private static IEnumerable<IHintTrigger> DefaultTriggers()
	{
		yield return new RiskLimitPeriodTrigger();
		yield return new UncoveredSaleMarginTrigger();
		yield return new ProfitTargetReachedTrigger();
		yield return new EdgeSaleCapTrigger();
		yield return new RollTimeWindowTrigger();
		yield return new RollThresholdTrigger();
		yield return new AtmDecayWindowTrigger();
		yield return new MinStraddleSizeTrigger();
		yield return new FlatWinStreakTrigger();
		yield return new UnfreezeProfitRatioTrigger();
		yield return new SyntheticCloseItmTrigger();
	}
}
