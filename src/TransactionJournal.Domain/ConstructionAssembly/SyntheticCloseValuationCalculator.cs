using TransactionJournal.Domain.Analytics;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Расчёт величины «зафиксировано синтетикой» — паритетного лока связки
/// к экспирации: количество × |страйк − цена линейной сделки| + премия
/// встречного опциона − FIFO-стоимость входа закрываемой части ноги −
/// (комиссии обеих сделок связки + FIFO-доля комиссий входа ноги).
/// Внутренняя стоимость закрывается линейной ногой, временная — премией
/// встречного опциона, поэтому на экспирации платёж закрытой части не зависит
/// от цены базового актива; расчёт марок не читает вовсе, и сбой котировок
/// касается только нереализованных метрик. Стоимость входа и доля комиссий
/// выводятся FIFO-примитивами аналитики из сделок ноги: средняя цена входа —
/// количество-взвешенная средняя открывающих частей, доля комиссий — та же
/// средняя, взятая по удельной комиссии каждой сделки. Калькулятор чистый:
/// состояния между вызовами не хранит.
/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-locked-value
/// Traceability: change:synthetic-close-logical-layer/design#d4
/// Traceability: adr:docs/adr/0014-synthetic-close-logical-layer.md#consequences
/// </summary>
public static class SyntheticCloseValuationCalculator
{
	/// <summary>
	/// Вычисляет величину лока связки к экспирации. Комиссии входят со знаком
	/// уменьшения результата, как и в FIFO-движке: уплаченная снижает лок,
	/// rebate повышает.
	/// </summary>
	/// <param name="link">Распознанная связка: количество, страйк закрываемой ноги.</param>
	/// <param name="valuation">Цены и комиссии сделок связки и поток сделок входа закрываемой ноги.</param>
	/// <returns>Величина «зафиксировано синтетикой» в USDT.</returns>
	/// <exception cref="ArgumentNullException">Связка, ценовые данные или поток ноги не заданы.</exception>
	/// <exception cref="ArgumentException">В потоке ноги нет сделок входа либо открывающих частей — стоимость входа не определена.</exception>
	// Формула лока: внутренняя стоимость + премия встречного опциона −
	// стоимость входа закрываемой части − комиссии связки и FIFO-доля комиссий
	// входа ноги.
	// Traceability: change:synthetic-close-logical-layer/design#d4
	public static decimal CalculateToExpiry(SyntheticCloseLink link, SyntheticCloseValuation valuation)
	{
		ArgumentNullException.ThrowIfNull(link);
		ArgumentNullException.ThrowIfNull(valuation);
		ArgumentNullException.ThrowIfNull(valuation.ClosedLegEntries);

		// Вход ноги — только сделки потока: пометки и закрывающие записи не
		// образуют стоимость входа, их комиссии в лок не входят.
		var entryTrades = valuation.ClosedLegEntries
			.Where(entry => entry.Kind == PositionFifoEntryKind.Trade)
			.ToList();
		if (entryTrades.Count == 0)
		{
			throw new ArgumentException(
				"В потоке закрываемой ноги нет сделок входа: стоимость входа закрываемой части не определена.",
				nameof(valuation));
		}

		var engine = new PositionFifoEngine();

		// Стоимость входа закрываемой части — количество-взвешенная средняя
		// цена открывающих частей ноги: тот же FIFO-примитив средней цены
		// входа, что и в метриках позиций.
		// Traceability: openspec:analytics/performance#scenario-position-average-entry-from-opening-parts
		var fifo = engine.Match(entryTrades);
		if (fifo.AverageEntryPrice is null)
		{
			throw new ArgumentException(
				"В потоке закрываемой ноги нет открывающих частей: стоимость входа закрываемой части не определена.",
				nameof(valuation));
		}

		// Доля комиссий входа ноги, приходящаяся на закрываемую часть: та же
		// FIFO-средняя по открывающим частям, взятая по удельной комиссии
		// каждой сделки; rebate уменьшает долю, уплаченная комиссия увеличивает.
		var feeFifo = engine.Match(entryTrades
			.Select(trade => trade with
			{
				Price = trade.Quantity == 0m ? 0m : trade.Fee / Math.Abs(trade.Quantity),
				Fee = 0m,
			})
			.ToList());
		var entryFeePerUnit = feeFifo.AverageEntryPrice.GetValueOrDefault();

		var quantity = link.Quantity;
		return quantity * Math.Abs(link.Strike - valuation.LinearPrice)
			+ quantity * valuation.CounterOptionPrice
			- quantity * fifo.AverageEntryPrice.Value
			- (valuation.LinearFee + valuation.CounterOptionFee + quantity * entryFeePerUnit);
	}
}
