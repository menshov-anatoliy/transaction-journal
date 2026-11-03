namespace TransactionJournal.Application.Analytics;

using System.Globalization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Вычислитель маркеров изменения результата конструкции: строит хронологию
/// событий журнала, считает накопленный зафиксированный денежный результат
/// в каждой точке события и применяет порог проявления со сквозной нумерацией.
/// События — окна опционных исполнений по правилам сборки v5 (ролл целиком
/// одно событие, переиспользуется атомарность <see cref="AssemblyWindow"/>),
/// каждая фьючерсная сделка робота, изменения набора действующих связок
/// синтетического закрытия, внешние корректировки PnL и закрывающие записи.
/// Значение в точке события — сводная формула «с учётом зафиксированного
/// синтетикой» плюс внешние корректировки минус входная стоимость ещё
/// открытых остатков, применённые к срезу журнала: премия открытия
/// фиксируется сразу — покупка стреддла за 100 даёт первый маркер −100,
/// а закрытия и связки двигают накопленный итог к денежному результату;
/// при закрытии всех остатков значение совпадает со сводной формулой.
/// Котировок вычислитель не читает: нереализованная часть открытых остатков
/// в историю не входит. Вычислитель чистый: хранилище не трогает, история
/// пересчитывается при каждом чтении — спор связки задним числом
/// перестраивает все последующие маркеры.
/// </summary>
/// <remarks>
/// Вычислитель публикует уже выведенные маркеры: порог проявления и
/// перенумерация применены до контрактов, UI фильтрацию не повторяет.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-value
// Traceability: change:add-finresult-result-history-markers/design#d1
/// </remarks>
public sealed class ConstructionResultMarkersCalculator
{
	/// <summary>Доля планового риска, образующая порог проявления промежуточного маркера.</summary>
	private const decimal DisplayThresholdFraction = 0.05m;

	/// <summary>Ранг события-сделки в хронологии позиции: сделка раньше закрывающих записей того же момента.</summary>
	private const int TradeRank = 0;

	/// <summary>Ранг биржевой закрывающей записи в хронологии позиции.</summary>
	private const int ExpiryRank = 1;

	/// <summary>Ранг ручной пометки в хронологии позиции.</summary>
	private const int ManualAnnotationRank = 2;

	/// <summary>Ранг «конец момента» для срезов событий, не являющихся записями потока позиции.</summary>
	private const int EndOfMomentRank = 3;

	private readonly PositionFifoEngine _fifoEngine = new();

	/// <summary>
	/// Вычисляет историю маркеров изменения результата конструкции. Первое
	/// событие даёт первый маркер; последний выводится безусловно и равен
	/// текущему накопленному зафиксированному результату; промежуточные
	/// выводятся только при отличии от последнего выведенного не менее чем
	/// на 5% планового риска — при незаданном или нулевом плановом риске
	/// порога нет и выводятся все события. Номера присваиваются только
	/// выведенным маркерам, подряд без пропусков; подавленные события в
	/// нумерации и описании не участвуют. У конструкции без событий маркеров
	/// нет: пустая история — обычное состояние.
	/// </summary>
	/// <param name="input">Срез журнала конструкции: потоки позиций, сделки, закрывающие записи, связки, корректировки, плановый риск.</param>
	/// <returns>История выведенных маркеров с финальной нумерацией.</returns>
	/// <exception cref="ArgumentNullException">Вход не задан.</exception>
	/// <remarks>
	/// Порядок событий — время записи, при равенстве — стабильный идентификатор
	/// записи; первое и последнее события выводятся безусловно.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-threshold
	// Traceability: change:add-finresult-result-history-markers/design#d3
	/// </remarks>
	public ConstructionResultMarkers Calculate(ResultMarkersInput input)
	{
		ArgumentNullException.ThrowIfNull(input);

		var events = BuildEvents(input);

		// Порог проявления — 5% планового риска: незаданный или нулевой риск
		// базы порога не образует, все события становятся маркерами.
		// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-no-threshold-without-planned-risk
		decimal? threshold = input.PlannedRiskUsdt is > 0m
			? DisplayThresholdFraction * input.PlannedRiskUsdt.Value
			: null;

		var markers = new List<ResultMarker>(events.Count);
		decimal? lastDisplayedTotal = null;
		for (var index = 0; index < events.Count; index++)
		{
			var markerEvent = events[index];
			var total = ValueAt(input, markerEvent);

			// Первое событие и последнее выводятся безусловно; промежуточное —
			// только при отличии от последнего выведенного не менее порога.
			// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-first-last-unconditional
			// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-below-threshold-suppressed
			var isFirst = markers.Count == 0;
			var isLast = index == events.Count - 1;
			if (isFirst == false
				&& isLast == false
				&& threshold is { } limit
				&& Math.Abs(total - lastDisplayedTotal.GetValueOrDefault()) < limit)
			{
				continue;
			}

			// Нумерация сплошная по выведенным: подавленные события номеров
			// не получают и пропусков в последовательности не создают.
			// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-numbering-displayed-only
			markers.Add(new ResultMarker
			{
				Number = markers.Count + 1,
				EventTime = markerEvent.Time,
				Kind = markerEvent.Kind,
				ChangeUsdt = total - (lastDisplayedTotal ?? 0m),
				TotalUsdt = total,
			});
			lastDisplayedTotal = total;
		}

		return new ConstructionResultMarkers { Markers = markers };
	}

	#region Хронология событий

	/// <summary>Событие истории конструкции: время, стабильный ключ порядка, вид и точка среза журнала.</summary>
	private sealed record MarkerEvent(DateTimeOffset Time, string SortKey, ResultMarkerKind Kind, CutKey Cut, long? AdjustmentId);

	/// <summary>Точка среза журнала: момент, ранг вида записи и ключ источника — порядок единой хронологии потоков позиций.</summary>
	private readonly record struct CutKey(DateTimeOffset Time, int Rank, string SourceKey) : IComparable<CutKey>
	{
		/// <summary>Порядок срезов совпадает с порядком хронологии потоков: момент, ранг вида, ключ источника.</summary>
		public int CompareTo(CutKey other)
		{
			var byTime = Time.CompareTo(other.Time);
			if (byTime != 0)
			{
				return byTime;
			}

			var byRank = Rank.CompareTo(other.Rank);
			return byRank != 0 ? byRank : string.CompareOrdinal(SourceKey, other.SourceKey);
		}
	}

	/// <summary>
	/// Строит хронологию событий конструкции: окна опционных исполнений по
	/// правилам сборки v5 (группировка по базовому активу, зазор не более
	/// 60 минут, разрыв на закрывающих записях экспираций — ролл целиком
	/// одно событие), каждая фьючерсная сделка отдельным событием, границы
	/// интервалов связок, корректировки и закрывающие записи. Порядок — по
	/// времени события, при равенстве — по стабильному идентификатору записи.
	/// </summary>
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
	// Traceability: change:add-finresult-result-history-markers/design#d2
	private static List<MarkerEvent> BuildEvents(ResultMarkersInput input)
	{
		var events = new List<MarkerEvent>();

		// Окна опционных исполнений: группировка по базовому активу повторяет
		// правила сборки v5, атомарность окна переиспользуется у сборщика —
		// ролл, усреднение и открытие дают по одному событию на окно.
		// Правила окон и ключей сделок объявлены один раз в общем материале
		// хронологии и переиспользуются читателем хвостов списка.
		// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-first-window-gives-first-marker
		// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-roll-window-single-marker
		foreach (var window in ResultMarkerJournalMaterial.BuildOptionWindows(input.Trades, input.ClosingEntries))
		{
			var last = window.Executions[^1];
			var time = DateTimeOffset.FromUnixTimeMilliseconds(last.ExecTimeMs);
			events.Add(new MarkerEvent(
				time,
				$"window:{last.ExecId}",
				ResultMarkerKind.OptionWindow,
				new CutKey(time, TradeRank, last.ExecId),
				null));
		}

		// Фьючерсные сделки робота не группируются: каждая сделка — отдельное
		// событие со своим вкладом в реализованный результат.
		// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-futures-trade-marker
		foreach (var trade in input.Trades)
		{
			if (string.Equals(trade.Category, ResultMarkerJournalMaterial.LinearCategory, StringComparison.Ordinal) == false)
			{
				continue;
			}

			var tradeKey = ResultMarkerJournalMaterial.TradeKey(trade);
			events.Add(new MarkerEvent(
				trade.ExecutedAt,
				$"trade:{tradeKey}",
				ResultMarkerKind.FuturesTrade,
				new CutKey(trade.ExecutedAt, TradeRank, tradeKey),
				null));
		}

		// Границы интервалов связок: появление связки и её снятие спором —
		// изменения набора действующих связок, каждое даёт свой маркер.
		// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-synthetic-link-change-marker
		foreach (var link in input.Links)
		{
			events.Add(new MarkerEvent(
				link.Since,
				$"link:{link.LinkId}",
				ResultMarkerKind.SyntheticLink,
				new CutKey(link.Since, EndOfMomentRank, string.Empty),
				null));
			if (link.Until is { } until)
			{
				events.Add(new MarkerEvent(
					until,
					$"dispute:{link.LinkId}",
					ResultMarkerKind.SyntheticLink,
					new CutKey(until, EndOfMomentRank, string.Empty),
					null));
			}
		}

		// Внешние корректировки PnL входят в историю каждая своей записью.
		// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-adjustment-and-closing-entry-marker
		foreach (var adjustment in input.Adjustments)
		{
			events.Add(new MarkerEvent(
				adjustment.Date,
				$"adjustment:{adjustment.AdjustmentId.ToString(CultureInfo.InvariantCulture)}",
				ResultMarkerKind.PnLAdjustment,
				new CutKey(adjustment.Date, EndOfMomentRank, string.Empty),
				adjustment.AdjustmentId));
		}

		// Закрывающие записи — delivery, OTM-экспирация и ручная пометка —
		// каждая образует маркер со своим срезом реализованного результата.
		// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-adjustment-and-closing-entry-marker
		foreach (var closing in input.ClosingEntries)
		{
			var rank = closing.Kind == PositionClosingKind.ManualAnnotation ? ManualAnnotationRank : ExpiryRank;
			events.Add(new MarkerEvent(
				closing.ClosedAt,
				$"closing:{closing.SourceKey}",
				KindOf(closing.Kind),
				new CutKey(closing.ClosedAt, rank, closing.SourceKey),
				null));
		}

		// Порядок событий — время записи, при равенстве — стабильный
		// идентификатор записи: перечитывание даёт ту же хронологию.
		// Traceability: change:add-finresult-result-history-markers/design#d2
		return events
			.OrderBy(markerEvent => markerEvent.Time)
			.ThenBy(markerEvent => markerEvent.SortKey, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>Вид закрывающей записи в типе события маркера.</summary>
	private static ResultMarkerKind KindOf(PositionClosingKind kind) => kind switch
	{
		PositionClosingKind.Delivery => ResultMarkerKind.ClosingDelivery,
		PositionClosingKind.OtmExpiry => ResultMarkerKind.ClosingOtmExpiry,
		PositionClosingKind.ManualAnnotation => ResultMarkerKind.ClosingManualMark,
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид закрывающей записи."),
	};

	#endregion

	#region Значение на срезе журнала

	/// <summary>
	/// Накопленный зафиксированный денежный результат в точке события:
	/// реализованный результат потоков позиций на срезе минус входная
	/// стоимость ещё открытых остатков плюс сумма локов действующих связок
	/// плюс внешние корректировки на момент события. Нереализованная часть
	/// и котировки в значение не входят.
	/// </summary>
	// Премия открытия фиксируется сразу: входная стоимость открытых FIFO-слоёв
	// вычитается на каждом срезе, поэтому покупка за 100 сразу даёт −100,
	// а при закрытии всех остатков значение совпадает со сводной формулой.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-value
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-first-window-gives-first-marker
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-open-residual-entry-cost-counts
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-value-includes-locks-and-adjustments
	// Traceability: change:add-finresult-result-history-markers/design#d1
	private decimal ValueAt(ResultMarkersInput input, MarkerEvent markerEvent)
	{
		var (realizedPnL, openEntryCost) = FixedCashResultAt(input, markerEvent.Cut);
		return realizedPnL
			- openEntryCost
			+ LockedTotalAt(input, markerEvent.Time)
			+ AdjustmentsTotalAt(input, markerEvent);
	}

	/// <summary>
	/// Реализованный результат и входная стоимость открытых остатков потоков
	/// позиций конструкции на срезе: по каждому инструменту FIFO-движок
	/// прогоняется по записям, не позднее точки среза в единой хронологии
	/// «момент, ранг вида, ключ источника», — тот же движок, что считает
	/// итоговый реализованный результат. Входная стоимость открытых остатков —
	/// сумма |остаток| × средняя цена открытого слоя по инструментам среза.
	/// </summary>
	// Срез пересчитывается при чтении поверх тех же записей, что и метрики:
	// спор связки или правка журнала перестраивает историю без следов.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-link-dispute-recomputes-history
	private (decimal RealizedPnL, decimal OpenEntryCost) FixedCashResultAt(ResultMarkersInput input, CutKey cut)
	{
		decimal realizedPnL = 0m;
		decimal openEntryCost = 0m;
		foreach (var entries in input.EntriesBySymbol.Values)
		{
			// Полный прогон движка по префиксу потока даёт реализованный PnL
			// префикса: сопоставление встречных частей причинно, слои закрываются
			// только частями того же потока, комиссии входят в момент записи.
			var prefix = PositionFifoEngine.OrderByTimeline(entries)
				.TakeWhile(entry => EntryKey(entry).CompareTo(cut) <= 0)
				.ToList();
			if (prefix.Count == 0)
			{
				continue;
			}

			var fifo = _fifoEngine.Match(prefix);
			realizedPnL += fifo.RealizedPnL;

			// Входная стоимость открытого остатка инструмента — количество-
			// взвешенная цена его открытых FIFO-слоёв: премия открытия числится
			// в истории исходящим потоком до закрытия остатка.
			// Traceability: change:add-finresult-result-history-markers/design#d1
			if (fifo.Residual != 0m)
			{
				openEntryCost += Math.Abs(fifo.Residual) * fifo.AverageOpenPrice.GetValueOrDefault();
			}
		}

		return (realizedPnL, openEntryCost);
	}

	/// <summary>Сумма локов связок, действующих в указанный момент: интервал «появление включительно — спор исключая».</summary>
	private static decimal LockedTotalAt(ResultMarkersInput input, DateTimeOffset time) => input.Links
		.Where(link => link.Since <= time && (link.Until is null || time < link.Until.Value))
		.Sum(link => link.LockedValueUsdt);

	/// <summary>
	/// Сумма внешних корректировок на момент события: корректировки с более
	/// ранней датой входят целиком, с равной — упорядоченные идентификатором;
	/// событие-корректировка включает себя и предшествующие по идентификатору,
	/// прочие события того же момента — все корректировки этой даты.
	/// </summary>
	private static decimal AdjustmentsTotalAt(ResultMarkersInput input, MarkerEvent markerEvent)
	{
		decimal total = 0m;
		foreach (var adjustment in input.Adjustments
			.OrderBy(candidate => candidate.Date)
			.ThenBy(candidate => candidate.AdjustmentId))
		{
			var included = adjustment.Date < markerEvent.Time
				|| (adjustment.Date == markerEvent.Time
					&& (markerEvent.AdjustmentId is null || adjustment.AdjustmentId <= markerEvent.AdjustmentId.Value));
			if (included)
			{
				total += adjustment.AmountUsdt;
			}
		}

		return total;
	}

	/// <summary>Ключ записи потока позиции в единой хронологии: момент, ранг вида, ключ источника.</summary>
	private static CutKey EntryKey(PositionFifoEntry entry) => new(entry.At, RankOf(entry.Kind), entry.SourceKey);

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
