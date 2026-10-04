namespace TransactionJournal.Hints.Engine.Triggers;

using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Hints.Ports;

/// <summary>
/// ac-35 «Минимальный размер стреддла для интрадея»: в конструкции есть
/// стреддл (купленные кол и пут одного базового актива), но объёмы ниже
/// минимума порога — опционов меньше minStraddleOptions или фьючерсов меньше
/// minStraddleFutures (соотношение 30:15). Объёмы считаются по абсолютным
/// остаткам позиций; факты — фактические объёмы в подсказке.
/// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
/// </summary>
public sealed class MinStraddleSizeTrigger : IHintTrigger
{
	/// <inheritdoc />
	public string Key => "min-straddle-size";

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

		var minOptions = CorpusThresholds.Decimal(input.Card, "minStraddleOptions");
		var minFutures = CorpusThresholds.Decimal(input.Card, "minStraddleFutures");
		if (minOptions is null || minFutures is null)
		{
			return TriggerOutcome.NotFired();
		}

		var legs = ConstructionLegs.Options(construction);

		// Стреддл в конструкции обязателен: правило интрадея оценивает размер
		// стреддла, а не любую конструкцию с малыми объёмами.
		var hasStraddle = legs
			.GroupBy(leg => leg.BaseCoin)
			.Any(group => group.Any(leg => leg.Type == Domain.Materialization.OptionType.Call)
				&& group.Any(leg => leg.Type == Domain.Materialization.OptionType.Put && leg.Residual > 0m)
				&& group.Any(leg => leg.Type == Domain.Materialization.OptionType.Call && leg.Residual > 0m));
		if (hasStraddle == false)
		{
			return TriggerOutcome.NotFired();
		}

		var optionBaseCoins = legs.Select(leg => leg.BaseCoin).ToHashSet();
		var optionsQty = legs.Sum(leg => Math.Abs(leg.Residual));
		var futuresQty = construction.Positions
			.Where(position => LinearSymbolParser.TryParseBaseCoin(position.Symbol, out var baseCoin)
				&& baseCoin is not null
				&& optionBaseCoins.Contains(baseCoin))
			.Sum(position => Math.Abs(position.Residual));

		if (optionsQty >= minOptions && futuresQty >= minFutures)
		{
			return TriggerOutcome.NotFired();
		}

		return TriggerOutcome.FiredWith(new Dictionary<string, string>
		{
			["opts"] = JournalMath.FormatNumber(optionsQty),
			["fut"] = JournalMath.FormatNumber(futuresQty),
		});
	}
}
