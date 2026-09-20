namespace TransactionJournal.Materialization;

/// <summary>
/// Предупреждение сверки экспирации с биржей: аккаунтовый deliveryRpl расходится
/// с собственным расчётом результата журнала по закрывающим записям. Предупреждение
/// не блокирует синхронизацию и не подменяет собственный расчёт — собственный результат
/// журнала всегда считается из собственных записей.
// Traceability: openspec:sync/bybit-history#scenario-delivery-reconciliation-warning
// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
/// </summary>
public sealed record ExpiryReconciliationWarning
{
	/// <summary>Инструмент доставки — символ опциона.</summary>
	public required string Symbol { get; init; }

	/// <summary>Момент доставки биржевой записи.</summary>
	public required DateTimeOffset DeliveryTime { get; init; }

	/// <summary>Биржевой реализованный PnL доставки (deliveryRpl) — величина сверки, а не результат журнала.</summary>
	public required decimal DeliveryRpl { get; init; }

	/// <summary>Собственный расчёт результата журнала по сделкам и закрывающим записям инструмента.</summary>
	public required decimal OwnResult { get; init; }

	/// <summary>Расхождение сверки: deliveryRpl минус собственный расчёт.</summary>
	public required decimal Difference { get; init; }

	/// <summary>Ключ источника «symbol|deliveryTimeMs» биржевой delivery-записи.</summary>
	public required string SourceKey { get; init; }
}
