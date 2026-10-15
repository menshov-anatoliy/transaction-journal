import { render, screen } from "@testing-library/react";
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
