namespace TransactionJournal.Domain.Materialization;

/// <summary>
/// Разобранные из символа вечного линейного фьючерса части: базовый актив и
/// котируемая валюта. Разобранные части — признак линейной ноги реального риска:
/// строка символа только распознаётся, а участие ноги в расчёте определяет калькулятор.
/// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
/// </summary>
public sealed record LinearSymbolParts
{
	/// <summary>Базовый актив из символа без хвоста котируемой валюты, например XAUT или ETH.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Котируемая валюта из хвоста символа: USDT или USDC.</summary>
	public required string QuoteCoin { get; init; }
}
