using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Запись исполнения сделки из GET /v5/execution/list. Числа биржа возвращает строками,
/// пустая строка разбирается как null; поля волатильности есть только у категории option.
/// Traceability: doc:docs/research/bybit-api.md#1-история-исполнения-сделок-unified-аккаунта-execution-list
/// </summary>
public sealed class BybitExecution
{
	/// <summary>Символ инструмента, например BTC-29DEC23-45000-C или BTCUSDT.</summary>
	[JsonPropertyName("symbol")]
	public string Symbol { get; init; } = string.Empty;

	/// <summary>Идентификатор ордера биржи.</summary>
	[JsonPropertyName("orderId")]
	public string OrderId { get; init; } = string.Empty;

	/// <summary>Пользовательский идентификатор ордера; пуст, если не задавался.</summary>
	[JsonPropertyName("orderLinkId")]
	public string OrderLinkId { get; init; } = string.Empty;

	/// <summary>Сторона исполнения: Buy или Sell.</summary>
	[JsonPropertyName("side")]
	public string Side { get; init; } = string.Empty;

	/// <summary>Цена ордера; пуста у рыночных ордеров.</summary>
	[JsonPropertyName("orderPrice")]
	public decimal? OrderPrice { get; init; }

	/// <summary>Заявленное количество ордера.</summary>
	[JsonPropertyName("orderQty")]
	public decimal? OrderQty { get; init; }

	/// <summary>Незаполненный остаток ордера.</summary>
	[JsonPropertyName("leavesQty")]
	public decimal? LeavesQty { get; init; }

	/// <summary>Причина создания ордера (CreateByUser, CreateByClosing и другие).</summary>
	[JsonPropertyName("createType")]
	public string CreateType { get; init; } = string.Empty;

	/// <summary>Тип ордера: Market или Limit.</summary>
	[JsonPropertyName("orderType")]
	public string OrderType { get; init; } = string.Empty;

	/// <summary>Тип стоп-ордера; UNKNOWN у обычных ордеров.</summary>
	[JsonPropertyName("stopOrderType")]
	public string? StopOrderType { get; init; }

	/// <summary>Комиссия исполнения со знаком: положительная — уплачена, отрицательная — rebate.</summary>
	[JsonPropertyName("execFee")]
	public decimal? ExecFee { get; init; }

	/// <summary>Идентификатор исполнения — идемпотентный ключ записи журнала.</summary>
	[JsonPropertyName("execId")]
	public string ExecId { get; init; } = string.Empty;

	/// <summary>Цена исполнения.</summary>
	[JsonPropertyName("execPrice")]
	public decimal? ExecPrice { get; init; }

	/// <summary>Исполненное количество.</summary>
	[JsonPropertyName("execQty")]
	public decimal? ExecQty { get; init; }

	/// <summary>Тип исполнения: Trade, Delivery, Settle и другие значения биржи.</summary>
	[JsonPropertyName("execType")]
	public string ExecType { get; init; } = string.Empty;

	/// <summary>Стоимость исполнения.</summary>
	[JsonPropertyName("execValue")]
	public decimal? ExecValue { get; init; }

	/// <summary>Время исполнения, мс с эпохи Unix.</summary>
	[JsonPropertyName("execTime")]
	public long ExecTimeMs { get; init; }

	/// <summary>Валюта комиссии исполнения.</summary>
	[JsonPropertyName("feeCurrency")]
	public string? FeeCurrency { get; init; }

	/// <summary>Признак мейкерского исполнения.</summary>
	[JsonPropertyName("isMaker")]
	public bool IsMaker { get; init; }

	/// <summary>Ставка комиссии исполнения.</summary>
	[JsonPropertyName("feeRate")]
	public decimal? FeeRate { get; init; }

	/// <summary>Маркировочная цена на момент исполнения.</summary>
	[JsonPropertyName("markPrice")]
	public decimal? MarkPrice { get; init; }

	/// <summary>Идентификатор блочной сделки.</summary>
	[JsonPropertyName("blockTradeId")]
	public string? BlockTradeId { get; init; }

	/// <summary>Закрытая этим исполнением часть позиции.</summary>
	[JsonPropertyName("closedSize")]
	public decimal? ClosedSize { get; init; }

	/// <summary>Порядковый номер биржи; уникальность — пара seq + symbol.</summary>
	[JsonPropertyName("seq")]
	public long? Seq { get; init; }

	#region Поля опционов

	/// <summary>Подразумеваемая волатильность сделки; только для категории option.</summary>
	[JsonPropertyName("tradeIv")]
	public decimal? TradeIv { get; init; }

	/// <summary>Подразумеваемая волатильность марки; только для категории option.</summary>
	[JsonPropertyName("markIv")]
	public decimal? MarkIv { get; init; }

	/// <summary>Цена индекса на момент исполнения; только для категории option.</summary>
	[JsonPropertyName("indexPrice")]
	public decimal? IndexPrice { get; init; }

	/// <summary>Цена базового актива; только для категории option.</summary>
	[JsonPropertyName("underlyingPrice")]
	public decimal? UnderlyingPrice { get; init; }

	#endregion
}
