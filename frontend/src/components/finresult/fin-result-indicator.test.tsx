import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { computeFinResultGeometry } from "@/lib/finresult/geometry";
import type { FinResultInput } from "@/lib/finresult/geometry";
import {
	CompactFinResultIndicator,
	FullFinResultIndicator,
	MediumFinResultIndicator,
} from "./fin-result-indicator";

// Рендер вида индикатора с провайдером подсказок: Radix Tooltip требует
// провайдера-предка, в тестах подсказки открываются без задержки.
function renderIndicator(ui: Parameters<typeof render>[0]) {
	return render(<TooltipProvider delayDuration={0}>{ui}</TooltipProvider>);
}

// Смоук-проверки компонентов индикатора финрезультата: рендер строится на
// геометрии калькулятора (юнит-эталоны покрыты в geometry.test.ts), здесь
// проверяем публичный контракт представлений — какие зоны, подписи и
// области выдаёт каждый из трёх видов; маркерных частей истории (бейджи,
// штрихи, триггеры расшифровок) ни один вид не рисует.
// Traceability: issue:#94

// Вход кейса C3: маркер текущего результата — итог −70, подписи плановых
// делений «−300 / 0 / +900».
const CASE_C3: FinResultInput = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: -250,
	unrealized: 180,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "finite",
};

// Вход кейса C6: сверхприбыль — итог +1 400 растягивает золотую зону,
// маркер стоит на краю шкалы и подписан итогом.
const CASE_C6: FinResultInput = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: 150,
	unrealized: 1250,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "finite",
};

// Вход сценария недоступного риска: исходные данные позиции
// неполны, расчёт реального риска не удался — числа в контракте нет.
const CASE_UNAVAILABLE: FinResultInput = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: 200,
	unrealized: null,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "unavailable",
};

// Вход сценария finresult-risk-state-without-plan: плановых границ нет,
// состояние риска при этом остаётся видимым.
const CASE_UNAVAILABLE_WITHOUT_PLAN: FinResultInput = {
	plannedRisk: null,
	plannedProfit: null,
	realized: 100,
	unrealized: null,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "unavailable",
};

describe("полный индикатор", () => {
	it("показывает подписи шкалы и маркер текущего результата на полосе", () => {
		// Act: полный вид для карточки конструкции.
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: деления шкалы «−300 / 0 / +900» и подпись итога.
		expect(screen.getByText("−300")).toBeInTheDocument();
		expect(screen.getByText("0")).toBeInTheDocument();
		expect(screen.getByText("+900")).toBeInTheDocument();
		expect(screen.getByText("−70")).toBeInTheDocument();

		// Assert: частей прежней границы в полосе и подписях нет.
		expect(container.querySelector('[data-part="border"]')).toBeNull();
		expect(container.querySelector('[data-part="border-label"]')).toBeNull();
	});

	// Тест удаления: бейджи, штрихи и триггеры подсказок маркеров изменения
	// результата выведены из индикатора — история результата осталась
	// в журнале событий, и ни один вид её на полосе не рисует
	// (ADR-0024: тесты — живая спецификация поведения).
	// Traceability: issue:#89
	it("не рисует бейджи, штрихи и триггеры расшифровок маркеров истории", () => {
		// Act: один и тот же вход во все три представления.
		const views = [FullFinResultIndicator, MediumFinResultIndicator, CompactFinResultIndicator];

		for (const View of views) {
			const { container, unmount } = renderIndicator(<View input={CASE_C3} />);

			// Assert: ни бейджа с номером и суммой, ни штриха истории,
			// ни хит-зоны расшифровки на полосе нет.
			expect(container.querySelector('[data-part="marker-badge"]')).toBeNull();
			expect(container.querySelector('[data-part="marker-tick"]')).toBeNull();
			expect(container.querySelector('[data-part="marker-tick-trigger"]')).toBeNull();
			unmount();
		}
	});

	it("позиционирует маркер по доле геометрии", () => {
		// Act.
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: маркер стоит на доле итога −70 из шкалы −300…+900.
		const marker = container.querySelector<HTMLElement>('[data-part="marker"]');
		expect(marker).not.toBeNull();
		expect(Number.parseFloat(marker?.style.left ?? "")).toBeCloseTo((230 / 1200) * 100, 3);
	});

	it("помечает тон маркера по знаку итога", () => {
		// Act: итог отрицательный (кейс C3).
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: маркер в отрицательном тоне.
		const marker = container.querySelector<HTMLElement>('[data-part="marker"]');
		expect(marker?.dataset.tone).toBe("negative");
	});

	// Сверхприбыль: золотая зона тянется до итога, маркер остаётся на краю
	// шкалы без клипа и подписан итогом.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	it("рисует золотую зону до итога и подписывает маркер итогом", () => {
		// Act: итог +1 400 на краю растянутой шкалы (кейс C6).
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_C6} />);

		// Assert: золотая зона отрисована, маркер подписан итогом
		// (тысячный разделитель — неразрывный пробел, при поиске текст
		// нормализуется в обычный).
		expect(container.querySelector('[data-part="super-zone"]')).not.toBeNull();
		expect(screen.getByText("+1 400")).toBeInTheDocument();
	});

	// Вертикальный разнос: метка нулевого уровня прижата к полосе одинаково
	// с обеих сторон — верхней стороной стека под полосой и нижней стороной
	// стека над полосой; внешний уровень уходит на свои строки от полосы.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	it("прижимает метки нулевого уровня к полосе одинаково с обеих сторон", () => {
		// Arrange: в jsdom полоса не измеряется (ширина 0 даёт вырожденный
		// разнос), поэтому фиксируем ширину явно — калькулятор в тесте и в
		// компоненте считает по номинальной ширине полного вида 480px.
		const rectSpy = vi
			.spyOn(HTMLElement.prototype, "getBoundingClientRect")
			.mockReturnValue({ width: 480, height: 0, top: 0, left: 0, x: 0, y: 0, right: 480, bottom: 0, toJSON: () => ({}) } as DOMRect);
		try {
			const geometry = computeFinResultGeometry(CASE_C3, {
				scaleWidthPx: 480,
				aboveCharWidthPx: 7,
				belowCharWidthPx: 6.5,
			});
			const placements = geometry.placements;
			expect(placements).not.toBeNull();
			const { container } = renderIndicator(<FullFinResultIndicator input={CASE_C3} />);

			const partToKey = {
				"risk-scale": "risk",
				"zero-scale": "zero",
				"profit-scale": "profit",
				"marker-label": "marker",
			} as const;
			const lineHeight = 14;

			// Act: стеки сторон — первый и третий ребёнок корня (между ними полоса).
			const root = container.firstElementChild as HTMLElement;
			const stacks = [
				{ side: "above" as const, stack: root.children[0] as HTMLElement },
				{ side: "below" as const, stack: root.children[2] as HTMLElement },
			];

			for (const { side, stack } of stacks) {
				const stackRows = Number.parseFloat(stack.style.height) / lineHeight;
				expect(stackRows).toBeGreaterThan(0);

				for (const el of Array.from(stack.querySelectorAll<HTMLElement>("[data-part]"))) {
					const key = partToKey[el.dataset.part as keyof typeof partToKey];
					const placement = placements![key];
					expect(placement?.side).toBe(side);

					const expectedTop =
						side === "below"
							? placement!.level * lineHeight
							: (stackRows - placement!.level - 1) * lineHeight;

					// Assert: метка стоит на своём уровне, нулевой — вплотную к полосе.
					expect(el.style.top).toBe(`${expectedTop}px`);
					if (placement!.level === 0 && side === "below") {
						expect(el.style.top).toBe("0px");
					}
					if (placement!.level === 0 && side === "above") {
						expect(Number.parseFloat(el.style.top) + lineHeight).toBe(stackRows * lineHeight);
					}
				}
			}
		} finally {
			rectSpy.mockRestore();
		}
	});
});

describe("средний индикатор", () => {
	it("показывает шкалу и подписи в уменьшенном виде", () => {
		// Act: средний вид для сводных карточек.
		renderIndicator(<MediumFinResultIndicator input={CASE_C3} />);

		// Assert: те же смысловые подписи, что и в полном виде; маркер — итог.
		expect(screen.getByText("−300")).toBeInTheDocument();
		expect(screen.getByText("+900")).toBeInTheDocument();
		expect(screen.getByText("−70")).toBeInTheDocument();
	});

	it("подписывает плановые границы приглушённо, в отличие от полного вида", () => {
		// Средний вид краткой карточки (мастер rMxFZ): плановые границы
		// риска и профита подписаны приглушённым тоном — цветным остаётся
		// только маркер итога.
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Act: один и тот же кейс в среднем и полном виде.
		const { container: medium, unmount: unmountMedium } = renderIndicator(<MediumFinResultIndicator input={CASE_C3} />);

		// Assert: в среднем виде подписи плановых границ на textMuted.
		const mediumRisk = medium.querySelector('[data-part="risk-scale"]');
		expect(mediumRisk?.className).toContain("text-text-muted");
		expect(mediumRisk?.className).not.toContain("fin-negative");
		const mediumProfit = medium.querySelector('[data-part="profit-scale"]');
		expect(mediumProfit?.className).toContain("text-text-muted");
		unmountMedium();

		const { container: full } = renderIndicator(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: в полном виде те же подписи цветные (мастер onchD).
		expect(full.querySelector('[data-part="risk-scale"]')?.className).toContain("text-[color:var(--fin-negative)]");
		expect(full.querySelector('[data-part="profit-scale"]')?.className).toContain("text-[color:var(--fin-positive-strong)]");
	});
});

describe("состояние риска в представлениях", () => {
	// Перечень состояний вырожден в unavailable: ветки «∞»/«риск не
	// ограничен» удалены из индикатора. Недоступный риск показывает статус
	// отдельной строкой, плановое деление подписано плановым риском,
	// маркер итога скрыт вместе с числом («маркера и числа нет» —
	// ячейка 8 макета), числовой подписи реального риска нет.
	// Traceability: issue:#98
	// Traceability: adr:docs/adr/0025-real-risk-negative-part-of-current-result.md#consequences
	it("полный: при недоступном риске показывает «не удалось рассчитать» без числа риска", () => {
		// Act: недоступный расчёт при плюсе +5 942.16 — по ячейке 8 макета
		// «маркера и числа нет»: точка маркера и его подпись скрыты.
		const { container } = renderIndicator(
			<FullFinResultIndicator input={{ ...CASE_UNAVAILABLE, realized: 5942.16 }} />,
		);

		// Assert: статус состояния, плановое деление, золотая зона итога.
		expect(screen.getByText("не удалось рассчитать")).toHaveAttribute("data-part", "risk-state");
		expect(screen.getByText("плановый риск −300")).toBeInTheDocument();
		expect(container.querySelector('[data-part="super-zone"]')).not.toBeNull();

		// Assert: маркера и числа нет — ни точки, ни суммы итога, ни
		// числовой подписи реального риска и процента.
		expect(container.querySelector('[data-part="marker"]')).toBeNull();
		expect(screen.queryByText("+5 942.16")).not.toBeInTheDocument();
		expect(screen.queryByText(/USDT/)).not.toBeInTheDocument();
		expect(screen.queryByText(/%/)).not.toBeInTheDocument();
	});

	// Недоступность расчёта — своё состояние «не удалось рассчитать», план
	// не выдаётся за реальный риск.
	// Traceability: issue:#98
	it("полный: показывает «не удалось рассчитать» без числа реального риска", () => {
		// Act.
		renderIndicator(<FullFinResultIndicator input={CASE_UNAVAILABLE} />);

		// Assert: статус состояния и плановое деление.
		expect(screen.getByText("не удалось рассчитать")).toBeInTheDocument();
		expect(screen.getByText("плановый риск −300")).toBeInTheDocument();
		expect(screen.queryByText(/USDT/)).not.toBeInTheDocument();
	});

	// Средний вид показывает то же состояние, что и полный.
	// Traceability: issue:#98
	it("средний: показывает состояние недоступного риска как полный", () => {
		// Act.
		const { container } = renderIndicator(<MediumFinResultIndicator input={CASE_UNAVAILABLE} />);

		// Assert: статус есть, ложного процента рядом нет.
		expect(screen.getByText("не удалось рассчитать")).toBeInTheDocument();
		expect(screen.queryByText(/100%/)).not.toBeInTheDocument();
		expect(container.querySelector('[data-part="border"]')).toBeNull();
	});

	// По ячейке 8 макета при недоступном риске «маркера и числа нет»:
	// точка маркера и его числовая подпись не рендерятся вовсе, статус
	// состояния занимает собственную строку колонки без наплыва.
	// Traceability: issue:#98
	it("полный: при недоступном риске маркер и его число скрыты", () => {
		// Act.
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_UNAVAILABLE} />);

		// Assert: ни точки маркера на полосе, ни числовой подписи итога.
		expect(container.querySelector('[data-part="marker"]')).toBeNull();
		expect(container.querySelector('[data-part="marker-label"]')).toBeNull();
	});

	// Компакт показывает «?» видимым бейджем поверх полосы: без сдвига
	// полосы 132px, состояние названо в title и aria-label отдельно от
	// плановой границы.
	// Traceability: issue:#98
	it("компактный: показывает «?» и называет состояние в title и aria-label", () => {
		// Act: недоступный расчёт в строке таблицы.
		const { container } = renderIndicator(<CompactFinResultIndicator input={CASE_UNAVAILABLE} />);

		// Assert: бейдж ? поверх полосы вместо маркера (ячейка 8 макета),
		// полоса и плановая зона на месте, ширина 132px сохранена.
		expect(container.firstElementChild).toHaveClass("w-[132px]");
		expect(container.querySelector('[data-part="risk-state"]')).toHaveTextContent("?");
		expect(container.querySelector('[data-part="marker"]')).toBeNull();
		expect(container.querySelector('[data-part="bar"]')).not.toBeNull();
		expect(container.querySelector('[data-part="risk-zone"]')).not.toBeNull();
		const unavailable = screen.getByRole("img");
		expect(unavailable).toHaveAttribute("title", "не удалось рассчитать");
		expect(unavailable.getAttribute("aria-label")).toContain("не удалось рассчитать");
		expect(unavailable.getAttribute("aria-label")).toContain("плановый риск −300");
	});

	// Без плановых границ полоса нейтральная, но состояние риска видно.
	// Traceability: openspec:ui/screens#scenario-finresult-risk-state-without-plan
	it("нейтральная полоса показывает состояние риска в подробном и компактном видах", () => {
		// Act: подробный вид без плановых параметров.
		const full = renderIndicator(<FullFinResultIndicator input={CASE_UNAVAILABLE_WITHOUT_PLAN} />);

		// Assert: зон нет, статус состояния на месте.
		expect(full.container.querySelector('[data-part="risk-zone"]')).toBeNull();
		expect(screen.getByText("не удалось рассчитать")).toBeInTheDocument();
		full.unmount();

		// Act: компакт без плановых параметров.
		const { container } = renderIndicator(<CompactFinResultIndicator input={CASE_UNAVAILABLE_WITHOUT_PLAN} />);

		// Assert: бейдж ?, единая заливка нейтральной полосы.
		expect(container.querySelector('[data-part="risk-state"]')).toHaveTextContent("?");
		expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
	});

	// Сбой котировок не скрывает предупреждение о состоянии риска.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	it("сбой котировок не скрывает состояние риска", () => {
		// Act: нереализованная часть недоступна, статус unavailable сохранён.
		renderIndicator(<FullFinResultIndicator input={{ ...CASE_UNAVAILABLE, quotesDegraded: true }} />);

		// Assert: статус виден, описание скринридеру помечает неполноту.
		expect(screen.getByText("не удалось рассчитать")).toBeInTheDocument();
		expect(screen.getByRole("img").getAttribute("aria-label")).toContain("неполный");
	});

	// Конечный риск сохраняет прежний вид всех трёх представлений: без
	// статуса и бейджа состояния.
	// Traceability: issue:#98
	it("finite не выводит статус состояния и бейдж", () => {
		// Arrange: конечный риск кейса C3 во всех представлениях.
		const views = [FullFinResultIndicator, MediumFinResultIndicator, CompactFinResultIndicator];

		for (const View of views) {
			// Act.
			const { container, unmount } = renderIndicator(<View input={CASE_C3} />);

			// Assert: ни статуса, ни бейджа состояния.
			expect(container.querySelector('[data-part="risk-state"]')).toBeNull();
			unmount();
		}
	});
});

describe("компактный индикатор", () => {
	// Компакт сохраняет ширину 132px мастера и токенные цвета в любой ячейке.
	// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	it("использует ширину макета и токенные цвета маркера итога", () => {
		// Arrange / Act: компактный индикатор строки таблицы.
		const { container } = renderIndicator(<CompactFinResultIndicator input={CASE_C3} />);

		// Assert: размер задаётся компонентом, обводка маркера итога —
		// токен surface.
		expect(container.firstElementChild).toHaveClass("w-[132px]");
		expect(container.querySelector('[data-part="marker"]')).toHaveClass("border-surface");
	});

	it("рисует только полосу без текстовых подписей", () => {
		// Act: компакт для строк таблицы конструкций.
		const { container } = renderIndicator(<CompactFinResultIndicator input={CASE_C3} />);

		// Assert: полоса с зоной и маркером; текстовых подписей нет.
		expect(container.querySelector('[data-part="bar"]')).not.toBeNull();
		expect(container.querySelector('[data-part="marker"]')).not.toBeNull();
		expect(container.textContent).toBe("");
	});

	it("нейтральная полоса не рисует зон", () => {
		// Act: плановые границы не заданы.
		const { container } = renderIndicator(
			<CompactFinResultIndicator
				input={{
					plannedRisk: null,
					plannedProfit: null,
					realized: -50,
					unrealized: 30,
					adjustments: 0,
					quotesDegraded: false,
					hasOpenResidual: true,
					realRiskStatus: "finite",
				}}
			/>,
		);

		// Assert: ни зон — заполнение от нуля до края.
		expect(container.querySelector('[data-part="risk-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="profit-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
	});

	it("помечает сбой котировок признаком неполноты", () => {
		// Act: сбой котировок при открытых остатках — комбинация, которую
		// отдают read-модели: нереализованная часть не оценена, но остатки
		// открыты, поэтому скрывать есть что.
		// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
		const { container } = renderIndicator(
			<CompactFinResultIndicator
				input={{
					plannedRisk: 300,
					plannedProfit: 900,
					realized: -250,
					unrealized: null,
					adjustments: 0,
					quotesDegraded: true,
					hasOpenResidual: true,
					realRiskStatus: "finite",
				}}
			/>,
		);

		// Assert: индикатор помечен неполным; единая заливка строится по
		// доступной части итога — состав заливки проверяют эталоны калькулятора.
		const root = container.firstElementChild as HTMLElement;
		expect(root.dataset.incomplete).toBe("true");
		expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
	});

	// Перенесённая калькулятором на другую сторону метка не теряется: стек
	// собирается по фактической стороне из разноса, а не по канонической.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	it("рендерит метку маркера в стеке фактической стороны", () => {
		// Act: итог +420 и метка профита на одной стороне — маркер ниже,
		// деление профита уходит наверх.
		const { container } = renderIndicator(
			<FullFinResultIndicator
				input={{
					plannedRisk: 300,
					plannedProfit: 900,
					realized: 520,
					unrealized: -100,
					adjustments: 0,
					quotesDegraded: false,
					hasOpenResidual: true,
					realRiskStatus: "finite",
				}}
			/>,
		);

		// Assert: подпись маркера живёт в стеке под полосой (третий ребёнок
		// корня), а деление профита — в стеке над полосой.
		const markerLabel = container.querySelector<HTMLElement>('[data-part="marker-label"]');
		expect(markerLabel).not.toBeNull();
		const root = container.firstElementChild as HTMLElement;
		expect(markerLabel?.parentElement).toBe(root.children[2]);
	});
});

// Единая заливка итога после упрощения рендера: отдельного слоя
// нереализованной части больше нет, а зелёный и золотой участки
// положительного итога стыкуются без перекрытия. Контракт общий, но
// проверяется на каждом из трёх представлений.
describe("заливки итога во всех представлениях", () => {
	const views = [FullFinResultIndicator, MediumFinResultIndicator, CompactFinResultIndicator];

	// fill-unreal удалён из рендера: слой нереализованной части не рисуется
	// ни в одном представлении ни при отрицательном итоге, ни при
	// сверхприбыли — заливка итога всегда одна (fill-main).
	// Traceability: openspec:ui/screens#scenario-finresult-negative-total-single-fill
	it("не рисует fill-unreal ни в одном представлении", () => {
		// Arrange: отрицательный итог (кейс C3) и сверхприбыль (кейс C6) —
		// раньше оба рисовали fill-unreal при ненулевой нереализованной части.
		const cases = [CASE_C3, CASE_C6];

		for (const View of views) {
			for (const input of cases) {
				// Act: рендер каждого представления на каждом знаке итога.
				const { container, unmount } = renderIndicator(<View input={input} />);

				// Assert: fill-unreal отсутствует, единая заливка на месте.
				expect(container.querySelector('[data-part="fill-unreal"]')).toBeNull();
				expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
				unmount();
			}
		}
	});

	// Золотой участок видим: зелёная заливка заканчивается ровно на плановом
	// профите, золотая тянется от профита до маркера на краю шкалы — слои
	// стыкуются без перекрытия, видимость золота не зависит от их порядка.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	it("рисует зелёный и золотой участки встык до маркера", () => {
		// Доли из inline-стилей — проценты шкалы: хелперы убирают повторный
		// разбор одних и тех же координат в ассертах ниже.
		const leftPercent = (part: HTMLElement | null): number =>
			Number.parseFloat(part?.style.left ?? "");
		const widthPercent = (part: HTMLElement | null): number =>
			Number.parseFloat(part?.style.width ?? "");
		for (const View of views) {
			// Act: кейс C6 — профит +900, итог +1 400 на краю растянутой шкалы
			// (шкала −300…+1 400: ноль 300/1700, профит 1200/1700).
			const { container, unmount } = renderIndicator(<View input={CASE_C6} />);
			const fill = container.querySelector<HTMLElement>('[data-part="fill-main"]');
			const superZone = container.querySelector<HTMLElement>('[data-part="super-zone"]');
			const marker = container.querySelector<HTMLElement>('[data-part="marker"]');

			// Assert: зелёный участок от нуля до планового профита.
			expect(fill).not.toBeNull();
			expect(leftPercent(fill)).toBeCloseTo((300 / 1700) * 100, 3);
			expect(leftPercent(fill) + widthPercent(fill)).toBeCloseTo((1200 / 1700) * 100, 3);
			// Assert: золотой участок от планового профита до маркера итога;
			// маркер проверяем явно, чтобы сбой рендера не маскировался NaN.
			expect(superZone).not.toBeNull();
			expect(marker).not.toBeNull();
			expect(leftPercent(superZone)).toBeCloseTo((1200 / 1700) * 100, 3);
			expect(leftPercent(superZone) + widthPercent(superZone)).toBeCloseTo(
				leftPercent(marker),
				3,
			);
			unmount();
		}
	});
});

describe("доступность индикатора", () => {
	it("несёт текстовое описание состояния для скринридеров", () => {
		// Act.
		renderIndicator(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: состояние озвучивается итогом маркера текущего результата.
		const labelled = screen.getByRole("img", { name: /результат −70/ });
		expect(labelled).toHaveAttribute("aria-label", expect.stringContaining("−70"));
	});
});
