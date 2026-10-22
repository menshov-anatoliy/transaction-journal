using TransactionJournal.Application.Bybit;
using System.Text.Json.Serialization;

namespace TransactionJournal.Infrastructure.Bybit;

/// <summary>
/// Тикер инструмента из GET /v5/market/tickers: символ и публичная котировка, по которой
/// провайдер котировок оценивает нереализованный результат открытых остатков. Котировка приходит
/// строкой с инвариантным разделителем, пустая строка означает отсутствие значения.
/// Помимо котировки тот же эндпоинт отдаёт опциональные рыночные поля для инструментов
/// чата — греки и подразумеваемые волатильности опциона, открытый интерес,
/// бид-аск и ставку фандинга; состав полей зависит от категории тикера, отсутствующие
/// значения приходят пустой строкой и разбираются в null, а потребители котировки их не читают.
/// Traceability: openspec:analytics/performance#requirement-mark-provider
/// Traceability: doc:docs/research/bybit-api.md#3-публичные-котировки-tickers-без-аутентификации
/// Traceability: change:add-assistant-chat/design#d3
/// </summary>
public sealed class BybitTicker
{
	/// <summary>Символ инструмента, например BTC-29DEC23-25000-C или BTCUSDT.</summary>
	[JsonPropertyName("symbol")]
	public string Symbol { get; init; } = string.Empty;

	/// <summary>Публичная котировка инструмента на момент ответа биржи.</summary>
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

	/// <summary>Котировка базового актива опциона — якорь окрестности ATM доски; у не-опционов отсутствует.</summary>
	[JsonPropertyName("underlyingPrice")]
	public decimal? UnderlyingPrice { get; init; }

	/// <summary>Подразумеваемая волатильность котировки опциона; у не-опционов отсутствует.</summary>
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
	/// <remarks>
	/// Сверка живым ответом GET /v5/market/tickers?category=option&amp;baseCoin=BTC
	/// (2026-10-18): тета отрицательная у всех 672 тикеров (доски 21OCT26 — около
	/// 2.6 суток до экспирации и 25JUN27 — около 250 суток), единицы — котируемая
	/// монета (USDT) на контракт в сутки, тот же масштаб цен, что у markPrice.
	/// Контрольный тикер BTC-21OCT26-79500-C-USDT: markPrice 2091.86150741,
	/// underlyingPrice 81154.40530188, страйк 79500 — временная стоимость
	/// markPrice − (underlyingPrice − strike) = 437.45620553, тета −164.32019599:
	/// |тета| × 2.66 оставшихся суток ≈ 437 ≈ временной стоимости. У дальних досок
	/// |тета| на два порядка меньше временной стоимости (BTC-25JUN27-150000-C-USDT:
	/// временная 688.2548849, тета −7.0701304) — распад растягивается на весь
	/// остаток жизни. Знак и порядок величины сходятся с временнОй стоимостью,
	/// поэтому маппинг decimal? правки не требует: это суточная тета на контракт
	/// в котируемой монете, отрицательная для длинной позиции.
	/// Traceability: change:add-option-lifetime-metrics/design#d1
	/// Traceability: adr:docs/adr/0017-option-lifetime-metrics.md#considered-options
	/// </remarks>
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
