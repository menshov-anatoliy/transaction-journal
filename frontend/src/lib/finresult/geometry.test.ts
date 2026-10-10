import { describe, expect, it } from "vitest";
import {
	computeFinResultGeometry,
	type FinResultInput,
} from "./geometry";

// Калькулятор геометрии индикатора финрезультата: чистая математика в долях
// шкалы, без React и DOM. Кейсы C1–C7 покрывают семантику реального риска:
// граница = реализованный результат − реальный риск, маркер = итог. Доли
// пересчитаны модельно из входов; пиксельные сверки с дизайн-макетом
// (стенд Y5nap, полный индикатор 936 px) оставлены только там, где макет
// согласуется с новой семантикой — прежде всего позиции маркеров.
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
// как в подписях шкалы «−300 / 0 / +900» всех кейсов макета. Остатки
// закрыты (hasOpenResidual: false) — так сбой котировок не делает
// индикатор неполным; кейсы с открытыми остатками переопределяют флаг.
function caseInput(overrides: Partial<FinResultInput>): FinResultInput {
	return {
		plannedRisk: 300,
		plannedProfit: 900,
		realized: 0,
		unrealized: 0,
		quotesDegraded: false,
		hasOpenResidual: false,
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
	c7: caseInput({ realized: 950, realRisk: 0, unrealized: -100 }),
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
	// Реализованный убыток 250: граница реального риска уходит за левый
	// край шкалы (−550 = −250 − 300) и прижимается к краю, маркер стоит
	// на итоге −250 — точка отсчёта реальный риск, а не плановый.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции
	// Traceability: openspec:ui/screens#scenario-finresult-border-left-clip

	it("граница прижата к левому краю, маркер на итоге −250", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c2);

		// Assert: граница −550 за левым краем шкалы — насечка прижата к краю.
		expect(geometry.borderAt).toBe(0);

		// Assert: маркер = итог −250 (макет: центр 45 px).
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 45);
		expect(geometry.markerClipped).toBe(false);

		// Assert: единая заливка итога — от итога до нуля.
		expect(geometry.fillMain).toEqual({ from: 50 / 1200, to: 0.25 });
	});

	it("подписи: риск есть 300 USDT · 100%, маркер «−250»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c2);

		// Assert: граница подписывается величиной реального риска (300),
		// а не остатком до неё; маркер — итог.
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("300 USDT · 100%");
		expect(geometry.labels.marker).toBe("−250");
		expect(geometry.tone).toBe("negative");
	});
});

describe("кейс C3 · нереализованная 180 меньше риска", () => {
	// Реализованный убыток 250, нереализованная прибыль 180: граница
	// прижата к левому краю (−550), маркер — итог −70, между краем и
	// итогом бледная заливка нереализованной части.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("маркер на итоге −70 правее прижатой границы", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c3);

		// Assert: граница −550 прижата к левому краю, маркер — итог −70
		// (макет: центр 178.7 px).
		expect(geometry.borderAt).toBe(0);
		expect(geometry.markerAt).toBeCloseTo(230 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 178.7);

		// Assert: заливка итога — единый диапазон от нуля до итога −70.
		expect(geometry.fillMain).toEqual({ from: 230 / 1200, to: 0.25 });
	});

	it("подписи: маркер «−70» — итог, граница по реальному риску", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c3);

		// Assert: у маркера подпись итога, граница — реальный риск 300.
		expect(geometry.labels.marker).toBe("−70");
		expect(geometry.labels.borderValue).toBe("300 USDT · 100%");
		expect(geometry.tone).toBe("negative");
	});
});

describe("кейс C4 · суммарный результат выше планового риска", () => {
	// Реализованный убыток 250, нереализованная прибыль 385: итог +135
	// выше нуля — маркер зелёный в зоне прибыли, граница прижата к левому
	// краю (−550).
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("маркер зелёный на итоге +135", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c4);

		// Assert: граница −550 прижата к левому краю.
		expect(geometry.borderAt).toBe(0);

		// Assert: маркер = итог +135 (макет: центр 333.3 + 6 = 339.3 px).
		expect(geometry.markerAt).toBeCloseTo(435 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 339.3);
		expect(geometry.tone).toBe("positive");

		// Assert: заливка итога — единый зелёный диапазон от нуля до +135.
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 435 / 1200 });
	});

	it("подписи: маркер «+135» — итог, граница по реальному риску", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c4);

		// Assert: маркер — итог, граница — реальный риск 300.
		expect(geometry.labels.marker).toBe("+135");
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("300 USDT · 100%");
	});
});

describe("кейс C5 · нереализованная приближается к целевому профиту", () => {
	// Реализованная прибыль 150, нереализованная 699: граница реального
	// риска опускается до −150 (150 − 300), маркер — итог +849 в зоне
	// прибыли у целевого профита, золотой зоны ещё нет.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

	it("граница −150 в зоне риска, маркер +849 у профита", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c5);

		// Assert: граница realized − реальныйРиск = −150.
		expect(geometry.borderAt).toBeCloseTo(150 / 1200, 4);

		// Assert: маркер — итог +849 (макет: центр 896.2 px).
		expect(geometry.markerAt).toBeCloseTo(1149 / 1200, 4);
		expectMatchesLayout(geometry.markerAt ?? 0, 896.2);
		expect(geometry.superZone).toBeNull();
		expect(geometry.tone).toBe("positive");
	});

	it("подписи: риск есть 300 USDT · 100%, маркер «+849»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c5);

		// Assert: граница подписывается реальным риском (300), маркер — итог.
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("300 USDT · 100%");
		expect(geometry.labels.marker).toBe("+849");
	});
});

describe("граница сверхприбыли · итог ровно на плановом профите", () => {
	// Золотая зона возникает только при выходе итога за +Профит: на
	// равенстве total === plannedProfit золота ещё нет, шкала не
	// растягивается, зелёная заливка тянется до самого плана.
	// Traceability: openspec:ui/screens#requirement-risk-profit-hint

	it("итог +900 равен профиту: золота нет, зелёная заливка до плана", () => {
		// Act: реализованная 150 и нереализованная 750 дают итог ровно +900.
		const geometry = computeFinResultGeometry(caseInput({ realized: 150, unrealized: 750 }));

		// Assert: равенство не считается выходом за профит — золота нет,
		// шкала остаётся −300…+900.
		expect(geometry.superZone).toBeNull();
		expect(geometry.scaleMax).toBe(900);
		expect(geometry.profitZone?.to).toBeCloseTo(1, 4);

		// Assert: зелёная заливка от нуля до правого края плана, маркер —
		// итог на профите без клипа.
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 1 });
		expect(geometry.markerAt).toBeCloseTo(1, 4);
		expect(geometry.markerClipped).toBe(false);
	});
});

describe("кейс C6 · сверхприбыль: итог превысил профит", () => {
	// Реализованная 150, нереализованная 1250, итог +1400 за +900: золотая
	// зона тянется до самого итога (+1 400) без капа, маркер стоит на
	// правом краю шкалы без клипа и подписан итогом.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker

	it("золотая зона до итога +1 400, маркер на краю без клипа", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c6);

		// Assert: шкала растянута до итога, золото — от профита до края.
		expect(geometry.scaleMax).toBe(1400);
		expect(geometry.riskZone?.to).toBeCloseTo(300 / 1700, 4);
		expect(geometry.profitZone?.to).toBeCloseTo(1200 / 1700, 4);

		// Assert: зелёный участок — до профита, золото за ним от профита до
		// итога: слои не перекрываются.
		expect(geometry.fillMain).toEqual({ from: 300 / 1700, to: 1200 / 1700 });
		expect(geometry.superZone).toEqual({ from: 1200 / 1700, to: 1 });

		// Assert: граница −150, маркер на краю шкалы, клипа нет.
		expect(geometry.borderAt).toBeCloseTo(150 / 1700, 4);
		expect(geometry.markerAt).toBe(1);
		expect(geometry.markerClipped).toBe(false);
	});

	it("подписи: маркер «+1 400» — итог на краю шкалы", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c6);

		// Assert: маркер подписан итогом, деления остаются плановыми.
		expect(geometry.labels.marker).toBe("+1\u00A0400");
		expect(geometry.labels.profit).toBe("+900");
		expect(geometry.tone).toBe("positive");
	});
});

describe("кейс C7 · граница реального риска правее плана: сверхприбыли нет", () => {
	// Реализованная 950 за +900 при нулевом реальном риске, итог +850
	// (нереализованная −100): граница правее планового профита не создаёт
	// золотую зону и не растягивает шкалу — насечка клипуется по правому
	// краю +900, истинное значение остаётся в подписи границы.
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-border

	it("золотой зоны нет, шкала остаётся на +900, граница прижата к краю", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c7);

		// Assert: шкала не расширена, золотой зоны нет — граница не тянет
		// сверхприбыль, правый край шкалы остаётся на +900.
		expect(geometry.scaleMax).toBe(900);
		expect(geometry.profitZone?.to).toBeCloseTo(1, 4);
		expect(geometry.superZone).toBeNull();

		// Assert: насечка +950 за краем шкалы прижата к правому краю,
		// маркер — итог +850 внутри шкалы.
		expect(geometry.borderAt).toBe(1);
		expect(geometry.markerAt).toBeCloseTo(1150 / 1200, 4);
		expect(geometry.markerClipped).toBe(false);

		// Assert: зелёная заливка итога — от нуля до маркера.
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 1150 / 1200 });
	});

	it("подписи: риска нет 0 USDT · 0%, маркер «+850»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c7);

		// Assert: у нулевого реального риска нет и процентов, маркер — итог.
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("0 USDT · 0%");
		expect(geometry.labels.marker).toBe("+850");
		expect(geometry.tone).toBe("positive");
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

describe("граница реального риска", () => {
	// Граница = реализованный результат − реальный риск: где конструкция
	// окажется при худшем исходе открытых остатков с учётом закрытых сделок.
	// Traceability: openspec:ui/screens#scenario-finresult-border-real-risk

	it("реальный риск 150: граница между realized и нулём", () => {
		// Act: реализованная +200, реальный риск 150 → граница +50.
		const geometry = computeFinResultGeometry(caseInput({ realized: 200, realRisk: 150 }));

		// Assert: граница на +50, маркер на итоге +200.
		expect(geometry.borderAt).toBeCloseTo(350 / 1200, 4);
		expect(geometry.markerAt).toBeCloseTo(500 / 1200, 4);

		// Assert: подпись границы — величина реального риска в USDT и процентах.
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("150 USDT · 50%");
		expect(geometry.labels.marker).toBe("+200");
	});

	// Свежая позиция: ликвидация сейчас вернёт около нуля, а весь реальный
	// риск (120) впереди — граница стоит на −реальныйРиск.
	// Traceability: openspec:ui/screens#scenario-finresult-fresh-position-real-risk
	it("свежая позиция: маркер «≈ 0», граница на −реальныйРиск", () => {
		// Act: сделок закрытия нет, метрика реального риска доступна.
		const geometry = computeFinResultGeometry(caseInput({ realized: 0, realRisk: 120 }));

		// Assert: маркер у нуля, граница −120 не совпадает с левым краем шкалы.
		expect(geometry.markerAt).toBeCloseTo(0.25, 3);
		expect(geometry.labels.marker).toBe("≈ 0");
		expect(geometry.borderAt).toBeCloseTo(180 / 1200, 4);

		// Assert: подпись границы — доступная метрика 120 USDT · 40%.
		expect(geometry.labels.borderValue).toBe("120 USDT · 40%");
	});

	// Нет метрики реального риска — граница считается по заглушке плановым
	// риском: поле realRisk опционально для вызывающих компонентов.
	// Traceability: openspec:ui/screens#scenario-finresult-real-risk-fallback-planned
	it("realRisk недоступен: заглушка плановым риском 300", () => {
		// Act: поле realRisk не передано вызывающим компонентом.
		const geometry = computeFinResultGeometry(caseInput({ realized: 100 }));

		// Assert: граница 100 − 300 = −200, подпись по плановому риску.
		expect(geometry.borderAt).toBeCloseTo(100 / 1200, 4);
		expect(geometry.labels.borderTitle).toBe("риск есть");
		expect(geometry.labels.borderValue).toBe("300 USDT · 100%");
	});

	// Сверхприбыль: золотая зона тянется до итога без капа, маркер остаётся
	// на краю шкалы и подписывается итогом.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	it("итог за профитом: золотая зона тянется до итога, маркер без клипа", () => {
		// Act: итог +1250 за профитом +900, реальный риск 50.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, unrealized: 1050, realRisk: 50 }),
		);

		// Assert: шкала растянута до итога, золото — от профита до края.
		expect(geometry.scaleMax).toBe(1250);
		expect(geometry.superZone).toEqual({ from: 1200 / 1550, to: 1 });
		expect(geometry.markerAt).toBe(1);
		expect(geometry.markerClipped).toBe(false);

		// Assert: маркер подписан итогом «+1 250».
		expect(geometry.labels.marker).toBe("+1\u00A0250");
	});
});

describe("разнос и прижатие меток", () => {
	// Метрики разметки пресета: полоса 936 px, символ над полосой 7 px,
	// под полосой 6.5 px — как в полном индикаторе макета.
	const LAYOUT = { scaleWidthPx: 936, aboveCharWidthPx: 7, belowCharWidthPx: 6.5 };

	// Граница и маркер совпали на одной позиции: маркер выигрывает нижний
	// уровень, граница по правилу коллизии уходит на противоположную сторону,
	// деление нуля опускается уровнем ниже — наплыва нет.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	it("совпавшие граница и маркер разносятся без наплыва", () => {
		// Act: реализованная +200, нереализованная −150, реальный риск 150 —
		// итог +50 и граница +50 совпали на одной позиции.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, unrealized: -150, realRisk: 150 }),
			LAYOUT,
		);

		// Assert: маркер на нижнем уровне 0, граница — на противоположной
		// стороне уровня 0, деление нуля ушло под полосу на свободное место.
		expect(geometry.placements?.marker).toEqual({ side: "below", level: 0, align: "center" });
		expect(geometry.placements?.border).toEqual({ side: "above", level: 0, align: "center" });
		expect(geometry.placements?.zero).toEqual({ side: "below", level: 0, align: "center" });
	});

	// Порядок перебора мест — уровни внешним циклом, стороны внутри: при
	// коллизии метка сначала уходит на противоположную сторону и лишь затем
	// на следующий уровень предпочитаемой стороны.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	it("коллизия на предпочитаемой стороне уводит метку на противоположную, а не уровнем ниже", () => {
		// Act: итог +420 на x≈0.6, граница +100 на той же позиции — нижний
		// уровень занят маркером.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 520, unrealized: -100, realRisk: 100 }),
			LAYOUT,
		);

		// Assert: маркер остался на нижнем уровне 0, граница ушла наверх
		// (выше деления нуля нет коллизии), а не на нижний уровень 1.
		expect(geometry.placements?.marker).toEqual({ side: "below", level: 0, align: "center" });
		expect(geometry.placements?.border).toEqual({ side: "above", level: 0, align: "center" });
	});

	// Крайние метки прижимаются к краям шкалы изнутри, габариты не растут.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-edge-flush
	it("крайние метки прижимаются к краям шкалы изнутри", () => {
		// Act: итог +5 000 — маркер на правом краю, граница прижата слева.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 0, unrealized: 5000 }),
			LAYOUT,
		);

		// Assert: маркер «+5 000» выровнен по правому краю, деление риска и
		// граница «300 USDT · 100%» — по левому.
		expect(geometry.placements?.marker).toEqual({ side: "below", level: 0, align: "end" });
		expect(geometry.placements?.risk).toEqual({ side: "above", level: 0, align: "start" });
		expect(geometry.placements?.border).toEqual({ side: "below", level: 0, align: "start" });
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
		// Граница по заглушке плановым риском уходит за левый край — клип.
		expect(geometry.borderAt).toBe(0);
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
		// Заглушка границы плановым профитом: 200 − 900 = −700.
		expect(geometry.borderAt).toBeCloseTo(200 / 1800, 4);
	});

	it("состояние 3: итог далеко за профитом — золотая зона клипуется, значение выносится", () => {
		// Act: превышение так велико, что золотая надбавка упирается в порог.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 0, unrealized: 5000 }),
		);

		// Assert: золото тянется до итога без капа, маркер на краю без клипа.
		expect(geometry.scaleMax).toBe(5000);
		expect(geometry.superZone).toEqual({ from: 1200 / 5300, to: 1 });
		expect(geometry.markerAt).toBe(1);
		expect(geometry.markerClipped).toBe(false);
		expect(geometry.labels.marker).toBe("+5\u00A0000");
	});

	it("состояние 4: нет открытых остатков — маркер на итоге, граница по заглушке", () => {
		// Act: нереализованной части нет.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null }),
		);

		// Assert: маркер на итоге −250; граница по заглушке плановым риском
		// (−550) прижата к левому краю — маркер и граница не совпадают.
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.borderAt).toBe(0);
		expect(geometry.fillMain).toEqual({ from: 50 / 1200, to: 0.25 });
		expect(geometry.labels.marker).toBe("−250");
	});

	// Сбой котировок скрывает нереализованную часть открытых остатков, но не
	// убирает границу: «неполный» индикатор продолжает показывать реальный
	// риск. Read-модели отдают сбой марок вместе с null нереализованной
	// части именно при открытых остатках.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	it("состояние 5: сбой котировок при открытых остатках — «неполный», нереализованная скрыта", () => {
		// Act: котировки недоступны, остатки открыты — нереализованная
		// часть не оценена и придёт как null.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null, quotesDegraded: true, hasOpenResidual: true }),
		);

		// Assert: нереализованная часть скрыта, индикатор помечен неполным;
		// маркер — realized −250, граница по заглушке прижата к левому краю.
		expect(geometry.incomplete).toBe(true);
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.borderAt).toBe(0);
		// Граница со своей подписью остаётся: риск виден и без котировок.
		expect(geometry.labels.borderValue).toBe("300 USDT · 100%");
		// Ноль остаётся нулём, а отсутствие данных не превращается в ноль.
		expect(geometry.labels.marker).toBeNull();
	});

	// Сбой котировок без открытых остатков не помечает индикатор неполным:
	// скрывать нечего — нереализованной части не существует. Число маркера
	// всё равно не показывается: при сбое котировок подпись — без числа.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	it("состояние 5а: сбой котировок без открытых остатков — индикатор полный", () => {
		// Act: котировки недоступны, но все остатки закрыты.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null, quotesDegraded: true, hasOpenResidual: false }),
		);

		// Assert: неполноты нет, маркер стоит на реализованном результате,
		// но подписи числа нет — сбой котировок запрещает выводить итог.
		expect(geometry.incomplete).toBe(false);
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.labels.marker).toBeNull();
	});

	// Закрытая конструкция с нулевым реальным риском: граница совпадает
	// с realized, подпись «риска нет» без процентов.
	// Traceability: openspec:ui/screens#scenario-finresult-closed-no-risk
	it("состояние 6: закрытая конструкция — рисков нет, граница на realized", () => {
		// Act: остатков нет, реализованная выше целевого профита, риск 0.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 945, unrealized: null, realRisk: 0 }),
		);

		// Assert: граница = realized 945 на краю шкалы, маркер на ней,
		// золотая зона от реализованной, нереализованной нет.
		expect(geometry.incomplete).toBe(false);
		expect(geometry.borderAt).toBe(1);
		expect(geometry.markerAt).toBe(1);
		// Заливка итога — зелёный участок до профита, золото — за профитом.
		expect(geometry.fillMain).toEqual({ from: 300 / 1245, to: 1200 / 1245 });
		expect(geometry.superZone).toEqual({ from: 1200 / 1245, to: 1 });
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("0 USDT · 0%");
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
		["NaN в realRisk", caseInput({ realRisk: Number.NaN })],
	])("ThrowOn%s", (_name, input) => {
		// Act + Assert: некорректное значение отвергается ошибкой.
		expect(() => computeFinResultGeometry(input)).toThrow();
	});
});
