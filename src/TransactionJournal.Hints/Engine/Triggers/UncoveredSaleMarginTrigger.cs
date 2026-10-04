namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-03 «ГО непокрытой продажи ×10»: в конструкции есть проданный опцион, у
/// которого нет противоноги — купленного опциона того же базового актива в
/// этой же конструкции. Порог ×10 — справочная величина карточки, в условии
/// не участвует: срабатывание структурное, факты не нужны — шаблон статичен.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class UncoveredSaleMarginTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "uncovered-sale-margin";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		if (input.Construction is null)
		{
			return TriggerOutcome.NotFired();
		}

		var legs = ConstructionLegs.Options(input.Construction);
		var sold = legs.Where(leg => leg.Residual < 0m).ToArray();
		var uncovered = sold.Any(leg =>
			legs.Any(other => other.Residual > 0m && other.BaseCoin == leg.BaseCoin) == false);

		return uncovered
			? TriggerOutcome.FiredWith(new Dictionary<string, string>())
			: TriggerOutcome.NotFired();
	}
}
