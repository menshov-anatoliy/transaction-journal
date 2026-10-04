namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-38 «Серия плюсовых сделок во флэте — добавление рабочей части»: фьючерсные
/// (линейные) сделки конструкции подряд закрываются в плюс — последняя
/// непрерывная серия плюсовых сделок достигла порога partsUnlockWins. Плюсовость
/// сделки считается построночно: FIFO-доля реализованного результата потока
/// символа, включая комиссию. Размытое правило — решение об добавлении части
/// остаётся за человеком.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class FlatWinStreakTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "flat-win-streak";

	/// <inheritdoc />
	public HintSubjectKind SubjectKind => HintSubjectKind.Construction;

	/// <inheritdoc />
	public TriggerOutcome Evaluate(TriggerEvaluationInput input)
	{
		var construction = input.Construction;
		if (construction is null)
		{
			return TriggerOutcome.NotFired();
		}

		var unlockWins = CorpusThresholds.Decimal(input.Card, "partsUnlockWins");
		if (unlockWins is null)
		{
			return TriggerOutcome.NotFired();
		}

		// Линейные сделки конструкции, сгруппированные по символу: FIFO-доля
		// считается в потоке одного символа, серии идут по хронологии сделок.
		var perSymbol = construction.Trades
			.GroupBy(trade => trade.Symbol)
			.Where(group => LinearSymbolParser.TryParseBaseCoin(group.Key, out _))
			.ToArray();

		var streak = 0;
		foreach (var group in perSymbol)
		{
			var ordered = group.OrderBy(trade => trade.ExecutedAt).ThenBy(trade => trade.ExecId, StringComparer.Ordinal).ToArray();
			var symbolStreak = 0;
			for (var index = ordered.Length - 1; index >= 0; index--)
			{
				var contribution = JournalMath.FifoContribution(ordered, index);
				if (contribution > 0m)
				{
					symbolStreak++;
				}
				else if (contribution < 0m)
				{
					break;
				}

				// Открывающая нога с нулевым вкладом серию не рвёт: серия
				// считается по закрывающим сделкам, открывшиеся части нейтральны.
			}

			streak = Math.Max(streak, symbolStreak);
		}

		return streak >= unlockWins
			? TriggerOutcome.FiredWith(new Dictionary<string, string>
			{
				["wins"] = JournalMath.FormatNumber(streak),
			})
			: TriggerOutcome.NotFired();
	}
}
