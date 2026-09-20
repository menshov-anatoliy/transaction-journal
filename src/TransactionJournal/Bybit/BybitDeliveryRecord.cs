using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Delivery-запись экспирации из GET /v5/asset/delivery-record: содержит расчётную цену
/// доставки, страйк и реализованный PnL вместо цены и комиссии исполнения. Это единственный
/// источник данных об экспирациях для закрывающих записей журнала.
/// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
/// Traceability: doc:docs/research/bybit-api.md#21-основной-источник--get-delivery-record
/// </summary>
public sealed class BybitDeliveryRecord
{
	/// <summary>Время доставки, мс с эпохи Unix; часть идемпотентного ключа symbol + deliveryTime.</summary>
	[JsonPropertyName("deliveryTime")]
	public long DeliveryTimeMs { get; init; }

	/// <summary>Символ инструмента, например BTC-29DEC22-16000-P.</summary>
	[JsonPropertyName("symbol")]
	public string Symbol { get; init; } = string.Empty;

	/// <summary>Сторона позиции: Buy или Sell.</summary>
	[JsonPropertyName("side")]
	public string Side { get; init; } = string.Empty;

	/// <summary>Исполненный размер позиции.</summary>
	[JsonPropertyName("position")]
	public decimal? Position { get; init; }

	/// <summary>Средняя входная цена позиции.</summary>
	[JsonPropertyName("entryPrice")]
	public decimal? EntryPrice { get; init; }

	/// <summary>Расчётная цена экспирации.</summary>
	[JsonPropertyName("deliveryPrice")]
	public decimal? DeliveryPrice { get; init; }

	/// <summary>Страйк инструмента.</summary>
	[JsonPropertyName("strike")]
	public decimal? Strike { get; init; }

	/// <summary>Комиссия за доставку.</summary>
	[JsonPropertyName("fee")]
	public decimal? Fee { get; init; }

	/// <summary>
	/// Реализованный PnL доставки по данным биржи; используется только для предупреждающей
	/// сверки с собственным расчётом журнала, а не вместо него.
	/// </summary>
	[JsonPropertyName("deliveryRpl")]
	public decimal? DeliveryRpl { get; init; }
}
