namespace TransactionJournal.Application.Materialization;

/// <summary>
/// Сделка «Входящих», выведенная из сырья журнала: атрибуты соответствуют
/// исходной записи — время, инструмент, знаковое количество по стороне,
/// цена, комиссия со знаком и её валюта. USDC-величины приведены к USDT
/// в паритете 1:1, потому что журнал ведёт учёт в одной валюте. Сделки обоих
/// источников — биржевые и ручные — материализуются в одну проекцию и различаются
/// признаком источника; у ручных сделок execId отсутствует.
// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
/// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
/// </summary>
public sealed record MaterializedTrade
{
	/// <summary>
	/// Биржевой идентификатор исполнения — ключ сделки и залог идемпотентности
	/// проекции; у ручных сделок отсутствует, их идентичность несёт <see cref="ManualTradeId"/>.
	/// </summary>
	public string? ExecId { get; init; }

	/// <summary>Источник сделки: биржа или ручной ввод.</summary>
	// Признак источника берётся из сырья и остаётся в проекции: списки записей
	// различают биржевые и ручные записи бейджем источника.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
	public required TradeSource Source { get; init; }

	/// <summary>
	/// Идентификатор строки сырья ручной сделки — ключ её идентичности в проекции;
	/// у биржевых сделок отсутствует.
	/// </summary>
	public long? ManualTradeId { get; init; }

	/// <summary>
	/// Привязка ручной сделки к конструкции из её сырья; null — сделка во «Входящих».
	/// У биржевых сделок привязка живёт в пользовательских данных и здесь не дублируется.
	/// </summary>
	public long? ConstructionId { get; init; }

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
