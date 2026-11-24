import { describe, expect, it } from "vitest";
import {
	computeFinResultGeometry,
	type FinResultInput,
	type FinResultLabelLayout,
} from "./geometry";

// Калькулятор геометрии индикатора финрезультата: чистая математика в долях
// шкалы, без React и DOM. Кейсы покрывают построение шкалы и зон, маркер
// текущего результата (реализованный + нереализованный + корректировки),
// заливки итога и вертикальный разнос текстовых меток; маркерных частей
// истории (точки, бейджи, расширение шкалы до истории) в геометрии больше
// нет — история результата остаётся в журнале событий.
// Traceability: issue:#94

// Полный индикатор макета: ширина полосы 936 px.
const FULL_WIDTH = 936;

// Метрики разметки полного пресета: полоса 936 px, символ над полосой 7 px,
// под полосой 6.5 px.
const LAYOUT: FinResultLabelLayout = {
	scaleWidthPx: FULL_WIDTH,
	aboveCharWidthPx: 7,
	belowCharWidthPx: 6.5,
};

// Базовый вход кейсов: плановый риск 300, плановый профит 900 — как в
// подписях шкалы «−300 / 0 / +900» макета. Остатки закрыты
// (hasOpenResidual: false) — так сбой котировок не делает индикатор
// неполным; кейсы с открытыми остатками переопределяют флаг.
function caseInput(overrides: Partial<FinResultInput>): FinResultInput {
	return {
		plannedRisk: 300,
		plannedProfit: 900,
		realized: 0,
		unrealized: 0,
		adjustments: 0,
		quotesDegraded: false,
		hasOpenResidual: false,
		realRiskStatus: "finite",
		...overrides,
	};
}

const CASE_FRESH = caseInput({});

describe("кейс C1 · только что открыли", () => {
	// Свежая позиция: сделок закрытия нет, нереализованная ≈ 0 (комиссии и
	// спред), маркеров истории в индикаторе больше нет. Маркер стоит у нуля:
	// ликвидация сейчас вернёт ≈ 0. Кейс обновлён срезом #94 — прежняя метка
	// вела на требование, чей текст живой спеки всё ещё предписывает бейджи
	// истории, поэтому ссылка переведена на задачу, меняющую поведение.
	// Traceability: issue:#94
	it("маркер у нуля", () => {
		// Act: свежая конструкция без реализованного результата.
		const geometry = computeFinResultGeometry(CASE_FRESH);

		// Assert: маркер — позиция итога ≈ 0.
		expect(geometry.markerAt).toBeCloseTo(0.25, 3);

		// Assert: шкала и зоны из подписей «−300 / 0 / +900».
		expect(geometry.zeroAt).toBeCloseTo(0.25, 3);
		expect(geometry.riskZone).toEqual({ from: 0, to: 0.25 });
		expect(geometry.profitZone).toEqual({ from: 0.25, to: 1 });
		expect(geometry.superZone).toBeNull();
	});

	it("подписи: плановые деления и маркер «≈ 0»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASE_FRESH);

		// Assert: подписи из макета — деления плановых зон и маркер итога.
		expect(geometry.labels.risk).toBe("−300");
		expect(geometry.labels.profit).toBe("+900");
		expect(geometry.labels.marker).toBe("≈ 0");
		expect(geometry.labels.riskState).toBeNull();
		expect(geometry.tone).toBe("negative");
	});
});

describe("единая заливка отрицательного итога", () => {
	// Отрицательный итог рисуется одним красным тоном от нуля до маркера
	// поверх фона плановой зоны риска, без разложения на реализованную и
	// нереализованную части — у заливки итога нет отдельных оттенков.
	// Traceability: openspec:ui/screens#scenario-finresult-negative-total-single-fill
	it("итог −135 — единая заливка от −135 до нуля", () => {
		// Act: реализованный −100, нереализованный −35, итог −135.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -100, unrealized: -35 }),
		);

		// Assert: заливка — один диапазон от итога до нуля шкалы.
		expect(geometry.tone).toBe("negative");
		expect(geometry.fillMain).toEqual({ from: 165 / 1200, to: 0.25 });

		// Assert: нереализованная часть не выделяется отдельной заливкой —
		// конец заливки совпадает с маркером итога.
		expect(geometry.markerAt).toBeCloseTo(165 / 1200, 4);
	});
});

describe("внешние корректировки в текущем результате", () => {
	// Канон «текущего результата»: реализованная часть + доступная
	// нереализованная + внешние корректировки PnL. Маркер стоит на полной
	// сумме и подписывается ею же; шкала, золотая зона и заливки строятся
	// от того же total.
	// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-includes-adjustments

	it("маркер включает корректировки: +200 +150 +50 → +400", () => {
		// Act: реализованный +200, нереализованный +150, корректировки +50.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, unrealized: 150, adjustments: 50 }),
		);

		// Assert: маркер подписан полным результатом +400.
		expect(geometry.labels.marker).toBe("+400");

		// Assert: позиция маркера соответствует total +400 на шкале
		// −300…+900, клипа нет.
		expect(geometry.markerAt).toBeCloseTo(700 / 1200, 4);
		expect(geometry.scaleMax).toBe(900);

		// Assert: заливка от нуля до +400, золота нет — профит не превышен.
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 700 / 1200 });
		expect(geometry.superZone).toBeNull();
		expect(geometry.tone).toBe("positive");
	});

	it("свежая позиция с корректировками подписывается суммой, а не «≈ 0»", () => {
		// Act: реализованного и нереализованного нет, корректировки +50.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 0, unrealized: 0, adjustments: 50 }),
		);

		// Assert: «≈ 0» остаётся только при нулевом полном результате —
		// с корректировками подпись обычная сумма.
		expect(geometry.labels.marker).toBe("+50");
		expect(geometry.markerAt).toBeCloseTo(350 / 1200, 4);
	});

	// Сбой котировок: индикатор помечается неполным, а маркер остаётся
	// подписанным доступной частью результата — реализованным и
	// корректировками, скрытая нереализованная часть в подпись не попадает.
	// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-incomplete-with-adjustments
	it("сбой котировок оставляет результат по доступной части: +250", () => {
		// Act: реализованный +200, скрытая нереализованная +150,
		// корректировки +50.
		const geometry = computeFinResultGeometry(
			caseInput({
				realized: 200,
				unrealized: null,
				adjustments: 50,
				quotesDegraded: true,
				hasOpenResidual: true,
			}),
		);

		// Assert: индикатор неполный, маркер подписан доступной частью +250.
		expect(geometry.incomplete).toBe(true);
		expect(geometry.labels.marker).toBe("+250");

		// Assert: позиция маркера и заливка соответствуют доступной части
		// +250, скрытые +150 в геометрию не попали.
		expect(geometry.markerAt).toBeCloseTo(550 / 1200, 4);
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 550 / 1200 });
	});
});

describe("кейс C6 · сверхприбыль: итог превысил профит", () => {
	// Реализованная 150, нереализованная 1250, итог +1400 за +900: золотая
	// зона тянется до самого итога (+1 400) без капа, маркер стоит на
	// правом краю шкалы и подписан итогом.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker

	it("золотая зона до итога +1 400, маркер на краю без клипа", () => {
		// Act.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 150, unrealized: 1250 }),
		);

		// Assert: шкала растянута до итога, золото — от профита до края.
		expect(geometry.scaleMax).toBe(1400);
		expect(geometry.riskZone?.to).toBeCloseTo(300 / 1700, 4);
		expect(geometry.profitZone?.to).toBeCloseTo(1200 / 1700, 4);

		// Assert: зелёный участок — до профита, золото за ним от профита до
		// итога: слои не перекрываются.
		expect(geometry.fillMain).toEqual({ from: 300 / 1700, to: 1200 / 1700 });
		expect(geometry.superZone).toEqual({ from: 1200 / 1700, to: 1 });

		// Assert: маркер на краю шкалы — шкала охватывает итог, клипа нет.
		expect(geometry.markerAt).toBe(1);
	});

	it("итог ровно на плановом профите: золота нет, зелёная заливка до плана", () => {
		// Act: реализованная 150 и нереализованная 750 дают итог ровно +900.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 150, unrealized: 750 }),
		);

		// Assert: равенство не считается выходом за профит — золота нет,
		// шкала остаётся −300…+900.
		expect(geometry.superZone).toBeNull();
		expect(geometry.scaleMax).toBe(900);
		expect(geometry.profitZone?.to).toBeCloseTo(1, 4);

		// Assert: зелёная заливка от нуля до правого края плана, маркер —
		// итог на профите.
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 1 });
		expect(geometry.markerAt).toBeCloseTo(1, 4);
	});
});

describe("охват шкалы без истории маркеров", () => {
	// Тест удаления: шкала больше не охватывает маркеры истории — охват
	// строится только по плановым границам и итогу текущего результата;
	// точки и бейджи истории выведены из геометрии
	// (ADR-0024: тесты — живая спецификация поведения).
	// Traceability: issue:#89
	it("шкала строится по плановым границам и итогу", () => {
		// Act: плановый риск 400, итог +950 за профитом.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: 400, realized: 950, unrealized: null }),
		);

		// Assert: минимум — плановый риск, максимум — итог.
		expect(geometry.scaleMin).toBe(-400);
		expect(geometry.scaleMax).toBe(950);

		// Assert: плановая зона риска — от планового риска до нуля.
		expect(geometry.riskZone).toEqual({ from: 0, to: 400 / 1350 });
	});
});

describe("разнос и прижатие текстовых меток", () => {
	// Порядок перебора мест — уровни внешним циклом, стороны внутри: при
	// коллизии метка сначала уходит на противоположную сторону и лишь затем
	// на следующий уровень предпочитаемой стороны.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	it("коллизия на предпочитаемой стороне уводит метку на противоположную", () => {
		// Act: итог +50 и метка профита на одной стороне — нижний уровень
		// занят маркером, деление уходит наверх.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 50 }),
			LAYOUT,
		);

		// Assert: маркер на нижнем уровне 0, деление профита — над полосой у
		// правого края шкалы (профит — её максимум).
		expect(geometry.placements?.marker).toEqual({ side: "below", level: 0, align: "center" });
		expect(geometry.placements?.profit).toEqual({ side: "above", level: 0, align: "end" });
	});

	it("крайние метки прижимаются к краям шкалы изнутри", () => {
		// Act: итог +5 000 — маркер на правом краю, деление риска слева.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 0, unrealized: 5000 }),
			LAYOUT,
		);

		// Assert: маркер «+5 000» выровнен по правому краю, деление риска —
		// по левому.
		expect(geometry.placements?.marker).toEqual({ side: "below", level: 0, align: "end" });
		expect(geometry.placements?.risk).toEqual({ side: "above", level: 0, align: "start" });
	});
});

describe("состояния риска", () => {
	// Не конечный риск (unbounded/unavailable) не изображается конечной
	// границей: состояние выводится текстом вне шкалы, плановое деление
	// подписывается плановым риском, маркер итога сохраняется.
	// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
	// Traceability: openspec:ui/screens#scenario-finresult-unavailable-risk
	it("unbounded: статус вне шкалы, маркер итога сохранён", () => {
		// Act: итог +5 942.16 при плановом риске 400 — хвост коротких коллов
		// не ограничен.
		const geometry = computeFinResultGeometry(
			caseInput({
				plannedRisk: 400,
				realized: 5942.16,
				unrealized: null,
				realRiskStatus: "unbounded",
			}),
		);

		// Assert: состояние вынесено наружу, плановое деление названо
		// плановым риском.
		expect(geometry.realRiskStatus).toBe("unbounded");
		expect(geometry.labels.riskState).toBe("риск не ограничен");
		expect(geometry.labels.risk).toBe("плановый риск −400");

		// Assert: итог на краю шкалы; шкала охватывает итог.
		expect(geometry.markerAt).toBe(1);
		expect(geometry.labels.marker).toBe("+5\u00A0942.16");
		expect(geometry.tone).toBe("positive");

		// Assert: золотая зона от профита +900 до итога, зона риска до нуля.
		expect(geometry.superZone).toEqual({ from: 1300 / 6342.16, to: 1 });
		expect(geometry.riskZone).toEqual({ from: 0, to: 400 / 6342.16 });
	});

	it("unavailable: статус «не удалось рассчитать», плановое деление подписано", () => {
		// Act: расчёт риска не удался при плановом риске 300.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, unrealized: null, realRiskStatus: "unavailable" }),
		);

		// Assert: статус «не удалось рассчитать», плановое деление —
		// плановый риск −300, маркер итога +200 на своём месте.
		expect(geometry.realRiskStatus).toBe("unavailable");
		expect(geometry.labels.riskState).toBe("не удалось рассчитать");
		expect(geometry.labels.risk).toBe("плановый риск −300");
		expect(geometry.markerAt).toBeCloseTo(500 / 1200, 4);
		expect(geometry.labels.marker).toBe("+200");
	});

	// Без плановых границ полоса нейтральная: зон и маркера итога нет,
	// состояние риска сохраняется отдельно от полосы.
	// Traceability: openspec:ui/screens#scenario-finresult-neutral-without-params
	// Traceability: openspec:ui/screens#scenario-finresult-risk-state-without-plan
	it("нейтральная полоса без плановых параметров", () => {
		// Act: границы не настроены.
		const geometry = computeFinResultGeometry(
			caseInput({
				plannedRisk: null,
				plannedProfit: null,
				realized: -50,
				unrealized: 30,
			}),
		);

		// Assert: полоса без зон, заполнение от нуля до края по знаку итога.
		expect(geometry.neutral).toBe(true);
		expect(geometry.riskZone).toBeNull();
		expect(geometry.profitZone).toBeNull();
		expect(geometry.superZone).toBeNull();
		expect(geometry.zeroAt).toBeCloseTo(0.5, 3);
		expect(geometry.fillMain).toEqual({ from: 0, to: 0.5 });
		expect(geometry.labels.risk).toBeNull();
		expect(geometry.labels.profit).toBeNull();
		expect(geometry.labels.marker).toBe("−20");
	});

	// Задан один параметр — шкала односторонняя.
	// Traceability: openspec:ui/screens#scenario-finresult-single-bound
	it("задан только риск: зона риска", () => {
		// Act: профит не задан.
		const geometry = computeFinResultGeometry(
			caseInput({
				plannedRisk: 300,
				plannedProfit: null,
				realized: -150,
				unrealized: null,
			}),
		);

		// Assert: шкала симметрична риску, зона прибыли отсутствует.
		expect(geometry.riskZone).toEqual({ from: 0, to: 0.5 });
		expect(geometry.profitZone).toBeNull();
		expect(geometry.labels.risk).toBe("−300");
		expect(geometry.labels.profit).toBeNull();
	});
});

describe("сбой котировок", () => {
	// Сбой котировок скрывает только нереализованную часть текущего
	// результата.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	it("сбой котировок скрывает только нереализованную часть", () => {
		// Act: сбой котировок при открытых остатках.
		const geometry = computeFinResultGeometry(
			caseInput({
				realized: -250,
				unrealized: null,
				quotesDegraded: true,
				hasOpenResidual: true,
			}),
		);

		// Assert: индикатор неполный, маркер — на доступной части −250.
		expect(geometry.incomplete).toBe(true);
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.labels.marker).toBe("−250");
	});

	it("сбой котировок без открытых остатков — индикатор полный", () => {
		// Act: котировки недоступны, но все остатки закрыты.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null, quotesDegraded: true, hasOpenResidual: false }),
		);

		// Assert: неполноты нет, маркер стоит на доступной части результата.
		expect(geometry.incomplete).toBe(false);
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.labels.marker).toBe("−250");
	});
});

describe("отказ на некорректных входах", () => {
	// Негативные сценарии: NaN и бесконечности ломают доли шкалы — калькулятор
	// отказывается считать, а не выдаёт бессмысленную геометрию.
	it.each([
		["NaN в realized", caseInput({ realized: Number.NaN })],
		["Infinity в unrealized", caseInput({ unrealized: Number.POSITIVE_INFINITY })],
		["NaN в adjustments", caseInput({ adjustments: Number.NaN })],
		["NaN в plannedRisk", caseInput({ plannedRisk: Number.NaN })],
	])("ThrowOn%s", (_name, input) => {
		// Act + Assert: некорректное значение отвергается ошибкой.
		expect(() => computeFinResultGeometry(input)).toThrow();
	});
});
