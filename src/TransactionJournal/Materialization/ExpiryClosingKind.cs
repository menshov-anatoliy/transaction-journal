namespace TransactionJournal.Materialization;

/// <summary>
/// Вид закрывающей записи экспирации: delivery ITM-опциона из биржевой записи
/// либо автоматическое закрытие OTM-экспирации без биржевой записи.
// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
/// </summary>
public enum ExpiryClosingKind
{
	/// <summary>Закрытие delivery ITM-опциона: аккаунтовая delivery-запись биржи, делённая по остаткам конструкций.</summary>
	Delivery,

	/// <summary>Автоматическое закрытие OTM-экспирации по нулевой цене в deliveryTime инструмента.</summary>
	OtmExpiry,
}
