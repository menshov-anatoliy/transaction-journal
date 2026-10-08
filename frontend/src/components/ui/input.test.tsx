import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Input } from "./input";

// Проверяется тема поля ввода по дизайн-нодам «Фильтры» Body #5 «Входящие»
// (VYItk/euvt0/AYWJR) макета design.pen: белая поверхность, кайма $border,
// радиус 8, паддинги [7,10], Inter 12/normal. Пиксельная сверка —
// скриншот-парой в .wf-research/design-audit/screenshots/07-pages/05-inbox/.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("поле ввода: тема дизайн-примитива", () => {
	it("несёт геометрию и типографику дизайн-системы", () => {
		// Act: базовый рендер текстового поля.
		render(<Input placeholder="Поиск" />);

		// Assert: радиус 8 (шаг --radius-sm), паддинги [7,10], Inter 12.
		const field = screen.getByPlaceholderText("Поиск");
		expect(field).toHaveAttribute("data-slot", "input");
		const classes = field.className;
		expect(classes).toContain("rounded-sm");
		expect(classes).toContain("bg-card");
		expect(classes).toContain("px-2.5");
		expect(classes).toContain("py-[7px]");
		expect(classes).toContain("text-xs");
	});

	it("остаётся рабочим полем: ввод и событие onChange работают", async () => {
		// Arrange: контролируемое поле с обработчиком.
		const onChange = vi.fn();
		const user = userEvent.setup();
		render(<Input value="" onChange={(event) => onChange(event.currentTarget.value)} />);

		// Act: ввод символа.
		await user.type(screen.getByRole("textbox"), "A");

		// Assert: обработчик получил введённое значение.
		expect(onChange).toHaveBeenLastCalledWith("A");
	});
});
