import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionCardPosition } from "@/lib/api/construction-card";
import { PositionsTable } from "./positions-table";

// Канал котировок подменён: форма пометки спрашивает последнюю марку
// инструмента при открытии, сетевых запросов в тестах быть не должно.
vi.mock("@/lib/api/construction-card", () => ({
	fetchLastInstrumentMark: vi.fn().mockResolvedValue(null),
}));

// Проверяется распад остатка линейной ноги в таблице позиций карточки
// (мастер design.pen aU7Nx/LFEkd): при неоспоренной синтетике позиция
// показывает инструментальный остаток и под ним раздельно синтетический
// хедж связки и прочий (скальперский) остаток; без синтетического
// содержимого распад не выводится.
// Traceability: openspec:ui/screens#requirement-detail-synthetic-close-line

/** Базовая открытая линейная позиция карточки. */
function makePosition(overrides: Partial<ConstructionCardPosition> = {}): ConstructionCardPosition {
	return {
		symbol: "ETHUSDT",
		residual: 0.34,
		averageEntryPrice: 3400,
		averageClosePrice: null,
		realizedPnL: -0.68,
		realizedPnLPercent: -0.1,
		unrealizedPnL: -97.18,
		unrealizedPnLPercent: -0.95,
		totalPnL: -97.86,
		totalPnLPercent: -0.96,
		accumulatedFees: 0.4,
		openedAt: "2026-10-01T09:05:00+00:00",
		closedAt: null,
		isOpen: true,
		comment: null,
		markValue: 1055,
		priceChangePercent: -0.1,
		syntheticResidual: null,
		otherResidual: null,
		daysToExpiry: null,
		dailyDecayUsdt: null,
		intrinsicValueUsdt: null,
		timeValueUsdt: null,
		decayDayAgoUsdt: null,
		...overrides,
	};
}

// matchMedia-заглушка как в тестах каркаса: рабочий стол 1280px.
function setViewport(width: number) {
	Object.defineProperty(window, "innerWidth", { value: width, writable: true, configurable: true });
	window.matchMedia = vi.fn().mockImplementation((query: string) => ({
		matches: query.includes("max-width") ? width <= 1023 : false,
		media: query,
		onchange: null,
		addEventListener: vi.fn(),
		removeEventListener: vi.fn(),
		addListener: vi.fn(),
		removeListener: vi.fn(),
		dispatchEvent: vi.fn(),
	})) as typeof window.matchMedia;
}

beforeEach(() => {
	setViewport(1280);
});

function renderTable(positions: readonly ConstructionCardPosition[], onAddCloseMark: () => void = vi.fn()) {
	return render(
		<PositionsTable
			constructionId={7}
			positions={positions}
			markPending={false}
			onAddCloseMark={onAddCloseMark}
			onSaveComment={vi.fn()}
		/>,
	);
}

describe("PositionsTable: распад остатка линейной ноги", () => {
	it("показывает синтетический хедж и прочий остаток раздельно при связке", () => {
		// Сценарий «Линейная нога разделяет синтетику и скальпы»: остаток
		// 0.34 распадается на 0.30 синтетики связки и 0.04 прочего остатка.
		// Traceability: openspec:ui/screens#scenario-detail-linear-leg-splits-synthetic-and-scalps
		// Arrange: линейная нога с неоспоренной синтетической частью.
		const position = makePosition({ syntheticResidual: 0.3, otherResidual: 0.04 });

		// Act: рендер таблицы позиций.
		renderTable([position]);

		// Assert: инструментальный остаток в главной строке и распад
		// под ним в одной ячейке «Остаток».
		const split = document.querySelector('[data-slot="linear-leg-residual-split"]');
		expect(split).toBeTruthy();
		expect(split?.textContent).toContain("0.34");
		expect(split?.textContent).toContain("синтетика 0.3 · прочее 0.04");
	});

	it("не выводит распад у позиции без синтетического содержимого", () => {
		// Arrange: скальперская нога без связок — контракт присылает null.
		const position = makePosition({ syntheticResidual: null, otherResidual: null });

		// Act: рендер таблицы позиций.
		renderTable([position]);

		// Assert: распада нет, остаток показан одной величиной.
		expect(document.querySelector('[data-slot="linear-leg-residual-split"]')).toBeNull();
		expect(screen.getByText("0.34")).toBeTruthy();
	});

	it("не выводит распад у закрытой позиции ноги", () => {
		// Arrange: закрытая нога без синтетики — как в контракте без связок.
		const position = makePosition({ isOpen: false, residual: 0, syntheticResidual: null, otherResidual: null });

		// Act: рендер таблицы позиций.
		renderTable([position]);

		// Assert: узел распада в таблице не появился.
		expect(document.querySelector('[data-slot="linear-leg-residual-split"]')).toBeNull();
	});
});

describe("PositionsTable: колонки времени жизни опционной ноги", () => {
	// Опционная нога на 9 сутках: состав величин как в макете design.pen.
	function makeOptionLeg(overrides: Partial<ConstructionCardPosition> = {}): ConstructionCardPosition {
		return makePosition({
			symbol: "ETH-28MAR-1600-P",
			daysToExpiry: 9,
			dailyDecayUsdt: -6.8,
			intrinsicValueUsdt: 180.4,
			timeValueUsdt: 1059.6,
			decayDayAgoUsdt: -6.2,
			...overrides,
		});
	}

	// Колонки времени жизни строки позиции: сутки и распад выводятся
	// величинами, тултип состава живёт на ячейке стоимости и раскрывает
	// внутреннюю/временную стоимость и динамику распада «вчера → сегодня»
	// нативным title.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-columns-and-tooltip
	it("выводит сутки и распад ноги с тултипом состава на стоимости", () => {
		// Arrange: открытая опционная нога на 9 сутках с распадом и составом.
		// Act: рендер таблицы позиций.
		renderTable([makeOptionLeg()]);

		// Assert: значения суток и распада на месте; тултип один — на ячейке
		// стоимости, ячейки суток и распада без title (строковый матчер сверяет
		// нормализованный title, toHaveAttribute — сырую строку с переносами).
		const tooltipLines = [
			"ETH-28MAR-1600-P · состав позиции",
			"внутренняя +180.4 · временная +1059.6",
			"распад вчера → сегодня: -6.2 → -6.8",
		];
		const row = screen.getByRole("row", { name: /ETH-28MAR-1600-P/ });
		expect(within(row).getAllByTitle(tooltipLines.join(" ")).length).toBe(1);
		expect(within(row).getByText("+1055")).toHaveAttribute("title", tooltipLines.join("\n"));
		expect(within(row).getByText("9")).not.toHaveAttribute("title");
		expect(within(row).getByText("-6.8")).not.toHaveAttribute("title");
	});

	// Строка открытой опционной ноги с остатком суток не более 14
	// подсвечивается фоном внимания, а значение суток выводится красным;
	// дальше зоны подсветки нет.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-expiry-row-highlight
	it("подсвечивает строку ноги в зоне предупреждения и красит сутки", () => {
		// Arrange: две открытые ноги — на 9 и на 21 сутках.
		// Act: рендер таблицы позиций.
		renderTable([makeOptionLeg(), makeOptionLeg({ symbol: "ETH-28JUN-1900-C", daysToExpiry: 21, dailyDecayUsdt: -1.2, decayDayAgoUsdt: null })]);

		// Assert: строка на 9 сутках несёт фон внимания и красные сутки,
		// строка на 21 сутках — без фона и без красного; вчерашний распад
		// без снимка истории даёт прочерк в тултипе.
		const warningRow = screen.getByRole("row", { name: /ETH-28MAR-1600-P/ });
		expect(warningRow).toHaveClass("bg-risk-soft");
		expect(within(warningRow).getByText("9")).toHaveClass("text-destructive");
		const farRow = screen.getByRole("row", { name: /ETH-28JUN-1900-C/ });
		expect(farRow).not.toHaveClass("bg-risk-soft");
		expect(within(farRow).getByText("21")).not.toHaveClass("text-destructive");
		expect(within(farRow).getAllByTitle(/распад вчера → сегодня: — → -1.2/).length).toBe(1);
	});

	// Закрытая позиция и фьючерсная нога показывают в колонках времени жизни
	// прочерк без признака ошибки и без подсветки строки.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-degradation
	it("показывает прочерки времени жизни у закрытой позиции и фьючерсной ноги", () => {
		// Arrange: фьючерсная нога (null величин) и закрытая опционная позиция.
		// Act: рендер таблицы позиций.
		renderTable([makePosition({ symbol: "ETHUSDT" }), makeOptionLeg({ isOpen: false, daysToExpiry: null, dailyDecayUsdt: null })]);

		// Assert: обе строки без подсветки, в колонках суток и распада —
		// прочерки; ячейки дней не несут тултипа состава.
		for (const rowName of [/ETHUSDT/, /ETH-28MAR-1600-P/]) {
			const row = screen.getByRole("row", { name: rowName });
			expect(row).not.toHaveClass("bg-risk-soft");
			const cells = within(row).getAllByRole("cell");
			expect(cells[6]).toHaveTextContent("—");
			expect(cells[7]).toHaveTextContent("—");
		}
		expect(screen.queryByTitle(/состав позиции/)).toBeNull();
	});
});

// Форма пометки закрытия по мастеру «Мастер/Пометка закрытия · форма»:
// поле опционального количества с подсказкой остатка и подсказка внутренней
// стоимости |расчётная цена − страйк| для опционной ноги в деньгах.
// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
describe("PositionsTable: форма пометки — количество и подсказка ВС", () => {
	// Путовая нога в деньгах: страйк 1600, внутренняя стоимость контракта
	// 240 (120 на остатке 0.5) — расчётная цена восстанавливается как 1360.
	function makeItmPut(overrides: Partial<ConstructionCardPosition> = {}): ConstructionCardPosition {
		return makePosition({
			symbol: "ETH-28MAR-1600-P",
			residual: 0.5,
			intrinsicValueUsdt: 120,
			timeValueUsdt: 30,
			...overrides,
		});
	}

	async function openMarkForm(position: ConstructionCardPosition, onAddCloseMark = vi.fn()) {
		renderTable([position], onAddCloseMark);
		const user = userEvent.setup();
		await user.click(await screen.findByRole("button", { name: "закрыть пометкой…" }));
		return { user, onAddCloseMark };
	}

	// Сценарий «Форма пометки подсказывает внутреннюю стоимость»: для ноги
	// в деньгах подсказка показывает |расчётная цена − страйк| в USDT.
	// Traceability: openspec:ui/screens#scenario-mark-form-intrinsic-hint
	it("показывает подсказку внутренней стоимости у ноги в деньгах", async () => {
		// Arrange/Act: открыта форма пометки путовой ноги в деньгах.
		await openMarkForm(makeItmPut());

		// Assert: подсказка видна с восстановленными компонентами величины.
		const hint = document.querySelector('[data-slot="mark-intrinsic-hint"]');
		expect(hint).toBeTruthy();
		expect(hint?.textContent).toContain("|1360 − 1600| = 240 USDT");
	});

	// Подсказка скрыта, когда данных нет: OTM-нога (нулевая внутренняя
	// стоимость), нога без рыночных данных (null) и линейная нога — форма
	// ввод цены не блокирует.
	// Traceability: openspec:ui/screens#scenario-mark-form-intrinsic-hint
	it("скрывает подсказку у OTM-ноги, без рыночных данных и у линейной ноги", async () => {
		// Arrange/Act: три формы по очереди — OTM, без тикера, линейная нога;
		// перед каждым следующим открытием форма размонтируется.
		await openMarkForm(makeItmPut({ intrinsicValueUsdt: 0 }));
		expect(document.querySelector('[data-slot="mark-intrinsic-hint"]')).toBeNull();
		cleanup();

		await openMarkForm(makeItmPut({ intrinsicValueUsdt: null }));
		expect(document.querySelector('[data-slot="mark-intrinsic-hint"]')).toBeNull();
		cleanup();

		await openMarkForm(makePosition({ symbol: "ETHUSDT" }));
		expect(document.querySelector('[data-slot="mark-intrinsic-hint"]')).toBeNull();
	});

	// Сценарий «Частичная пометка оставляет остаток» на стороне формы:
	// placeholder ведёт остатком, подсказка считает остаток после частичного
	// закрытия, заданное количество уходит в команду постановки пометки.
	// Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity
	it("отправляет частичное количество и ведёт остаток подсказкой", async () => {
		// Arrange: открытая форма с командой-двойником.
		const { user, onAddCloseMark } = await openMarkForm(makeItmPut());
		const quantity = await screen.findByLabelText("количество пометки");
		expect(quantity.getAttribute("placeholder")).toBe("весь остаток — 0.5");

		// Act: задано частичное количество 0.2 из остатка 0.5.
		await user.type(quantity, "0.2");

		// Assert: подсказка показывает остаток после частичного закрытия,
		// команда несёт заданное количество.
		expect(screen.getByText("частичное закрытие — останется 0.3")).toBeTruthy();
		await user.click(screen.getByRole("button", { name: "Поставить пометку" }));
		expect(onAddCloseMark).toHaveBeenCalledWith({ symbol: "ETH-28MAR-1600-P", markedAt: expect.any(String), price: null, quantity: 0.2 });
	});

	// Незаданное количество уходит в команду как null — весь остаток.
	// Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity
	it("отправляет null при незаданном количестве", async () => {
		// Arrange: форма с командой-двойником.
		const { user, onAddCloseMark } = await openMarkForm(makeItmPut());

		// Act: количество не тронуто, пометка ставится сразу.
		await user.click(screen.getByRole("button", { name: "Поставить пометку" }));

		// Assert: в команде quantity null — закроется весь остаток.
		expect(onAddCloseMark).toHaveBeenCalledWith({ symbol: "ETH-28MAR-1600-P", markedAt: expect.any(String), price: null, quantity: null });
	});
});

// Форма пометки скрывается только после успешной постановки; при отказе
// сервера она остаётся открытой с человекочитаемой причиной отказа.
// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
describe("PositionsTable: форма пометки закрывается по результату", () => {
	async function openForm(position: ConstructionCardPosition, onAddCloseMark: () => void) {
		renderTable([position], onAddCloseMark);
		const user = userEvent.setup();
		await user.click(await screen.findByRole("button", { name: "закрыть пометкой…" }));
		return { user, onAddCloseMark };
	}

	it("скрывает форму после успешной постановки пометки", async () => {
		// Arrange: открытая форма с командой, разрешающейся успехом.
		const { user, onAddCloseMark } = await openForm(
			makePosition({ symbol: "ETH-28MAR-1600-P", residual: 0.5, intrinsicValueUsdt: 120, timeValueUsdt: 30 }),
			vi.fn().mockResolvedValue(undefined),
		);

		// Act: пометка ставится.
		await user.click(screen.getByRole("button", { name: "Поставить пометку" }));

		// Assert: команда выполнена, форма скрыта.
		await waitFor(() => expect(onAddCloseMark).toHaveBeenCalledTimes(1));
		expect(screen.queryByRole("button", { name: "Поставить пометку" })).not.toBeInTheDocument();
	});

	it("оставляет форму открытой с причиной отказа при ошибке сервера", async () => {
		// Arrange: открытая форма с отклонённой командой.
		const { user } = await openForm(
			makePosition({ symbol: "ETH-28MAR-1600-P", residual: 0.5, intrinsicValueUsdt: 120, timeValueUsdt: 30 }),
			vi.fn().mockRejectedValue(new Error("инструмент неизвестен")),
		);

		// Act: попытка постановки пометки.
		await user.click(screen.getByRole("button", { name: "Поставить пометку" }));

		// Assert: форма осталась открыта, причина отказа показана в форме.
		expect(await screen.findByText(/Пометка не поставлена: инструмент неизвестен/)).toHaveAttribute("role", "alert");
		expect(screen.getByRole("button", { name: "Поставить пометку" })).toBeInTheDocument();
	});
});
