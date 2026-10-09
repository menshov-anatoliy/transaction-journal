using TransactionJournal.Application.Bybit;
using System.Text.Json.Serialization;

namespace TransactionJournal.Infrastructure.Bybit;

/// <summary>
/// Тикер инструмента из GET /v5/market/tickers: символ и публичная марка, по которой
/// провайдер марок оценивает нереализованный результат открытых остатков. Марка приходит
/// строкой с инвариантным разделителем, пустая строка означает отсутствие значения.
/// Помимо марки тот же эндпоинт отдаёт опциональные рыночные поля для инструментов
/// чата — греки и подразумеваемые волатильности опциона, открытый интерес,
/// бид-аск и ставку фандинга; состав полей зависит от категории тикера, отсутствующие
/// значения приходят пустой строкой и разбираются в null, а потребители марки их не читают.
/// Traceability: openspec:analytics/performance#requirement-mark-provider
/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
/// Traceability: change:add-assistant-chat/design#d3
/// </summary>
public sealed class BybitTicker
{
	/// <summary>Символ инструмента, например BTC-29DEC23-25000-C или BTCUSDT.</summary>
	[JsonPropertyName("symbol")]
	public string Symbol { get; init; } = string.Empty;

	/// <summary>Публичная марка инструмента на момент ответа биржи.</summary>
	[JsonPropertyName("markPrice")]
	public decimal? MarkPrice { get; init; }

	#region Бид-аск

	/// <summary>Лучшая цена покупки; для тикера без стакана биржа значение не отдаёт.</summary>
	[JsonPropertyName("bid1Price")]
	public decimal? Bid1Price { get; init; }

	/// <summary>Объём лучшей цены покупки.</summary>
	[JsonPropertyName("bid1Size")]
	public decimal? Bid1Size { get; init; }

	/// <summary>Лучшая цена продажи; для тикера без стакана биржа значение не отдаёт.</summary>
	[JsonPropertyName("ask1Price")]
	public decimal? Ask1Price { get; init; }

	/// <summary>Объём лучшей цены продажи.</summary>
	[JsonPropertyName("ask1Size")]
	public decimal? Ask1Size { get; init; }

	#endregion

	#region Подразумеваемая волатильность

	/// <summary>Марка базового актива опциона — якорь окрестности ATM доски; у не-опционов отсутствует.</summary>
	[JsonPropertyName("underlyingPrice")]
	public decimal? UnderlyingPrice { get; init; }

	/// <summary>Подразумеваемая волатильность марки опциона; у не-опционов отсутствует.</summary>
	[JsonPropertyName("markIv")]
	public decimal? MarkIv { get; init; }

	/// <summary>Подразумеваемая волатильность лучшего бида опциона; у не-опционов отсутствует.</summary>
	[JsonPropertyName("bid1Iv")]
	public decimal? Bid1Iv { get; init; }

	/// <summary>Подразумеваемая волатильность лучшего аска опциона; у не-опционов отсутствует.</summary>
	[JsonPropertyName("ask1Iv")]
	public decimal? Ask1Iv { get; init; }

	#endregion

	#region Греки

	/// <summary>Дельта опциона; у не-опционов отсутствует.</summary>
	[JsonPropertyName("delta")]
	public decimal? Delta { get; init; }

	/// <summary>Гамма опциона; у не-опционов отсутствует.</summary>
	[JsonPropertyName("gamma")]
	public decimal? Gamma { get; init; }

	/// <summary>Вега опциона; у не-опционов отсутствует.</summary>
	[JsonPropertyName("vega")]
	public decimal? Vega { get; init; }

	/// <summary>Тета опциона; у не-опционов отсутствует.</summary>
	[JsonPropertyName("theta")]
	public decimal? Theta { get; init; }

	#endregion

	#region Открытый интерес

	/// <summary>Открытый интерес: в базовой валюте у фьючерсов, в контрактах у опционов.</summary>
	[JsonPropertyName("openInterest")]
	public decimal? OpenInterest { get; init; }

	/// <summary>Открытый интерес в котируемой валюте; отдаётся для фьючерсов.</summary>
	[JsonPropertyName("openInterestValue")]
	public decimal? OpenInterestValue { get; init; }

	#endregion

	#region Фандинг

	/// <summary>Текущая ставка фандинга бессрочного фьючерса; у опционов отсутствует.</summary>
	[JsonPropertyName("fundingRate")]
	public decimal? FundingRate { get; init; }

	#endregion
}
