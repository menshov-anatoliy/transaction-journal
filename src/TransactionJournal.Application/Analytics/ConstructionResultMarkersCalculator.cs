namespace TransactionJournal.Application.Analytics;

using System.Globalization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Materialization;

/// <summary>
/// Вычислитель маркеров изменения результата конструкции: строит хронологию
/// операций с вкладами в накопленный зафиксированный денежный результат и
/// применяет порог проявления со сквозной нумерацией. Операции: окно
/// опционных исполнений по правилам сборки v5 — сумма знаковых денежных
/// потоков его сделок (ролл целиком одна операция, вид Открытие/Закрытие/Ролл
/// выводится из состава сделок по живому остатку), активация связки
/// синтетического закрытия — лок «зафиксировано синтетикой», снятие спором —
/// тот же лок с обратным знаком, закрытие фьючерсной позиции до нулевого
/// остатка — прирост реализованного PnL цикла по FIFO с комиссиями,
/// закрывающая запись — количество × эффективная цена в соглашении денежного
/// потока, внешняя корректировка PnL — сумма. Операции с нулевым по факту
/// вкладом события не создают: OTM-экспирация по нулевой цене и пометка без
/// эффективной цены исчезают из истории. Значение маркера — префиксная сумма
/// вкладов операций до события включительно: котировок вычислитель не читает,
/// нереализованная часть открытых остатков в историю не входит, а незакрытая
/// фьючерсная позиция не входит ни в один маркер. Вычислитель чистый:
/// хранилище не трогает, история пересчитывается при каждом чтении — спор
/// связки задним числом перестраивает все последующие маркеры.
/// </summary>
/// <remarks>
/// Вычислитель публикует уже выведенные маркеры с перечнями операций
/// расшифровки: порог проявления и перенумерация применены до контрактов,
/// UI фильтрацию не повторяет.
// Traceability: openspec:analytics/performance#requirement-result-markers-history-events
// Traceability: openspec:analytics/performance#requirement-result-marker-value
// Traceability: change:fix-finresult-marker-contributions/design#d1
/// </remarks>
public sealed class ConstructionResultMarkersCalculator
{
	/// <summary>Доля планового риска, образующая порог проявления промежуточного маркера.</summary>
	private const decimal DisplayThresholdFraction = 0.05m;

	/// <summary>Ранг сделки в хронологии позиции: сделка раньше закрывающих записей того же момента.</summary>
	private const int TradeRank = 0;

	/// <summary>Ранг биржевой закрывающей записи в хронологии позиции.</summary>
	private const int ExpiryRank = 1;

	/// <summary>Ранг ручной пометки в хронологии позиции.</summary>
	private const int ManualAnnotationRank = 2;

	/// <summary>Ранг «конец момента» для операций связок и корректировок.</summary>
	private const int EndOfMomentRank = 3;

	private readonly PositionFifoEngine _fifoEngine = new();

	#region Вычисление истории

	/// <summary>
	/// Вычисляет историю маркеров изменения результата конструкции. Первое
	/// событие даёт первый маркер; последний выводится безусловно и равен
	/// сумме вкладов всех операций; промежуточные выводятся только при
	/// отличии от последнего выведенного не менее чем на 5% планового риска —
	/// при незаданном или нулевом плановом риске порога нет и выводятся все
	/// события. Номера присваиваются только выведенным маркерам, подряд без
	/// пропусков; подавленные события в нумерацию не попадают, но входят
	/// в перечень операций следующего выведенного маркера. У конструкции
	/// без событий маркеров нет: пустая история — обычное состояние.
	/// </summary>
	/// <param name="input">Материал истории: потоки позиций, сделки, закрывающие записи, связки, корректировки, плановый риск.</param>
	/// <returns>История выведенных маркеров с финальной нумерацией и перечнями операций расшифровки.</returns>
	/// <exception cref="ArgumentNullException">Вход не задан.</exception>
	/// <remarks>
	/// Порядок операций — время записи, при равенстве — ранг вида записи и
	/// стабильный ключ источника; первое и последнее события выводятся
	/// безусловно.
	// Traceability: openspec:analytics/performance#requirement-result-marker-threshold
	/// </remarks>
	public ConstructionResultMarkers Calculate(ResultMarkersInput input)
	{
		ArgumentNullException.ThrowIfNull(input);

		var operations = BuildOperations(input);

		// Порог проявления — 5% планового риска: незаданный или нулевой риск
		// базы порога не образует, все события становятся маркерами.
		// Traceability: openspec:analytics/performance#scenario-marker-no-threshold-without-planned-risk
		decimal? threshold = input.PlannedRiskUsdt is > 0m
			? DisplayThresholdFraction * input.PlannedRiskUsdt.Value
			: null;

		var markers = new List<ResultMarker>(operations.Count);
		var pendingOperations = new List<ResultMarkerOperation>();
		decimal cumulativeTotal = 0m;
		decimal? lastDisplayedTotal = null;
		for (var index = 0; index < operations.Count; index++)
		{
			var operation = operations[index];

			// Значение маркера — префиксная сумма вкладов операций до события
			// включительно; операция, подавленная порогом, ждёт в перечне
			// операций следующего выведенного маркера.
			// Traceability: openspec:analytics/performance#requirement-result-marker-value
			// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
			cumulativeTotal += operation.AmountUsdt;
			pendingOperations.Add(new ResultMarkerOperation
			{
				Kind = operation.Kind,
				EventTime = operation.Time,
				AmountUsdt = operation.AmountUsdt,
			});

			// Первое событие и последнее выводятся безусловно; промежуточное —
			// только при отличии от последнего выведенного не менее порога.
			// Traceability: openspec:analytics/performance#scenario-marker-first-last-unconditional
			// Traceability: openspec:analytics/performance#scenario-marker-below-threshold-suppressed
			var isFirst = markers.Count == 0;
			var isLast = index == operations.Count - 1;
			if (isFirst == false
				&& isLast == false
				&& threshold is { } limit
				&& Math.Abs(cumulativeTotal - lastDisplayedTotal.GetValueOrDefault()) < limit)
			{
				continue;
			}

			// Нумерация сплошная по выведенным: подавленные события номеров
			// не получают и пропусков в последовательности не создают.
			// Traceability: openspec:analytics/performance#scenario-marker-numbering-displayed-only
			markers.Add(new ResultMarker
			{
				Number = markers.Count + 1,
				EventTime = operation.Time,
				Kind = operation.Kind,
				ChangeUsdt = cumulativeTotal - (lastDisplayedTotal ?? 0m),
				TotalUsdt = cumulativeTotal,
				Operations = pendingOperations.ToArray(),
			});
			pendingOperations = [];
			lastDisplayedTotal = cumulativeTotal;
		}

		return new ConstructionResultMarkers { Markers = markers };
	}

	#endregion

	#region Хронология операций

	/// <summary>Операция истории конструкции: момент, ключ стабильного порядка, вид и знаковый вклад.</summary>
	private sealed record MarkerOperation(DateTimeOffset Time, string SortKey, ResultMarkerKind Kind, decimal AmountUsdt);

	/// <summary>
	/// Строит хронологию операций конструкции с вкладами: окна опционных
	/// исполнений по правилам сборки v5, закрытия фьючерсных циклов, границы
	/// интервалов связок, корректировки и закрывающие записи. Операции
	/// с нулевым по факту вкладом события не создают вовсе — это не подавление
	/// порогом, а отсутствие события в истории. Порядок — по времени операции,
	/// при равенстве — по рангу вида записи и стабильному ключу источника:
	/// перечитывание даёт ту же хронологию.
	/// </summary>
	// Traceability: openspec:analytics/performance#requirement-result-markers-history-events
	// Traceability: change:fix-finresult-marker-contributions/design#d1
	private List<MarkerOperation> BuildOperations(ResultMarkersInput input)
	{
		var operations = new List<MarkerOperation>();
		var linearSymbols = LinearSymbols(input.Trades);
		AddOptionWindowOperations(input, operations);
		AddFuturesCycleOperations(input, linearSymbols, operations);
		AddLinkOperations(input.Links, operations);
		AddAdjustmentOperations(input.Adjustments, operations);
		AddClosingEntryOperations(input.ClosingEntries, linearSymbols, operations);
		return operations
			.Where(candidate => candidate.AmountUsdt != 0m)
			.OrderBy(candidate => candidate.Time)
			.ThenBy(candidate => candidate.SortKey, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>Множество фьючерсных символов конструкции: их закрывающие записи живут в циклах FIFO, а не отдельными вкладами.</summary>
	private static HashSet<string> LinearSymbols(IReadOnlyList<MaterializedTrade> trades)
	{
		var symbols = new HashSet<string>(StringComparer.Ordinal);
		foreach (var trade in trades)
		{
			if (string.Equals(trade.Category, ResultMarkerJournalMaterial.LinearCategory, StringComparison.Ordinal))
			{
				symbols.Add(trade.Symbol);
			}
		}

		return symbols;
	}

	/// <summary>
	/// Добавляет операции окон опционных исполнений: вклад окна — сумма
	/// знаковых денежных потоков его сделок (покупка — расход, продажа —
	/// приход, комиссия — расход). Вид окна выводится из состава сделок
	/// по живому остатку: без закрывающих — Открытие, с закрывающими без
	/// открывающих — Закрытие, с закрывающими и открывающими — Ролл.
	/// Время операции — момент последнего исполнения окна, как в правилах
	/// сборки v5: ролл, усреднение и открытие целиком живут в одном окне.
	/// </summary>
	// Классификация окон переиспользует правило IsClosingTrade сборщика
	// конструкций: сделка против ненулевого живого остатка аккаунта —
	// закрывающая; правило объявлено один раз и не дублируется.
	// Traceability: openspec:analytics/performance#scenario-first-window-gives-first-marker
	// Traceability: openspec:analytics/performance#scenario-roll-window-single-marker
	// Traceability: change:fix-finresult-marker-contributions/design#d3
	private static void AddOptionWindowOperations(ResultMarkersInput input, List<MarkerOperation> operations)
	{
		var cashFlowByTradeKey = CashFlowByTradeKey(input.Trades);
		var closingByTradeKey = ClosingTradesByLiveResidual(input);
		foreach (var window in ResultMarkerJournalMaterial.BuildOptionWindows(input.Trades, input.ClosingEntries))
		{
			decimal amount = 0m;
			var hasClosing = false;
			var hasOpening = false;
			foreach (var execution in window.Executions)
			{
				if (cashFlowByTradeKey.TryGetValue(execution.ExecId, out var cashFlow))
				{
					amount += cashFlow;
				}

				if (closingByTradeKey.GetValueOrDefault(execution.ExecId))
				{
					hasClosing = true;
				}
				else
				{
					hasOpening = true;
				}
			}

			// Нулевой по факту вклад окна события не создаёт.
			if (amount == 0m)
			{
				continue;
			}

			var last = window.Executions[^1];
			var time = DateTimeOffset.FromUnixTimeMilliseconds(last.ExecTimeMs);
			operations.Add(new MarkerOperation(
				time,
				$"{TradeRank:00}|window:{last.ExecId}",
				KindOfWindow(hasClosing, hasOpening),
				amount));
		}
	}

	/// <summary>Знаковые денежные потоки сделок по ключу сделки в потоке позиции.</summary>
	private static Dictionary<string, decimal> CashFlowByTradeKey(IReadOnlyList<MaterializedTrade> trades)
	{
		var byKey = new Dictionary<string, decimal>(StringComparer.Ordinal);
		foreach (var trade in trades)
		{
			byKey[ResultMarkerJournalMaterial.TradeKey(trade)] = TradeCashFlow(trade);
		}

		return byKey;
	}

	/// <summary>Знаковый денежный поток сделки: покупка — расход, продажа — приход, комиссия — расход.</summary>
	private static decimal TradeCashFlow(MaterializedTrade trade) => -(trade.Quantity * trade.Price) - trade.Fee;

	/// <summary>
	/// Классифицирует опционные сделки по живому остатку аккаунта: по единой
	/// хронологии сделок и закрывающих записей опционных символов копится
	/// остаток каждого символа, и сделка против ненулевого остатка —
	/// закрывающая. Закрывающие записи обнуляют остаток, поэтому повторное
	/// открытие символа после экспирации классифицируется как открывающее.
	/// </summary>
	// Правило классификации — то же, что у сборщика конструкций при flush
	// окна: переиспользуется IsClosingTrade, дубликат правила не заводится.
	// Traceability: change:fix-finresult-marker-contributions/design#d3
	private static Dictionary<string, bool> ClosingTradesByLiveResidual(ResultMarkersInput input)
	{
		var heldBySymbol = new Dictionary<string, decimal>(StringComparer.Ordinal);
		var closingByTradeKey = new Dictionary<string, bool>(StringComparer.Ordinal);
		var timeline = new List<(DateTimeOffset At, int Rank, string SourceKey, string Symbol, decimal Quantity, bool IsTrade)>();

		// В хронологии классификации участвуют только опционные сделки
		// с разбираемым символом — те же, что группируются в окна сборки;
		// фьючерсные сделки классифицируются правилами фьючерсных циклов.
		foreach (var trade in input.Trades)
		{
			if (string.Equals(trade.Category, ResultMarkerJournalMaterial.OptionCategory, StringComparison.Ordinal) == false
				|| OptionSymbolParser.TryParse(trade.Symbol, out var parts) == false
				|| parts is null)
			{
				continue;
			}

			timeline.Add((trade.ExecutedAt, TradeRank, ResultMarkerJournalMaterial.TradeKey(trade), trade.Symbol, trade.Quantity, true));
		}

		foreach (var closing in input.ClosingEntries)
		{
			if (OptionSymbolParser.TryParse(closing.Symbol, out var parts) == false || parts is null)
			{
				continue;
			}

			timeline.Add((closing.ClosedAt, RankOfClosingKind(closing.Kind), closing.SourceKey, closing.Symbol, closing.Quantity, false));
		}

		foreach (var item in timeline
			.OrderBy(item => item.At)
			.ThenBy(item => item.Rank)
			.ThenBy(item => item.SourceKey, StringComparer.Ordinal))
		{
			var held = heldBySymbol.GetValueOrDefault(item.Symbol);
			if (item.IsTrade)
			{
				closingByTradeKey[item.SourceKey] = ConstructionAssembler.IsClosingTrade(item.Quantity, held);
			}

			heldBySymbol[item.Symbol] = held + item.Quantity;
		}

		return closingByTradeKey;
	}

	/// <summary>
	/// Добавляет операции закрытий фьючерсных циклов: по каждому линейному
	/// символу хронология записей потока позиции прогоняется через FIFO, и
	/// момент, где остаток становится нулевым после ненулевого либо меняет
	/// знак (разворот через ноль — то же обнуление), образует операцию
	/// «Закрытие фьючерса» с вкладом — приростом кумулятивного реализованного
	/// PnL потока от предыдущего нуля, с комиссиями. Открывающие сделки и
	/// частичные закрытия событий не создают, а незакрытая позиция не входит
	/// ни в один маркер: незакрытая позиция не является зафиксированным
	/// результатом конструкции.
	/// </summary>
	// События считаются из тех же записей потока позиции, что дают
	// реализованный результат аналитики: комиссии входят в FIFO как сейчас.
	// Traceability: openspec:analytics/performance#scenario-futures-closed-trade-marker
	// Traceability: openspec:analytics/performance#scenario-futures-open-partial-no-marker
	// Traceability: openspec:analytics/performance#scenario-futures-open-position-not-counted
	// Traceability: change:fix-finresult-marker-contributions/design#d2
	private void AddFuturesCycleOperations(ResultMarkersInput input, HashSet<string> linearSymbols, List<MarkerOperation> operations)
	{
		foreach (var symbol in linearSymbols)
		{
			if (input.EntriesBySymbol.TryGetValue(symbol, out var entries) == false || entries.Count == 0)
			{
				continue;
			}

			AddFuturesCycleOperations(operations, PositionFifoEngine.OrderByTimeline(entries));
		}
	}

	/// <summary>Прогоняет хронологию записей одного инструмента через FIFO и добавляет операции обнулений остатка.</summary>
	private void AddFuturesCycleOperations(List<MarkerOperation> operations, IReadOnlyList<PositionFifoEntry> timeline)
	{
		decimal residual = 0m;
		decimal realizedAtLastZero = 0m;
		for (var count = 1; count <= timeline.Count; count++)
		{
			// Полный прогон движка по префиксу хронологии даёт остаток и
			// накопленный реализованный PnL префикса: сопоставление встречных
			// частей причинно, слои закрываются только частями того же потока.
			var fifo = _fifoEngine.Match(timeline.Take(count));
			var cycleClosed = residual != 0m
				&& (fifo.Residual == 0m || Math.Sign(fifo.Residual) != Math.Sign(residual));
			if (cycleClosed)
			{
				var entry = timeline[count - 1];
				operations.Add(new MarkerOperation(
					entry.At,
					$"{RankOf(entry.Kind):00}|futures:{entry.SourceKey}",
					ResultMarkerKind.FuturesClose,
					fifo.RealizedPnL - realizedAtLastZero));
				realizedAtLastZero = fifo.RealizedPnL;
			}

			residual = fifo.Residual;
		}
	}

	/// <summary>
	/// Добавляет операции границ интервалов связок: активация даёт вклад
	/// «зафиксировано синтетикой», снятие спором — тот же лок с обратным
	/// знаком. Реальное закрытие позиции связку автоматически не снимает —
	/// вычислитель потребляет готовые интервалы read-модели.
	/// </summary>
	// Traceability: openspec:analytics/performance#scenario-synthetic-link-change-marker
	// Traceability: openspec:analytics/performance#scenario-link-dispute-recomputes-history
	private static void AddLinkOperations(IReadOnlyList<ResultMarkerLinkInterval> links, List<MarkerOperation> operations)
	{
		foreach (var link in links)
		{
			operations.Add(new MarkerOperation(
				link.Since,
				$"{EndOfMomentRank:00}|link:{link.LinkId}",
				ResultMarkerKind.SyntheticClose,
				link.LockedValueUsdt));
			if (link.Until is { } until)
			{
				operations.Add(new MarkerOperation(
					until,
					$"{EndOfMomentRank:00}|dispute:{link.LinkId}",
					ResultMarkerKind.SyntheticRelease,
					-link.LockedValueUsdt));
			}
		}
	}

	/// <summary>Добавляет операции внешних корректировок PnL: вклад — знаковая сумма корректировки.</summary>
	// Traceability: openspec:analytics/performance#scenario-adjustment-and-closing-entry-marker
	private static void AddAdjustmentOperations(IReadOnlyList<ResultMarkerAdjustment> adjustments, List<MarkerOperation> operations)
	{
		foreach (var adjustment in adjustments)
		{
			operations.Add(new MarkerOperation(
				adjustment.Date,
				$"{EndOfMomentRank:00}|adjustment:{adjustment.AdjustmentId.ToString(CultureInfo.InvariantCulture)}",
				ResultMarkerKind.PnLAdjustment,
				adjustment.AmountUsdt));
		}
	}

	/// <summary>
	/// Добавляет операции закрывающих записей: вклад — количество ×
	/// эффективная цена в знаковом соглашении денежного потока записи
	/// (поставка длинного остатка — приход, откуп короткого — расход).
	/// Запись без эффективной цены и запись с нулевым по факту вкладом —
	/// OTM-экспирация по нулевой цене — события не создают. Вид операции
	/// выводится из вида записи: delivery — Поставка, ручная пометка —
	/// Закрытие. Закрывающие записи фьючерсных символов событий не создают:
	/// их экономика — цена × количество от нуля, а не финансовый результат, —
	/// и входит в историю только закрытием фьючерсного цикла по FIFO.
	/// </summary>
	// Эффективная цена delivery — внутренняя стоимость, ручной пометки — цена
	// пользователя или последняя известная котировка: при появлении цены
	// история пересчитывается, вычисление на лету гарантирует согласованность.
	// Пометка по фьючерсу закрывает цикл FIFO: вклад цикла уже равен
	// финансовому результату позиции, отдельный вклад записи считал бы
	// нотционал повторно и разошёлся бы с реализованным результатом.
	// Traceability: openspec:analytics/performance#scenario-adjustment-and-closing-entry-marker
	// Traceability: openspec:analytics/performance#scenario-otm-expiry-no-event
	// Traceability: openspec:analytics/performance#scenario-manual-mark-priced-contribution
	// Traceability: openspec:analytics/performance#scenario-futures-closed-trade-marker
	private static void AddClosingEntryOperations(IReadOnlyList<PositionClosingEntry> closingEntries, HashSet<string> linearSymbols, List<MarkerOperation> operations)
	{
		foreach (var closing in closingEntries)
		{
			if (linearSymbols.Contains(closing.Symbol))
			{
				continue;
			}

			if (closing.Price is not { } price)
			{
				continue;
			}

			var amount = -(closing.Quantity * price);
			if (amount == 0m)
			{
				continue;
			}

			operations.Add(new MarkerOperation(
				closing.ClosedAt,
				$"{RankOfClosingKind(closing.Kind):00}|closing:{closing.SourceKey}",
				KindOf(closing.Kind),
				amount));
		}
	}

	/// <summary>Вид окна по составу его сделок: закрывающие и открывающие — Ролл, только закрывающие — Закрытие, остальные — Открытие.</summary>
	private static ResultMarkerKind KindOfWindow(bool hasClosing, bool hasOpening) => hasClosing
		? hasOpening ? ResultMarkerKind.Roll : ResultMarkerKind.Closing
		: ResultMarkerKind.Opening;

	/// <summary>Вид закрывающей записи в виде операции маркера: поставка — Поставка, остальные — Закрытие.</summary>
	private static ResultMarkerKind KindOf(PositionClosingKind kind) => kind switch
	{
		PositionClosingKind.Delivery => ResultMarkerKind.Delivery,
		PositionClosingKind.OtmExpiry => ResultMarkerKind.Closing,
		PositionClosingKind.ManualAnnotation => ResultMarkerKind.Closing,
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид закрывающей записи."),
	};

	/// <summary>Ранг закрывающей записи в хронологии позиции — те же правила, что у сопоставления FIFO.</summary>
	private static int RankOfClosingKind(PositionClosingKind kind) => kind == PositionClosingKind.ManualAnnotation
		? ManualAnnotationRank
		: ExpiryRank;

	/// <summary>Ранг вида записи потока позиции — те же правила хронологии, что у сопоставления FIFO.</summary>
	private static int RankOf(PositionFifoEntryKind kind) => kind switch
	{
		PositionFifoEntryKind.Trade => TradeRank,
		PositionFifoEntryKind.ExpiryClosing => ExpiryRank,
		PositionFifoEntryKind.ManualAnnotation => ManualAnnotationRank,
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид записи потока позиции."),
	};

	#endregion
}
