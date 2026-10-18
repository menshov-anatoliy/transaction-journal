import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionCardPosition } from "@/lib/api/construction-card";
import { PositionsTable } from "./positions-table";

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

function renderTable(positions: readonly ConstructionCardPosition[]) {
	return render(
		<PositionsTable
			constructionId={7}
			positions={positions}
			markPending={false}
			onAddCloseMark={vi.fn()}
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

	// Колонки времени жизни строки позиции с тултипом состава: сутки и распад
	// выводятся величинами, тултип раскрывает внутреннюю/временную стоимость
	// и динамику распада «вчера → сегодня» нативным title.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-columns-and-tooltip
	it("выводит сутки и распад ноги с тултипом состава", () => {
		// Arrange: открытая опционная нога на 9 сутках с распадом и составом.
		// Act: рендер таблицы позиций.
		renderTable([makeOptionLeg()]);

		// Assert: значения суток и распада на месте, тултип обеих ячеек несёт
		// заголовок состава, стоимости и динамику «вчера → сегодня» (пробельные
		// переносы title матчер нормализует).
		const tooltipText =
			"ETH-28MAR-1600-P · состав позиции внутренняя +180.4 · временная +1059.6 распад вчера → сегодня: -6.2 → -6.8";
		expect(screen.getAllByTitle(tooltipText).length).toBe(2);
		expect(screen.getByText("-6.8")).toBeTruthy();
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
		expect(within(farRow).getAllByTitle(/распад вчера → сегодня: — → -1.2/).length).toBe(2);
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
