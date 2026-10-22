import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ConstructionCardClosingEntry, ConstructionCardClosingWarning } from "@/lib/api/construction-card";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ClosingEntriesTable } from "./closing-entries-table";

// Таблица закрывающих записей: правка ручной пометки из строки — форма с
// ценой, количеством и временем; количество предзаполняется применённым
// значением записи, пустое поле возвращает пометку к закрытию всего остатка.
// Частичная пометка после правки цены или времени сохраняет частичность,
// не расширяясь молча до всего остатка.
// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
// Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity

function renderTable(entries: ConstructionCardClosingEntry[], onEditAnnotation: (input: unknown) => void = () => undefined) {
	const warnings: ConstructionCardClosingWarning[] = [];
	render(
		<TooltipProvider delayDuration={0}>
			<ClosingEntriesTable
				entries={entries}
				warnings={warnings}
				actionsPending={false}
				onEditAnnotation={onEditAnnotation as never}
				onDeleteAnnotation={() => undefined}
			/>
		</TooltipProvider>,
	);
}

/** Частичная ручная пометка: закрыла 0.3 из 0.5 путовой ноги. */
const partialAnnotation: ConstructionCardClosingEntry = {
	closedAt: "2026-10-15T13:30:00+00:00",
	kind: "manual-mark",
	symbol: "ETH-28MAR-1600-P",
	quantity: -0.3,
	price: 100,
	amountUsdt: -30,
	manualAnnotationId: 21,
};

describe("таблица закрывающих записей: правка ручной пометки", () => {
	it("предзаполняет количество значением записи и сохраняет частичность при правке", async () => {
		// Arrange: раскрытая форма правки частичной пометки; замер команды правки.
		const user = userEvent.setup();
		const onEditAnnotation = vi.fn();
		renderTable([partialAnnotation], onEditAnnotation);
		await user.click(screen.getByRole("button", { name: "править" }));

		// Assert: поле количества предзаполнено применённым количеством записи.
		const quantityInput = screen.getByLabelText("количество пометки") as HTMLInputElement;
		expect(quantityInput.value).toBe("0.3");

		// Act: правим только цену — количество не трогаем.
		const priceInput = screen.getByPlaceholderText("пусто — последняя котировка");
		await user.clear(priceInput);
		await user.type(priceInput, "95");
		await user.click(screen.getByRole("button", { name: "Сохранить пометку" }));

		// Assert: правка сохранила заданное количество — частичная пометка не
		// превратилась в закрытие всего остатка.
		expect(onEditAnnotation).toHaveBeenCalledWith({
			annotationId: 21,
			annotation: { symbol: "ETH-28MAR-1600-P", annotatedAt: expect.any(String), price: 95, quantity: 0.3 },
		});
	});

	it("пустое количество возвращает пометку к закрытию всего остатка", async () => {
		// Arrange: раскрытая форма правки частичной пометки.
		const user = userEvent.setup();
		const onEditAnnotation = vi.fn();
		renderTable([partialAnnotation], onEditAnnotation);
		await user.click(screen.getByRole("button", { name: "править" }));

		// Act: владелец очищает количество — остаток закрывается целиком.
		await user.clear(screen.getByLabelText("количество пометки"));
		await user.click(screen.getByRole("button", { name: "Сохранить пометку" }));

		// Assert: null-количество ушло в команду — семантика формы создания:
		// незаданное количество закрывает весь остаток.
		expect(onEditAnnotation).toHaveBeenCalledWith({
			annotationId: 21,
			annotation: { symbol: "ETH-28MAR-1600-P", annotatedAt: expect.any(String), price: 100, quantity: null },
		});
	});
});

// Методы расчёта, зафиксированные в справочнике для других метрик: у суммы
// закрывающей записи метода нет, и чужая формула в подсказке не появляется.
const knownMethodTexts = Object.values(METRIC_TOOLTIPS)
	.map((tooltip) => tooltip.method)
	.filter((method): method is string => method !== undefined);

describe("таблица закрывающих записей: подсказки заголовков колонок", () => {
	// Сумма закрывающей записи поясняется в заголовке подсказкой метрики —
	// только назначением, без метода расчёта.
	// Traceability: openspec:ui/screens#requirement-column-header-tooltips
	it("даёт подсказку колонке «Сумма» с назначением без метода", async () => {
		// Arrange: таблица закрывающих записей под провайдером подсказок.
		const user = userEvent.setup();
		renderTable([partialAnnotation]);

		// Act: наведение на заголовок колонки суммы.
		await user.hover(screen.getByText("Сумма"));

		// Assert: попап содержит заголовок и назначение из справочника
		// и не содержит метода ни одной другой метрики.
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.closingEntriesAmount.title);
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.closingEntriesAmount.purpose);
		for (const method of knownMethodTexts) {
			expect(tooltip).not.toHaveTextContent(method);
		}
	});

	// Колонка времени записи — дата/время: подсказка метрики у дат нет.
	// Traceability: openspec:ui/screens#scenario-date-columns-skip-tooltip
	it("не даёт подсказки колонке времени", async () => {
		// Arrange: таблица закрывающих записей под провайдером подсказок.
		const user = userEvent.setup();
		renderTable([partialAnnotation]);

		// Act: наведение на заголовок колонки времени.
		await user.hover(screen.getByText("Время"));

		// Assert: подсказка не открылась.
		expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
	});

	// Колонка «Детали» объединяет количество и цену закрытия —
	// подсказка даёт по разделу на каждый показатель колонки.
	// Traceability: openspec:ui/screens#scenario-column-tooltip-sections-per-indicator
	it("даёт подсказку колонке «Детали» с разделами количества и цены", async () => {
		// Arrange: таблица закрывающих записей под провайдером подсказок.
		const user = userEvent.setup();
		renderTable([partialAnnotation]);

		// Act: наведение на заголовок колонки деталей.
		await user.hover(screen.getByText("Детали"));

		// Assert: попап содержит заголовок и оба раздела показателей.
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.closingEntryDetails.title);
		for (const section of METRIC_TOOLTIPS.closingEntryDetails.sections ?? []) {
			expect(tooltip).toHaveTextContent(section.label);
			expect(tooltip).toHaveTextContent(section.text);
		}
	});
});
