import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ConstructionCardClosingEntry, ConstructionCardClosingWarning } from "@/lib/api/construction-card";
import { ClosingEntriesTable } from "./closing-entries-table";

// Таблица закрывающих записей: правка ручной пометки из строки — форма с
// ценой, количеством и временем; количество предзаполняется применённым
// значением записи, пустое поле возвращает пометку к закрытию всего остатка.
// Частичная пометка после правки цены или времени сохраняет частичность,
// не расширяясь молча до всего остатка.
// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
// Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity

function renderTable(entries: ConstructionCardClosingEntry[], onEditMark: (input: unknown) => void = () => undefined) {
	const warnings: ConstructionCardClosingWarning[] = [];
	render(
		<ClosingEntriesTable
			entries={entries}
			warnings={warnings}
			actionsPending={false}
			onEditMark={onEditMark as never}
			onDeleteMark={() => undefined}
		/>,
	);
}

/** Частичная ручная пометка: закрыла 0.3 из 0.5 путовой ноги. */
const partialMark: ConstructionCardClosingEntry = {
	closedAt: "2026-10-15T13:30:00+00:00",
	kind: "manual-mark",
	symbol: "ETH-28MAR-1600-P",
	quantity: -0.3,
	price: 100,
	amountUsdt: -30,
	manualMarkId: 21,
};

describe("таблица закрывающих записей: правка ручной пометки", () => {
	it("предзаполняет количество значением записи и сохраняет частичность при правке", async () => {
		// Arrange: раскрытая форма правки частичной пометки; замер команды правки.
		const user = userEvent.setup();
		const onEditMark = vi.fn();
		renderTable([partialMark], onEditMark);
		await user.click(screen.getByRole("button", { name: "править" }));

		// Assert: поле количества предзаполнено применённым количеством записи.
		const quantityInput = screen.getByLabelText("количество пометки") as HTMLInputElement;
		expect(quantityInput.value).toBe("0.3");

		// Act: правим только цену — количество не трогаем.
		const priceInput = screen.getByPlaceholderText("пусто — последняя марка");
		await user.clear(priceInput);
		await user.type(priceInput, "95");
		await user.click(screen.getByRole("button", { name: "Сохранить пометку" }));

		// Assert: правка сохранила заданное количество — частичная пометка не
		// превратилась в закрытие всего остатка.
		expect(onEditMark).toHaveBeenCalledWith({
			markId: 21,
			mark: { symbol: "ETH-28MAR-1600-P", markedAt: expect.any(String), price: 95, quantity: 0.3 },
		});
	});

	it("пустое количество возвращает пометку к закрытию всего остатка", async () => {
		// Arrange: раскрытая форма правки частичной пометки.
		const user = userEvent.setup();
		const onEditMark = vi.fn();
		renderTable([partialMark], onEditMark);
		await user.click(screen.getByRole("button", { name: "править" }));

		// Act: владелец очищает количество — остаток закрывается целиком.
		await user.clear(screen.getByLabelText("количество пометки"));
		await user.click(screen.getByRole("button", { name: "Сохранить пометку" }));

		// Assert: null-количество ушло в команду — семантика формы создания:
		// незаданное количество закрывает весь остаток.
		expect(onEditMark).toHaveBeenCalledWith({
			markId: 21,
			mark: { symbol: "ETH-28MAR-1600-P", markedAt: expect.any(String), price: 100, quantity: null },
		});
	});
});
