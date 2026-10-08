import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MarkdownViewer } from "./markdown-viewer";

// Проверяется политика Markdown-рендера журнального текста: GFM (таблицы,
// автоссылки), мягкие переносы строк, экранирование сырого HTML и вырезание
// небезопасных схем ссылок.
// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

describe("MarkdownViewer", () => {
	it("рендерит GFM-таблицу", () => {
		// Arrange: таблица из журнального комментария.
		const text = [
			"| Нога | Количество |",
			"| --- | --- |",
			"| C-40000 | 2 |",
		].join("\n");

		// Act: единый рендер журнального текста.
		const { container } = render(<MarkdownViewer text={text} />);

		// Assert: GFM-таблица собирается в табличную разметку.
		const table = container.querySelector("table");
		expect(table).not.toBeNull();
		expect(table?.textContent).toContain("Нога");
		expect(table?.textContent).toContain("C-40000");
	});

	it("превращает голый адрес в автоссылку GFM", () => {
		// Act: адрес без Markdown-разметки.
		render(<MarkdownViewer text={"См. https://bybit.com/docs"} />);

		// Assert: GFM-автоссылка ведёт на исходный адрес.
		const link = screen.getByRole("link", { name: /bybit\.com/ });
		expect(link.getAttribute("href")).toBe("https://bybit.com/docs");
	});

	it("переносит строку по одиночному переводу", () => {
		// Act: две строки без пустой строки между ними.
		const { container } = render(
			<MarkdownViewer text={"первая строка\nвторая строка"} />,
		);

		// Assert: мягкий перенос становится <br>.
		expect(container.querySelector("br")).not.toBeNull();
		expect(container.textContent).toContain("первая строка");
		expect(container.textContent).toContain("вторая строка");
	});

	it("экранирует сырой HTML вместо исполнения", () => {
		// Arrange: инъекция скрипта в журнальном тексте.
		const text = 'текст <script>alert("x")</script> конец';

		// Act: рендер без WYSIWYG-обработки HTML.
		const { container } = render(<MarkdownViewer text={text} />);

		// Assert: тег остаётся экранированным текстом, элементом не становится.
		expect(container.querySelector("script")).toBeNull();
		expect(container.textContent).toContain('<script>alert("x")</script>');
	});

	it("вырезает javascript-схему из ссылок", () => {
		// Act: ссылка с атакующей схемой.
		const { container } = render(
			<MarkdownViewer text={"[вредная](javascript:alert(1))"} />,
		);

		// Assert: схема вырезана, href не содержит javascript.
		const link = container.querySelector("a");
		expect(link).not.toBeNull();
		expect(link?.getAttribute("href")).not.toContain("javascript");
	});

	it("сохраняет безопасную ссылку", () => {
		// Act: обычная Markdown-ссылка.
		render(<MarkdownViewer text={"[сайт](https://bybit.com)"} />);

		// Assert: безопасная схема проходит политику без изменений.
		const link = screen.getByRole("link", { name: "сайт" });
		expect(link.getAttribute("href")).toBe("https://bybit.com");
	});
});
