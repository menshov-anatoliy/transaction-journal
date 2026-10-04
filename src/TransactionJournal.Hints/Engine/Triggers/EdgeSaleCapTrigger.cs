namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-18 «Продажа дальних краёв не более 1/3»: суммарный объём проданных
/// опционов конструкции превышает долю порога от объёма всех опционов.
/// Доля читается из thresholds карточки («1/3»); опционов нет — проверять
/// нечего. Факты не нужны — шаблон статичен.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class EdgeSaleCapTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "edge-sale-cap";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		if (input.Construction is null)
		{
			return TriggerOutcome.NotFired();
		}

		var cap = CorpusThresholds.Fraction(input.Card, "edgeVolumeCap");
		var legs = ConstructionLegs.Options(input.Construction);
		var totalQty = legs.Sum(leg => Math.Abs(leg.Residual));
		if (cap is null || totalQty <= 0m)
		{
			return TriggerOutcome.NotFired();
		}

		var soldQty = legs.Sum(leg => Math.Max(0m, -leg.Residual));
		return soldQty / totalQty > cap
			? TriggerOutcome.FiredWith(new Dictionary<string, string>())
			: TriggerOutcome.NotFired();
	}
}
