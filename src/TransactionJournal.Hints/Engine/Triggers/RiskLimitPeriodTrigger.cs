namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-01 «Лимиты риска на период»: суммарный убыток торговой недели, месяца
/// или квартала достиг лимита периода от совокупного выделенного капитала.
/// PnL периода считается построночно: каждой сделке хронологии атрибутируется
/// её FIFO-доля результата, сделки периода суммируются. Условие проверяется
/// от меньшего периода к большему — срабатывает первый (мельчайший)
/// пересечённый период. Журнальное правило: оценивается один раз по снимку.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// Traceability: openspec:hints/hint-lifecycle#requirement-hint-subject-v1-closed-set
/// </summary>
public sealed class RiskLimitPeriodTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "risk-limit-period";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Journal;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		var capital = input.Snapshot.Constructions.Sum(item => item.AllocatedCapitalUsdt ?? 0m);
		var weeklyLimit = CorpusThresholds.Decimal(input.Card, "weeklyRiskLimit");
		var monthlyLimit = CorpusThresholds.Decimal(input.Card, "monthlyRiskLimit");
		var quarterlyLimit = CorpusThresholds.Decimal(input.Card, "quarterlyRiskLimit");

		// Хотя бы один лимит периода не читается из карточки — условие
		// непроверяемо частично, правило не срабатывает вовсе.
		if (capital <= 0m
			|| weeklyLimit is null
			|| monthlyLimit is null
			|| quarterlyLimit is null)
		{
			return TriggerOutcome.NotFired();
		}

		var periods = JournalMath.LossPeriods(input.AsOf);
		var orderedTrades = input.Snapshot.Constructions
			.SelectMany(construction => construction.Trades)
			.OrderBy(trade => trade.ExecutedAt)
			.ThenBy(trade => trade.ExecId, StringComparer.Ordinal)
			.ToArray();

		for (var periodIndex = 0; periodIndex < periods.Count; periodIndex++)
		{
			var period = periods[periodIndex];
			var periodPnL = orderedTrades
				.Select((trade, index) => (Trade: trade, Index: index))
				.Where(pair => period.Contains(pair.Trade))
				.Sum(pair => JournalMath.FifoContribution(orderedTrades, pair.Index));

			// Периоды проверяются от меньшего к большему: срабатывает первый
			// пересечённый — мельчайший период, чей лимит уже достигнут.
			var limit = periodIndex switch
			{
				0 => weeklyLimit!.Value,
				1 => monthlyLimit!.Value,
				_ => quarterlyLimit!.Value,
			};
			var lossPct = -periodPnL / capital * 100m;
			if (lossPct >= limit)
			{
				return TriggerOutcome.FiredWith(
					new Dictionary<string, string>
					{
						["period"] = period.Name,
						["lossPct"] = JournalMath.FormatNumber(decimal.Round(lossPct, 2)),
						["limitPct"] = JournalMath.FormatNumber(limit),
					},
					period.Key);
			}
		}

		return TriggerOutcome.NotFired();
	}
}
