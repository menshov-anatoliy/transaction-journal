import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
// проверяем публичный контракт представлений — какие бейджи, подписи, штрихи
// и области выдаёт каждый из трёх видов.
// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#requirement-risk-profit-hint

// Вход кейса C3: маркер текущего результата — итог −70, подписи плановых
// делений «−300 / 0 / +900»; маркеров истории нет.
const CASE_C3: FinResultInput = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: -250,
	unrealized: 180,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "finite",
	resultMarkers: [],
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
	resultMarkers: [],
};

// Вход истории результата: окно сборки −100 и синтетическое закрытие +30 —
// последний бейдж −70, текущий результат −40 за счёт нереализованной части.
// Время события без зоны — формат даты стабилен в любой часовой зоне;
// у каждой метки — расшифровка перечнем операций из контракта.
const CASE_MARKERS: FinResultInput = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: -70,
	unrealized: 30,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "finite",
	resultMarkers: [
		{
			number: 1,
			eventTime: "2026-06-20T14:30:00",
			kind: "opening",
			changeUsdt: -100,
			totalUsdt: -100,
			operations: [{ kind: "opening", date: "2026-06-20T14:30:00", contributionUsdt: -100 }],
		},
		{
			number: 2,
			eventTime: "2026-06-25T10:05:00",
			kind: "synthetic-close",
			changeUsdt: 30,
			totalUsdt: -70,
			operations: [{ kind: "synthetic-close", date: "2026-06-25T10:05:00", contributionUsdt: 30 }],
		},
	],
};

// Вход сценария finresult-unbounded-risk: открытый нетто-короткий колл,
// плановый риск 400, положительный итог +5 942.16 — хвост не ограничен,
// числа реального риска в контракте нет.
const CASE_UNBOUNDED: FinResultInput = {
	plannedRisk: 400,
	plannedProfit: 900,
	realized: 5942.16,
	unrealized: null,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "unbounded",
	resultMarkers: [],
};

// Вход сценария finresult-unavailable-risk: исходные данные позиции
// неполны, расчёт реального риска не удался.
const CASE_UNAVAILABLE: FinResultInput = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: 200,
	unrealized: null,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "unavailable",
	resultMarkers: [],
};

// Вход сценария finresult-risk-state-without-plan: плановых границ нет,
// состояние риска при этом остаётся видимым.
const CASE_UNBOUNDED_WITHOUT_PLAN: FinResultInput = {
	plannedRisk: null,
	plannedProfit: null,
	realized: 100,
	unrealized: null,
	adjustments: 0,
	quotesDegraded: false,
	hasOpenResidual: true,
	realRiskStatus: "unbounded",
	resultMarkers: [],
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
		expect(container.querySelector('[data-part="marker-tick"]')).toBeNull();
	});

	// Каждый маркер истории показан номерным бейджем с суммой над полосой,
	// номера идут подряд без пропусков.
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-result-markers-badges-above-bar
	it("рисует номерные бейджи маркеров с суммами над полосой", () => {
		// Act: история из двух событий.
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_MARKERS} />);

		// Assert: два бейджа с тонами по знаку суммы; номера 1 и 2 идут
		// подряд, суммы — накопленные итоги точек.
		const badges = container.querySelectorAll<HTMLElement>('[data-part="marker-badge"]');
		expect(badges).toHaveLength(2);
		expect(badges[0]).toHaveTextContent("1");
		expect(badges[0]).toHaveTextContent("−100");
		expect(badges[0].dataset.tone).toBe("negative");
		expect(badges[1]).toHaveTextContent("2");
		expect(badges[1]).toHaveTextContent("−70");
		expect(badges[1].dataset.tone).toBe("negative");

		// Assert: под каждым бейджем — тёмный штрих на полосе.
		expect(container.querySelectorAll('[data-part="marker-tick"]')).toHaveLength(2);
	});

	// Последняя метка отличается от маркера текущего результата: бейдж
	// подписывает зафиксированный итог над полосой, маркер — текущий
	// результат под полосой.
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-result-marker-last-vs-current
	it("показывает последний бейдж −70 над полосой и маркер −40 под ней", () => {
		// Act: история до −70, нереализованная часть подняла итог до −40.
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_MARKERS} />);

		// Assert: подпись маркера текущего результата −40 живёт в стеке под
		// полосой, последний бейдж −70 — в стеке над полосой.
		const markerLabel = container.querySelector<HTMLElement>('[data-part="marker-label"]');
		expect(markerLabel).toHaveTextContent("−40");
		const belowStack = markerLabel?.parentElement;
		const badges = container.querySelectorAll<HTMLElement>('[data-part="marker-badge"]');
		const lastBadge = badges[badges.length - 1];
		expect(lastBadge).toHaveTextContent("−70");
		expect(lastBadge.parentElement).not.toBe(belowStack);
	});

	// Подсказка числа сосуществует с попапом чтения: наведение на бейдж
	// маркера открывает расшифровку метки — перечень операций изменения
	// с предыдущей выведенной метки, а сумма перечня равна изменению и
	// восстанавливает накопленный итог; попап «Как читать индикатор»
	// доступен по своему триггеру карточки (его открытие проверяют тесты
	// metric-strip).
	// Traceability: openspec:ui/screens#scenario-marker-badge-breakdown-tooltip
	// Traceability: openspec:ui/screens#scenario-indicator-number-tooltip-coexists
	it("открывает подсказку-расшифровку метки наведением на бейдж", async () => {
		// Arrange: полный вид с историей результата.
		const user = userEvent.setup();
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_MARKERS} />);

		// Act: наведение на первый бейдж.
		const badge = container.querySelector<HTMLElement>('[data-part="marker-badge"]');
		await user.hover(within(badge!).getByText("−100"));

		// Assert: подсказка по роли tooltip — заголовок «Метка 1 · сумма»,
		// изменение с начала истории, операция вида «вид · дата · сумма»
		// и строка-инвариант «Σ перечня = изменение».
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent("Метка 1 · −100 USDT");
		expect(tooltip).toHaveTextContent("изменение с начала истории: −100 USDT");
		expect(tooltip).toHaveTextContent("Открытие · 2026-06-20 · −100 USDT");
		expect(tooltip).toHaveTextContent("Σ перечня = изменение");
	});

	// Нумерация меток сплошная, а операции подавленных порогом событий
	// сервер включает в расшифровку следующего выведенного маркера: бейджа
	// для подавленного события нет, но его вклад виден в перечне метки 2
	// и входит в её изменение.
	// Traceability: openspec:ui/screens#scenario-result-marker-below-threshold-hidden
	it("включает операции подавленного события в расшифровку следующей метки", async () => {
		// Arrange: после метки 1 (−100) событие −5 подавлено порогом,
		// следующая выведенная метка 2 накопила −75 — её изменение +25
		// складывается из выведенной операции +30 и подавленной −5.
		const input: FinResultInput = {
			...CASE_MARKERS,
			resultMarkers: [
				CASE_MARKERS.resultMarkers[0],
				{
					number: 2,
					eventTime: "2026-06-28T12:00:00",
					kind: "closing",
					changeUsdt: 25,
					totalUsdt: -75,
					operations: [
						{ kind: "closing", date: "2026-06-27T09:00:00", contributionUsdt: 30 },
						{ kind: "futures-close", date: "2026-06-28T12:00:00", contributionUsdt: -5 },
					],
				},
			],
		};
		const user = userEvent.setup();
		const { container } = renderIndicator(<FullFinResultIndicator input={input} />);

		// Assert: выведенных бейджей два, номера идут подряд без пропусков —
		// отдельного бейджа у подавленного события нет.
		const badges = container.querySelectorAll<HTMLElement>('[data-part="marker-badge"]');
		expect(badges).toHaveLength(2);
		expect(badges[0]).toHaveTextContent("1");
		expect(badges[1]).toHaveTextContent("2");

		// Act: раскрытие расшифровки метки 2.
		await user.hover(within(badges[1]).getByText("−75"));
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		const tooltip = screen.getByRole("tooltip");

		// Assert: перечень включает операцию подавленного события; сумма
		// перечня 30 + (−5) = +25 равна изменению метки.
		expect(tooltip).toHaveTextContent("Метка 2 · −75 USDT");
		expect(tooltip).toHaveTextContent("изменение с метки 1: +25 USDT");
		expect(tooltip).toHaveTextContent("Закрытие · 2026-06-27 · +30 USDT");
		expect(tooltip).toHaveTextContent("Закрытие фьючерса · 2026-06-28 · −5 USDT");
		expect(tooltip).toHaveTextContent("Σ перечня = изменение");
	});

	// Несколько однотипных операций перечисляются построчно с индивидуальными
	// суммами, без свёртки «×N»; при переполнении перечень прокручивается.
	// Traceability: openspec:ui/screens#scenario-marker-breakdown-no-grouping
	it("перечисляет однотипные операции построчно и прокручивает переполнение", async () => {
		// Arrange: серия фьючерсных закрытий в одной метке — сервер отдаёт
		// каждую операцию отдельной записью перечня.
		const input: FinResultInput = {
			...CASE_MARKERS,
			resultMarkers: [
				{
					number: 1,
					eventTime: "2026-06-30T18:00:00",
					kind: "futures-close",
					changeUsdt: 60,
					totalUsdt: 60,
					operations: [
						{ kind: "futures-close", date: "2026-06-28T10:00:00", contributionUsdt: 20 },
						{ kind: "futures-close", date: "2026-06-29T10:00:00", contributionUsdt: 25 },
						{ kind: "futures-close", date: "2026-06-30T18:00:00", contributionUsdt: 15 },
					],
				},
			],
		};
		const user = userEvent.setup();
		const { container } = renderIndicator(<FullFinResultIndicator input={input} />);

		// Act: наведение на единственный бейдж метки.
		const badge = container.querySelector<HTMLElement>('[data-part="marker-badge"]');
		await user.hover(within(badge!).getByText("+60"));
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		const tooltip = screen.getByRole("tooltip");

		// Assert: три строки по одной операции — индивидуальные даты и суммы,
		// свёртки «×N» нет.
		expect(tooltip).toHaveTextContent("Закрытие фьючерса · 2026-06-28 · +20 USDT");
		expect(tooltip).toHaveTextContent("Закрытие фьючерса · 2026-06-29 · +25 USDT");
		expect(tooltip).toHaveTextContent("Закрытие фьючерса · 2026-06-30 · +15 USDT");
		expect(tooltip.textContent).not.toContain("×3");
		expect(tooltip.textContent).not.toContain("×N");

		// Assert: переполнение — прокрутка попапа: ограничение высоты
		// и вертикальный скролл на контенте подсказки.
		const content = document.querySelector('[data-slot="tooltip-content"]');
		expect(content?.className).toContain("overflow-y-auto");
		expect(content?.className).toContain("max-h-[240px]");
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
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-finresult-labels-no-overlap
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
				badgeChromePx: 30,
				badgeRows: 2,
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
	// Средний вид показывает только последний маркер истории — тёмный
	// вертикальный штрих на полосе в его позиции, без номера и суммы.
	// Traceability: openspec:ui/screens#scenario-medium-compact-last-marker-only
	it("рисует только штрих последнего маркера без бейджей", () => {
		// Act: средний вид с полной историей карточки (мобильная сводка).
		const { container } = renderIndicator(<MediumFinResultIndicator input={CASE_MARKERS} />);

		// Assert: один штрих в позиции последнего маркера −70 (позиция
		// несёт триггер подсказки, штрих внутри него).
		const trigger = container.querySelector<HTMLElement>('[data-part="marker-tick-trigger"]');
		const tick = container.querySelector<HTMLElement>('[data-part="marker-tick"]');
		expect(trigger).not.toBeNull();
		expect(tick).not.toBeNull();
		expect(Number.parseFloat(trigger?.style.left ?? "")).toBeCloseTo((230 / 1200) * 100, 3);

		// Assert: бейджей с номерами и суммами нет, области описания нет.
		expect(container.querySelector('[data-part="marker-badge"]')).toBeNull();
		expect(container.querySelector('[data-part="marker-description"]')).toBeNull();
		expect(container.querySelector('[data-part="marker-description-empty"]')).toBeNull();
	});

	// Расшифровка последней метки доступна как подсказка на области
	// последнего маркера: перечень операций контракта списка/превью.
	// Traceability: openspec:ui/screens#scenario-medium-compact-last-marker-only
	it("открывает расшифровку последней метки подсказкой на штрихе", async () => {
		// Arrange: средний вид с последней меткой и её перечнем операций.
		const user = userEvent.setup();
		const { container } = renderIndicator(<MediumFinResultIndicator input={CASE_MARKERS} />);

		// Act: наведение на область последнего маркера.
		const trigger = container.querySelector<HTMLElement>('[data-part="marker-tick-trigger"]');
		expect(trigger).not.toBeNull();
		await user.hover(trigger!);
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		const tooltip = screen.getByRole("tooltip");

		// Assert: та же расшифровка, что у бейджа полного вида.
		expect(tooltip).toHaveTextContent("Метка 2 · −70 USDT");
		expect(tooltip).toHaveTextContent("изменение с метки 1: +30 USDT");
		expect(tooltip).toHaveTextContent("Синтетическое закрытие · 2026-06-25 · +30 USDT");
		expect(tooltip).toHaveTextContent("Σ перечня = изменение");
	});

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
	// Полный вид показывает «риск не ограничен» отдельным статусом, плановое
	// деление подписано плановым риском, при этом итог и золотая зона
	// сохранены, а числовой подписи реального риска нет.
	// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
	it("полный: показывает «риск не ограничен» без числа риска", () => {
		// Act: неограниченный хвост коротких коллов при плюсе +5 942.16.
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_UNBOUNDED} />);

		// Assert: статус состояния, плановое деление, итог и золотая зона.
		expect(screen.getByText("риск не ограничен")).toHaveAttribute("data-part", "risk-state");
		expect(screen.getByText("плановый риск −400")).toBeInTheDocument();
		expect(screen.getByText("+5 942.16")).toBeInTheDocument();
		expect(container.querySelector('[data-part="super-zone"]')).not.toBeNull();

		// Assert: числовой подписи реального риска и процента нет.
		expect(screen.queryByText(/USDT/)).not.toBeInTheDocument();
		expect(screen.queryByText(/%/)).not.toBeInTheDocument();
	});

	// Недоступность расчёта — своё состояние «не удалось рассчитать», план
	// не выдаётся за реальный риск.
	// Traceability: openspec:ui/screens#scenario-finresult-unavailable-risk
	it("полный: показывает «не удалось рассчитать» без числа реального риска", () => {
		// Act.
		renderIndicator(<FullFinResultIndicator input={CASE_UNAVAILABLE} />);

		// Assert: статус состояния и плановое деление.
		expect(screen.getByText("не удалось рассчитать")).toBeInTheDocument();
		expect(screen.getByText("плановый риск −300")).toBeInTheDocument();
		expect(screen.queryByText(/USDT/)).not.toBeInTheDocument();
	});

	// Средний вид показывает то же состояние, что и полный.
	// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
	it("средний: показывает состояние неограниченного риска как полный", () => {
		// Act.
		const { container } = renderIndicator(<MediumFinResultIndicator input={CASE_UNBOUNDED} />);

		// Assert: статус есть, ложного процента рядом нет.
		expect(screen.getByText("риск не ограничен")).toBeInTheDocument();
		expect(screen.queryByText(/100%/)).not.toBeInTheDocument();
		expect(container.querySelector('[data-part="border"]')).toBeNull();
	});

	// Метки не наплывают: статус состояния занимает собственную строку
	// колонки, маркер остаётся в стеке под полосой.
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-finresult-labels-no-overlap
	it("полный: статус состояния и метка маркера занимают разные строки", () => {
		// Act.
		const { container } = renderIndicator(<FullFinResultIndicator input={CASE_UNBOUNDED} />);

		// Assert: статус — прямой ребёнок корневой колонки, маркер живёт
		// в стеке меток под полосой, поэтому пересечение невозможно.
		const state = screen.getByText("риск не ограничен");
		const root = container.firstElementChild as HTMLElement;
		expect(state.parentElement).toBe(root);
		const markerLabel = container.querySelector<HTMLElement>('[data-part="marker-label"]');
		expect(markerLabel).not.toBeNull();
		expect(markerLabel!.parentElement).not.toBe(root);
		expect(markerLabel!.parentElement?.parentElement).toBe(root);
	});

	// Компакт различает ∞ и ? видимым бейджем поверх полосы: без сдвига
	// полосы 132px, состояние названо в title и aria-label отдельно от
	// плановой границы.
	// Traceability: openspec:ui/screens#scenario-finresult-compact-risk-state
	it("компактный: различает ∞ и ? и называет состояние в title и aria-label", () => {
		// Act: неограниченный хвост в строке таблицы.
		const { container, rerender } = renderIndicator(<CompactFinResultIndicator input={CASE_UNBOUNDED} />);

		// Assert: бейдж ∞ поверх полосы, полоса и плановая зона на месте,
		// ширина 132px сохранена.
		expect(container.firstElementChild).toHaveClass("w-[132px]");
		expect(container.querySelector('[data-part="risk-state"]')).toHaveTextContent("∞");
		expect(container.querySelector('[data-part="bar"]')).not.toBeNull();
		expect(container.querySelector('[data-part="risk-zone"]')).not.toBeNull();
		const unbounded = screen.getByRole("img");
		expect(unbounded).toHaveAttribute("title", "риск не ограничен");
		expect(unbounded.getAttribute("aria-label")).toContain("риск не ограничен");
		expect(unbounded.getAttribute("aria-label")).toContain("плановый риск −400");

		// Act: недоступный расчёт в той же строке.
		rerender(<CompactFinResultIndicator input={CASE_UNAVAILABLE} />);

		// Assert: бейдж ? и своё состояние в подсказке.
		expect(container.querySelector('[data-part="risk-state"]')).toHaveTextContent("?");
		expect(screen.getByRole("img")).toHaveAttribute("title", "не удалось рассчитать");
	});

	// Без плановых границ полоса нейтральная, но состояние риска видно.
	// Traceability: openspec:ui/screens#scenario-finresult-risk-state-without-plan
	it("нейтральная полоса показывает состояние риска в подробном и компактном видах", () => {
		// Act: подробный вид без плановых параметров.
		const full = renderIndicator(<FullFinResultIndicator input={CASE_UNBOUNDED_WITHOUT_PLAN} />);

		// Assert: зон нет, статус состояния на месте.
		expect(full.container.querySelector('[data-part="risk-zone"]')).toBeNull();
		expect(screen.getByText("риск не ограничен")).toBeInTheDocument();
		full.unmount();

		// Act: компакт без плановых параметров.
		const { container } = renderIndicator(<CompactFinResultIndicator input={CASE_UNBOUNDED_WITHOUT_PLAN} />);

		// Assert: бейдж ∞, единая заливка нейтральной полосы.
		expect(container.querySelector('[data-part="risk-state"]')).toHaveTextContent("∞");
		expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
	});

	// Сбой котировок не скрывает предупреждение о состоянии риска.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	it("сбой котировок не скрывает состояние риска", () => {
		// Act: нереализованная часть недоступна, статус unbounded сохранён.
		renderIndicator(<FullFinResultIndicator input={{ ...CASE_UNBOUNDED, realized: -250, quotesDegraded: true }} />);

		// Assert: статус виден, описание скринридеру помечает неполноту.
		expect(screen.getByText("риск не ограничен")).toBeInTheDocument();
		expect(screen.getByRole("img").getAttribute("aria-label")).toContain("неполный");
	});

	// Конечный риск сохраняет прежний вид всех трёх представлений: без
	// статуса и бейджа состояния.
	// Traceability: change:show-unbounded-finresult-risk/design#d3
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
	it("использует ширину макета и токенный цвет штриха последнего маркера", () => {
		// Arrange / Act: компактный индикатор с историей результата.
		const { container } = renderIndicator(<CompactFinResultIndicator input={CASE_MARKERS} />);

		// Assert: размер задаётся компонентом, штрих последнего маркера —
		// тёмный токен textPrimary, обводка маркера итога — surface.
		expect(container.firstElementChild).toHaveClass("w-[132px]");
		expect(container.querySelector('[data-part="marker-tick"]')).toHaveClass("bg-text-primary");
		expect(container.querySelector('[data-part="marker"]')).toHaveClass("border-surface");
	});

	it("рисует только полосу без текстовых подписей", () => {
		// Act: компакт для строк таблицы конструкций с историей.
		const { container } = renderIndicator(<CompactFinResultIndicator input={CASE_MARKERS} />);

		// Assert: полоса с зонами, маркером и одним штрихом последнего
		// маркера; текстовых подписей и бейджей нет.
		expect(container.querySelector('[data-part="bar"]')).not.toBeNull();
		expect(container.querySelector('[data-part="marker"]')).not.toBeNull();
		expect(container.querySelectorAll('[data-part="marker-tick"]')).toHaveLength(1);
		expect(container.querySelector('[data-part="marker-badge"]')).toBeNull();
		expect(container.textContent).toBe("");
	});

	// Расшифровка последней метки доступна и в компактном виде строки
	// таблицы: подсказка на области последнего маркера, без номера и суммы.
	// Traceability: openspec:ui/screens#scenario-medium-compact-last-marker-only
	it("открывает расшифровку последней метки подсказкой на штрихе", async () => {
		// Arrange: компакт с последней меткой из контракта строки списка.
		const user = userEvent.setup();
		const { container } = renderIndicator(
			<CompactFinResultIndicator
				input={{
					...CASE_MARKERS,
					resultMarkers: [
						{
							totalUsdt: 15.25,
							eventAt: "2026-06-18T09:05:00",
							operations: [{ kind: "closing", date: "2026-06-18T09:05:00", contributionUsdt: 15.25 }],
						},
					],
				}}
			/>,
		);

		// Act: наведение на область последнего маркера.
		const trigger = container.querySelector<HTMLElement>('[data-part="marker-tick-trigger"]');
		expect(trigger).not.toBeNull();
		await user.hover(trigger!);
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		const tooltip = screen.getByRole("tooltip");

		// Assert: расшифровка без номера метки — контракт строки номера
		// не публикует; перечень и инвариант на месте.
		expect(tooltip).toHaveTextContent("Метка изменения результата · +15.25 USDT");
		expect(tooltip).toHaveTextContent("Закрытие · 2026-06-18 · +15.25 USDT");
		expect(tooltip).toHaveTextContent("Σ перечня = изменение");
	});

	it("нейтральная полоса не рисует зон и штрихов истории", () => {
		// Act: плановые границы не заданы, история передана — на нейтральной
		// полосе маркеры истории не выводятся.
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
					resultMarkers: [{ totalUsdt: -100 }],
				}}
			/>,
		);

		// Assert: ни зон, ни штрихов истории — заполнение от нуля до края.
		expect(container.querySelector('[data-part="risk-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="profit-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="marker-tick"]')).toBeNull();
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
					resultMarkers: [{ totalUsdt: -550 }],
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
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-finresult-labels-no-overlap
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
					resultMarkers: [],
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

	// Число маркеров истории названо в описании: пользователь скринридера
	// слышит объём истории без чтения области описания.
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-result-markers-badges-above-bar
	it("называет число маркеров истории в описании", () => {
		// Act.
		renderIndicator(<FullFinResultIndicator input={CASE_MARKERS} />);

		// Assert: в aria-label названы оба маркера истории.
		expect(screen.getByRole("img").getAttribute("aria-label")).toContain(
			"маркеров изменения результата 2",
		);
	});
});
