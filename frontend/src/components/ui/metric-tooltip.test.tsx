import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { MetricHint, MetricTooltipContent } from "./metric-tooltip";
import { TooltipProvider } from "./tooltip";

// Проверяется обвязка подсказки метрики: попап строится из справочника
// текстов — заголовок, назначение и разделы по каждому показателю
// многопоказательной метрики; триггер открывает подсказку наведением.
// Traceability: openspec:ui/design-system#requirement-design-metric-tooltip-pattern
// Traceability: openspec:ui/screens#scenario-metric-tooltip-on-hover-focus
function renderHint() {
	return render(
		<TooltipProvider delayDuration={0}>
			<MetricHint tooltip={METRIC_TOOLTIPS.capitalPercent}>
				64% (600 USDT)
			</MetricHint>
		</TooltipProvider>,
	);
}

describe("подсказка метрики: обвязка справочника", () => {
	it("содержимое повторяет структуру справочника: заголовок, назначение, разделы", () => {
		// Попап подсказки показывает заголовок метрики, назначение и
		// по разделу на каждый показатель ячейки.
		// Traceability: openspec:ui/screens#scenario-multi-indicator-sections

		// Arrange/Act: содержимое для многопоказательной метрики.
		render(<MetricTooltipContent tooltip={METRIC_TOOLTIPS.capitalPercent} />);

		// Assert: заголовок, назначение и разделы присутствуют;
		// заголовок может совпадать с меткой раздела — ищем все вхождения.
		expect(
			screen.getAllByText(METRIC_TOOLTIPS.capitalPercent.title).length,
		).toBeGreaterThan(0);
		expect(screen.getByText(METRIC_TOOLTIPS.capitalPercent.purpose)).toBeInTheDocument();
		for (const section of METRIC_TOOLTIPS.capitalPercent.sections ?? []) {
			// Метки разделов могут повторять заголовок — проверяем наличие
			// всех вхождений без требования уникальности.
			expect(screen.getAllByText(section.label).length).toBeGreaterThan(0);
			expect(screen.getAllByText(section.text).length).toBeGreaterThan(0);
		}
	});

	it("наведение на триггер открывает подсказку метрики", async () => {
		// Подсказка появляется при наведении курсора на значение метрики.
		// Traceability: openspec:ui/screens#scenario-metric-tooltip-on-hover-focus

		// Arrange: триггер со значением метрики.
		const user = userEvent.setup();
		renderHint();

		// Act: наведение на значение.
		await user.hover(screen.getByText("64% (600 USDT)"));

		// Assert: попап со справочным текстом появился в портале.
		expect(screen.getByRole("tooltip")).toHaveTextContent(
			METRIC_TOOLTIPS.capitalPercent.title,
		);
	});
});
