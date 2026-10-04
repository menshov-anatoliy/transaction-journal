namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-40 «Разморозка залипшей части»: итог конструкции в плюсе, и накопленная
/// прибыль не меньше кратности порога (unfreezeProfitRatio) от худшего
/// нереализованного убытка открытой позиции — прибыль позволяет закрыть
/// убыточную «залипшую» часть с сохранением результата. Доли продажи (10-20%)
/// — рекомендация карточки, в условии не участвуют.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class UnfreezeProfitRatioTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "unfreeze-profit-ratio";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		var construction = input.Construction;
		if (construction is null || construction.TotalPnL is not { } totalPnL || totalPnL <= 0m)
		{
			return TriggerOutcome.NotFired();
		}

		var ratio = CorpusThresholds.Decimal(input.Card, "unfreezeProfitRatio");
		var worstLoss = construction.Positions
			.Select(position => position.UnrealizedPnL)
			.OfType<decimal>()
			.Where(pnl => pnl < 0m)
			.DefaultIfEmpty(0m)
			.Min();
		if (ratio is null || worstLoss >= 0m)
		{
			return TriggerOutcome.NotFired();
		}

		return totalPnL >= ratio * Math.Abs(worstLoss)
			? TriggerOutcome.FiredWith(new Dictionary<string, string>
			{
				["profit"] = JournalMath.FormatNumber(decimal.Round(totalPnL, 2)),
				["loss"] = JournalMath.FormatNumber(decimal.Round(Math.Abs(worstLoss), 2)),
			})
			: TriggerOutcome.NotFired();
	}
}
