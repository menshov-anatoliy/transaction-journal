import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { ConstructionPreview } from "@/lib/api/constructions";
import { ConstructionPreviewCard } from "./construction-preview";

// Проверяется превью выделенной конструкции правой области по концепции §3:
// read-only сводка метрик, полный индикатор финрезультата, период, счётчики
// позиций/сделок/корректировок, кнопки «Открыть карточку» и «В новом окне».
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

const preview: ConstructionPreview = {
	constructionId: 7,
	name: "ETH-240628-3200C+P",
	status: "open",
	allocatedCapitalUsdt: 3000,
	riskPercent: 3,
	riskUsdt: 90,
	profitPercent: 8,
	profitUsdt: 240,
	realizedPnL: 60.25,
	unrealizedPnL: 40.25,
	adjustmentsPnL: 5,
	totalPnL: 105.5,
	totalPnLPercent: 3.5,
	markValue: 500,
	capitalUsagePercent: 16.7,
	realRiskUsdt: 45,
	openedAt: "2026-06-18T09:05:00",
	closedAt: null,
	marksAsOf: "2026-06-20T14:30:00",
	hasMarkFailure: false,
	counts: { positions: 2, openPositions: 1, trades: 3, adjustments: 1 },
};

function renderPreview(overrides: Partial<ConstructionPreview> = {}) {
	return render(
		<MemoryRouter>
			<ConstructionPreviewCard preview={{ ...preview, ...overrides }} />
		</MemoryRouter>,
	);
}

describe("превью конструкции", () => {
	it("показывает сводку метрик, период и счётчики записей", () => {
		// Act: превью открытой конструкции.
		renderPreview();

		// Assert: имя и метрики итога и производных величин видны.
		expect(screen.getByRole("heading", { name: "ETH-240628-3200C+P" })).toBeInTheDocument();
		// Итог в сводке: метрика содержит итог одним текстом.
		const totalMetric = Array.from(document.querySelectorAll('[data-slot="metric-value"]')).find(
			(el) => el.textContent === "+105.5",
		);
		expect(totalMetric).toBeDefined();
		// Маркер индикатора подписан итогом realized + unrealized
		// (+100.5; корректировки 5 входят только в сводку totalPnL 105.5).
		const markerLabel = document.querySelector('[data-part="marker-label"]');
		expect(markerLabel).not.toBeNull();
		expect(markerLabel?.textContent).toContain("+100.5");
		expect(screen.getByText("+60.25")).toBeInTheDocument();
		// Нереализованная часть видна в сводке.
		expect(screen.getByText("+40.25")).toBeInTheDocument();
		expect(screen.getByText("+5")).toBeInTheDocument();
		expect(screen.getByText("3000")).toBeInTheDocument();
		expect(screen.getByText("+500")).toBeInTheDocument();
		expect(screen.getByText("+16.7%")).toBeInTheDocument();
		// Период конструкции.
		expect(screen.getByText("2026-06-18")).toBeInTheDocument();
		// Счётчики записей превью.
		expect(screen.getByText("позиции: 2 (1 открыто)")).toBeInTheDocument();
		expect(screen.getByText("сделки: 3")).toBeInTheDocument();
		expect(screen.getByText("корректировки: 1")).toBeInTheDocument();
	});

	it("рендерит полный индикатор финрезультата с плановыми границами", () => {
		// Act: превью открытой конструкции.
		renderPreview();

		// Assert: полный индикатор присутствует в превью.
		expect(document.querySelector('[data-slot="fin-result-full"]')).not.toBeNull();
	});

	it("показывает отметку времени котировок", () => {
		// Act: превью с оценёнными марками.
		renderPreview();

		// Assert: отметка «котировки на» с локальным временем.
		expect(screen.getByText(/котировки на/i)).toBeInTheDocument();
		expect(screen.getByText("2026-06-20 14:30")).toBeInTheDocument();
	});

	it("деградирует при сбое котировок неполным итогом", () => {
		// Act: превью со сбоем марок.
		renderPreview({ unrealizedPnL: null, totalPnL: null, hasMarkFailure: true, marksAsOf: null });

		// Assert: признак сбоя котировок вместо величин и отметки времени.
		expect(screen.getAllByText("неполный (сбой котировок)").length).toBeGreaterThan(0);
		expect(screen.queryByText(/котировки на/i)).not.toBeInTheDocument();
	});

	it("ведёт «Открыть карточку» на маршрут карточки конструкции", () => {
		// Act: превью открытой конструкции.
		renderPreview();

		// Assert: ссылка указывает на маршрут карточки.
		expect(screen.getByRole("link", { name: /открыть карточку/i })).toHaveAttribute("href", "/constructions/7");
	});

	it("рендерит сводку метрик примитивом Metric по мастеру превью N6abN", () => {
		// Act: превью открытой конструкции.
		renderPreview();

		// Assert: восемь показателей — примитивы Метрика, dl-грида больше нет
		// (мастер «Карточка конструкции/Превью», секция Metrics ноды N6abN).
		// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
		const metrics = document.querySelectorAll('[data-slot="metric"]');
		expect(metrics).toHaveLength(8);
		expect(document.querySelector("dl")).toBeNull();

		// Значения — базовая типографика примитива: 15/600 textPrimary
		// (инстансы превью не переопределяют размер и цвет значения).
		// Из двух вхождений итога берём вхождение сводки — внутри metric-value.
		const total = screen
			.getAllByText("+105.5")
			.map((element) => element.closest('[data-slot="metric-value"]'))
			.find(Boolean);
		expect(total?.className).toContain("text-[15px]");
		expect(total?.className).toContain("font-semibold");
		expect(total?.className).toContain("text-text-primary");

		// Сетка показателей — две колонки с зазорами мастера: 12 по горизонтали,
		// 10 по вертикали (MRow1/MRow2, секция Metrics ноды N6abN).
		const grid = metrics[0]?.parentElement;
		expect(grid?.className).toContain("grid-cols-2");
		expect(grid?.className).toContain("gap-x-3");
		expect(grid?.className).toContain("gap-y-2.5");

		// Подписи — Caption мастера: 11/normal textMuted с трекингом 0.3.
		const caption = metrics[0]?.querySelector("span");
		expect(caption?.className).toContain("text-[11px]");
		expect(caption?.className).toContain("text-text-muted");
	});

	it("открывает «В новом окне» монтаж SPA карточки", async () => {
		// Arrange: перехват открытия окна.
		const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);
		const user = userEvent.setup();
		renderPreview();

		// Act: кнопка «В новом окне».
		await user.click(screen.getByRole("button", { name: /в новом окне/i }));

		// Assert: открыта карточка в монтаже /spa нового окна.
		expect(openSpy).toHaveBeenCalledWith("/spa/constructions/7", "_blank");
		openSpy.mockRestore();
	});
});
