namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Domain.Materialization;
using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-43 «Опцион в деньгах — синтетическое закрытие»: среди купленных опционов
/// конструкции есть опцион в деньгах (кол — спот выше страйка, пут — ниже).
/// Срабатывание структурное, факты не нужны — шаблон описывает приём
/// синтетического закрытия статично. Размытое правило: приём как решение.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class SyntheticCloseItmTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "synthetic-close-itm";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		if (input.Construction is null)
		{
			return TriggerOutcome.NotFired();
		}

		var legs = ConstructionLegs.Options(input.Construction)
			.Where(leg => leg.Residual > 0m)
			.ToArray();
		if (legs.Length == 0)
		{
			return TriggerOutcome.NotFired();
		}

		var baseCoin = legs[0].BaseCoin;
		if (ConstructionLegs.TrySpot(input.Marks, baseCoin, out var spot) == false)
		{
			return TriggerOutcome.NotFired();
		}

		var itm = legs.Any(leg => leg.Type == OptionType.Call
			? spot > leg.Strike
			: spot < leg.Strike);

		return itm
			? TriggerOutcome.FiredWith(new Dictionary<string, string>())
			: TriggerOutcome.NotFired();
	}
}
