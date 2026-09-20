using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Спецификация инструмента из GET /v5/market/instruments-info: канонические тип опциона,
/// базовый актив, расчётная валюта и время delivery, которым справочник журнала доверяет
/// больше, чем разбору строки символа. Фильтры цены/лота остаются в сыром JSON записи.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
/// </summary>
public sealed class BybitInstrumentInfo
{
	/// <summary>Символ инструмента, например BTC-24JUN23-56000-P или BTCUSDT.</summary>
	[JsonPropertyName("symbol")]
	public string Symbol { get; init; } = string.Empty;

	/// <summary>Тип контракта линейных инструментов (LinearPerpetual и другие); пуст у опционов.</summary>
	[JsonPropertyName("contractType")]
	public string? ContractType { get; init; }

	/// <summary>Статус инструмента (Trading, Delisted и другие).</summary>
	[JsonPropertyName("status")]
	public string? Status { get; init; }

	/// <summary>Базовый актив.</summary>
	[JsonPropertyName("baseCoin")]
	public string? BaseCoin { get; init; }

	/// <summary>Валюта котирования.</summary>
	[JsonPropertyName("quoteCoin")]
	public string? QuoteCoin { get; init; }

	/// <summary>Расчётная валюта (у опционов — USDC).</summary>
	[JsonPropertyName("settleCoin")]
	public string? SettleCoin { get; init; }

	/// <summary>Время запуска инструмента, мс.</summary>
	[JsonPropertyName("launchTime")]
	public long? LaunchTimeMs { get; init; }

	/// <summary>
	/// Время delivery: экспирация опциона или датированного фьючерса, делистинг перпа;
	/// ноль означает отсутствие доставки.
	/// </summary>
	[JsonPropertyName("deliveryTime")]
	public long? DeliveryTimeMs { get; init; }

	/// <summary>Ставка комиссии доставки; пустая строка у бессрочных контрактов.</summary>
	[JsonPropertyName("deliveryFeeRate")]
	public decimal? DeliveryFeeRate { get; init; }

	/// <summary>Тип опциона: Call или Put; отсутствует у линейных инструментов.</summary>
	[JsonPropertyName("optionsType")]
	public string? OptionsType { get; init; }

	/// <summary>Отображаемое имя инструмента.</summary>
	[JsonPropertyName("displayName")]
	public string? DisplayName { get; init; }

	/// <summary>Интервал фандинга в минутах; только у бессрочных контрактов.</summary>
	[JsonPropertyName("fundingInterval")]
	public int? FundingInterval { get; init; }
}
