namespace TransactionJournal.Materialization;

/// <summary>
/// Сделка «Входящих», выведенная из сырой записи исполнения: атрибуты соответствуют
/// биржевой записи — время, инструмент, знаковое количество по стороне исполнения,
/// цена исполнения, комиссия со знаком и её валюта. USDC-величины приведены к USDT
/// в паритете 1:1, потому что журнал ведёт учёт в одной валюте.
// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
/// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
/// </summary>
public sealed record MaterializedTrade
{
	/// <summary>Биржевой идентификатор исполнения — ключ сделки и залог идемпотентности проекции.</summary>
	public required string ExecId { get; init; }

	/// <summary>Торговая категория записи: linear или option.</summary>
	public required string Category { get; init; }

	/// <summary>Инструмент сделки, например BTC-29DEC23-45000-C или BTCUSDT.</summary>
	public required string Symbol { get; init; }

	/// <summary>Время исполнения сделки.</summary>
	public required DateTimeOffset ExecutedAt { get; init; }

	/// <summary>Знаковое количество: покупка положительна, продажа отрицательна.</summary>
	public required decimal Quantity { get; init; }

	/// <summary>Цена исполнения из биржевой записи (execPrice).</summary>
	public required decimal Price { get; init; }

	/// <summary>Комиссия со знаком биржевой записи (execFee): положительная уплачена, отрицательная — rebate.</summary>
	public required decimal Fee { get; init; }

	/// <summary>Валюта комиссии; USDC приведена к USDT по ADR-0001, null — биржа валюту не указала.</summary>
	public string? FeeCurrency { get; init; }

	/// <summary>Признак мейкерского исполнения.</summary>
	public required bool IsMaker { get; init; }

	/// <summary>Канонические атрибуты опциона из справочника инструментов; null у линейных инструментов.</summary>
	public TradeOptionAttributes? Option { get; init; }
}
