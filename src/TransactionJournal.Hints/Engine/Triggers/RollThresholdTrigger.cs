namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Domain.Materialization;
using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-27 «Порог смещения для ролла»: среди купленных опционов конструкции
/// есть нога, для которой цена ушла от страйка в неблагоприятную сторону не
/// менее порога процентов (кол — цена ниже страйка, пут — выше). Берётся
/// максимальное неблагоприятное смещение; марка спота обязательна.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class RollThresholdTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "roll-threshold";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		if (input.Construction is null)
		{
			return TriggerOutcome.NotFired();
		}

		var threshold = CorpusThresholds.Decimal(input.Card, "rollThreshold");
		var legs = ConstructionLegs.Options(input.Construction)
			.Where(leg => leg.Residual > 0m)
			.ToArray();
		if (threshold is null || legs.Length == 0)
		{
			return TriggerOutcome.NotFired();
		}

		var baseCoin = legs[0].BaseCoin;
		if (ConstructionLegs.TrySpot(input.Marks, baseCoin, out var spot) == false)
		{
			return TriggerOutcome.NotFired();
		}

		// Неблагоприятное смещение — удаление спота от страйка в сторону убытка
		// купленной ноги, в процентах от страйка; среди ног берётся максимум.
		var worstShift = legs.Max(leg => leg.Type == OptionType.Call
			? spot < leg.Strike ? (leg.Strike - spot) / leg.Strike * 100m : 0m
			: spot > leg.Strike ? (spot - leg.Strike) / leg.Strike * 100m : 0m);

		return worstShift >= threshold
			? TriggerOutcome.FiredWith(new Dictionary<string, string>
			{
				["shiftPct"] = JournalMath.FormatNumber(decimal.Round(worstShift, 2)),
			})
			: TriggerOutcome.NotFired();
	}
}
