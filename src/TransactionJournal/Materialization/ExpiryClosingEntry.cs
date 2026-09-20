namespace TransactionJournal.Materialization;

/// <summary>
/// Закрывающая запись экспирации опциона, выведенная из сырых записей синхронизации:
/// количество обнуляет журнальный остаток, эффективная цена равна внутренней стоимости
/// на расчётной цене экспирации для delivery ITM-опциона либо нулю для OTM-экспирации.
/// Запись производна и пересчитывается при чтении из сырья и текущих привязок сделок.
// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
/// </summary>
public sealed record ExpiryClosingEntry
{
	/// <summary>
	/// Идентификатор конструкции, к которой отнесена запись; null — доля остатка
	/// непривязанных сделок «Входящих». Привязка сделок остаётся ручным действием,
	/// поэтому состав записей пересчитывается при перепривязке.
	/// </summary>
	public required string? ConstructionId { get; init; }

	/// <summary>Инструмент закрывающей записи — символ опциона, например BTC-29DEC23-45000-C.</summary>
	public required string Symbol { get; init; }

	/// <summary>Вид записи: delivery ITM-опциона или автоматическое OTM-закрытие.</summary>
	public required ExpiryClosingKind Kind { get; init; }

	/// <summary>Момент закрытия: deliveryTime биржевой записи либо канонический deliveryTime инструмента для OTM.</summary>
	public required DateTimeOffset ClosedAt { get; init; }

	/// <summary>
	/// Знаковое количество, обнуляющее остаток конструкции по инструменту:
	/// противоположно знаку закрываемого остатка.
	/// </summary>
	public required decimal Quantity { get; init; }

	/// <summary>
	/// Эффективная цена закрытия: внутренняя стоимость max(0, deliveryPrice − strike) для колла,
	/// max(0, strike − deliveryPrice) для пута либо ноль при OTM-экспирации.
	/// </summary>
	public required decimal EffectivePrice { get; init; }

	/// <summary>
	/// Доля комиссии delivery аккаунтовой записи, пропорциональная остатку конструкции;
	/// валюта — USDC в паритете 1:1 к USDT. Ноль у OTM-закрытия.
	// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
	/// </summary>
	public required decimal Fee { get; init; }

	/// <summary>Ключ источника «symbol|deliveryTimeMs» биржевой delivery-записи либо инструмента для OTM.</summary>
	public required string SourceKey { get; init; }
}
