import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ConstructionCardAdjustment } from "@/lib/api/construction-card";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { TooltipProvider } from "@/components/ui/tooltip";
import { AdjustmentsTable } from "./adjustments-table";

// Таблица корректировок PnL: заголовок числовой колонки суммы поясняется
// подсказкой метрики; колонки даты, описания и источника — без подсказок.
// Traceability: openspec:ui/screens#requirement-column-header-tooltips
// Traceability: openspec:ui/screens#scenario-date-columns-skip-tooltip

/** Ручная корректировка PnL для таблицы. */
const adjustments: ConstructionCardAdjustment[] = [
	{
		adjustmentId: 5,
		date: "2026-09-30T00:00:00+00:00",
		description: "PnL робота grid-ETH за сентябрь",
		source: "manual",
		amountUsdt: 87.4,
	},
];

function renderTable() {
	render(
		<TooltipProvider delayDuration={0}>
			<AdjustmentsTable
				adjustments={adjustments}
				actionsPending={false}
				onAdd={vi.fn()}
				onEdit={vi.fn()}
				onDelete={vi.fn()}
			/>
		</TooltipProvider>,
	);
}

// Методы расчёта, зафиксированные в справочнике для других метрик: у суммы
// корректировки метода нет, и чужая формула в подсказке не появляется.
const knownMethodTexts = Object.values(METRIC_TOOLTIPS)
	.map((tooltip) => tooltip.method)
	.filter((method): method is string => method !== undefined);

describe("таблица корректировок PnL: подсказки заголовков колонок", () => {
	// Сумма корректировки поясняется в заголовке подсказкой метрики —
	// только назначением, без метода расчёта.
	// Traceability: openspec:ui/screens#requirement-column-header-tooltips
	it("даёт подсказку колонке «Сумма» с назначением без метода", async () => {
		// Arrange: таблица корректировок под провайдером подсказок.
		const user = userEvent.setup();
		renderTable();

		// Act: наведение на заголовок колонки суммы.
		await user.hover(screen.getByText("Сумма"));

		// Assert: попап содержит заголовок и назначение из справочника
		// и не содержит метода ни одной другой метрики.
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.adjustmentValue.title);
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.adjustmentValue.purpose);
		for (const method of knownMethodTexts) {
			expect(tooltip).not.toHaveTextContent(method);
		}
	});

	// Колонка даты корректировки — дата: подсказка метрики у дат нет.
	// Traceability: openspec:ui/screens#scenario-date-columns-skip-tooltip
	it("не даёт подсказки колонке даты", async () => {
		// Arrange: таблица корректировок под провайдером подсказок.
		const user = userEvent.setup();
		renderTable();

		// Act: наведение на заголовок колонки даты.
		await user.hover(screen.getByText("Дата"));

		// Assert: подсказка не открылась.
		expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
	});
});
