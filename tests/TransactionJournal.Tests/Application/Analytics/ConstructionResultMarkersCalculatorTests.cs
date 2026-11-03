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
/// Проверки вычислителя маркеров изменения результата: события истории
/// (окна сборки v5, фьючерсные сделки, связки, корректировки, закрывающие
/// записи), значение на срезе журнала — сводная формула «с учётом
/// зафиксированного синтетикой» плюс корректировки без нереализованной части,
/// пересчёт истории при споре связки, порог проявления 5% планового риска
/// и сквозная нумерация выведенных маркеров.
/// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
/// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-value
/// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-threshold
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
	// окне сборки суммарной стоимостью 100; премия открытия фиксируется сразу:
	// первый маркер равен −100 (входная стоимость открытых остатков минус
	// реализованный результат с нулевыми комиссиями).
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-first-window-gives-first-marker
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-open-residual-entry-cost-counts
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

		// Assert: единственное событие окна — первый маркер с номером 1;
		// значение −100 — входная стоимость открытых остатков окна.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		var first = history.Markers.Single();
		Assert.That(first.Number, Is.EqualTo(1));
		Assert.That(first.Kind, Is.EqualTo(ResultMarkerKind.OptionWindow));
		Assert.That(first.EventTime, Is.EqualTo(BaseAt.AddMinutes(1)));
		Assert.That(first.TotalUsdt, Is.EqualTo(-100m));
		Assert.That(first.ChangeUsdt, Is.EqualTo(-100m));
		Assert.That(history.LastMarker, Is.SameAs(first));
	}

	[TestMethod]
	[Description("Ролл образует один маркер с суммарным денежным потоком окна")]
	// Окно с закрывающей и открывающей сделками одной ноги — ролл по правилам
	// сборки v5: окно атомарно и даёт один маркер, а не по маркеру на сделку.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-roll-window-single-marker
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

		// Assert: ровно два события — окно открытия (−101: комиссия 1 и
		// входная стоимость 100) и окно ролла; ролл даёт один маркер
		// с денежным итогом среза: реализованный (90 − 100) × 1 минус
		// комиссии трёх сделок = −13, минус входная стоимость новой ноги 110.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		var roll = history.Markers[1];
		Assert.That(roll.Kind, Is.EqualTo(ResultMarkerKind.OptionWindow));
		Assert.That(roll.EventTime, Is.EqualTo(rollAt.AddMinutes(5)));
		Assert.That(roll.TotalUsdt, Is.EqualTo(-123m));
		Assert.That(roll.ChangeUsdt, Is.EqualTo(-22m));
	}

	[TestMethod]
	[Description("Фьючерсная сделка, изменившая результат, даёт отдельный маркер")]
	// Частичное закрытие фьючерсной ноги реализует FIFO-результат — сделка
	// робота образует маркер со значением, включающим эту сделку.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-futures-trade-marker
	public void TryIfFuturesTradeGivesMarker()
	{
		// Arrange: перп куплен 0.5 по 3500 (комиссия 0.2), позже продан 0.5
		// по 3600 (комиссия 0.3) — закрытие реализует (3600 − 3500) × 0.5.
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

		// Assert: второй маркер — фьючерсная сделка со значением FIFO среза:
		// +50 реализации минус комиссии двух сделок = 49.5.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		var tradeMarker = history.Markers[1];
		Assert.That(tradeMarker.Kind, Is.EqualTo(ResultMarkerKind.FuturesTrade));
		Assert.That(tradeMarker.EventTime, Is.EqualTo(BaseAt.AddDays(1)));
		Assert.That(tradeMarker.TotalUsdt, Is.EqualTo(49.5m));
	}

	[TestMethod]
	[Description("Появление и снятие связки дают маркеры с учётом нового набора действующих связок")]
	// Связка стала действующей — маркер со значением, включающим лок; спор
	// снял связку — следующий маркер пересчитан без этого лока.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-synthetic-link-change-marker
	public void TryIfSyntheticLinkChangeGivesMarkers()
	{
		// Arrange: перп куплен 1 по 3500 (комиссия 1); связка действует с момента
		// поздней из двух сделок и держит лок +30; спор снял связку два дня спустя.
		// Связка во входе задана интервалом: вычислитель потребляет готовые
		// интервалы связок read-модели, распознавание остаётся снаружи.
		var openedAt = BaseAt;
		var linkedAt = BaseAt.AddDays(1);
		var disputedAt = BaseAt.AddDays(2);
		var input = Input(
			Trades(LinearTrade("exec-perp", PerpSymbol, openedAt, 1m, 3500m, 1m)),
			Entries(TradeEntry(PerpSymbol, "exec-perp", openedAt, 1m, 3500m, 1m)),
			links:
			[
				new ResultMarkerLinkInterval { LinkId = "exec-perp|exec-call", Since = linkedAt, Until = disputedAt, LockedValueUsdt = 30m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: три события — фьючерсное открытие (−3501: комиссия 1 и
		// входная стоимость 3500), появление связки (итог включает лок +30)
		// и снятие спором (итог без лока).
		Assert.That(history.Markers, Has.Count.EqualTo(3));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.FuturesTrade));
		var linkMarker = history.Markers[1];
		Assert.That(linkMarker.Kind, Is.EqualTo(ResultMarkerKind.SyntheticLink));
		Assert.That(linkMarker.EventTime, Is.EqualTo(linkedAt));
		Assert.That(linkMarker.TotalUsdt, Is.EqualTo(-3471m));
		var disputeMarker = history.Markers[2];
		Assert.That(disputeMarker.Kind, Is.EqualTo(ResultMarkerKind.SyntheticLink));
		Assert.That(disputeMarker.EventTime, Is.EqualTo(disputedAt));
		Assert.That(disputeMarker.TotalUsdt, Is.EqualTo(-3501m));
	}

	[TestMethod]
	[Description("Корректировка и закрывающая запись дают по маркеру каждая")]
	// Внешняя корректировка PnL и закрывающая запись (delivery, OTM-экспирация,
	// ручная пометка) входят в историю каждая своей записью.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-adjustment-and-closing-entry-marker
	public void TryIfAdjustmentAndClosingEntryGiveMarkers()
	{
		// Arrange: колл куплен по 100 (комиссия 1), затем корректировка +5,
		// затем OTM-экспирация закрывает ногу по нулевой цене (комиссия 0);
		// закрывающая запись входит и в поток позиции, как в аналитике.
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 1m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 1m),
				new PositionFifoEntry
				{
					At = BaseAt.AddDays(30),
					Kind = PositionFifoEntryKind.ExpiryClosing,
					SourceKey = "call|expiry",
					Quantity = -1m,
					Price = 0m,
					Fee = 0m,
				}),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.OtmExpiry, CallSymbol, BaseAt.AddDays(30), "call|expiry", -1m, 0m),
			],
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = BaseAt.AddDays(1), AmountUsdt = 5m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: три маркера — окно открытия (−101: комиссия 1 и входная
		// стоимость 100), корректировка (−101 + 5 = −96) и экспирация, после
		// которой остатков нет: значение −100 + 5 = −96 совпадает со сводной
		// формулой (реализованный −101 плюс корректировка +5).
		Assert.That(history.Markers, Has.Count.EqualTo(3));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-101m));
		Assert.That(history.Markers[1].Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(-96m));
		Assert.That(history.Markers[2].Kind, Is.EqualTo(ResultMarkerKind.ClosingOtmExpiry));
		Assert.That(history.Markers[2].TotalUsdt, Is.EqualTo(-96m));
	}

	[TestMethod]
	[Description("Входная стоимость открытых остатков входит в значение маркера")]
	// Конструкция открыта покупкой за 100, остатки ещё открыты, реализованных
	// закрытий нет, действующая связка зафиксировала +30: маркер равен −70 —
	// премия открытия фиксируется сразу.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-open-residual-entry-cost-counts
	public void TryIfOpenResidualEntryCostCountsInMarkerValue()
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

		// Assert: открытие даёт −100 (входная стоимость открытого остатка),
		// связка поднимает накопленный итог до −70; разница до маркера текущего
		// результата осталась бы нереализованной частью.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-100m));
		var linkMarker = history.LastMarker!;
		Assert.That(linkMarker.Kind, Is.EqualTo(ResultMarkerKind.SyntheticLink));
		Assert.That(linkMarker.TotalUsdt, Is.EqualTo(-70m));
	}

	[TestMethod]
	[Description("Значение маркера включает локи действующих связок и внешние корректировки")]
	// На момент события реализованный результат −100, действующие связки
	// зафиксировали +30, внешние корректировки +5 — маркер равен −65.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-value-includes-locks-and-adjustments
	public void TryIfMarkerValueIncludesLocksAndAdjustments()
	{
		// Arrange: OTM-экспирация реализовала −100; связка действует с того же
		// часа и держит +30; корректировка +5 стоит после экспирации.
		var expiryAt = BaseAt;
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt.AddDays(-30), 1m, 100m, 0m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt.AddDays(-30), 1m, 100m, 0m),
				new PositionFifoEntry
				{
					At = expiryAt,
					Kind = PositionFifoEntryKind.ExpiryClosing,
					SourceKey = "call|delivery",
					Quantity = -1m,
					Price = 0m,
					Fee = 0m,
				}),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.OtmExpiry, CallSymbol, expiryAt, "call|delivery", -1m, 0m),
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

		// Assert: четыре маркера — окно открытия (−100 входной стоимости),
		// появление связки (−100 + 30 = −70 при открытом остатке), экспирация,
		// закрывшая остаток (−100 + 30 = −70) и корректировка со значением
		// −100 + 30 + 5 = −65: при закрытых остатках значение совпало со сводной
		// формулой.
		Assert.That(history.Markers, Has.Count.EqualTo(4));
		Assert.That(history.Markers[1].Kind, Is.EqualTo(ResultMarkerKind.SyntheticLink));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(-70m));
		Assert.That(history.Markers[2].Kind, Is.EqualTo(ResultMarkerKind.ClosingOtmExpiry));
		Assert.That(history.Markers[2].TotalUsdt, Is.EqualTo(-70m));
		var last = history.LastMarker!;
		Assert.That(last.Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(last.TotalUsdt, Is.EqualTo(-65m));
	}

	[TestMethod]
	[Description("Нереализованный PnL открытых остатков в значение маркера не входит")]
	// После события открытые остатки выросли в цене на +40 по текущим
	// котировкам — значение последнего маркера не меняется: котировок
	// вычислитель не читает, нереализованная часть в историю не входит.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-value-excludes-unrealized
	public void TryIfMarkerValueExcludesUnrealized()
	{
		// Arrange: перп открыт и остаётся открытым — реализованный результат
		// нулевой минус комиссия; рост цены на +40 существовал бы только
		// в нереализованной части, у которой во входе вычислителя места нет.
		var input = Input(
			Trades(LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 1m)),
			Entries(TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 1m)),
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: единственный маркер несёт зафиксированную часть среза —
		// реализованный результат (−1 комиссия) минус входная стоимость
		// открытого остатка 3500; рост котировок на +40 в значение не попал.
		Assert.That(history.Markers, Has.Count.EqualTo(1));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-3501m));
	}

	[TestMethod]
	[Description("Спор связки пересчитывает все последующие маркеры")]
	// Действующая связка оспорена после того, как её лок вошёл в маркеры:
	// история при следующем чтении пересчитана без этого лока.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-link-dispute-recomputes-history
	public void TryIfLinkDisputeRecomputesHistory()
	{
		// Arrange: перп открыт (комиссия 1), связка с локом +30 действует
		// с первого дня; спора пока нет.
		var openedAt = BaseAt;
		var linkedAt = BaseAt.AddDays(1);
		var linksWithoutDispute = new[]
		{
			new ResultMarkerLinkInterval { LinkId = "exec-perp|exec-call", Since = linkedAt, Until = null, LockedValueUsdt = 30m },
		};
		var before = _calculator.Calculate(Input(
			Trades(LinearTrade("exec-perp", PerpSymbol, openedAt, 1m, 3500m, 1m)),
			Entries(TradeEntry(PerpSymbol, "exec-perp", openedAt, 1m, 3500m, 1m)),
			links: linksWithoutDispute,
			plannedRiskUsdt: null));

		// Act: связка оспорена — тот же журнал с моментом спора даёт новую историю.
		var disputedAt = BaseAt.AddDays(2);
		var after = _calculator.Calculate(Input(
			Trades(LinearTrade("exec-perp", PerpSymbol, openedAt, 1m, 3500m, 1m)),
			Entries(TradeEntry(PerpSymbol, "exec-perp", openedAt, 1m, 3500m, 1m)),
			links:
			[
				linksWithoutDispute[0] with { Until = disputedAt },
			],
			plannedRiskUsdt: null));

		// Assert: до спора последний маркер несёт лок поверх входной стоимости
		// (−3501 + 30 = −3471), после спора появляются событие снятия и последний
		// маркер без лока (−3501).
		Assert.That(before.LastMarker!.TotalUsdt, Is.EqualTo(-3471m));
		Assert.That(after.Markers, Has.Count.EqualTo(before.Markers.Count + 1));
		Assert.That(after.LastMarker!.Kind, Is.EqualTo(ResultMarkerKind.SyntheticLink));
		Assert.That(after.LastMarker.TotalUsdt, Is.EqualTo(-3501m));
	}

	[TestMethod]
	[Description("Событие ниже порога подавлено")]
	// Плановый риск 400 — порог 20; микро-закрытие приносит 3.65 выручки —
	// маркер для события не выводится; финальная продажа выводится безусловно.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-below-threshold-suppressed
	public void TryIfMarkerBelowThresholdIsSuppressed()
	{
		// Arrange: открытие 1 контракта по 3500, микро-закрытие 0.001 по 3650
		// (выручка 3.65 — ниже порога 20) и закрытие остатка 0.999 по 3800.
		var input = Input(
			Trades(
				LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-sell-1", PerpSymbol, BaseAt.AddDays(1), -0.001m, 3650m, 0m),
				LinearTrade("exec-sell-2", PerpSymbol, BaseAt.AddDays(2), -0.999m, 3800m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-1", BaseAt.AddDays(1), -0.001m, 3650m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-2", BaseAt.AddDays(2), -0.999m, 3800m, 0m)),
			plannedRiskUsdt: 400m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: микро-закрытие подавлено; выведены только безусловные первый
		// и последний маркеры.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].Number, Is.EqualTo(1));
		Assert.That(history.LastMarker!.Number, Is.EqualTo(2));
		Assert.That(history.LastMarker!.EventTime, Is.EqualTo(BaseAt.AddDays(2)));
	}

	[TestMethod]
	[Description("Кумулятивный дрейф сравнивается с последним выведенным маркером")]
	// Плановый риск 400 — порог 20; три последовательных микро-закрытия приносят
	// по +8 выручки и накапливают +24 от последнего выведенного маркера —
	// третье выводится, так как суммарное отличие достигло порога.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-threshold-cumulative-drift
	public void TryIfMarkerThresholdUsesCumulativeDrift()
	{
		// Arrange: открытие 1 контракта по 3500, три микро-закрытия 0.001 по 8000 —
		// выручка +8 на каждое событие, накопленные итоги −3500, −3492, −3484, −3476.
		var input = Input(
			Trades(
				LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-sell-1", PerpSymbol, BaseAt.AddDays(1), -0.001m, 8000m, 0m),
				LinearTrade("exec-sell-2", PerpSymbol, BaseAt.AddDays(2), -0.001m, 8000m, 0m),
				LinearTrade("exec-sell-3", PerpSymbol, BaseAt.AddDays(3), -0.001m, 8000m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-1", BaseAt.AddDays(1), -0.001m, 8000m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-2", BaseAt.AddDays(2), -0.001m, 8000m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-3", BaseAt.AddDays(3), -0.001m, 8000m, 0m)),
			plannedRiskUsdt: 400m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: первые два дрейфа (8 и 16) подавлены, третий достиг порога
		// и выведен; последний маркер совпадает с третьим событием.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.LastMarker!.EventTime, Is.EqualTo(BaseAt.AddDays(3)));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-3476m));
	}

	[TestMethod]
	[Description("Первый и последний маркеры выводятся безусловно")]
	// Первое и последнее события изменили результат менее чем на 5% планового
	// риска — оба выводятся, последний равен текущему накопленному
	// зафиксированному результату.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-first-last-unconditional
	public void TryIfFirstAndLastMarkersAreUnconditional()
	{
		// Arrange: плановый риск 1000 — порог 50; первое событие даёт −5,
		// последнее +5, промежуточных событий нет.
		var input = Input(
			Trades(
				LinearTrade("exec-perp", PerpSymbol, BaseAt, 0.5m, 3500m, 0m),
				LinearTrade("exec-sell", PerpSymbol, BaseAt.AddDays(1), -0.5m, 3510m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp", BaseAt, 0.5m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell", BaseAt.AddDays(1), -0.5m, 3510m, 0m)),
			plannedRiskUsdt: 1000m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: оба события выведены, несмотря на малые изменения;
		// первый маркер — входная стоимость открытого остатка (−1750),
		// последний равен накопленному денежному результату +5.
		Assert.That(history.Markers, Has.Count.EqualTo(2));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-1750m));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(5m));
	}

	[TestMethod]
	[Description("Без планового риска порога нет — выводятся все события")]
	// Незаданный или нулевой плановый риск порога не образует: выводятся все
	// маркеры изменения результата.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-no-threshold-without-planned-risk
	public void TryIfNoThresholdWithoutPlannedRisk()
	{
		// Arrange: три малых события; плановый риск не задан, затем ноль —
		// оба варианта выводят все события.
		var input = Input(
			Trades(
				LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-sell-1", PerpSymbol, BaseAt.AddDays(1), -0.1m, 3580m, 0m),
				LinearTrade("exec-sell-2", PerpSymbol, BaseAt.AddDays(2), -0.1m, 3660m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-1", BaseAt.AddDays(1), -0.1m, 3580m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-2", BaseAt.AddDays(2), -0.1m, 3660m, 0m)),
			plannedRiskUsdt: null);

		// Act: истории без риска и с нулевым риском.
		var withoutRisk = _calculator.Calculate(input);
		var withZeroRisk = _calculator.Calculate(Input(
			Trades(
				LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-sell-1", PerpSymbol, BaseAt.AddDays(1), -0.1m, 3580m, 0m),
				LinearTrade("exec-sell-2", PerpSymbol, BaseAt.AddDays(2), -0.1m, 3660m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-1", BaseAt.AddDays(1), -0.1m, 3580m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-2", BaseAt.AddDays(2), -0.1m, 3660m, 0m)),
			plannedRiskUsdt: 0m));

		// Assert: все три события выведены в обоих вариантах.
		Assert.That(withoutRisk.Markers, Has.Count.EqualTo(3));
		Assert.That(withZeroRisk.Markers, Has.Count.EqualTo(3));
	}

	[TestMethod]
	[Description("Нумерация сплошная по выведенным маркерам")]
	// Между выведенными маркерами были подавленные события — номера выведенных
	// идут подряд без пропусков.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-marker-numbering-displayed-only
	public void TryIfNumberingCoversDisplayedOnly()
	{
		// Arrange: порог 20 (риск 400); открытие 1 контракта по 3500 и серия
		// закрытий: микро-продажи приносят копейки (подавлены), крупные продажи —
		// 190 и 197.5 выручки (выведены).
		var input = Input(
			Trades(
				LinearTrade("exec-perp", PerpSymbol, BaseAt, 1m, 3500m, 0m),
				LinearTrade("exec-sell-a", PerpSymbol, BaseAt.AddDays(1), -0.001m, 3650m, 0m),
				LinearTrade("exec-sell-b", PerpSymbol, BaseAt.AddDays(2), -0.05m, 3800m, 0m),
				LinearTrade("exec-sell-c", PerpSymbol, BaseAt.AddDays(3), -0.001m, 3800m, 0m),
				LinearTrade("exec-sell-d", PerpSymbol, BaseAt.AddDays(4), -0.001m, 3800m, 0m),
				LinearTrade("exec-sell-e", PerpSymbol, BaseAt.AddDays(5), -0.05m, 3950m, 0m)),
			Entries(
				TradeEntry(PerpSymbol, "exec-perp", BaseAt, 1m, 3500m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-a", BaseAt.AddDays(1), -0.001m, 3650m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-b", BaseAt.AddDays(2), -0.05m, 3800m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-c", BaseAt.AddDays(3), -0.001m, 3800m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-d", BaseAt.AddDays(4), -0.001m, 3800m, 0m),
				TradeEntry(PerpSymbol, "exec-sell-e", BaseAt.AddDays(5), -0.05m, 3950m, 0m)),
			plannedRiskUsdt: 400m);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: накопленные итоги −3496.35, −3306.35, −3302.55, −3298.75 и
		// −3101.25 дают выведенные маркеры на событиях 2 и 5; номера 1, 2, 3
		// идут подряд без пропусков.
		Assert.That(history.Markers.Select(marker => marker.Number).ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
		Assert.That(history.Markers[1].EventTime, Is.EqualTo(BaseAt.AddDays(2)));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(-3306.35m));
		Assert.That(history.LastMarker!.EventTime, Is.EqualTo(BaseAt.AddDays(5)));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-3101.25m));
	}

	[TestMethod]
	[Description("Равное время событий упорядочивается стабильным идентификатором записи")]
	// Порядок событий — время записи, при равенстве — стабильный идентификатор:
	// закрывающая запись и фьючерсная сделка одного момента дают детерминированный
	// порядок и согласованное значение последнего маркера.
	// Traceability: change:add-finresult-result-history-markers/design#d2
	public void TryIfEqualTimesOrderByStableIdentifier()
	{
		// Arrange: в один момент 12:00 стоят корректировка (+5) и закрывающая
		// запись OTM (−100 реализации); при равном времени порядок задаёт
		// стабильный идентификатор записи: «adjustment:11» лексикографически
		// раньше «closing:call|delivery».
		var moment = BaseAt.AddDays(1);
		var input = Input(
			Trades(OptionTrade("exec-call", CallSymbol, BaseAt, 1m, 100m, 0m)),
			Entries(
				TradeEntry(CallSymbol, "exec-call", BaseAt, 1m, 100m, 0m),
				new PositionFifoEntry
				{
					At = moment,
					Kind = PositionFifoEntryKind.ExpiryClosing,
					SourceKey = "call|delivery",
					Quantity = -1m,
					Price = 0m,
					Fee = 0m,
				}),
			closingEntries:
			[
				ClosingEntry(PositionClosingKind.OtmExpiry, CallSymbol, moment, "call|delivery", -1m, 0m),
			],
			adjustments:
			[
				new ResultMarkerAdjustment { AdjustmentId = 11, Date = moment, AmountUsdt = 5m },
			],
			plannedRiskUsdt: null);

		// Act: вычисляем историю маркеров.
		var history = _calculator.Calculate(input);

		// Assert: события одного момента упорядочены по идентификатору и несут
		// согласованное значение среза: окно даёт −100 входной стоимости, срез
		// «конец момента» включает закрывающую запись уже для корректировки,
		// поэтому итоги обоих событий момента равны −95.
		Assert.That(history.Markers, Has.Count.EqualTo(3));
		Assert.That(history.Markers[0].Kind, Is.EqualTo(ResultMarkerKind.OptionWindow));
		Assert.That(history.Markers[0].TotalUsdt, Is.EqualTo(-100m));
		Assert.That(history.Markers[1].Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(history.Markers[1].TotalUsdt, Is.EqualTo(-95m));
		Assert.That(history.LastMarker!.Kind, Is.EqualTo(ResultMarkerKind.ClosingOtmExpiry));
		Assert.That(history.LastMarker!.TotalUsdt, Is.EqualTo(-95m));
	}

	[TestMethod]
	[Description("Null-вход отклоняется вычислителем")]
	// Нарушенный контракт вызова не доходит до построения истории:
	// вычислитель чистый и отклоняет отсутствие входа явной ошибкой.
	// Traceability: change:add-finresult-result-history-markers/design#d1
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullInputInMarkersCalculation()
	{
		// Arrange — Act — Assert
		_calculator.Calculate(null!);
	}

	#region Помощники

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

	/// <summary>Действующая закрывающая запись конструкции.</summary>
	private static PositionClosingEntry ClosingEntry(PositionClosingKind kind, string symbol, DateTimeOffset at, string sourceKey, decimal quantity, decimal price) => new()
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
			.GroupBy(entry => SymbolOf(entry.SourceKey, trades))
			.ToDictionary(group => group.Key, group => (IReadOnlyList<PositionFifoEntry>)group.ToList()),
		Trades = trades,
		ClosingEntries = closingEntries ?? [],
		Links = links ?? [],
		Adjustments = adjustments ?? [],
		PlannedRiskUsdt = plannedRiskUsdt,
	};

	/// <summary>Символ записи потока по ключу источника: сделка ищется среди сделок сценария, закрывающие — на колл-ноге.</summary>
	private static string SymbolOf(string sourceKey, MaterializedTrade[] trades) =>
		trades.FirstOrDefault(trade => trade.ExecId == sourceKey)?.Symbol ?? CallSymbol;

	/// <summary>Список сделок сценария.</summary>
	private static MaterializedTrade[] Trades(params MaterializedTrade[] trades) => trades;

	/// <summary>Список записей потоков сценария.</summary>
	private static PositionFifoEntry[] Entries(params PositionFifoEntry[] entries) => entries;

	#endregion
}
