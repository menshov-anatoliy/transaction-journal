import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Checkbox } from "./checkbox";

// Проверяется дизайн-примитив чекбокса по мастер-нодам «CB» таблицы
// «Непривязанные сделки» Body #5 (lpeaS — checked, mtES4 — unchecked)
// макета design.pen: квадрат 16×16, радиус 4, кайма $border на $surface,
// отмеченное состояние — заливка $accent с белой галкой 10×10. Нативный
// input сохраняет семантику role=checkbox.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("чекбокс: тема дизайн-примитива", () => {
	it("несёт геометрию мастера: квадрат 16, радиус 4, кайма на поверхности", () => {
		// Act: базовый рендер неотмеченного чекбокса.
		render(<Checkbox />);

		// Assert: обёртка 16×16, input с радиусом 4 и токенами поверхности.
		const box = screen.getByRole("checkbox");
		expect(box).toHaveAttribute("type", "checkbox");
		expect(box.closest("[data-slot='checkbox']")?.className).toContain("size-4");
		expect(box.className).toContain("rounded-[4px]");
		expect(box.className).toContain("border-border");
		expect(box.className).toContain("bg-card");
	});

	it("показывает галку поверх заливки accent в отмеченном состоянии", () => {
		// Act: рендер отмеченного чекбокса.
		render(<Checkbox checked readOnly />);

		// Assert: заливка $accent и кайма accent включаются состоянием checked,
		// галка lucide check 10×10 появляется через peer-checked.
		const box = screen.getByRole("checkbox");
		expect(box.className).toContain("checked:bg-primary");
		expect(box.className).toContain("checked:border-primary");
		const check = box.closest("[data-slot='checkbox']")?.querySelector("svg");
		// У SVG-элемента className — SVGAnimatedString, класс читается атрибутом.
		expect(check?.getAttribute("class")).toContain("peer-checked:opacity-100");
		expect(check?.getAttribute("class")).toContain("size-2.5");
	});

	it("остаётся нативным контролом: клик переключает состояние", async () => {
		// Arrange: неконтролируемый чекбокс с обработчиком.
		const onChange = vi.fn();
		const user = userEvent.setup();
		render(<Checkbox onChange={(event) => onChange(event.currentTarget.checked)} />);

		// Act: клик по чекбоксу.
		await user.click(screen.getByRole("checkbox"));

		// Assert: обработчик получил состояние checked.
		expect(onChange).toHaveBeenCalledWith(true);
		expect(screen.getByRole("checkbox")).toBeChecked();
	});
});
