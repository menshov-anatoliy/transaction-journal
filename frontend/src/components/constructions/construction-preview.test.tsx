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

		// Assert: имя, метрики итога и производные величины видны.
		expect(screen.getByRole("heading", { name: "ETH-240628-3200C+P" })).toBeInTheDocument();
		expect(screen.getByText("+105.5")).toBeInTheDocument();
		expect(screen.getByText("+60.25")).toBeInTheDocument();
		// Нереализованная часть видна и в сводке, и в подписи маркера индикатора.
		expect(screen.getAllByText("+40.25").length).toBeGreaterThan(0);
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
