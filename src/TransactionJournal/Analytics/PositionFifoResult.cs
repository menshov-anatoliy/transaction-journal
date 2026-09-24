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

	/// <summary>
	/// Средняя цена входа — количество-взвешенная цена всех открывающих частей
	/// FIFO-потока (частей, добавившихся в слои), включая впоследствии закрытые;
	/// null, если открывающих частей нет. Это цена, по которой входили, а не цена
	/// текущего остатка: AverageOpenPrice остаётся базой нереализованной оценки.
	// Traceability: openspec:analytics/performance#scenario-position-average-entry-from-opening-parts
	/// </summary>
	public required decimal? AverageEntryPrice { get; init; }

	/// <summary>
	/// Средняя цена закрытия — количество-взвешенная эффективная цена всех
	/// закрывающих частей потока (частей, сопоставленных слоям); null, если
	/// закрывающих частей нет — позиция ещё не закрывалась.
	// Traceability: openspec:analytics/performance#scenario-position-average-close-from-closing-parts
	/// </summary>
	public required decimal? AverageClosePrice { get; init; }

	/// <summary>Реализованный PnL: FIFO-результат встречных частей, уменьшенный на комиссии всех записей потока.</summary>
	public required decimal RealizedPnL { get; init; }

	/// <summary>Накопленные комиссии потока: уплаченные складываются, rebate снижает сумму.</summary>
	public required decimal AccumulatedFees { get; init; }
}
