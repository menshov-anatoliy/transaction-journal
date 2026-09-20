namespace TransactionJournal.Analytics;

/// <summary>
/// Калькулятор метрик позиции: поверх результата FIFO-движка выводит метрики
/// позиции «конструкция × инструмент» — чистый остаток, реализованный PnL,
/// накопленные комиссии, среднюю цену открытого остатка и временные
/// характеристики: дату открытия, дату закрытия и длительность. Калькулятор
/// чистый: хранилище не читает, результатов не пишет, каждая метрика — функция
/// переданного набора записей, поэтому правка состава записей (перенос сделок,
/// пометок) отражается очередным вызовом без следов прежнего расчёта.
// Traceability: openspec:analytics/performance#requirement-position-metrics
// Traceability: change:add-analytics/design#d1
// Traceability: change:add-analytics/design#d5
/// </summary>
public sealed class PositionMetricsCalculator
{
	private readonly PositionFifoEngine _engine = new();

	/// <summary>
	/// Вычисляет метрики позиции из её записей: агрегаты — движком FIFO по тому же
	/// набору, даты — проходом по единой хронологии движка. Дата открытия — время
	/// первой записи позиции; дата закрытия — момент последнего обнуления остатка,
	/// и пока после обнуления позиция переоткрыта, датой закрытия становится только
	/// следующее обнуление. Закрытая позиция не имеет нереализованной части: её
	/// нереализованный PnL равен нулю и марок не требует, средняя цена и марка не
	/// вычисляются; нереализованная оценка открытого остатка марками подключается
	/// слоем марок при запросе и до этого остаётся null.
	/// </summary>
	/// <param name="constructionId">Конструкция, которой принадлежит позиция.</param>
	/// <param name="symbol">Инструмент позиции.</param>
	/// <param name="entries">Записи потока позиции: сделки и закрывающие записи.</param>
	/// <returns>Метрики позиции.</returns>
	/// <exception cref="ArgumentNullException">Записи не заданы.</exception>
	/// <exception cref="ArgumentException">Поток записей пуст — у позиции нет записей, либо ключ источника записи не задан или пуст.</exception>
	public PositionMetrics Calculate(long constructionId, string symbol, IEnumerable<PositionFifoEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		var ordered = PositionFifoEngine.OrderByTimeline(entries);
		if (ordered.Count == 0)
		{
			// Позиция существует только своими записями: пустой поток означал бы
			// метрики позиции, которой нет, — это повреждение входных данных.
			throw new ArgumentException("Поток записей позиции пуст: метрики выводятся из записей позиции.", nameof(entries));
		}

		var fifo = _engine.Match(ordered);

		// Даты выводятся проходом по той же хронологии: открытие — время первой
		// записи, закрытие — последнее обнуление накопленного остатка. Обнуления
		// после переоткрытия перезаписывают дату закрытия, поэтому метрики всегда
		// отражают текущий состав записей.
		// Traceability: openspec:analytics/performance#scenario-reopen-updates-dates
		var openedAt = ordered[0].At;
		var cumulative = 0m;
		DateTimeOffset? closedAt = null;
		foreach (var entry in ordered)
		{
			cumulative += entry.Quantity;
			if (cumulative == 0m)
			{
				closedAt = entry.At;
			}
		}

		// Позиция открыта, пока остаток не нулевой: у открытой нет даты закрытия
		// и длительности, у закрытой нет нереализованной части, средней цены и марки.
		// Traceability: openspec:analytics/performance#scenario-closed-position-has-no-unrealized
		var isOpen = fifo.Residual != 0m;
		return new PositionMetrics
		{
			ConstructionId = constructionId,
			Symbol = symbol,
			Residual = fifo.Residual,
			RealizedPnL = fifo.RealizedPnL,
			AccumulatedFees = fifo.AccumulatedFees,
			AverageOpenPrice = fifo.AverageOpenPrice,
			MarkPrice = null,
			UnrealizedPnL = isOpen ? null : 0m,
			OpenedAt = openedAt,
			ClosedAt = isOpen ? null : closedAt,
			Duration = isOpen ? null : closedAt - openedAt,
		};
	}
}
