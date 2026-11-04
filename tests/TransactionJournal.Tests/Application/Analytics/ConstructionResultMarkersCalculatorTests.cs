namespace TransactionJournal.Tests.Application.Analytics;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки вычислителя маркеров изменения результата: события истории —
/// окна опционных исполнений (Открытие/Закрытие/Ролл), закрытия фьючерсных
/// циклов по FIFO, связки синтетического закрытия, корректировки и закрывающие
/// записи; значение маркера — префиксная сумма вкладов операций; перечни
/// операций расшифровки со своими инвариантами; порог проявления 5%
/// планового риска и сквозная нумерация выведенных маркеров.
// Traceability: openspec:analytics/performance#requirement-result-markers-history-events
// Traceability: openspec:analytics/performance#requirement-result-marker-value
// Traceability: openspec:analytics/performance#requirement-result-marker-threshold
/// </summary>
[TestClass]
public class ConstructionResultMarkersCalculatorTests
{
	/// <summary>Символ колл-ноги стреддла сценария открытия.</summary>
	private const string CallSymbol = "ETH-25SEP26-1600-C-USDT";

	/// <summary>Символ пут-ноги стреддла сценария открытия.</summary>
	private const string PutSymbol = "ETH-25SEP26-1600-P-USDT";

	/// <summary>Символ фьючерсной ноги конструкции.</summary>
	private const string PerpSymbol = "ETHUSDT";

	/// <summary>Базовый момент журнала сценария: 10 сентября 2026 года 10:00 UTC.</summary>
	private static readonly DateTimeOffset BaseAt = new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

	private readonly ConstructionResultMarkersCalculator _calculator = new();

	[TestMethod]
	[Description("Первое окно даёт первый маркер −100 и является единственным событием окна")]
	// Конструкция открыта покупкой стреддла — колл и пут исполнены в одном
	// окне сборки суммарной стоимостью 100: вклад окна — сумма денежных
	// потоков сделок (покупка — расход), премия открытия фиксируется сразу.
	// Traceability: openspec:analytics/performance#scenario-first-window-gives-first-marker
	public void TryIfFirstWindowGivesFirstMarker()
	{
		// Arrange: стреддл куплен в одном окне — колл 60 и пут 40 без комиссий;
		// плановый риск не задан, порога нет.
		var input = Input(
			Trades(
				OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 60m, 0m),
				OptionTrade("exec-put", PutSymbol, BaseAt.AddMinutes(1), 1m, 40m, 0m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 60m, 0m),
				TradeEntry(PutSymbol, "exec-put", BaseAt.AddMinutes(1), 1m, 40m, 0m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственное событие окна — первый маркер с номером 1 вида
		// Открытие; значение −100 — сумма вкладов сделок окна.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		var first = history.Markers.Single();
		Assert.That(first.Number, Is.EqualTo(1));
		Assert.That(first.Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(first.EventTime, Is.EqualTo(BaseAt.AddMinutes(1)));
		Assert.That(first.TotalUsdt, Is.EqualTo(-100m));
		Assert.That(first.ChangeUsdt, Is.EqualTo(-100m));
		Assert.That(first.Operations, Has.Count.EqualTo(1));
		Assert.That(first.Operations[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(first.Operations[0].AmountUsdt, Is.EqualTo(-100m));
		Assert.That(history.LastMarker, Is.SameAs(first));
	}

	[TestMethod]
	[Description("Ролл образует один маркер с суммарным денежным потоком окна")]
	// Окно с закрывающей и открывающей сделками одной ноги — ролл по правилам
	// сборки v5: окно атомарно и даёт один маркер вида Ролл, а не по маркеру
	// на сделку; вклад равен выручке ликвидации старой позиции плюс стоимости
	// новой.
	// Traceability: openspec:analytics/performance#scenario-roll-window-single-marker
	public void TryIfRollWindowGivesSingleMarker()
	{
		// Arrange: колл куплен по 100 (комиссия 1), через месяц ролл — продажа
		// по 90 и покупка новой доски по 110 в одном окне (комиссии по 1).
		var openedAt = BaseAt;
		var rollAt = BaseAt.AddDays(30);
		var input = Input(
			Trades(
				OptionTrade("exec-open", CallSymbol, openedAt, 1m, 100m, 1m),
				OptionTrade("exec-close", CallSymbol, rollAt, -1m, 90m, 1m),
				OptionTrade("exec-roll-open", CallSymbol, rollAt.AddMinutes(5), 1m, 110m, 1m)),
			Entries(
				TradeEntry(CallSymbol, "exec-open", openedAt, 1m, 100m, 1m),
				TradeEntry(CallSymbol, "exec-close", rollAt, -1m, 90m, 1m),
				TradeEntry(CallSymbol, "exec-roll-open", rollAt.AddMinutes(5), 1m, 110m, 1m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: ровно два события — окно открытия (−101: премия 100 и
		// комиссия 1) и окно ролла одним маркером: выручка продажи +89 и
		// стоимость покупки −111 дают вклад −22, итог −123.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-101m));
		var roll = history.Markers[1];
		Assert.That(roll.Kind, Is.EqualTo(ResultMarkerKind.Roll));
		Assert.That(roll.EventTime, Is.EqualTo(rollAt.AddMinutes(5)));
		Assert.That(roll.ChangeUsdt, Is.EqualTo(-22m));
		Assert.That(roll.TotalUsdt, Is.EqualTo(-123m));
		Assert.That(roll.Operations, Has.Count.EqualTo(1));
		Assert.That(roll.Operations[0].Kind, Is.EqualTo(ResultMarkerKind.Roll));
		Assert.That(roll.Operations[0].AmountUsdt, Is.EqualTo(-22m));
	}

	[TestMethod]
	[Description("Фьючерсная позиция даёт маркер при нулевом остатке — финрезультат цикла")]
	// Открывающая сделка события не создаёт: маркер появляется только в момент
	// обнуления остатка, вклад — прирост реализованного PnL цикла по FIFO
	// с комиссиями обеих сделок.
	// Traceability: openspec:analytics/performance#scenario-futures-closed-trade-marker
	public void TryIfFuturesClosedTradeGivesSingleCycleMarker()
	{
		// Arrange: перп куплен 0.5 по 3500 (комиссия 0.2), позже продан 0.5
		// по 3600 (комиссия 0.3) — закрытие до нулевого остатка реализует
		// (3600 − 3500) × 0.5 минус комиссии 0.5.
		var input = Input(
			Trades(
				LinearTrade("exec-perp-buy", PerpSymbol, BaseAt, 0.5m, 3500m, 0.2m),
				LinearTrade("exec-perp-sell", PerpSymbol, BaseAt.AddDays(1), -0.5m, 3600m, 0.3m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp-buy", BaseAt, 0.5m, 3500m, 0.2m),
				TradeEntry(PerpSymbol, "exec-perp-sell", BaseAt.AddDays(1), -0.5m, 3600m, 0.3m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственный маркер — Закрытие фьючерса в момент продажи
		// со вкладом round-trip: +50 реализации минус 0.5 комиссий = 49.5.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		var cycle = history.Markers.Single();
		Assert.That(cycle.Kind, Is.EqualTo(ResultMarkerKind.FuturesClose));
		Assert.That(cycle.EventTime, Is.EqualTo(BaseAt.AddDays(1)));
		Assert.That(cycle.ChangeUsdt, Is.EqualTo(49.5m));
		Assert.That(cycle.TotalUsdt, Is.EqualTo(49.5m));
		Assert.That(cycle.Operations, Has.Count.EqualTo(1));
		Assert.That(cycle.Operations[0].Kind, Is.EqualTo(ResultMarkerKind.FuturesClose));
		Assert.That(cycle.Operations[0].AmountUsdt, Is.EqualTo(49.5m));
	}

	[TestMethod]
	[DataRow(0.4)]
	[DataRow(0.001)]
	[Description("Открытая и частично закрытая фьючерсная позиция событий не даёт")]
	// Частичное закрытие оставляет ненулевой остаток — события и вклада нет:
	// накопленный итог маркеров не меняется, история остаётся пустой.
	// Traceability: openspec:analytics/performance#scenario-futures-open-partial-no-marker
	public void TryIfFuturesPartialCloseGivesNoMarker(double closedQuantity)
	{
		// Arrange: перп куплен 1 по 3500 и частично продан на заданную долю —
		// остаток после продажи не нулевой.
		var quantity = Convert.ToDecimal(closedQuantity);
		var input = Input(
			Trades(
				LinearTrade("exec-perp-buy", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-perp-sell", PerpSymbol, BaseAt.AddDays(1), -quantity, 3600m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp-buy", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-perp-sell", BaseAt.AddDays(1), -quantity, 3600m, 0m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: событий нет — открытие и частичное закрытие маркеров
		// не создают.
		Assert.That(history.Markers, Is.Empty);
		Assert.That(history.LastMarker, Is.Null);
	}

	[TestMethod]
	[Description("Появление и снятие связки дают маркеры с вкладом лока и обратным вкладом")]
	// Связка стала действующей — маркер Синтетическое закрытие с вкладом лока;
	// спор снял связку — маркер Снятие синтетики с обратным вкладом; реальное
	// закрытие позиции связку автоматически не снимает.
	// Traceability: openspec:analytics/performance#scenario-synthetic-link-change-marker
	public void TryIfSyntheticLinkChangeGivesMarkers()
	{
		// Arrange: перп куплен 1 по 3500 (комиссия 1) и остаётся открытым —
		// его вклад в историю не входит; связка действует с момента T+1 день
		// и держит лок +30, спор снял связку два дня спустя.
		var linkedAt = BaseAt.AddDays(1);
		var disputedAt = BaseAt.AddDays(2);
		var input = Input(
			Trades(LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 1m)),
			Entries(TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 1m)),
			links:
			[
				new ResultMarkerLinkInterval { LinkId = "exec-perp|exec-call", Since = linkedAt, Until = disputedAt, LockedValueUsdt = 30m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: два события — активация (+30) и снятие спором (−30, итог 0);
		// открытая фьючерсная позиция событий не создаёт.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		var linkMarker = history.Markers[0];
		Assert.That(linkMarker.Kind, Is.EqualTo(ResultMarkerKind.SyntheticClose));
		Assert.That(linkMarker.EventTime, Is.EqualTo(linkedAt));
		Assert.That(linkMarker.ChangeUsdt, Is.EqualTo(30m));
		Assert.That(linkMarker.TotalUsdt, Is.EqualTo(30m));
		var disputeMarker = history.Markers[1];
		Assert.That(disputeMarker.Kind, Is.EqualTo(ResultMarkerKind.SyntheticRelease));
		Assert.That(disputeMarker.EventTime, Is.EqualTo(disputedAt));
		Assert.That(disputeMarker.ChangeUsdt, Is.EqualTo(-30m));
		Assert.That(disputeMarker.TotalUsdt, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Корректировка и закрывающая запись с потоком дают по маркеру каждая")]
	// Внешняя корректировка PnL и закрывающая запись с ненулевым денежным
	// потоком (delivery по внутренней стоимости) входят в историю каждая своей
	// операцией: корректировка — суммой, поставка — количество × цена.
	// Traceability: openspec:analytics/performance#scenario-adjustment-and-closing-entry-marker
	public void TryIfAdjustmentAndDeliveryGiveMarkers()
	{
		// Arrange: колл куплен по 100 (комиссия 1), затем корректировка +5,
		// затем delivery закрывает ногу по внутренней стоимости 20 (комиссия 0);
		// закрывающая запись входит и в поток позиции, как в аналитике.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 1m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 1m),
				ExpiryEntry(CallSymbol, "call|delivery", BaseAt.AddDays(30), -1m, 20m, 0m)),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.Delivery, CallSymbol, BaseAt.AddDays(30), "call|delivery", -1m, 20m),
			],
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 5m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: три маркера — окно открытия (−101), корректировка (−96) и
		// поставка (вклад +20, итог −76); итог совпадает с реализованным
		// результатом конструкции (−81 по FIFO плюс корректировка +5).
		Assert.That(history.Markers, Has.Count.EqualTo(3));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-101m));
		Assert.That(history.Markers[1].Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(history.Markers[1].ChangeUsdt, Is.EqualTo(5m));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(-96m));
		var delivery = history.Markers[2];
		Assert.That(delivery.Kind, Is.EqualTo(ResultMarkerKind.Delivery));
		Assert.That(delivery.ChangeUsdt, Is.EqualTo(20m));
		Assert.That(delivery.TotalUsdt, Is.EqualTo(-76m));
		AssertOperationsInvariants(history);
	}

	[TestMethod]
	[DataRow((int)PositionClosingKind.OtmExpiry, 0)]
	[Description("Закрывающая запись без денежного потока события не создаёт")]
	// OTM-экспирация по нулевой цене имеет нулевой по факту вклад — события
	// нет вовсе, а не подавление порогом.
	// Traceability: openspec:analytics/performance#scenario-otm-expiry-no-event
	public void TryIfZeroFlowClosingEntriesGiveNoEvent(int closingKind, double price)
	{
		// Arrange: колл куплен по 100 (комиссия 1), затем OTM-запись с нулевой
		// ценой; запись входит и в поток позиции, как в аналитике.
		var kind = (PositionClosingKind)closingKind;
		var closingPrice = Convert.ToDecimal(price);
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 1m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 1m),
				ExpiryEntry(CallSymbol, "call|expiry", BaseAt.AddDays(30), -1m, closingPrice, 0m)),
			closingEntries:
			[
				ClosingEntry(kind, CallSymbol, BaseAt.AddDays(30), "call|expiry", -1m, closingPrice),
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственный маркер — окно открытия; закрывающая запись
		// события не создала.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-101m));
	}

	[TestMethod]
	[Description("Ручная пометка без эффективной цены события не создаёт")]
	// Пометка без цены пользователя и без известной котировки не имеет
	// эффективной цены — события нет; при появлении цены история
	// пересчитывается вычислением на лету.
	// Traceability: openspec:analytics/performance#scenario-otm-expiry-no-event
	public void TryIfManualMarkWithoutPriceGivesNoEvent()
	{
		// Arrange: колл куплен по 100 (комиссия 1), затем ручная пометка без
		// эффективной цены; в поток позиции пометка при неизвестной цене
		// попадает с нулевой ценой подстановки.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 1m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 1m),
				ExpiryEntry(CallSymbol, "manual:5", BaseAt.AddDays(1), -1m, 0m, 0m)),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.ManualAnnotation, CallSymbol, BaseAt.AddDays(1), "manual:5", -1m, null),
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственный маркер — окно открытия; пометка события
		// не создала.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-101m));
	}

	[TestMethod]
	[Description("Значение маркера включает вклады локов действующих связок и корректировок")]
	// Все остатки закрыты, вклады открытия и закрытия дают −100, действующая
	// связка зафиксировала +30, внешняя корректировка +5 — последний маркер
	// равен −65.
	// Traceability: openspec:analytics/performance#scenario-marker-value-includes-locks-and-adjustments
	public void TryIfMarkerValueIncludesLocksAndAdjustments()
	{
		// Arrange: колл куплен за 100, OTM-экспирация закрыла остаток по нулю
		// (вклад закрытия нулевой); связка действует с того же часа и держит
		// +30; корректировка +5 стоит после экспирации.
		var openedAt = BaseAt.AddDays(-30);
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, openedAt, 1m, 100m, 0m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", openedAt, 1m, 100m, 0m),
				ExpiryEntry(CallSymbol, "call|expiry", BaseAt, -1m, 0m, 0m)),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.OtmExpiry, CallSymbol, BaseAt, "call|expiry", -1m, 0m),
			],
			links:
			[
				new ResultMarkerLinkInterval { LinkId = "exec-put|exec-call", Since = BaseAt.AddDays(-29), Until = null, LockedValueUsdt = 30m },
			],
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 5m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: три маркера — окно открытия (−100), появление связки
		// (−100 + 30 = −70) и корректировка (−100 + 30 + 5 = −65); OTM-экспирация
		// события не создала, при закрытых остатках итог — сумма всех вкладов.
		Assert.That(history.Markers, Has.Count.EqualTo(3));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-100m));
		Assert.That(history.Markers[1].Kind, Is.EqualTo(ResultMarkerKind.SyntheticClose));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(-70m));
		var last = history.LastMarker!;
		Assert.That(last.Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(last.TotalUsdt, Is.EqualTo(-65m));
		AssertOperationsInvariants(history);
	}

	[TestMethod]
	[Description("Вклад открытия фиксируется сразу — покупка за 100 даёт первый маркер −100")]
	// Конструкция открыта покупкой за 100, остатки ещё открыты, действующая
	// связка дала вклад +30: маркер равен −70 — премия открытия фиксируется
	// сразу, разница до маркера текущего результата — нереализованная часть.
	// Traceability: openspec:analytics/performance#scenario-marker-open-residual-entry-cost-counts
	public void TryIfOpenPremiumCountsImmediatelyInMarkerValue()
	{
		// Arrange: покупка колла за 100 без комиссий, остаток открыт; связка
		// действует с часа спустя и держит лок +30; закрывающих записей нет.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m)),
			links:
			[
				new ResultMarkerLinkInterval { LinkId = "exec-put|exec-call", Since = BaseAt.AddDays(1), Until = null, LockedValueUsdt = 30m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: открытие даёт −100 (вклад премии), связка поднимает
		// накопленный итог до −70.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-100m));
		var linkMarker = history.LastMarker!;
		Assert.That(linkMarker.Kind, Is.EqualTo(ResultMarkerKind.SyntheticClose));
		Assert.That(linkMarker.TotalUsdt, Is.EqualTo(-70m));
	}

	[TestMethod]
	[Description("Нереализованный PnL открытых остатков в значение маркера не входит")]
	// После события открытые остатки выросли в цене на +40 по текущим
	// котировкам — значение последнего маркера не меняется: котировок во входе
	// вычислителя нет, нереализованная часть в историю не входит.
	// Traceability: openspec:analytics/performance#scenario-marker-value-excludes-unrealized
	public void TryIfMarkerValueExcludesUnrealized()
	{
		// Arrange: стреддл куплен за 100, остатки открыты; рост котировок на +40
		// существовал бы только в нереализованной части, у которой во входе
		// вычислителя места нет — история не может от котировок зависеть.
		var input = Input(
			Trades(
				OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 60m, 0m),
				OptionTrade("exec-put", PutSymbol, BaseAt.AddMinutes(1), 1m, 40m, 0m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 60m, 0m),
				TradeEntry(PutSymbol, "exec-put", BaseAt.AddMinutes(1), 1m, 40m, 0m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственный маркер несёт зафиксированную премию −100;
		// рост котировок на +40 в значение не попал.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-100m));
	}

	[TestMethod]
	[Description("Спор связки пересчитывает все последующие маркеры")]
	// Действующая связка оспорена после того, как её лок вошёл в маркеры:
	// снятие даёт вклад с обратным знаком, история при следующем чтении
	// пересчитана без этого лока.
	// Traceability: openspec:analytics/performance#scenario-link-dispute-recomputes-history
	public void TryIfLinkDisputeRecomputesHistory()
	{
		// Arrange: колл куплен за 100, связка с локом +30 действует
		// с первого дня; спора пока нет.
		var linkedAt = BaseAt.AddDays(1);
		var linksWithoutDispute = new[]
		{
			new ResultMarkerLinkInterval { LinkId = "exec-put|exec-call", Since = linkedAt, Until = null, LockedValueUsdt = 30m },
		};
		var trades = Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m));
		var entries = Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m));
		var before = _calculator.Calculate(Input(trades, entries, links: linksWithoutDispute, plannedRiskUsdt: null));

		// Act: связка оспорена — тот же журнал с моментом спора даёт новую историю.
		var disputedAt = BaseAt.AddDays(2);
		var after = _calculator.Calculate(Input(
			trades,
			entries,
			links:
			[
				linksWithoutDispute[0] with { Until = disputedAt },
			],
			plannedRiskUsdt: null));

		// Assert: до спора последний маркер несёт лок (−100 + 30 = −70),
		// после спора появляются событие снятия с обратным вкладом и последний
		// маркер без лока (−100).
		Assert.That(before.LastMarker!.TotalUsdt, Is.EqualTo(-70m));
		Assert.That(after.Markers, Has.Count.EqualTo(before.Markers.Count + 1));
		var release = after.LastMarker!;
		Assert.That(release.Kind, Is.EqualTo(ResultMarkerKind.SyntheticRelease));
		Assert.That(release.ChangeUsdt, Is.EqualTo(-30m));
		Assert.That(release.TotalUsdt, Is.EqualTo(-100m));
	}

	[TestMethod]
	[Description("Незакрытая фьючерсная позиция не входит ни в один маркер")]
	// Фьючерсная позиция конструкции не закрыта до нулевого остатка — её
	// сделки не дают событий и вкладов: незакрытая позиция не является
	// зафиксированным результатом.
	// Traceability: openspec:analytics/performance#scenario-futures-open-position-not-counted
	public void TryIfOpenFuturesPositionIsNotCounted()
	{
		// Arrange: перп куплен 1 по 3500 (комиссия 1) и остаётся открытым.
		var trades = Trades(LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 1m));
		var entries = Entries(TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 1m));

		// Act: история без связок и та же история с действующей связкой.
		var withoutLinks = _calculator.Calculate(Input(trades, entries, plannedRiskUsdt: null));
		var withLink = _calculator.Calculate(Input(
			trades,
			entries,
			links:
			[
				new ResultMarkerLinkInterval { LinkId = "exec-perp|exec-call", Since = BaseAt.AddDays(1), Until = null, LockedValueUsdt = 30m },
			],
			plannedRiskUsdt: null));

		// Assert: открытая позиция событий не даёт вовсе; с действующей связкой
		// единственный маркер несёт только лок +30 — вклад фьючерса в историю
		// не попал.
		Assert.That(withoutLinks.Markers, Is.Empty);
		Assert.That(withLink.Markers, Has.Count.EqualTo(1));
		Assert.That(withLink.LastMarker!.Kind, Is.EqualTo(ResultMarkerKind.SyntheticClose));
		Assert.That(withLink.LastMarker.TotalUsdt, Is.EqualTo(30m));
	}

	[TestMethod]
	[Description("Ручная пометка считается по эффективной цене и совпадает с реализованным результатом")]
	// Конструкция закрыта ручной пометкой с ценой пользователя: вклад пометки
	// равен количество × эффективная цена, и последний маркер совпадает
	// с реализованным результатом конструкции по FIFO.
	// Traceability: openspec:analytics/performance#scenario-manual-mark-priced-contribution
	public void TryIfManualMarkPricedContributionMatchesRealizedResult()
	{
		// Arrange: колл куплен за 100 без комиссий, затем ручная пометка
		// закрывает ногу по цене пользователя 25; пометка входит и в поток
		// позиции, как в аналитике.
		var entries = new[]
		{
			TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m),
			new PositionFifoEntry
			{
				At = BaseAt.AddDays(1),
				Kind = PositionFifoEntryKind.ManualAnnotation,
				SourceKey = "manual:5",
				Quantity = -1m,
				Price = 25m,
				Fee = 0m,
			},
		};
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			entries,
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.ManualAnnotation, CallSymbol, BaseAt.AddDays(1), "manual:5", -1m, 25m),
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров и реализованный результат тем же
		// FIFO-движком, что питает метрики позиций.
		var history = _calculator.Calculate(input);
		var realizedPnL = new PositionFifoEngine().Match(entries).RealizedPnL;

		// Assert: два маркера — открытие (−100) и закрытие пометкой (+25);
		// итог последнего маркера −75 равен реализованному результату.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-100m));
		var manualMark = history.LastMarker!;
		Assert.That(manualMark.Kind, Is.EqualTo(ResultMarkerKind.Closing));
		Assert.That(manualMark.ChangeUsdt, Is.EqualTo(25m));
		Assert.That(manualMark.TotalUsdt, Is.EqualTo(-75m));
		Assert.That(manualMark.TotalUsdt, Is.EqualTo(realizedPnL));
	}

	[TestMethod]
	[Description("Сумма перечня операций равна изменению маркера, включая подавленные события")]
	// Между выведенным маркером и предыдущим была подавленная порогом
	// корректировка: её операция входит в перечень расшифровки с суммой,
	// сумма перечня равна изменению, а итог предыдущего плюс сумма — итогу
	// маркера.
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	public void TryIfMarkerOperationsSumToChange()
	{
		// Arrange: порог 20 (риск 400); окно открытия −100, корректировка +5
		// подавлена, связка +30 выведена последним маркером.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m)),
			links:
			[
				new ResultMarkerLinkInterval { LinkId = "exec-put|exec-call", Since = BaseAt.AddDays(2), Until = null, LockedValueUsdt = 30m },
			],
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 5m },
			],
			plannedRiskUsdt: 400m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: два выведенных маркера; перечень второго содержит обе
		// операции — подавленную корректировку +5 и связку +30.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		var last = history.LastMarker!;
		Assert.That(last.Operations, Has.Count.EqualTo(2));
		Assert.That(last.Operations[0].Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(last.Operations[0].AmountUsdt, Is.EqualTo(5m));
		Assert.That(last.Operations[1].Kind, Is.EqualTo(ResultMarkerKind.SyntheticClose));
		Assert.That(last.Operations[1].AmountUsdt, Is.EqualTo(30m));
		AssertOperationsInvariants(history);
	}

	[TestMethod]
	[Description("Событие ниже порога подавлено")]
	// Плановый риск 400 — порог 20; корректировка +5 приносит изменение 5 —
	// маркер для события не выводится; следующая корректировка выводится
	// безусловно как последняя.
	// Traceability: openspec:analytics/performance#scenario-marker-below-threshold-suppressed
	public void TryIfMarkerBelowThresholdIsSuppressed()
	{
		// Arrange: окно открытия −100, корректировка +5 (ниже порога 20) и
		// корректировка +30 (последнее событие, выводится безусловно).
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m)),
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 5m },
				new ResultMarkerAdjustment { AdjustmentId = 12, Date = BaseAt.AddDays(2), AmountUsdt = 30m },
			],
			plannedRiskUsdt: 400m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: микро-корректировка подавлена; выведены только безусловные
		// первый и последний маркеры, итог последнего −65.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].Number, Is.EqualTo(1));
		Assert.That(history.LastMarker!.Number, Is.EqualTo(2));
		Assert.That(history.LastMarker!.EventTime, Is.EqualTo(BaseAt.AddDays(2)));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-65m));
	}

	[TestMethod]
	[Description("Кумулятивный дрейф сравнивается с последним выведенным маркером")]
	// Плановый риск 400 — порог 20; три последовательных корректировки по +8
	// накапливают +24 от последнего выведенного маркера — третья выводится,
	// так как суммарное отличие достигло порога.
	// Traceability: openspec:analytics/performance#scenario-marker-threshold-cumulative-drift
	public void TryIfMarkerThresholdUsesCumulativeDrift()
	{
		// Arrange: окно открытия −100 и три корректировки по +8: накопленные
		// итоги −92, −84 и −76 сравниваются с последним выведенным −100.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m)),
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 8m },
				new ResultMarkerAdjustment { AdjustmentId = 12, Date = BaseAt.AddDays(2), AmountUsdt = 8m },
				new ResultMarkerAdjustment { AdjustmentId = 13, Date = BaseAt.AddDays(3), AmountUsdt = 8m },
			],
			plannedRiskUsdt: 400m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: первые два дрейфа (8 и 16) подавлены, третий достиг порога
		// и выведен; последний маркер совпадает с третьим событием (итог −76).
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.LastMarker!.EventTime, Is.EqualTo(BaseAt.AddDays(3)));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-76m));
	}

	[TestMethod]
	[Description("Первый и последний маркеры выводятся безусловно")]
	// Первое и последнее события изменили результат менее чем на 5% планового
	// риска — оба выводятся, последний равен сумме вкладов всех операций.
	// Traceability: openspec:analytics/performance#scenario-marker-first-last-unconditional
	public void TryIfFirstAndLastMarkersAreUnconditional()
	{
		// Arrange: плановый риск 1000 — порог 50; окно открытия −100,
		// корректировки +5 и −5 — каждое изменение ниже порога.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m)),
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 5m },
				new ResultMarkerAdjustment { AdjustmentId = 12, Date = BaseAt.AddDays(2), AmountUsdt = -5m },
			],
			plannedRiskUsdt: 1000m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: оба события выведены, несмотря на малые изменения; первый
		// маркер — вклад премии −100, последний возвращает итог к −100.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-100m));
		Assert.That(history.LastMarker!.Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-100m));
	}

	[TestMethod]
	[DataRow(0)]
	[Description("Без планового риска порога нет — выводятся все события")]
	// Нулевой плановый риск и незаданный риск порога не образуют: выводятся
	// все маркеры изменения результата.
	// Traceability: openspec:analytics/performance#scenario-marker-no-threshold-without-planned-risk
	public void TryIfNoThresholdWithoutPlannedRisk(double plannedRisk)
	{
		// Arrange: три малых события — окно и две корректировки, каждое
		// изменение ниже пяти процентов любого разумного риска; история
		// вычисляется с нулевым и с незаданным плановым риском.
		var trades = Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m));
		var entries = Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m));
		var adjustments = new[]
		{
			new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 3m },
			new ResultMarkerAdjustment { AdjustmentId = 12, Date = BaseAt.AddDays(2), AmountUsdt = 2m },
		};
		var withZeroRisk = Input(trades, entries, adjustments: adjustments, plannedRiskUsdt: Convert.ToDecimal(plannedRisk));
		var withoutRisk = Input(trades, entries, adjustments: adjustments, plannedRiskUsdt: null);

		// Act: истории с нулевым и с незаданным плановым риском.
		var zeroRiskHistory = _calculator.Calculate(withZeroRisk);
		var noRiskHistory = _calculator.Calculate(withoutRisk);

		// Assert: все три события выведены в обоих вариантах.
		Assert.That(zeroRiskHistory.Markers, Has.Count.EqualTo(3));
		Assert.That(noRiskHistory.Markers, Has.Count.EqualTo(3));
	}

	[TestMethod]
	[Description("Нумерация сплошная по выведенным маркерам")]
	// Между выведенными маркерами были подавленные события — номера выведенных
	// идут подряд без пропусков.
	// Traceability: openspec:analytics/performance#scenario-marker-numbering-displayed-only
	public void TryIfNumberingCoversDisplayedOnly()
	{
		// Arrange: порог 20 (риск 400); окно открытия −100 и серия корректировок:
		// микро-правки подавлены, крупные +190 и +75 выведены.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m)),
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 5m },
				new ResultMarkerAdjustment { AdjustmentId = 12, Date = BaseAt.AddDays(2), AmountUsdt = 190m },
				new ResultMarkerAdjustment { AdjustmentId = 13, Date = BaseAt.AddDays(3), AmountUsdt = 3m },
				new ResultMarkerAdjustment { AdjustmentId = 14, Date = BaseAt.AddDays(4), AmountUsdt = 2m },
				new ResultMarkerAdjustment { AdjustmentId = 15, Date = BaseAt.AddDays(5), AmountUsdt = 75m },
			],
			plannedRiskUsdt: 400m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: накопленные итоги −95, 95, 98, 100 и 175 дают выведенные
		// маркеры на корректировках 2 и 5; номера 1, 2, 3 идут подряд без
		// пропусков, перечень второго маркера содержит подавленное событие.
		Assert.That(history.Markers.Select(marker => marker.Number).ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
		Assert.That(history.Markers[1].EventTime, Is.EqualTo(BaseAt.AddDays(2)));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(95m));
		Assert.That(history.Markers[1].Operations, Has.Count.EqualTo(2));
		Assert.That(history.LastMarker!.EventTime, Is.EqualTo(BaseAt.AddDays(5)));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(175m));
		AssertOperationsInvariants(history);
	}

	[TestMethod]
	[Description("Записи одного момента упорядочиваются рангом вида и стабильным ключом")]
	// Порядок операций — время записи, при равенстве — ранг вида и стабильный
	// ключ: закрывающая запись момента раньше корректировок и связок, поэтому
	// итоги событий момента детерминированы.
	// Traceability: change:fix-finresult-marker-contributions/design#d1
	public void TryIfEqualTimesOrderedByEntryRankBeforeEndOfMoment()
	{
		// Arrange: в один момент 10:00 стоят delivery (+20) и корректировка (+5);
		// при равном времени ранг закрывающей записи 01 раньше ранга 03
		// корректировки.
		var moment = BaseAt.AddDays(1);
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m),
				ExpiryEntry(CallSymbol, "call|delivery", moment, -1m, 20m, 0m)),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.Delivery, CallSymbol, moment, "call|delivery", -1m, 20m),
			],
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = moment, AmountUsdt = 5m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: события одного момента упорядочены рангом вида — поставка
		// (−80) раньше корректировки (−75), итоги детерминированы.
		Assert.That(history.Markers, Has.Count.EqualTo(3));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.Opening));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-100m));
		Assert.That(history.Markers[1].Kind, Is.EqualTo(ResultMarkerKind.Delivery));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(-80m));
		Assert.That(history.LastMarker!.Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-75m));
	}

	[TestMethod]
	[Description("Подряд идущие фьючерсные циклы дают отдельные маркеры")]
	// Второй round-trip того же инструмента считается от предыдущего нуля:
	// вклад — прирост реализованного PnL от контрольной точки, а не весь
	// накопленный результат потока.
	// Traceability: openspec:analytics/performance#scenario-futures-closed-trade-marker
	public void TryIfConsecutiveFuturesCyclesGiveSeparateMarkers()
	{
		// Arrange: перп дважды торгуется до нуля — куплен/продан 1 по 3500/3600,
		// затем куплен/продан 1 по 3400/3450, комиссии нулевые.
		var input = Input(
			Trades(
				LinearTrade("exec-buy-1", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-sell-1", PerpSymbol, BaseAt.AddDays(1), -1m, 3600m, 0m),
				LinearTrade("exec-buy-2", PerpSymbol, BaseAt.AddDays(2), 1m, 3400m, 0m),
				LinearTrade("exec-sell-2", PerpSymbol, BaseAt.AddDays(3), -1m, 3450m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-buy-1", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-1", BaseAt.AddDays(1), -1m, 3600m, 0m),
				TradeEntry(PerpSymbol, "exec-buy-2", BaseAt.AddDays(2), 1m, 3400m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-2", BaseAt.AddDays(3), -1m, 3450m, 0m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: два маркера Закрытие фьючерса — +100 и +50; итоги 100 и 150
		// показывают, что второй цикл считается от предыдущего нуля.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.FuturesClose));
		Assert.That(history.Markers[0].ChangeUsdt, Is.EqualTo(100m));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(100m));
		Assert.That(history.Markers[1].Kind, Is.EqualTo(ResultMarkerKind.FuturesClose));
		Assert.That(history.Markers[1].ChangeUsdt, Is.EqualTo(50m));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(150m));
		AssertOperationsInvariants(history);
	}

	[TestMethod]
	[Description("Разворот фьючерса через ноль даёт то же обнуление цикла")]
	// Продажа большим объёмом, чем остаток, проходит через ноль: цикл по
	// длинной стороне закрывается событием, развёрнутая короткая часть
	// остаётся открытой и событий больше не создаёт.
	// Traceability: change:fix-finresult-marker-contributions/design#d2
	public void TryIfFuturesReversalThroughZeroGivesCycleMarker()
	{
		// Arrange: перп куплен 1 по 3500, затем продан 3 по 3600 — разворот
		// в короткую позицию −2.
		var input = Input(
			Trades(
				LinearTrade("exec-buy", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-sell", PerpSymbol, BaseAt.AddDays(1), -3m, 3600m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-buy", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell", BaseAt.AddDays(1), -3m, 3600m, 0m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственный маркер — Закрытие фьючерса с вкладом длинного
		// цикла +100; короткий остаток −2 остаётся открытым и не входит
		// в историю.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.FuturesClose));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(100m));
	}

	[TestMethod]
	[Description("Пометка по фьючерсу даёт один вклад — закрытие цикла FIFO, без повторного вклада закрывающей записью")]
	// Пометка закрытия по фьючерсному символу закрывает цикл FIFO: вклад —
	// финансовый результат позиции (прирост реализованного PnL до нулевого
	// остатка). Отдельный вклад «количество × цена» считал бы нотционал
	// повторно и разошёлся бы с реализованным результатом конструкции.
	// Traceability: openspec:analytics/performance#scenario-manual-mark-priced-contribution
	// Traceability: openspec:analytics/performance#scenario-futures-closed-trade-marker
	public void TryIfManualMarkOnFuturesCountsOnceAsFuturesCycle()
	{
		// Arrange: перп куплен 1 по 3500, затем закрыт ручной пометкой по 3600.
		var input = Input(
			Trades(
				LinearTrade("exec-buy", PerpSymbol, BaseAt, 1m, 3500m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-buy", BaseAt, 1m, 3500m, 0m),
				MarkEntry(PerpSymbol, "manual:1", BaseAt.AddDays(1), -1m, 3600m)),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.ManualAnnotation, PerpSymbol, BaseAt.AddDays(1), "manual:1", -1m, 3600m),
			]);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственный маркер — Закрытие фьючерса с вкладом +100
		// (3600 − 3500); вклада вида Закрытие с нотционалом 3600 нет.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.FuturesClose));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(100m));
		Assert.That(history.Markers[0].Operations, Has.Count.EqualTo(1));
		Assert.That(history.Markers[0].Operations[0].Kind, Is.EqualTo(ResultMarkerKind.FuturesClose));
	}

	[TestMethod]
	[Description("Null-вход отклоняется вычислителем")]
	// Нарушенный контракт вызова не доходит до построения истории:
	// вычислитель чистый и отклоняет отсутствие входа явной ошибкой.
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullInputInMarkersCalculation()
	{
		// Arrange — Act — Assert
		_calculator.Calculate(null!);
	}

	#region Помощники

	/// <summary>Проверяет инварианты перечня операций каждого маркера истории.</summary>
	// Инварианты контракта: сумма вкладов перечня равна изменению маркера,
	// а итог предыдущего выведенного маркера плюс сумма перечня — итогу маркера.
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	private static void AssertOperationsInvariants(ConstructionResultMarkers history)
	{
		decimal previousTotal = 0m;
		foreach (var marker in history.Markers)
		{
			var operationsSum = marker.Operations.Sum(operation => operation.AmountUsdt);
			Assert.That(operationsSum, Is.EqualTo(marker.ChangeUsdt), $"Сумма перечня операций маркера {marker.Number} должна равняться его изменению.");
			Assert.That(previousTotal + operationsSum, Is.EqualTo(marker.TotalUsdt), $"Итог предыдущего маркера плюс сумма перечня должен равняться итогу маркера {marker.Number}.");
			previousTotal = marker.TotalUsdt;
		}
	}

	/// <summary>Опционная сделка конструкции: символ разбирается парсером опционов, категория — option.</summary>
	private static MaterializedTrade OptionTrade(string execId, string symbol, DateTimeOffset at, decimal quantity, decimal price, decimal fee) =>
		Trade(execId, symbol, "option", at, quantity, price, fee);

	/// <summary>Фьючерсная сделка робота: категория linear.</summary>
	private static MaterializedTrade LinearTrade(string execId, string symbol, DateTimeOffset at, decimal quantity, decimal price, decimal fee) =>
		Trade(execId, symbol, "linear", at, quantity, price, fee);

	/// <summary>Биржевая сделка конструкции с заданной категорией.</summary>
	private static MaterializedTrade Trade(string execId, string symbol, string category, DateTimeOffset at, decimal quantity, decimal price, decimal fee) => new()
	{
		ExecId = execId,
		Source = TradeSource.Exchange,
		Category = category,
		Symbol = symbol,
		ExecutedAt = at,
		Quantity = quantity,
		Price = price,
		Fee = fee,
		IsMaker = false,
	};

	/// <summary>Запись потока позиции для сделки с тем же ключом, что у сделки.</summary>
	private static PositionFifoEntry TradeEntry(string symbol, string execId, DateTimeOffset at, decimal quantity, decimal price, decimal fee) => new()
	{
		At = at,
		Kind = PositionFifoEntryKind.Trade,
		SourceKey = execId,
		Quantity = quantity,
		Price = price,
		Fee = fee,
	};

	/// <summary>Запись потока позиции для закрывающей записи с эффективной ценой.</summary>
	private static PositionFifoEntry ExpiryEntry(string symbol, string sourceKey, DateTimeOffset at, decimal quantity, decimal price, decimal fee) => new()
	{
		At = at,
		Kind = PositionFifoEntryKind.ExpiryClosing,
		SourceKey = sourceKey,
		Quantity = quantity,
		Price = price,
		Fee = fee,
	};

	/// <summary>Запись потока позиции для ручной пометки с эффективной ценой.</summary>
	private static PositionFifoEntry MarkEntry(string symbol, string sourceKey, DateTimeOffset at, decimal quantity, decimal price) => new()
	{
		At = at,
		Kind = PositionFifoEntryKind.ManualAnnotation,
		SourceKey = sourceKey,
		Quantity = quantity,
		Price = price,
		Fee = 0m,
	};

	/// <summary>Действующая закрывающая запись конструкции.</summary>
	private static PositionClosingEntry ClosingEntry(PositionClosingKind kind, string symbol, DateTimeOffset at, string sourceKey, decimal quantity, decimal? price) => new()
	{
		ConstructionId = 1,
		Symbol = symbol,
		Kind = kind,
		ClosedAt = at,
		Quantity = quantity,
		Price = price,
		SourceKey = sourceKey,
	};

	/// <summary>Вход вычислителя с согласованными потоками записей и сделками одной конструкции.</summary>
	private static ResultMarkersInput Input(
		MaterializedTrade[] trades,
		PositionFifoEntry[] entries,
		ResultMarkerLinkInterval[]? links = null,
		PositionClosingEntry[]? closingEntries = null,
		ResultMarkerAdjustment[]? adjustments = null,
		decimal? plannedRiskUsdt = null) => new()
	{
		EntriesBySymbol = entries
			.GroupBy(entry => SymbolOf(entry.SourceKey, trades, closingEntries))
			.ToDictionary(group => group.Key, group => (IReadOnlyList<PositionFifoEntry>)group.ToList()),
		Trades = trades,
		ClosingEntries = closingEntries ?? [],
		Links = links ?? [],
		Adjustments = adjustments ?? [],
		PlannedRiskUsdt = plannedRiskUsdt,
	};

	/// <summary>Символ записи потока: сделка ищется среди сделок сценария, закрывающие — среди закрывающих записей.</summary>
	private static string SymbolOf(string sourceKey, MaterializedTrade[] trades, PositionClosingEntry[]? closingEntries)
	{
		var byTrade = trades.FirstOrDefault(trade => trade.ExecId == sourceKey);
		if (byTrade is not null)
		{
			return byTrade.Symbol;
		}

		var byClosing = (closingEntries ?? []).FirstOrDefault(closing => closing.SourceKey == sourceKey);
		return byClosing?.Symbol ?? CallSymbol;
	}

	/// <summary>Список сделок сценария.</summary>
	private static MaterializedTrade[] Trades(params MaterializedTrade[] trades) => trades;

	/// <summary>Список записей потоков сценария.</summary>
	private static PositionFifoEntry[] Entries(params PositionFifoEntry[] entries) => entries;

	#endregion
}
