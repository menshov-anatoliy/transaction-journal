namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Domain.Data;
using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-14 «Плановая прибыль достигнута — разборка»: итог конструкции достиг
/// плановой прибыли. Цель берётся из параметров конструкции: процент — доля
/// выделенного капитала, USDT — прямая величина; цель или капитал не заданы —
/// условие непроверяемо, правило не срабатывает. Размытое правило: кандидат
/// на разборку, решение за человеком.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class ProfitTargetReachedTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "profit-target-reached";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		var construction = input.Construction;
		if (construction is null
			|| construction.TotalPnL is null
			|| construction.ProfitValue is null
			|| construction.ProfitUnit is null)
		{
			return TriggerOutcome.NotFired();
		}

		decimal? target = construction.ProfitUnit == TargetUnit.Usdt
			? construction.ProfitValue
			: construction.AllocatedCapitalUsdt is { } capital
				? construction.ProfitValue / 100m * capital
				: null;

		if (target is null || construction.TotalPnL < target)
		{
			return TriggerOutcome.NotFired();
		}

		// Факт шаблона — итог в процентах от капитала; без капитала процент
		// не определён, а шаблон требует факт, поэтому правило не срабатывает.
		if (construction.AllocatedCapitalUsdt is not { } capitalValue)
		{
			return TriggerOutcome.NotFired();
		}

		var pnlPct = construction.TotalPnLPercent ?? construction.TotalPnL.Value / capitalValue * 100m;
		return TriggerOutcome.FiredWith(new Dictionary<string, string>
		{
			["pnlPct"] = JournalMath.FormatNumber(decimal.Round(pnlPct, 2)),
		});
	}
}
