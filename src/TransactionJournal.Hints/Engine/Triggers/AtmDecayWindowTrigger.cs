namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-31 «Окно тета-распада у центра»: до ближайшей экспирации не более порога
/// дней (но уже наступил последний отрезок — от одного дня), и цена спота
/// внутри страйкового диапазона конструкции — риск тета-распада у центра,
/// кандидат на досрочный выход.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class AtmDecayWindowTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "atm-decay-window";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		if (input.Construction is null)
		{
			return TriggerOutcome.NotFired();
		}

		var windowDays = CorpusThresholds.Decimal(input.Card, "decayWindowDays");
		var legs = ConstructionLegs.Options(input.Construction);
		if (windowDays is null || legs.Count == 0)
		{
			return TriggerOutcome.NotFired();
		}

		var baseCoin = legs[0].BaseCoin;
		if (ConstructionLegs.TrySpot(input.Marks, baseCoin, out var spot) == false)
		{
			return TriggerOutcome.NotFired();
		}

		var nearestExpiry = legs.Min(leg => leg.ExpiryDate);
		var daysLeft = (decimal)(nearestExpiry.Date - input.AsOf.UtcDateTime.Date).TotalDays;
		if (daysLeft < 1m || daysLeft > windowDays)
		{
			return TriggerOutcome.NotFired();
		}

		var minStrike = legs.Min(leg => leg.Strike);
		var maxStrike = legs.Max(leg => leg.Strike);
		if (spot < minStrike || spot > maxStrike)
		{
			return TriggerOutcome.NotFired();
		}

		return TriggerOutcome.FiredWith(new Dictionary<string, string>
		{
			["daysLeft"] = JournalMath.FormatNumber(daysLeft),
		});
	}
}
