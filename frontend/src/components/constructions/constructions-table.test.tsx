import { act, render, screen, within } from "@testing-library/react";
import { Profiler, useState } from "react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ConstructionRow } from "@/lib/api/constructions";
import { ConstructionsTable } from "./constructions-table";

// Проверяется таблица конструкций раздела по концепции §3: паритет колонок
// мастера design.pen, бейджи живых подсказок и предупреждений в строке,
// компактный индикатор финрезультата, клик по строке выделяет (не навигация),
// сортировка колонками.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

const openRow: ConstructionRow = {
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
	adjustmentsPnL: 0,
	totalPnL: 100.5,
	totalPnLPercent: 3.35,
	markValue: 500,
	capitalUsagePercent: 16.7,
	realRiskUsdt: 45,
	worstCaseUsdt: 15.25,
	realRiskStatus: "finite",
	openedAt: "2026-06-18T09:05:00Z",
	closedAt: null,
	liveHintCount: 2,
	riskWarningsCount: 1,
};

const closedRow: ConstructionRow = {
	constructionId: 8,
	name: "BTC-240531-60000C",
	status: "closed",
	allocatedCapitalUsdt: null,
	riskPercent: null,
	riskUsdt: null,
	profitPercent: null,
	profitUsdt: null,
	realizedPnL: 0,
	unrealizedPnL: 0,
	adjustmentsPnL: 0,
	totalPnL: 0,
	totalPnLPercent: null,
	markValue: null,
	capitalUsagePercent: null,
	realRiskUsdt: 0,
	worstCaseUsdt: 0,
	realRiskStatus: "finite",
	openedAt: "2026-05-20T10:00:00Z",
	closedAt: "2026-05-31T12:00:00Z",
	liveHintCount: 0,
	riskWarningsCount: 0,
};

describe("таблица конструкций", () => {
	// Изменение выделения не должно запускать повторные обновления неизменных строк.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	it("ThrowOnSelectionRenderLoop: выделение единственной строки не зацикливает таблицу", async () => {
		// Arrange: минимальный сценарий без запросов API и контекстных панелей.
		const user = userEvent.setup();
		const rows = [openRow];
		let commits = 0;
		function Selection() {
			const [selectedId, setSelectedId] = useState<number | null>(null);
			return <ConstructionsTable rows={rows} selectedId={selectedId} onSelect={setSelectedId} />;
		}
		render(
			<Profiler id="table" onRender={() => {
				commits += 1;
				if (commits > 30) {
					throw new Error("Выделение строки вызвало бесконечное обновление таблицы");
				}
			}}>
				<Selection />
			</Profiler>,
		);
		await act(async () => { await Promise.resolve(); });

		// Act: выделение строки и завершение отложенных обновлений.
		await user.click(screen.getByRole("row", { name: /ETH-240628/ }));
		await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });
		const settledCommits = commits;
		await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });

		// Assert: выделение сохранено, таблица перестала перерисовываться.
		expect(screen.getByRole("row", { name: /ETH-240628/ })).toHaveAttribute("aria-selected", "true");
		expect(commits).toBe(settledCommits);
	});

	// Все состояния конструкции показаны единым StatusChip в дизайн-тонах.
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	it("показывает открытый, закрытый и архивный статусы пилюлями", () => {
		// Arrange / Act: строки всех трёх состояний.
		render(
			<ConstructionsTable
				rows={[openRow, closedRow, { ...closedRow, constructionId: 9, status: "archived" }]}
				selectedId={null}
				onSelect={() => {}}
			/>,
		);

		// Assert: подписи и цвета совпадают с карточкой конструкции.
		for (const [label, tone] of [["открыта", "text-accent-strong"], ["закрыта", "text-text-secondary"], ["архив", "text-text-muted"]]) {
			expect(screen.getByText(label)).toHaveAttribute("data-slot", "status-chip");
			expect(screen.getByText(label)).toHaveClass(tone, "rounded-full");
		}
	});

	it("выводит колонки паритета и строки с величинами", () => {
		// Act: таблица с двумя строками.
		render(<ConstructionsTable rows={[openRow, closedRow]} selectedId={null} onSelect={() => {}} />);

		// Assert: заголовки паритета колонок на месте (подписи по мастеру
		// design.pen: «Имя», глифы «✦» подсказок и «⚠» предупреждений).
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		for (const header of [
			"Имя",
			"✦",
			"⚠",
			"Статус",
			"Капитал",
			"Стоимость",
			"Реализов.",
			"Нереализов.",
			"Коррект.",
			"Итог",
			"% капитала",
			"Занято %",
			"Открыта",
			"Закрыта",
		]) {
			expect(screen.getByRole("columnheader", { name: header })).toBeInTheDocument();
		}

		// Assert: строки несут имя, статус словами и величины.
		expect(screen.getByRole("cell", { name: "ETH-240628-3200C+P" })).toBeInTheDocument();
		expect(screen.getByText("открыта")).toBeInTheDocument();
		expect(screen.getByText("закрыта")).toBeInTheDocument();
		expect(screen.getByText("+100.5")).toBeInTheDocument();
		// Незаданный капитал — прочерк, а не ноль.
		expect(screen.getAllByText("—").length).toBeGreaterThan(0);
	});

	it("показывает бейдж живых подсказок только у строк с подсказками", () => {
		// Act: таблица с двумя строками.
		render(<ConstructionsTable rows={[openRow, closedRow]} selectedId={null} onSelect={() => {}} />);

		// Assert: бейдж «✦ N» тоном риска у первой строки, у второй — прочерк.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		const badge = screen.getByRole("cell", { name: "✦ 2" }).querySelector('[data-slot="hint-badge"]');
		expect(badge).not.toBeNull();
		expect(badge?.className).toContain("text-[color:var(--risk)]");
		expect(screen.getByRole("row", { name: /BTC-240531/ }).querySelector('[data-slot="hint-badge"]')).toBeNull();
	});

	it("показывает бейдж предупреждений только у строк с предупреждениями", () => {
		// Act: таблица с двумя строками.
		render(<ConstructionsTable rows={[openRow, closedRow]} selectedId={null} onSelect={() => {}} />);

		// Assert: бейдж «⚠ N» тоном риска у строки с предупреждениями, доступное
		// имя несёт число предупреждений; у строки без предупреждений бейджа нет.
		// Traceability: openspec:ui/screens#scenario-list-row-warning-badge
		// Traceability: openspec:ui/screens#scenario-list-row-no-warning-badge
		const badge = screen.getByTitle("предупреждений: 1");
		expect(badge).toHaveAttribute("data-slot", "warning-badge");
		expect(badge).toHaveTextContent("⚠ 1");
		expect(badge).toHaveClass("text-[color:var(--risk)]", "font-semibold");
		expect(screen.getByRole("row", { name: /BTC-240531/ }).querySelector('[data-slot="warning-badge"]')).toBeNull();
	});

	// Строка с неограниченным реальным риском и предупреждениями: бейдж «∞»
	// компактного индикатора сохраняется, бейдж «⚠ N» показывается независимо.
	// Traceability: openspec:ui/screens#scenario-unbounded-badge-coexists-with-warnings
	it("показывает бейдж ∞ и бейдж предупреждений в строке независимо друг от друга", () => {
		// Act: таблица со строкой, у которой статус риска unbounded и есть предупреждения.
		render(
			<ConstructionsTable
				rows={[{ ...openRow, realRiskUsdt: null, realRiskStatus: "unbounded" as const }]}
				selectedId={null}
				onSelect={() => {}}
			/>,
		);

		// Assert: в одной строке живут бейдж «∞» поверх полосы и бейдж «⚠ 1».
		const row = screen.getByRole("row", { name: /ETH-240628/ });
		expect(row.querySelector('[data-slot="fin-result-compact"] [data-part="risk-state"]')).toHaveTextContent("∞");
		expect(row.querySelector('[data-slot="warning-badge"]')).toHaveTextContent("⚠ 1");
	});

	// Ячейка не переопределяет ширину 132px компактного мастера FMghY.
	// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
	it("рендерит компактный индикатор финрезультата в строке", () => {
		// Act: таблица с открытой строкой.
		render(<ConstructionsTable rows={[openRow]} selectedId={null} onSelect={() => {}} />);

		// Assert: компактный индикатор в ячейке итога.
		const indicator = screen.getByRole("row", { name: /ETH-240628/ }).querySelector('[data-slot="fin-result-compact"]');
		expect(indicator).not.toBeNull();
		expect(indicator).toHaveClass("w-[132px]");
		expect(indicator).not.toHaveClass("w-28");
	});

	it("выделяет строку кликом и подсвечивает выделенную", async () => {
		// Arrange: обработчик выделения.
		const user = userEvent.setup();
		const onSelect = vi.fn();
		render(<ConstructionsTable rows={[openRow, closedRow]} selectedId={7} onSelect={onSelect} />);

		// Act: клик по невыделенной строке.
		await user.click(screen.getByRole("row", { name: /BTC-240531/ }));

		// Assert: выделение пришло идентификатором строки.
		expect(onSelect).toHaveBeenCalledWith(8);
		expect(screen.getByRole("row", { name: /ETH-240628/ }).getAttribute("aria-selected")).toBe("true");
		expect(screen.getByRole("row", { name: /BTC-240531/ }).getAttribute("aria-selected")).toBe("false");
	});

	it("сортирует строки кликом по колонке", async () => {
		// Arrange: пользователь для нажатия заголовка сортировки.
		const user = userEvent.setup();
		render(<ConstructionsTable rows={[closedRow, openRow]} selectedId={null} onSelect={() => {}} />);

		// Act: сортировка по колонке «Итог».
		await user.click(screen.getByRole("button", { name: "Итог" }));

		// Assert: строка с большим итогом поднялась наверх.
		const body = screen.getAllByRole("row").filter((row) => row.querySelectorAll("td").length > 0);
		const firstDataRow = body.find((row) => within(row).queryByText("ETH-240628-3200C+P"))!;
		expect(body.indexOf(firstDataRow)).toBeLessThan(
			body.indexOf(body.find((row) => within(row).queryByText("BTC-240531-60000C"))!),
		);
	});

	it("двойной клик по строке запрашивает открытие карточки в новом окне", async () => {
		// Двойной клик по строке — команда навигации в карточку в новом
		// окне, отдельная от выделения одиночным кликом.
		// Traceability: openspec:ui/screens#scenario-list-double-click-opens-new-window
		// Arrange: шпион на window.open и обработчики таблицы.
		const user = userEvent.setup();
		const onSelect = vi.fn();
		const onOpenInNewWindow = vi.fn();
		render(<ConstructionsTable rows={[openRow, closedRow]} selectedId={null} onSelect={onSelect} onOpenInNewWindow={onOpenInNewWindow} />);

		// Act: двойной клик по строке.
		await user.dblClick(screen.getByRole("row", { name: /ETH-240628/ }));

		// Assert: идентификатор конструкции ушёл в обработчик нового окна.
		expect(onOpenInNewWindow).toHaveBeenCalledWith(7);
	});

	it("пустой список показывает явное сообщение", () => {
		// Act: таблица без строк.
		render(<ConstructionsTable rows={[]} selectedId={null} onSelect={() => {}} />);

		// Assert: сообщение отсутствия, а не пустая разметка.
		expect(screen.getByText("конструкций нет")).toBeInTheDocument();
	});
});
