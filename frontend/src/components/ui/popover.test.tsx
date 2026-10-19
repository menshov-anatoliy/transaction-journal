import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { Popover, PopoverContent, PopoverTrigger } from "./popover";

// Проверяется примитив подсказки чтения индикатора финрезультата: открытие
// триггером «?» с сообщением состояния для ассистивных технологий, закрытие
// по Esc, клику вне и повторному клику, возврат фокуса на триггер, а также
// токены паттерна «Попап правила/Краткий» макета design.pen.
function renderPopover() {
	return render(
		<Popover>
			<PopoverTrigger>?</PopoverTrigger>
			<PopoverContent>Подсказка чтения индикатора</PopoverContent>
		</Popover>,
	);
}

describe("поповер: механика открытия и закрытия подсказки", () => {
	it("открывается кликом по триггеру и сообщает состояние aria-expanded", async () => {
		// Триггер «?» открывает попап и сообщает открытое состояние
		// для ассистивных технологий.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-opens

		// Arrange: примитив с триггером и контентом в портале.
		const user = userEvent.setup();
		renderPopover();
		const trigger = screen.getByRole("button", { name: "?" });
		expect(trigger).toHaveAttribute("aria-expanded", "false");

		// Act: клик по триггеру «?».
		await user.click(trigger);

		// Assert: контент появился в портале, триггер сообщает открытое состояние.
		expect(screen.getByRole("dialog")).toHaveTextContent(
			"Подсказка чтения индикатора",
		);
		expect(trigger).toHaveAttribute("aria-expanded", "true");

		// Act: закрытие кликом вне попапа.
		await user.click(document.body);

		// Assert: триггер сообщает закрытое состояние.
		expect(trigger).toHaveAttribute("aria-expanded", "false");
	});

	it("закрывается по Escape", async () => {
		// Закрытие по Esc — один из обязательных способов убрать подсказку.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-closes

		// Arrange: попап открыт.
		const user = userEvent.setup();
		renderPopover();
		await user.click(screen.getByRole("button", { name: "?" }));
		expect(screen.getByRole("dialog")).toBeInTheDocument();

		// Act: нажатие Esc.
		await user.keyboard("{Escape}");

		// Assert: контент убран из портала.
		expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
	});

	it("закрывается кликом вне попапа", async () => {
		// Закрытие кликом вне попапа — обязательный способ убрать подсказку.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-closes

		// Arrange: попап открыт.
		const user = userEvent.setup();
		renderPopover();
		await user.click(screen.getByRole("button", { name: "?" }));
		expect(screen.getByRole("dialog")).toBeInTheDocument();

		// Act: клик по области вне попапа.
		await user.click(document.body);

		// Assert: контент убран из портала.
		expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
	});

	it("закрывается повторным кликом по тому же триггеру", async () => {
		// Повторный клик по триггеру «?» закрывает открытую подсказку.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-closes

		// Arrange: попап открыт.
		const user = userEvent.setup();
		renderPopover();
		const trigger = screen.getByRole("button", { name: "?" });
		await user.click(trigger);
		expect(screen.getByRole("dialog")).toBeInTheDocument();

		// Act: повторный клик по тому же триггеру.
		await user.click(trigger);

		// Assert: контент убран из портала.
		expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
	});

	it("возвращает фокус на триггер после закрытия", async () => {
		// После закрытия фокус возвращается на триггер — клавиатурный
		// пользователь остаётся на «?».
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-closes

		// Arrange: попап открыт.
		const user = userEvent.setup();
		renderPopover();
		const trigger = screen.getByRole("button", { name: "?" });
		await user.click(trigger);

		// Act: закрытие по Esc.
		await user.keyboard("{Escape}");

		// Assert: фокус вернулся на триггер.
		expect(trigger).toHaveFocus();
	});
});

describe("поповер: тема дизайн-примитива", () => {
	it("строит контент на токенах паттерна «Попап правила/Краткий»", async () => {
		// Токены попапа разрешаются в паттерн макета: фон $surface,
		// граница $border, радиус 10.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/design-system/spec#scenario-design-card-reading-tooltip-tokens

		// Arrange: попап открыт, контент в портале.
		const user = userEvent.setup();
		renderPopover();
		await user.click(screen.getByRole("button", { name: "?" }));

		// Assert: фон $surface (--popover), граница $border, радиус 10,
		// паддинг 12 (p-3) паттерна макета; уровень z-50 поверх карточки.
		const content = screen.getByRole("dialog");
		expect(content).toHaveClass("bg-popover", "text-popover-foreground");
		expect(content).toHaveClass("border");
		expect(content).toHaveClass("rounded-[10px]");
		expect(content).toHaveClass("p-3");
		expect(content).toHaveClass("shadow-lg", "z-50", "outline-none");
	});
});
