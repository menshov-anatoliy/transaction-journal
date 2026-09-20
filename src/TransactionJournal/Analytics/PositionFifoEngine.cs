namespace TransactionJournal.Analytics;

/// <summary>
/// Движок FIFO результата позиции: сопоставляет встречные части единого
/// хронологического потока записей — сделок и закрывающих записей с эффективными
/// ценами, — накапливает реализованный PnL и комиссии, возвращает непокрытый
/// остаток и его среднюю цену. Движок чистый: хранилище не читает, результатов
/// не пишет, поэтому любое изменение входных записей отражается очередным
/// вызовом без следов прежнего расчёта.
// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
// Traceability: change:add-analytics/design#d1
/// </summary>
public sealed class PositionFifoEngine
{
	#region Сопоставление

	/// <summary>
	/// Упорядочивает записи потока позиции в единую хронологию: по моменту,
	/// рангу вида и ключу источника. Порядок публичен, чтобы читающие слои
	/// обходили ту же хронологию, что и сопоставление, — например, для вывода
	/// дат позиции из её записей.
	/// </summary>
	/// <param name="entries">Записи потока позиции: сделки и закрывающие записи.</param>
	/// <returns>Записи в порядке единой хронологии.</returns>
	/// <exception cref="ArgumentNullException">Записи не заданы.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Вид записи потока неизвестен.</exception>
	public static List<PositionFifoEntry> OrderByTimeline(IEnumerable<PositionFifoEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		return entries
			.OrderBy(entry => entry.At)
			.ThenBy(entry => RankOf(entry.Kind))
			.ThenBy(entry => entry.SourceKey, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>
	/// Сопоставляет записи потока позиции по FIFO: каждая встречная запись закрывает
	/// старейшие ещё не закрытые части в порядке хронологии, комиссии записей
	/// уменьшают результат, непокрытые части образуют остаток со средней ценой.
	/// Порядок входной коллекции не важен: движок упорядочивает записи по моменту,
	/// рангу вида и ключу источника — те же правила устойчивости хронологии,
	/// что и у read-модели позиций.
	/// </summary>
	/// <param name="entries">Записи потока позиции: сделки и закрывающие записи.</param>
	/// <returns>Результат сопоставления: остаток, средняя цена, реализованный PnL и комиссии.</returns>
	/// <exception cref="ArgumentNullException">Записи не заданы.</exception>
	/// <exception cref="ArgumentException">Ключ источника записи не задан или пуст.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Вид записи потока неизвестен.</exception>
	public PositionFifoResult Match(IEnumerable<PositionFifoEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		var ordered = OrderByTimeline(entries);

		var layers = new Queue<FifoLayer>();
		var matchedPnL = 0m;
		var fees = 0m;
		foreach (var entry in ordered)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(entry.SourceKey);

			// Комиссия входит в результат со знаком уменьшения: уплаченная снижает PnL,
			// rebate повышает; USDC-величины приведены к USDT паритетом 1:1 ещё
			// материализатором сделок, движок получает уже нормализованное число.
			// Traceability: openspec:analytics/performance#scenario-fees-reduce-result
			// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
			fees += entry.Fee;

			var remaining = entry.Quantity;
			while (remaining != 0m && layers.Count > 0 && Math.Sign(layers.Peek().Quantity) != Math.Sign(remaining))
			{
				// Встречная часть закрывает старейший открытый слой: реализованный PnL —
				// разница цен на величину сопоставленной части с направлением слоя
				// (длинный слой закрыт продажей дороже, короткий — выкупом дешевле).
				// Traceability: openspec:analytics/performance#scenario-fifo-matches-chronologically
				var layer = layers.Peek();
				var direction = Math.Sign(layer.Quantity);
				var matched = Math.Min(Math.Abs(remaining), Math.Abs(layer.Quantity));
				matchedPnL += (entry.Price - layer.Price) * matched * direction;
				layer.Quantity -= direction * matched;
				remaining -= Math.Sign(remaining) * matched;
				if (layer.Quantity == 0m)
				{
					layers.Dequeue();
				}
			}

			if (remaining != 0m)
			{
				layers.Enqueue(new FifoLayer(remaining, entry.Price));
			}
		}

		// Остаток образуют только непокрытые части; средняя цена — количество-взвешенная
		// цена этих слоёв, у закрытой позиции средней цены нет.
		var residual = 0m;
		var weightedPrice = 0m;
		var absoluteQuantity = 0m;
		foreach (var layer in layers)
		{
			residual += layer.Quantity;
			weightedPrice += Math.Abs(layer.Quantity) * layer.Price;
			absoluteQuantity += Math.Abs(layer.Quantity);
		}

		return new PositionFifoResult
		{
			Residual = residual,
			AverageOpenPrice = residual == 0m ? null : weightedPrice / absoluteQuantity,
			RealizedPnL = matchedPnL - fees,
			AccumulatedFees = fees,
		};
	}

	#endregion

	#region Помощники

	/// <summary>Ранг вида записи в хронологии: сделка раньше биржевой записи, биржевая — раньше ручной пометки.</summary>
	private static int RankOf(PositionFifoEntryKind kind) => kind switch
	{
		PositionFifoEntryKind.Trade => 0,
		PositionFifoEntryKind.ExpiryClosing => 1,
		PositionFifoEntryKind.ManualMark => 2,
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид записи потока позиции."),
	};

	/// <summary>Открытый FIFO-слой: непокрытое знаковое количество и цена его образования.</summary>
	private sealed class FifoLayer
	{
		/// <summary>Создаёт слой из знакового количества и цены образования.</summary>
		public FifoLayer(decimal quantity, decimal price)
		{
			Quantity = quantity;
			Price = price;
		}

		/// <summary>Непокрытое знаковое количество слоя; уменьшается встречными частями.</summary>
		public decimal Quantity { get; set; }

		/// <summary>Цена образования слоя: цена исполнения либо эффективная цена закрывающей записи.</summary>
		public decimal Price { get; }
	}

	#endregion
}
