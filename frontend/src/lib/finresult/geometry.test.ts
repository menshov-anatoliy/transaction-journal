import { describe, expect, it } from "vitest";
import {
	computeFinResultGeometry,
	type FinResultInput,
} from "./geometry";

// Калькулятор геометрии индикатора финрезультата: чистая математика в долях
// шкалы, без React и DOM. Кейсы C1–C7 покрывают семантику худшего исхода:
// граница = worstCaseUsdt (при остаточном риске — «реализованный −
// реальный риск»), маркер = итог. Доли пересчитаны модельно из входов;
// пиксельные сверки с дизайн-макетом (стенд Y5nap, полный индикатор
// 936 px) оставлены только там, где макет согласуется с новой семантикой —
// прежде всего позиции маркеров.
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
// Пара реального риска и худшего исхода обязательна: по умолчанию
// конечный риск 300 — равный плановому, как в границах макета.
function caseInput(overrides: Partial<FinResultInput>): FinResultInput {
	const merged = {
		plannedRisk: 300,
		plannedProfit: 900,
		realized: 0,
		unrealized: 0,
		adjustments: 0,
		quotesDegraded: false,
		hasOpenResidual: false,
		realRisk: 300,
		realRiskStatus: "finite" as const,
		...overrides,
	};

	return {
		...merged,
		// Худший исход кейсов по умолчанию эквивалентен прежней формуле
		// «реализованный − реальный риск»; кейсы гарантированной прибыли
		// остатков задают worstCase явно.
		worstCase:
			overrides.worstCase ??
			(merged.realRiskStatus === "finite" && merged.realRisk !== null
				? (merged.realized ?? 0) - merged.realRisk
				: null),
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

	it("подписи: худший исход 100%, маркер «≈ 0»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c1);

		// Assert: подписи кейса 1:1 из макета (граница подписана значением
		// худшего исхода со знаком).
		expect(geometry.labels.risk).toBe("−300");
		expect(geometry.labels.profit).toBe("+900");
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("−300 USDT · 100%");
		expect(geometry.labels.marker).toBe("≈ 0");
		expect(geometry.tone).toBe("negative");
		expect(geometry.borderTone).toBe("negative");
	});
});

describe("кейс C2 · результат в зоне риска, прибыли нет", () => {
	// Реализованный убыток 250: граница худшего исхода уходит за левый
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

	it("подписи: худший исход −550 USDT · 183%, маркер «−250»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c2);

		// Assert: граница подписывается значением худшего исхода (−550) с
		// честным процентом от планового риска; маркер — итог.
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("−550 USDT · 183%");
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

	it("подписи: маркер «−70» — итог, граница по худшему исходу", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c3);

		// Assert: у маркера подпись итога, граница — худший исход −550.
		expect(geometry.labels.marker).toBe("−70");
		expect(geometry.labels.borderValue).toBe("−550 USDT · 183%");
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

	it("подписи: маркер «+135» — итог, граница по худшему исходу", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c4);

		// Assert: маркер — итог, граница — худший исход −550; тон подписи
		// границы — по знаку исхода (отрицателен), итога — плюс.
		expect(geometry.labels.marker).toBe("+135");
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("−550 USDT · 183%");
		expect(geometry.tone).toBe("positive");
		expect(geometry.borderTone).toBe("negative");
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

	it("подписи: худший исход −150 USDT · 50%, маркер «+849»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c5);

		// Assert: граница подписана значением худшего исхода (−150) с
		// процентом от планового риска, маркер — итог.
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("−150 USDT · 50%");
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

describe("кейс C7 · граница худшего исхода правее плана: сверхприбыли нет", () => {
	// Реализованная 950 за +900 при нулевом реальном риске (худший исход
	// равен реализованному), итог +850 (нереализованная −100): граница
	// правее планового профита не создаёт золотую зону и не растягивает
	// шкалу — насечка клипуется по правому краю +900, истинное значение
	// остаётся в подписи границы.
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

	it("подписи: риска нет +950 USDT · 106%, маркер «+850»", () => {
		// Act.
		const geometry = computeFinResultGeometry(CASES.c7);

		// Assert: граница подписана худшим исходом (+950, равен
		// реализованному) с честным процентом от планового профита;
		// маркер — итог.
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("+950 USDT · 106%");
		expect(geometry.labels.marker).toBe("+850");
		expect(geometry.tone).toBe("positive");
		expect(geometry.borderTone).toBe("positive");
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
		expect(geometry.markerClipped).toBe(false);

		// Assert: заливка от нуля до +400, золота нет — профит не превышен.
		expect(geometry.fillMain).toEqual({ from: 0.25, to: 700 / 1200 });
		expect(geometry.superZone).toBeNull();
		expect(geometry.tone).toBe("positive");
	});

	it("корректировки сверх профита тянут золотую зону до результата", () => {
		// Act: реализованный +150, нереализованный +1250, корректировки +500
		// → результат +1 900 за плановым профитом +900.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 150, unrealized: 1250, adjustments: 500 }),
		);

		// Assert: шкала растянута до результата, зелёный участок — до
		// профита, золото — от профита до результата.
		expect(geometry.scaleMax).toBe(1900);
		expect(geometry.fillMain).toEqual({ from: 300 / 2200, to: 1200 / 2200 });
		expect(geometry.superZone).toEqual({ from: 1200 / 2200, to: 1 });

		// Assert: маркер на краю шкалы без клипа, подписан результатом.
		expect(geometry.markerAt).toBe(1);
		expect(geometry.markerClipped).toBe(false);
		expect(geometry.labels.marker).toBe("+1\u00A0900");
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

	it("отрицательные корректировки уменьшают результат", () => {
		// Act: реализованный +200, нереализованный +150, корректировки −50.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, unrealized: 150, adjustments: -50 }),
		);

		// Assert: маркер на результате +300 — корректировки со знаком минус
		// не ломают ни подпись, ни позицию.
		expect(geometry.labels.marker).toBe("+300");
		expect(geometry.markerAt).toBeCloseTo(600 / 1200, 4);
	});

	it("нулевые корректировки сохраняют прежнее поведение", () => {
		// Act: adjustments 0 — результат равен сумме реализованной и
		// нереализованной частей, как до появления корректировок.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, unrealized: 150, adjustments: 0 }),
		);

		// Assert: маркер на +350 — прежние значения кейса не изменились.
		expect(geometry.labels.marker).toBe("+350");
		expect(geometry.markerAt).toBeCloseTo(650 / 1200, 4);
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

	it("сбой котировок без реализованной части подписи маркера не даёт", () => {
		// Act: реализованного результата нет, корректировки +50 доступны.
		const geometry = computeFinResultGeometry(
			caseInput({
				realized: null,
				unrealized: null,
				adjustments: 50,
				quotesDegraded: true,
				hasOpenResidual: true,
			}),
		);

		// Assert: без реализованной части результата нет — ни позиции, ни
		// подписи маркера; неполнота при открытых остатках сохранена.
		expect(geometry.incomplete).toBe(true);
		expect(geometry.labels.marker).toBeNull();
		expect(geometry.markerAt).toBeNull();
	});
});

describe("граница худшего исхода", () => {
	// Граница ставится контрактной знаковой метрикой худшего исхода: при
	// остаточном риске она равна «реализованный минус реальный риск»,
	// подписывается самим значением со знаком и процентом от плановой
	// границы зоны по знаку значения.
	// Traceability: openspec:ui/screens#scenario-finresult-border-real-risk

	it("реальный риск 150: граница на значении худшего исхода +50", () => {
		// Act: реализованная +200, реальный риск 150 → худший исход +50.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, realRisk: 150, worstCase: 50 }),
		);

		// Assert: граница на +50, маркер на итоге +200.
		expect(geometry.borderAt).toBeCloseTo(350 / 1200, 4);
		expect(geometry.markerAt).toBeCloseTo(500 / 1200, 4);

		// Assert: подпись границы — значение худшего исхода со знаком и
		// процентом от планового профита (50/900).
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("+50 USDT · 6%");
		expect(geometry.labels.marker).toBe("+200");
		expect(geometry.borderTone).toBe("positive");
	});

	// Свежая позиция: ликвидация сейчас вернёт около нуля, а весь реальный
	// риск (120) впереди — граница стоит на −реальныйРиск.
	// Traceability: openspec:ui/screens#scenario-finresult-fresh-position-real-risk
	it("свежая позиция: маркер «≈ 0», граница на −реальныйРиск", () => {
		// Act: сделок закрытия нет, метрика реального риска доступна.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 0, realRisk: 120, worstCase: -120 }),
		);

		// Assert: маркер у нуля, граница −120 не совпадает с левым краем шкалы.
		expect(geometry.markerAt).toBeCloseTo(0.25, 3);
		expect(geometry.labels.marker).toBe("≈ 0");
		expect(geometry.borderAt).toBeCloseTo(180 / 1200, 4);

		// Assert: подпись границы — худший исход −120 со знаком и процентом
		// от планового риска (120/300); тон — по знаку исхода.
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("−120 USDT · 40%");
		expect(geometry.borderTone).toBe("negative");
	});

	// Гарантированная прибыль остатков: полный синтетический паритетный лок
	// даёт положительный вклад в худший исход — насечка стоит правее
	// реализованного результата при нулевом реальном риске.
	// Traceability: openspec:ui/screens#scenario-finresult-guaranteed-profit-border
	it("гарантированная прибыль остатков ставит границу правее реализованного", () => {
		// Act: реализованный −46.6, паритетный лок с худшим платежом +35.7
		// → худший исход −10.9; планового риска нет — база процента не задана.
		const geometry = computeFinResultGeometry(
			caseInput({
				plannedRisk: null,
				realized: -46.6,
				unrealized: null,
				realRisk: 0,
				worstCase: -10.9,
			}),
		);

		// Assert: насечка −10.9 стоит правее маркера реализованного −46.6.
		expect(geometry.borderAt).toBeCloseTo(889.1 / 1800, 4);
		expect(geometry.markerAt).toBeCloseTo(853.4 / 1800, 4);
		expect(geometry.borderAt ?? 0).toBeGreaterThan(geometry.markerAt ?? 0);

		// Assert: титул «риска нет» при нулевом реальном риске; процент не
		// выводится — плановой базы для отрицательного исхода нет.
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("−10.9 USDT");
	});

	// Клип слева: худший исход за плановым риском прижимает насечку к левому
	// краю шкалы, процент остаётся честным — больше 100%.
	// Traceability: openspec:ui/screens#scenario-finresult-border-left-clip
	it("худший исход за левым краем: насечка клипуется, процент 130%", () => {
		// Act: плановый риск 400, худший исход −520 — меньше −Риск.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: 400, realRisk: 520, worstCase: -520 }),
		);

		// Assert: насечка прижата к левому краю шкалы.
		expect(geometry.borderAt).toBe(0);

		// Assert: подпись показывает истинное значение и честный процент
		// 520/400.
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("−520 USDT · 130%");
	});

	// Процент не выводится при незаданной плановой границе зоны: для
	// положительного исхода базой служит плановый профит.
	// Traceability: openspec:ui/screens#scenario-finresult-percent-omitted-without-plan
	it("процент без планового профита не выводится", () => {
		// Act: плановый профит не задан, плановый риск 300, худший исход +50.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedProfit: null, realized: 100, realRisk: 50, worstCase: 50 }),
		);

		// Assert: подпись без процента, значение со знаком; насечка на +50
		// шкалы −300…+300.
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("+50 USDT");
		expect(geometry.borderAt).toBeCloseTo(350 / 600, 4);
	});

	// Нулевое значение границы процента не получает даже при заданной базе.
	it("нулевое значение — подпись без процента", () => {
		// Act: закрытая безубыточная конструкция: остатков нет, исход 0.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 0, realRisk: 0, worstCase: 0 }),
		);

		// Assert: «риска нет» и «0 USDT» без процента.
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("0 USDT");
		expect(geometry.borderAt).toBeCloseTo(0.25, 4);
	});

	// Маркер итога не прижимается к границе: расхождение оценки ликвидации
	// и худшего исхода показывается в истинных положениях.
	// Traceability: openspec:ui/screens#scenario-finresult-marker-not-clamped
	it("маркер остаётся в истинном положении левее границы", () => {
		// Act: устаревшая котировка опустила итог до −150, худший исход −70.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: 100, realRisk: 70, worstCase: -70 }),
		);

		// Assert: маркер на истинном итоге −150, строго левее насечки
		// худшего исхода −70, без прижатия к границе.
		expect(geometry.markerAt).toBeCloseTo(150 / 1200, 4);
		expect(geometry.borderAt).toBeCloseTo(230 / 1200, 4);
		expect(geometry.markerAt ?? 0).toBeLessThan(geometry.borderAt ?? 0);
	});

	// Насечка считается только по контрактному худшему исходу: без числа
	// и при не конечном статусе насечки нет — подстановка планового риска
	// отменена спецификацией.
	// Traceability: openspec:ui/screens#scenario-finresult-real-risk-fallback-planned
	it("realRisk null: насечки и подписи границы нет", () => {
		// Act: контракта числа реального риска нет.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 100, realRisk: null, realRiskStatus: "unavailable" }),
		);

		// Assert: границы нет вовсе, плановое деление остаётся плановым.
		expect(geometry.borderAt).toBeNull();
		expect(geometry.borderTone).toBeNull();
		expect(geometry.labels.borderTitle).toBeNull();
		expect(geometry.labels.borderValue).toBeNull();
		expect(geometry.labels.risk).toBe("плановый риск −300");
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

describe("состояние риска без насечки", () => {
	// Не конечный риск (unbounded/unavailable) не изображается конечной
	// насечкой: насечка и её подпись отсутствуют, плановое деление
	// подписывается плановым риском, а состояние отдаётся текстом вне
	// шкалы. Маркер итога, единая заливка, золотая зона и нейтральный
	// режим сохраняются.
	// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
	// Traceability: openspec:ui/screens#scenario-finresult-unavailable-risk
	// Traceability: openspec:ui/screens#scenario-finresult-risk-state-without-plan
	// Traceability: change:show-unbounded-finresult-risk/design#d2

	it("unbounded: насечки нет, плановое деление подписано плановым риском, зоны и маркер сохранены", () => {
		// Act: итог +5 942.16 при плановом риске 400 — хвост коротких
		// коллов не ограничен, статус из API приходит unbounded.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: 400, realized: 5942.16, unrealized: null, realRisk: null, realRiskStatus: "unbounded" }),
		);

		// Assert: насечка и её подписи отсутствуют вовсе, статус состояния
		// вынесен наружу.
		expect(geometry.realRiskStatus).toBe("unbounded");
		expect(geometry.borderAt).toBeNull();
		expect(geometry.labels.borderTitle).toBeNull();
		expect(geometry.labels.borderValue).toBeNull();
		expect(geometry.labels.riskState).toBe("риск не ограничен");

		// Assert: плановое деление −400 подписано как плановый риск.
		expect(geometry.labels.risk).toBe("плановый риск −400");

		// Assert: маркер итога и единая заливка не задеты; зелёная заливка
		// идёт до планового профита, золото рисует участок за ним.
		expect(geometry.markerAt).toBe(1);
		expect(geometry.labels.marker).toBe("+5\u00A0942.16");
		expect(geometry.tone).toBe("positive");
		expect(geometry.fillMain).toEqual({ from: 400 / (5942.16 + 400), to: 1300 / (5942.16 + 400) });

		// Assert: золотая зона от профита +900 до итога, зона риска до нуля.
		expect(geometry.superZone).toEqual({ from: 1300 / (5942.16 + 400), to: 1 });
		expect(geometry.superZone?.to).toBe(1);
		expect(geometry.riskZone).toEqual({ from: 0, to: 400 / (5942.16 + 400) });

		// Assert: шкала растянута до итога.
		expect(geometry.scaleMax).toBe(5942.16);
	});

	it("unbounded: разнос не строит размещение для отсутствующей границы", () => {
		// Act: та же неограниченная позиция с метриками разметки.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: 400, realized: 5942.16, unrealized: null, realRisk: null, realRiskStatus: "unbounded" }),
			{ scaleWidthPx: 936, aboveCharWidthPx: 7, belowCharWidthPx: 6.5 },
		);

		// Assert: у границы нет ни координаты, ни размещения; длинная
		// плановая подпись «плановый риск −400» сталкивается с меткой
		// профита наверху и разносится вниз.
		expect(geometry.placements?.border).toBeNull();
		expect(geometry.placements?.risk).toEqual({ side: "below", level: 0, align: "start" });
	});

	it("unavailable: насечки нет, состояние «не удалось рассчитать», плановое деление подписано", () => {
		// Act: расчёт риска не удался при плановом риске 300.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, unrealized: null, realRisk: null, realRiskStatus: "unavailable" }),
		);

		// Assert: границы нет, статус «не удалось рассчитать», плановое
		// деление подписано плановым риском −300.
		expect(geometry.realRiskStatus).toBe("unavailable");
		expect(geometry.borderAt).toBeNull();
		expect(geometry.labels.borderValue).toBeNull();
		expect(geometry.labels.riskState).toBe("не удалось рассчитать");
		expect(geometry.labels.risk).toBe("плановый риск −300");

		// Assert: маркер итога +200 на своём месте.
		expect(geometry.markerAt).toBeCloseTo(500 / 1200, 4);
		expect(geometry.labels.marker).toBe("+200");
	});

	it("finite: насечка и подпись границы работают как раньше", () => {
		// Act: конечный реальный риск 150 при реализованной +200 —
		// худший исход +50.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 200, realRisk: 150, worstCase: 50 }),
		);

		// Assert: граница на худшем исходе +50, подпись значением исхода,
		// состояние не задаётся.
		expect(geometry.borderAt).toBeCloseTo(350 / 1200, 4);
		expect(geometry.labels.borderTitle).toBe("худший исход");
		expect(geometry.labels.borderValue).toBe("+50 USDT · 6%");
		expect(geometry.labels.riskState).toBeNull();
		expect(geometry.labels.risk).toBe("−300");
		expect(geometry.realRiskStatus).toBe("finite");
	});

	it("finite с нулевым риском: граница на нуле, состояния нет", () => {
		// Act: реальный риск 0 при закрытых остатках — худший исход равен
		// реализованному.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 945, unrealized: null, realRisk: 0, worstCase: 945 }),
		);

		// Assert: граница 945 совпадает с итогом и правым краем растянутой
		// шкалы, «риска нет», состояние отсутствует.
		expect(geometry.borderAt).toBe(1);
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.riskState).toBeNull();
	});

	it("сбой котировок при unbounded: неполный режим без насечки", () => {
		// Act: сбой котировок при открытых остатках и неограниченном хвосте.
		const geometry = computeFinResultGeometry(
			caseInput({
				realized: -250,
				unrealized: null,
				quotesDegraded: true,
				hasOpenResidual: true,
				realRisk: null,
				realRiskStatus: "unbounded",
			}),
		);

		// Assert: неполный режим, статус unbounded сохранён, насечки нет,
		// плановое деление подписано плановым риском.
		expect(geometry.incomplete).toBe(true);
		expect(geometry.realRiskStatus).toBe("unbounded");
		expect(geometry.borderAt).toBeNull();
		expect(geometry.labels.riskState).toBe("риск не ограничен");
		expect(geometry.labels.risk).toBe("плановый риск −300");

		// Маркер подписан доступной частью результата — реализованным и
		// корректировками, скрытая нереализованная часть в число не входит.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-incomplete-with-adjustments
		expect(geometry.labels.marker).toBe("−250");
	});

	it.each(["unbounded", "unavailable"] as const)("заданный статус %s не подставляет плановый риск в насечку", (status) => {
		// Act: реального риска нет, статус задан — прежняя подстановка
		// планового риска в насечку отменена.
		// Traceability: openspec:ui/screens#scenario-finresult-real-risk-fallback-planned
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 100, realRisk: null, realRiskStatus: status }),
		);

		// Assert: насечки нет вовсе, плановые −300 остаются только
		// границей плановой зоны.
		expect(geometry.borderAt).toBeNull();
		expect(geometry.labels.borderValue).toBeNull();
		expect(geometry.labels.risk).toBe("плановый риск −300");
		expect(geometry.riskZone).toEqual({ from: 0, to: 300 / 1200 });
	});

	it("нейтральная полоса без плановых параметров сохраняет состояние риска", () => {
		// Act: плановых границ нет, статус unbounded.
		const geometry = computeFinResultGeometry(
			caseInput({ plannedRisk: null, plannedProfit: null, realized: 100, unrealized: null, realRisk: null, realRiskStatus: "unbounded" }),
		);

		// Assert: нейтральная полоса без зон и насечки, состояние риска
		// передано текстом вне шкалы.
		expect(geometry.neutral).toBe(true);
		expect(geometry.riskZone).toBeNull();
		expect(geometry.profitZone).toBeNull();
		expect(geometry.superZone).toBeNull();
		expect(geometry.borderAt).toBeNull();
		expect(geometry.realRiskStatus).toBe("unbounded");
		expect(geometry.labels.riskState).toBe("риск не ограничен");
		expect(geometry.labels.risk).toBeNull();
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
		// граница «−300 USDT · 100%» — по левому.
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
		// Граница худшим исходом (−450 = −150 − 300) уходит за левый край — клип.
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
		// Насечка худшим исходом (дефолт −100): 200 − 300 = −100.
		expect(geometry.borderAt).toBeCloseTo(800 / 1800, 4);
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

	it("состояние 4: нет открытых остатков — маркер на итоге, граница по худшему исходу", () => {
		// Act: нереализованной части нет.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null }),
		);

		// Assert: маркер на итоге −250; граница по худшему исходу
		// (−550 = −250 − 300) прижата к левому краю — маркер и граница
		// не совпадают.
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.borderAt).toBe(0);
		expect(geometry.fillMain).toEqual({ from: 50 / 1200, to: 0.25 });
		expect(geometry.labels.marker).toBe("−250");
	});

	// Сбой котировок скрывает нереализованную часть открытых остатков, но не
	// убирает границу: «неполный» индикатор продолжает показывать реальный
	// риск. Read-модели отдают сбой котировок вместе с null нереализованной
	// части именно при открытых остатках.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	it("состояние 5: сбой котировок при открытых остатках — «неполный», нереализованная скрыта", () => {
		// Act: котировки недоступны, остатки открыты — нереализованная
		// часть не оценена и придёт как null.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null, quotesDegraded: true, hasOpenResidual: true }),
		);

		// Assert: нереализованная часть скрыта, индикатор помечен неполным;
		// маркер — на доступной части −250, граница по худшему исходу
		// прижата к краю.
		expect(geometry.incomplete).toBe(true);
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.borderAt).toBe(0);
		// Граница со своей подписью остаётся: худший исход виден и без
		// котировок.
		expect(geometry.labels.borderValue).toBe("−550 USDT · 183%");

		// Маркер подписан доступной частью результата — реализованным и
		// корректировками; скрытая нереализованная часть в число не входит.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-incomplete-with-adjustments
		expect(geometry.labels.marker).toBe("−250");
	});

	// Сбой котировок без открытых остатков не помечает индикатор неполным:
	// скрывать нечего — нереализованной части не существует. Маркер
	// подписывается доступной частью результата — реализованным и
	// корректировками.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	it("состояние 5а: сбой котировок без открытых остатков — индикатор полный", () => {
		// Act: котировки недоступны, но все остатки закрыты.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: -250, unrealized: null, quotesDegraded: true, hasOpenResidual: false }),
		);

		// Assert: неполноты нет, маркер стоит на доступной части результата
		// −250 (реализованный + корректировки).
		expect(geometry.incomplete).toBe(false);
		expect(geometry.markerAt).toBeCloseTo(50 / 1200, 4);
		expect(geometry.labels.marker).toBe("−250");
	});

	// Закрытая конструкция с нулевым реальным риском: граница совпадает
	// с realized, подпись «риска нет» со значением худшего исхода
	// (равен реализованному) и честным процентом от планового профита.
	// Traceability: openspec:ui/screens#scenario-finresult-closed-no-risk
	it("состояние 6: закрытая конструкция — рисков нет, граница на realized", () => {
		// Act: остатков нет, реализованная выше целевого профита, риск 0.
		const geometry = computeFinResultGeometry(
			caseInput({ realized: 945, unrealized: null, realRisk: 0, worstCase: 945 }),
		);

		// Assert: граница = realized 945 на краю шкалы, маркер на ней,
		// золотая зона от реализованной, нереализованной нет.
		expect(geometry.incomplete).toBe(false);
		expect(geometry.borderAt).toBe(1);
		expect(geometry.borderTone).toBe("positive");
		expect(geometry.markerAt).toBe(1);
		// Заливка итога — зелёный участок до профита, золото — за профитом.
		expect(geometry.fillMain).toEqual({ from: 300 / 1245, to: 1200 / 1245 });
		expect(geometry.superZone).toEqual({ from: 1200 / 1245, to: 1 });
		expect(geometry.labels.borderTitle).toBe("риска нет");
		expect(geometry.labels.borderValue).toBe("+945 USDT · 105%");
		expect(geometry.labels.marker).toBe("+945");
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
		["NaN в realRisk", caseInput({ realRisk: Number.NaN })],
		["NaN в worstCase", caseInput({ worstCase: Number.NaN })],
	])("ThrowOn%s", (_name, input) => {
		// Act + Assert: некорректное значение отвергается ошибкой.
		expect(() => computeFinResultGeometry(input)).toThrow();
	});
});
