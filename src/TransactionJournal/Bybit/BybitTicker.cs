using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Тикер инструмента из GET /v5/market/tickers: символ и публичная марка, по которой
/// провайдер марок оценивает нереализованный результат открытых остатков. Марка приходит
/// строкой с инвариантным разделителем, пустая строка означает отсутствие значения.
/// Traceability: openspec:analytics/performance#requirement-mark-provider
/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
/// </summary>
public sealed class BybitTicker
{
	/// <summary>Символ инструмента, например BTC-29DEC23-25000-C или BTCUSDT.</summary>
	[JsonPropertyName("symbol")]
	public string Symbol { get; init; } = string.Empty;

	/// <summary>Публичная марка инструмента на момент ответа биржи.</summary>
	[JsonPropertyName("markPrice")]
	public decimal? MarkPrice { get; init; }
}
