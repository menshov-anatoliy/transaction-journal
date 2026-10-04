namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Domain.Materialization;
using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-26 «Окно ролла»: до ближайшей экспирации открытых опционов конструкции
/// осталось дней из окна порога («7-10»), и цена спота находится внутри
/// страйкового диапазона этих опционов — цена у центра, кандидат на ролл.
/// Марка спота базового актива берётся из партии марок; опционов, окна или
/// марки нет — условие непроверяемо.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class RollTimeWindowTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "roll-time-window";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		if (input.Construction is null)
		{
			return TriggerOutcome.NotFired();
		}

		var window = CorpusThresholds.Range(input.Card, "rollWindowDays");
		var legs = ConstructionLegs.Options(input.Construction);
		if (window is null || legs.Count == 0)
		{
			return TriggerOutcome.NotFired();
		}

		// Один базовый актив на конструкцию журнала; спот — линейный фьючерс
		// «{база}USDT». Разные активы в одной конструкции не ожидаются.
		var baseCoin = legs[0].BaseCoin;
		if (ConstructionLegs.TrySpot(input.Marks, baseCoin, out var spot) == false)
		{
			return TriggerOutcome.NotFired();
		}

		var nearestExpiry = legs.Min(leg => leg.ExpiryDate);
		var daysLeft = (decimal)(nearestExpiry.Date - input.AsOf.UtcDateTime.Date).TotalDays;
		if (daysLeft < window.Value.Min || daysLeft > window.Value.Max)
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
