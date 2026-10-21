import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import {
	Tooltip,
	TooltipContent,
	TooltipProvider,
	TooltipTrigger,
} from "./tooltip";

// Проверяется примитив «Подсказка метрики»: открытие по наведению курсора
// и по фокусу с клавиатуры, закрытие по уходу фокуса, клику/тапу вне и Esc,
// доступное описание (роль tooltip и связь aria-describedby), а также токены
// паттерна «Подсказка метрики» макета design.pen: триггер — значение
// с пунктирным подчёркиванием без иконок, попап на токенах макета.
function renderTooltip() {
	return render(
		<TooltipProvider delayDuration={0}>
			<Tooltip>
				<TooltipTrigger>1250.50</TooltipTrigger>
				<TooltipContent>Итог конструкции: результат позиций с корректировками</TooltipContent>
			</Tooltip>
			<input aria-label="поле после подсказки" readOnly />
		</TooltipProvider>,
	);
}

describe("тултип: механика открытия и закрытия подсказки", () => {
	it("открывается при наведении курсора на значение метрики", async () => {
		// Наведение курсора на значение с пунктирным подчёркиванием
		// открывает подсказку метрики.
		// Traceability: openspec:ui/screens#scenario-metric-tooltip-on-hover-focus

		// Arrange: примитив с нулевой задержкой открытия и триггером-значением.
		const user = userEvent.setup();
		renderTooltip();
		const trigger = screen.getByRole("button", { name: "1250.50" });

		// Act: наведение курсора на значение метрики.
		await user.hover(trigger);
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());

		// Assert: контент появился в портале с текстом подсказки.
		expect(screen.getByRole("tooltip")).toHaveTextContent(
			"Итог конструкции: результат позиций с корректировками",
		);
	});

	it("открывается при переводе фокуса на значение с клавиатуры", async () => {
		// Получение фокуса значением с клавиатуры открывает ту же подсказку —
		// метрика доступна без мыши.
		// Traceability: openspec:ui/screens#scenario-metric-tooltip-on-hover-focus

		// Arrange: примитив в документе, фокус ещё не установлен.
		const user = userEvent.setup();
		renderTooltip();

		// Act: перевод фокуса клавишей Tab на триггер-значение.
		await user.tab();

		// Assert: подсказка появилась, фокус остаётся на триггере.
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		expect(screen.getByRole("button", { name: "1250.50" })).toHaveFocus();
	});

	it("закрывается при уходе фокуса с триггера", async () => {
		// Уход фокуса с значения закрывает подсказку — клавиатурный
		// способ убрать её.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics

		// Arrange: подсказка открыта фокусом с клавиатуры.
		const user = userEvent.setup();
		renderTooltip();
		await user.tab();
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());

		// Act: перевод фокуса дальше — уход с триггера (blur).
		await user.tab();

		// Assert: контент убран из портала.
		await waitFor(() =>
			expect(screen.queryByRole("tooltip")).not.toBeInTheDocument(),
		);
	});

	it("закрывается по клику вне триггера", async () => {
		// Тап или клик вне триггера закрывает открытую подсказку.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics

		// Arrange: подсказка открыта наведением.
		const user = userEvent.setup();
		renderTooltip();
		await user.hover(screen.getByRole("button", { name: "1250.50" }));
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());

		// Act: клик (pointerdown) по области вне триггера.
		await user.click(document.body);

		// Assert: контент убран из портала.
		expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
	});

	it("закрывается по Escape", async () => {
		// Esc закрывает подсказку — клавиатурный способ убрать её.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics

		// Arrange: подсказка открыта фокусом с клавиатуры.
		const user = userEvent.setup();
		renderTooltip();
		await user.tab();
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());

		// Act: нажатие Esc.
		await user.keyboard("{Escape}");

		// Assert: контент убран из портала.
		expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
	});

	it("связывает контент с триггером через aria-describedby и роль tooltip", async () => {
		// Контент подсказки доступен ассистивным технологиям: роль tooltip
		// и связь aria-describedby с триггером даёт Radix из коробки.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics

		// Arrange: подсказка открыта наведением.
		const user = userEvent.setup();
		renderTooltip();
		await user.hover(screen.getByRole("button", { name: "1250.50" }));
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());

		// Assert: атрибут aria-describedby триггера указывает на id контента.
		const content = screen.getByRole("tooltip");
		const trigger = screen.getByRole("button", { name: "1250.50" });
		expect(trigger).toHaveAttribute("aria-describedby", content.id);
	});
});

describe("тултип: тема дизайн-примитива", () => {
	it("показывает триггер значением с пунктирным подчёркиванием без иконок", () => {
		// Свёрнутое состояние макета: значение с пунктирным подчёркиванием
		// слабоконтрастным серым токеном, иконок-триггеров нет.
		// Traceability: openspec:ui/design-system#scenario-design-tooltip-both-states

		// Arrange: примитив в документе, подсказка ещё не открыта.
		renderTooltip();
		const trigger = screen.getByRole("button", { name: "1250.50" });

		// Assert: пунктирное подчёркивание токеном textMuted, без иконок внутри.
		expect(trigger).toHaveClass(
			"underline",
			"decoration-dashed",
			"decoration-text-muted",
		);
		expect(trigger.querySelector("svg")).toBeNull();
	});

	it("строит попап на токенах паттерна «Подсказка метрики»", async () => {
		// Токены попапа разрешаются в паттерн макета: фон $surface, граница
		// $border, радиус 10, паддинг 12, тень, мелкий текст, слой z-50.
		// Traceability: openspec:ui/design-system#scenario-design-tooltip-token-alignment

		// Arrange: подсказка открыта наведением, контент в портале.
		const user = userEvent.setup();
		renderTooltip();
		await user.hover(screen.getByRole("button", { name: "1250.50" }));
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());

		// Assert: классы токенов паттерна макета на контенте.
		const content = screen.getByRole("tooltip");
		expect(content).toHaveClass("bg-popover", "text-popover-foreground");
		expect(content).toHaveClass("border");
		expect(content).toHaveClass("rounded-[10px]");
		expect(content).toHaveClass("p-3");
		expect(content).toHaveClass("shadow-lg", "z-50");
		expect(content).toHaveClass("text-[11px]", "leading-snug", "max-w-72");
	});
});
