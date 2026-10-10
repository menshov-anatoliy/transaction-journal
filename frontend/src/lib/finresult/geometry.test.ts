import { describe, expect, it } from "vitest";
import {
	computeFinResultGeometry,
	type FinResultInput,
} from "./geometry";

// Калькулятор геометрии индикатора финрезультата: чистая математика в долях
// шкалы, без React и DOM. Эталоны кейсов C1–C7 взяты 1:1 из дизайн-макета
// (стенд Y5nap «Индикатор финрезультата · 7 кейсов», полный индикатор
// 936 px, компактный 132 px): доли пересчитаны вручную из координат макета
// и продублированы модельным пересчётом из подписей (риск/профит/результат),
// чтобы эталон не зависел от тестируемой формулы.
// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

// Полный индикатор макета: ширина полосы 936 px.
const FULL_WIDTH = 936;

// Допуск сверки с макетом: около процента шкалы — дизайнер ставил элементы
// с точностью до нескольких пикселей (ширина линии 2 px, маркера 8–12 px).
const LAYOUT_TOLERANCE = 0.008;

// Сверяет долю модели с координатой центра элемента макета.
function expectMatchesLayout(actual: number, px: number, width: number = FULL_WIDTH): void {
	expect(Math.abs(actual - px / width)).toBeLessThan(LAYOUT_TOLERANCE);
}

// Базовый вход кейсов стенда: плановый риск 300, плановый профит 900 —
// как в подписях шкалы «−300 / 0 / +900» всех кейсов макета.
function caseInput(overrides: Partial<FinResultInput>): FinResultInput {
	return {
		plannedRisk: 300,
		plannedProfit: 900,
		realized: 0,
		unrealized: 0,
		quotesDegraded: false,
		...overrides,
	};
}

// Входы кейсов C1–C7: реализованный и нереализованный результат восстановлены
// из подписей и координат макета (граница = realized, маркер = итог).
const CASES = {
	c1: caseInput({ realized: 0, unrealized: 0 }),
	c2: caseInput({ realized: -250, unrealized: 0 }),
	c3: caseInput({ realized: -250, unrealized: 180 }),
	c4: caseInput({ realized: -250, unrealized: 385 }),
	c5: caseInput({ realized: 150, unrealized: 699 }),
	c6: caseInput({ realized: 150, unrealized: 1250 }),
	c7: caseInput({ realized: 945, unrealized: 255 }),
} as const;

describe("кейс C1 · только что открыли", () => {
	// Свежая позиция: сделок закрытия нет, нереализованная ≈ 0 (комиссии и
	// спред). Маркер стоит у нуля: ликвидация сейчас вернёт ≈ 0, весь
	// реальный риск впереди — граница уходит на левый край планового риска.
	// Решение владельца тикета: маркер свежей позиции = «≈ 0», альтернатива
	// «итог − реальныйРиск» (маркер на −300) отклонена.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции
	// Traceability: issue:#62

	it("маркер у нуля, граница на левом краю планового риска", () => {
		// Act: свежая конструкция без реализованного результата.
		const geometry = computeFinResultGeometry(CASES.c1);

		// Assert: маркер — позиция итога ≈ 0 (макет: центр 230 px).
		expect(geometry.markerAt).toBeCloseTo(0.25, 3);
		expectMatchesLayout(geometry.markerAt ?? 0, 230);

		// Assert: граница — весь плановый риск впереди, левый край зоны риска.
		expect(geometry.borderAt).toBeCloseTo(0, 3);
		expectMatchesLayout(geometry.borderAt ?? 0, 1);

		// Assert: шкала и зоны из подписей «−300 / 0 / +900».
		expect(geometry.zeroAt).toBeCloseTo(0.25, 3);
		expect(geometry.riskZone).toEqual({ from: 0, to: 0.25 });
		expect(geometry.profitZone).toEqual({ from: 0.25, to: 1 });
		expect(geometry.superZone).toBeNull();
	});

	it("подписи: риск есть 100%, маркер «≈ 0»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c1);

		// Assert: подписи кейса 1:1 из макета.
		expect(geometry.labels.risk).toBe("−300");
		expect(geometry.labels.profit).toBe("+900");
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("300 USDT · 100%");
		expect(geometry.labels.marker).toBe("≈ 0");
		expect(geometry.tone).toBe("negative");
	});
});

describe("кейс C2 · результат в зоне риска, прибыли нет", () => {
	// Реализованный убыток 250: граница и маркер совпадают на −250,
	// нереализованной нет — точка отсчёта реальный риск, а не плановый.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("граница и маркер на −250, заливка до нуля", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c2);

		// Assert: граница = позиция realized −250 (макет: центр 40 px).
		expect(geometry.borderAt).toBeCloseTo(50 / 1200, 4);
		expectMatchesLayout(geometry.borderAt ?? 0, 40);

		// Assert: маркер = итог −250, совпадает с границей (макет: центр 45 px).
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 45);
		expect(geometry.markerClipped).toBe(false);

		// Assert: заливка основным тоном от итога до нуля, нереализованной нет.
		expect(geometry.fillMain).toEqual({ from: 50 / 1200, to: 0.25 });
		expect(geometry.fillUnreal).toBeNull();
	});

	it("подписи: риска осталось 250 (83%), маркер «0»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c2);

		// Assert: подписи 1:1 из макета.
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("250 USDT · 83%");
		expect(geometry.labels.marker).toBe("0");
		expect(geometry.tone).toBe("negative");
	});
});

describe("кейс C3 · нереализованная 180 меньше риска", () => {
	// Реализованный убыток 250, нереализованная прибыль 180: маркер правее
	// границы на величину нереализованной, между ними бледная заливка.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("маркер на итоге −70 правее границы", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c3);

		// Assert: граница −250, маркер — итог −70 (макет: центр 178.7 px).
		expect(geometry.borderAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.markerAt).toBeCloseTo(230 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 178.7);

		// Assert: бледная заливка нереализованной части — от границы до итога.
		expect(geometry.fillUnreal).toEqual({ from: 50 / 1200, to: 230 / 1200 });
		expect(geometry.fillMain).toEqual({ from: 230 / 1200, to: 0.25 });
	});

	it("подписи: маркер «+180» — значение нереализованной", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c3);

		// Assert: у маркера подпись нереализованной, итог ещё отрицательный.
		expect(geometry.labels.marker).toBe("+180");
		expect(geometry.labels.borderValue).toBe("250 USDT · 83%");
		expect(geometry.tone).toBe("negative");
	});
});

describe("кейс C4 · суммарный результат выше планового риска", () => {
	// Реализованный убыток 250, нереализованная прибыль 385: итог +135
	// выше планового риска 300 — маркер зелёный, в зоне прибыли.
	// Примечание: позиция границы в референсе onchD не переопределена кейсом
	// и наследует черновые координаты компонента — эталоном служат подписи
	// и позиция маркера.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("маркер зелёный на итоге +135", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c4);

		// Assert: маркер = итог +135 (макет: центр 333.3 + 6 = 339.3 px).
		expect(geometry.markerAt).toBeCloseTo(435 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 339.3);
		expect(geometry.tone).toBe("positive");

		// Assert: бледная заливка от границы −250 до итога +135.
		expect(geometry.fillUnreal).toEqual({ from: 50 / 1200, to: 435 / 1200 });
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 435 / 1200 });
	});

	it("подписи: маркер «+385», риска осталось 250", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c4);

		// Assert: подписи 1:1 из макета.
		expect(geometry.labels.marker).toBe("+385");
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("250 USDT · 83%");
	});
});

describe("кейс C5 · нереализованная приближается к целевому профиту", () => {
	// Реализованная прибыль 150 (риска нет), нереализованная 699:
	// маркер в зоне прибыли у целевого профита, золотой зоны ещё нет.
	// Примечание: подпись маркера «+1 100» в макете противоречит позиции
	// маркера (896.2 px = +849), отсутствию золотой зоны и формулировке
	// «приближается к целевому профиту» — признана устаревшей, эталон —
	// геометрия макета.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("граница +150, маркер +849 в зоне прибыли", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c5);

		// Assert: граница realized +150 (макет: центр 352 px).
		expect(geometry.borderAt).toBeCloseTo(450 / 1200, 4);
		expectMatchesLayout(geometry.borderAt ?? 0, 352);

		// Assert: маркер — итог +849 (макет: центр 896.2 px).
		expect(geometry.markerAt).toBeCloseTo(1149 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 896.2);
		expect(geometry.superZone).toBeNull();
		expect(geometry.tone).toBe("positive");
	});

	it("подписи: риска нет 150 (50%), маркер «+699»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c5);

		// Assert: подписи границы 1:1 из макета; маркер — нереализованная.
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("150 USDT · 50%");
		expect(geometry.labels.marker).toBe("+699");
	});
});

describe("кейс C6 · сверхприбыль: нереализованная превысила профит", () => {
	// Реализованная 150, нереализованная 1250, итог +1400 за +900: шкала
	// растягивается золотой зоной до половины превышения (+1 150), маркер
	// клипуется по правому краю, значение выносится числом «+1 400».
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("золотая зона до +1 150, маркер клипован", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c6);

		// Assert: зоны растянутой шкалы (макет: риск 193.7 px, профит до 774.6 px).
		expect(geometry.scaleMax).toBe(1150);
		expect(geometry.riskZone?.to).toBeCloseTo(300 / 1450, 4);
		expectMatchesLayout(geometry.riskZone?.to ?? 0, 193.7);
		expect(geometry.profitZone?.to).toBeCloseTo(1200 / 1450, 4);
		expectMatchesLayout(geometry.profitZone?.to ?? 0, 774.6);
		expect(geometry.superZone).toEqual({ from: 1200 / 1450, to: 1 });

		// Assert: граница +150 (макет: центр 291.5 px), маркер клипован на краю.
		expect(geometry.borderAt).toBeCloseTo(450 / 1450, 4);
		expectMatchesLayout(geometry.borderAt ?? 0, 291.5);
		expect(geometry.markerAt).toBe(1);
		expect(geometry.markerClipped).toBe(true);
	});

	it("подписи: маркер «+1 400» вынесен числом, край золота «+1 150»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c6);

		// Assert: при клипе маркера выносится итог, край золота подписан.
		expect(geometry.labels.marker).toBe("+1\u00A0400");
		// superEnd упразднён (спец-случай «половины превышения» удалён);
		// эталоны обновит задача 2.4 (блок 4).
		// expect(geometry.labels.superEnd).toBe("+1\u00A0150");
		expect(geometry.labels.profit).toBe("+900");
		expect(geometry.tone).toBe("positive");
	});
});

describe("кейс C7 · реализованная выше целевого профита", () => {
	// Реализованная 945 за +900, нереализованная 255, итог +1 200: золотая
	// зона тянется до границы (+945), граница — в золотой зоне, маркер
	// клипуется по правому краю со значением «+1 200».
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("золотая зона до границы +945, маркер клипован", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c7);

		// Assert: зоны (макет: риск 224.6 px, профит до 897.5 px).
		expect(geometry.scaleMax).toBe(945);
		expect(geometry.riskZone?.to).toBeCloseTo(300 / 1245, 4);
		expectMatchesLayout(geometry.riskZone?.to ?? 0, 224.6);
		expect(geometry.profitZone?.to).toBeCloseTo(1200 / 1245, 4);
		expectMatchesLayout(geometry.profitZone?.to ?? 0, 897.5);
		expect(geometry.superZone).toEqual({ from: 1200 / 1245, to: 1 });

		// Assert: граница realized на краю золотой зоны (макет: центр 933 px).
		expect(geometry.borderAt).toBe(1);
		expectMatchesLayout(geometry.borderAt ?? 0, 933);
		expect(geometry.markerAt).toBe(1);
		expect(geometry.markerClipped).toBe(true);
	});

	it("подписи: риска нет 945 (315%), маркер «+1 200»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c7);

		// Assert: подписи 1:1 из макета.
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("945 USDT · 315%");
		expect(geometry.labels.marker).toBe("+1\u00A0200");
		// superEnd упразднён; эталоны обновит задача 2.4 (блок 4).
		// expect(geometry.labels.superEnd).toBe("+945");
	});
});

describe("сверка кейсов с компактным индикатором макета", () => {
	// Компактный индикатор строится из той же геометрии: доли не зависят от
	// размера представления. Пропорции зон компактного референса макета
	// (44:88 px) дизайнер задал визуально шире риска, чем масштаб данных
	// 300:900, поэтому пиксельная сверка компактного не применяется —
	// сверяются структурные свойства представления.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it.each([
		["C2", CASES.c2],
		["C3", CASES.c3],
		["C4", CASES.c4],
		["C5", CASES.c5],
	])("%s: одна геометрия на полный и компактный виды", (_name, input) => {
		// Act: геометрия считается один раз на все представления.
		const geometry = computeFinResultGeometry(input);

		// Assert: маркер в пределах шкалы, структура зон согласована.
		expect(geometry.markerAt).not.toBeNull();
		expect((geometry.markerAt ?? 0)).toBeGreaterThanOrEqual(0);
		expect((geometry.markerAt ?? 0)).toBeLessThanOrEqual(1);
	});
});

describe("состояния индикатора из §9 концепта", () => {
	// Шесть состояний: нейтральная полоса, единственная зона, клип по
	// превышению, нет остатков, сбой котировок, закрытая конструкция.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("состояние 1: риск и профит не заданы — нейтральная полоса без зон", () => {
		// Act: границы конструкции не настроены.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: null, plannedProfit: null, realized: -50, unrealized: 30 }),
		);

		// Assert: полоса без зон, ноль в центре, заполнение от нуля до края
		// по знаку итога — пропорция без плановых границ не определена.
		expect(geometry.neutral).toBe(true);
		expect(geometry.riskZone).toBeNull();
		expect(geometry.profitZone).toBeNull();
		expect(geometry.superZone).toBeNull();
		expect(geometry.borderAt).toBeNull();
		expect(geometry.zeroAt).toBeCloseTo(0.5, 3);
		expect(geometry.fillMain).toEqual({ from: 0, to: 0.5 });
		expect(geometry.labels.risk).toBeNull();
		expect(geometry.labels.profit).toBeNull();
		expect(geometry.labels.marker).toBe("−20");
	});

	it("состояние 2a: задан только риск — единственная зона риска", () => {
		// Act: профит не задан.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: 300, plannedProfit: null, realized: -150, unrealized: null }),
		);

		// Assert: шкала симметрична риску, зона прибыли отсутствует.
		expect(geometry.riskZone).toEqual({ from: 0, to: 0.5 });
		expect(geometry.profitZone).toBeNull();
		expect(geometry.labels.risk).toBe("−300");
		expect(geometry.labels.profit).toBeNull();
		expect(geometry.markerAt).toBeCloseTo(0.25, 3);
	});

	it("состояние 2b: задан только профит — единственная зона прибыли", () => {
		// Act: риск не задан.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: null, plannedProfit: 900, realized: 200, unrealized: 100 }),
		);

		// Assert: шкала симметрична профиту, зона риска отсутствует.
		expect(geometry.riskZone).toBeNull();
		expect(geometry.profitZone).toEqual({ from: 0.5, to: 1 });
		expect(geometry.labels.risk).toBeNull();
		expect(geometry.labels.profit).toBe("+900");
		expect(geometry.markerAt).toBeCloseTo(1200 / 1800, 3);
	});

	it("состояние 3: итог далеко за профитом — золотая зона клипуется, значение выносится", () => {
		// Act: превышение так велико, что золотая надбавка упирается в порог.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 0, unrealized: 5000 }),
		);

		// Assert: золото не длиннее базовой шкалы, маркер клипован с выносом итога.
		expect(geometry.scaleMax).toBe(2100);
		expect(geometry.superZone).toEqual({ from: 1200 / 2400, to: 1 });
		expect(geometry.markerAt).toBe(1);
		expect(geometry.markerClipped).toBe(true);
		expect(geometry.labels.marker).toBe("+5\u00A0000");
		// superEnd упразднён; эталоны обновит задача 2.4 (блок 4).
		// expect(geometry.labels.superEnd).toBe("+2\u00A0100");
	});

	it("состояние 4: нет открытых остатков — маркер совпадает с границей", () => {
		// Act: нереализованной части нет.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null }),
		);

		// Assert: маркер на границе, бледной заливки нет.
		expect(geometry.markerAt).toBe(geometry.borderAt);
		expect(geometry.fillUnreal).toBeNull();
		expect(geometry.fillMain).toEqual({ from: 50 / 1200, to: 0.25 });
		expect(geometry.labels.marker).toBe("−250");
	});

	it("состояние 5: сбой котировок — «неполный», нереализованная скрыта", () => {
		// Act: котировки недоступны, хотя остатки открыты.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: 180, quotesDegraded: true }),
		);

		// Assert: нереализованная часть скрыта, индикатор помечен неполным.
		expect(geometry.incomplete).toBe(true);
		expect(geometry.markerAt).toBe(geometry.borderAt);
		expect(geometry.fillUnreal).toBeNull();
		// Ноль остаётся нулём, а отсутствие данных не превращается в ноль.
		expect(geometry.labels.marker).toBeNull();
	});

	it("состояние 6: закрытая конструкция — только реализованный результат", () => {
		// Act: остатков нет, реализованная выше целевого профита.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 945, unrealized: null }),
		);

		// Assert: маркер на границе, золотая зона от реализованной, нереализованной нет.
		expect(geometry.incomplete).toBe(false);
		expect(geometry.markerAt).toBe(geometry.borderAt);
		expect(geometry.fillUnreal).toBeNull();
		expect(geometry.superZone).toEqual({ from: 1200 / 1245, to: 1 });
		expect(geometry.labels.marker).toBe("+945");
	});
});

describe("отказ на некорректных входах", () => {
	// Негативные сценарии: NaN и бесконечности ломают доли шкалы — калькулятор
	// отказывается считать, а не выдаёт бессмысленную геометрию.
	it.each([
		["NaN в realized", caseInput({ realized: Number.NaN })],
		["Infinity в unrealized", caseInput({ unrealized: Number.POSITIVE_INFINITY })],
		["NaN в plannedRisk", caseInput({ plannedRisk: Number.NaN })],
	])("ThrowOn%s", (_name, input) => {
		// Act + Assert: некорректное значение отвергается ошибкой.
		expect(() => computeFinResultGeometry(input)).toThrow();
	});
});
