import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionCard } from "@/lib/api/construction-card";
import { ConstructionMetricStrip } from "./metric-strip";

// Проверяется строка «зафиксировано синтетикой» сводки деталей конструкции
// (мастер design.pen Z5uVG в карточке «Сводка метрик» h69OG): величина
// паритетного лока к экспирации и логический размер закрываемой ноги
// «0.5 → 0.2»; индикатор финрезультата и инструментальные величины
// строкой не пересчитываются. Спорные связки и отсутствие связок скрывают
// строку целиком.
// Traceability: openspec:ui/screens#requirement-detail-synthetic-close-line

/** Базовый снимок карточки: открытый стреддл с одной опционной ногой. */
function makeCard(overrides: Partial<ConstructionCard> = {}): ConstructionCard {
	return {
		constructionId: 7,
		name: "ETH-28MAR-1600-C+P",
		status: "open",
		allocatedCapitalUsdt: 3000,
		riskPercent: 3,
		riskUsdt: 90,
		riskUnit: "percent",
		profitPercent: 8,
		profitUsdt: 240,
		profitUnit: "usdt",
		comment: null,
		hasOpenResidual: true,
		hasMarkFailure: false,
		marksAsOf: "2026-10-15T14:30:00+00:00",
		metrics: {
			realizedPnL: 60.25,
			unrealizedPnL: 40.25,
			adjustmentsPnL: 5,
			totalPnL: 105.5,
			totalPnLPercent: 3.5,
			realizedPnLPercent: 2,
			unrealizedPnLPercent: 1.34,
			adjustmentsPnLPercent: 0.17,
			markValue: 500,
			capitalUsagePercent: 16.7,
			realRiskUsdt: 45,
			realRiskStatus: "finite",
			openedAt: "2026-10-01T09:05:00+00:00",
			closedAt: null,
			durationSeconds: 3600,
		},
		positions: [
			{
				symbol: "ETH-28MAR-1600-P",
				residual: 0.5,
				averageEntryPrice: 30,
				averageClosePrice: null,
				realizedPnL: 40,
				realizedPnLPercent: 1.33,
				unrealizedPnL: 30,
				unrealizedPnLPercent: 1,
				totalPnL: 70,
				totalPnLPercent: 2.33,
				accumulatedFees: 0.5,
				openedAt: "2026-10-01T09:05:00+00:00",
				closedAt: null,
				isOpen: true,
				comment: null,
				markValue: 300,
				priceChangePercent: 5,
				syntheticResidual: null,
				otherResidual: null,
			},
		],
		trades: [],
		closingEntries: [],
		closingWarnings: [],
		adjustments: [],
		syntheticCloses: [],
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

describe("ConstructionMetricStrip: строка «зафиксировано синтетикой»", () => {
	it("показывает лок к экспирации и логический размер ноги при неоспоренной связке", () => {
		// Сценарий «Сводка показывает зафиксированный синтетикой результат»:
		// связка 0.3 из путовой ноги 0.5 — строка с локом к экспирации,
		// нога показывается сокращённой до 0.2.
		// Traceability: openspec:ui/screens#scenario-detail-synthetic-locked-line-shown
		// Arrange: связка закрытия 0.3 путовой ноги из 0.5, лок 35.7 USDT.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
			}],
		});

		// Act: рендер сводки карточки.
		render(<ConstructionMetricStrip card={card} />);

		// Assert: строка выводится с величиной лока и сокращением ноги
		// 0.5 → 0.2; инструментальные величины в строке не повторяются.
		expect(screen.getByText("зафиксировано синтетикой")).toBeTruthy();
		expect(screen.getByText("+35.7 USDT")).toBeTruthy();
		expect(screen.getByText("ETH-28MAR-1600-P · нога 0.5 → 0.2")).toBeTruthy();
	});

	it("суммирует лок нескольких неоспоренных связок в одной строке", () => {
		// Arrange: две связки по одной и той же ноге: 0.1 + 0.2 из 0.5.
		const card = makeCard({
			syntheticCloses: [
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.1,
					lockedValueUsdt: 12,
					disputed: false,
					linearExecId: "exec-linear-1",
					counterOptionExecId: "exec-option-1",
				},
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.2,
					lockedValueUsdt: 23.7,
					disputed: false,
					linearExecId: "exec-linear-2",
					counterOptionExecId: "exec-option-2",
				},
			],
		});

		// Act: рендер сводки карточки.
		render(<ConstructionMetricStrip card={card} />);

		// Assert: лок 35.7 USDT, логический размер ноги 0.5 − 0.3 = 0.2.
		expect(screen.getByText("+35.7 USDT")).toBeTruthy();
		expect(screen.getByText("ETH-28MAR-1600-P · нога 0.5 → 0.2")).toBeTruthy();
	});

	it("не выводит строку без распознанных связок", () => {
		// Сценарий «Без связок и при споре строка отсутствует»: без связок
		// отображение не отличается от конструкции без синтетики.
		// Traceability: openspec:ui/screens#scenario-detail-synthetic-line-absent
		// Arrange: карточка без синтетических закрытий.
		const card = makeCard({ syntheticCloses: [] });

		// Act: рендер сводки карточки.
		render(<ConstructionMetricStrip card={card} />);

		// Assert: ни строки, ни её узлов в DOM нет — отображение не
		// отличается от конструкции без синтетики.
		expect(screen.queryByText("зафиксировано синтетикой")).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-locked-line"]')).toBeNull();
	});

	it("не выводит строку, когда все связки оспорены", () => {
		// Сценарий «Без связок и при споре строка отсутствует»: спорная
		// связка не входит ни в лок, ни в логический слой ноги.
		// Traceability: openspec:ui/screens#scenario-detail-synthetic-line-absent
		// Arrange: единственная связка оспорена.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: true,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
			}],
		});

		// Act: рендер сводки карточки.
		render(<ConstructionMetricStrip card={card} />);

		// Assert: строка отсутствует целиком, включая разделитель.
		expect(screen.queryByText("зафиксировано синтетикой")).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-locked-line"]')).toBeNull();
	});

	it("не пересчитывает инструментальные величины сводки логическим слоем", () => {
		// Arrange: связка уводит 0.3 из ноги с инструментальным остатком 0.5.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
			}],
		});

		// Act: рендер сводки карточки.
		render(<ConstructionMetricStrip card={card} />);

		// Assert: «Реализованный результат» остаётся инструментальным —
		// его величина совпадает с metrics.realizedPnL, лок в него не вмешался.
		expect(screen.getByText("+60.25")).toBeTruthy();
	});
});
