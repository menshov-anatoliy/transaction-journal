namespace TransactionJournal.Analytics;

/// <summary>
/// Запись единого хронологического потока позиции — вход движка FIFO: сделка
/// (количество, цена, комиссия) либо закрывающая запись с эффективной ценой
/// (delivery по внутренней стоимости, OTM-экспирация по нулю, ручная пометка
/// по цене пользователя). Граница funding-начислений проходит на этом входе:
/// у потока нет вида записи для funding, поэтому funding в результат конструкции
/// не попадает вовсе — фильтровать внутри движка нечего.
// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
/// </summary>
public sealed record PositionFifoEntry
{
	/// <summary>Момент записи: время исполнения сделки либо время закрывающей записи.</summary>
	public required DateTimeOffset At { get; init; }

	/// <summary>Вид записи: сделка, биржевая закрывающая запись экспирации либо ручная пометка.</summary>
	public required PositionFifoEntryKind Kind { get; init; }

	/// <summary>Ключ источника: execId сделки, «symbol|deliveryTimeMs» биржевой записи либо «manual:{id}» пометки.</summary>
	public required string SourceKey { get; init; }

	/// <summary>Знаковое количество: покупка положительна, продажа и закрывающая запись остатка отрицательны.</summary>
	public required decimal Quantity { get; init; }

	/// <summary>Цена образования части: цена исполнения сделки либо эффективная цена закрывающей записи.</summary>
	public required decimal Price { get; init; }

	/// <summary>
	/// Комиссия записи со знаком уменьшения результата: положительная уплачена,
	/// отрицательная — rebate. Берётся из записи исполнения либо из доли комиссии
	/// delivery-записи; величины в USDC уже приведены к USDT паритетом 1:1.
	// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
	/// </summary>
	public decimal Fee { get; init; }
}
