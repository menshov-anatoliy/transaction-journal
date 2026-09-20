namespace TransactionJournal.Analytics;

/// <summary>
/// Результат сопоставления FIFO по потоку записей позиции: непокрытый остаток
/// со средней ценой открытых слоёв, реализованный PnL и накопленные комиссии.
/// Результат производен и живёт одно чтение — он вычисляется из текущего набора
/// записей при каждом вызове, хранимых итогов движок не оставляет.
// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
// Traceability: change:add-analytics/design#d1
/// </summary>
public sealed record PositionFifoResult
{
	/// <summary>Непокрытый остаток: положителен для длинной позиции, отрицателен для короткой; ноль — позиция закрыта.</summary>
	public required decimal Residual { get; init; }

	/// <summary>Средняя цена открытого остатка — количество-взвешенная цена непокрытых FIFO-слоёв; null у закрытой позиции.</summary>
	public required decimal? AverageOpenPrice { get; init; }

	/// <summary>Реализованный PnL: FIFO-результат встречных частей, уменьшенный на комиссии всех записей потока.</summary>
	public required decimal RealizedPnL { get; init; }

	/// <summary>Накопленные комиссии потока: уплаченные складываются, rebate снижает сумму.</summary>
	public required decimal AccumulatedFees { get; init; }
}
