import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import {
	MarkdownEditDialog,
	type MarkdownEditDialogProps,
} from "./markdown-edit-dialog";

// Проверяется модальный split-редактор журнального текста: тулбар и textarea
// слева, live-превью справа (тем же единым MD-рендером), Ctrl+Enter —
// сохранить, WYSIWYG отсутствует.
// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

function renderDialog(props: Partial<MarkdownEditDialogProps> = {}) {
	const onSave = vi.fn<(text: string) => void>();
	const onOpenChange = vi.fn<(open: boolean) => void>();
	render(
		<MarkdownEditDialog
			open
			title="Комментарий конструкции"
			defaultText="исходный текст"
			onSave={onSave}
			onOpenChange={onOpenChange}
			{...props}
		/>,
	);
	return { onSave, onOpenChange };
}

describe("MarkdownEditDialog", () => {
	it("показывает тулбар и исходный текст в split-редакторе", () => {
		// Act: открытие модального редактора с текстом комментария.
		renderDialog();

		// Assert: тулбар команд, textarea с исходником и превью с текстом.
		// Попап рендерится порталом в body, поэтому поиск идёт по документу.
		expect(
			document.body.querySelector(".w-md-editor-toolbar"),
		).not.toBeNull();
		expect(screen.getByRole("textbox")).toHaveValue("исходный текст");
		// В превью текст отрендерен: у MDEditor есть собственное зеркало
		// подсветки, поэтому проверяем именно панель превью.
		const preview = document.body.querySelector(".w-md-editor-preview");
		expect(preview?.textContent).toContain("исходный текст");
	});

	it("обновляет live-превью по мере ввода", async () => {
		// Arrange: открытый редактор и пользователь за клавиатурой.
		const user = userEvent.setup();
		renderDialog();
		const editor = screen.getByRole("textbox");

		// Act: ввод заголовка в textarea слева.
		await user.type(editor, "\n# Заголовок правки");

		// Assert: справа заголовок отрендерен Markdown-вьюером.
		expect(
			screen.getByRole("heading", { name: "Заголовок правки" }),
		).toBeInTheDocument();
	});

	it("сохраняет текст по Ctrl+Enter", async () => {
		// Arrange: пользователь редактирует комментарий.
		const user = userEvent.setup();
		const { onSave, onOpenChange } = renderDialog();
		const editor = screen.getByRole("textbox");

		// Act: дописывание и сочетание сохранения.
		await user.type(editor, " и правка");
		await user.keyboard("{Control>}{Enter}{/Control}");

		// Assert: сохранён полный текст, диалог закрылся.
		expect(onSave).toHaveBeenCalledWith("исходный текст и правка");
		expect(onOpenChange).toHaveBeenCalledWith(false);
	});

	it("сохраняет текст кнопкой «Сохранить»", async () => {
		// Arrange: пользователь редактирует комментарий.
		const user = userEvent.setup();
		const { onSave, onOpenChange } = renderDialog();
		await user.type(screen.getByRole("textbox"), " и правка");

		// Act: сохранение кнопкой.
		await user.click(screen.getByRole("button", { name: "Сохранить" }));

		// Assert: текст передан родителю, диалог закрылся.
		expect(onSave).toHaveBeenCalledWith("исходный текст и правка");
		expect(onOpenChange).toHaveBeenCalledWith(false);
	});

	it("отменяет правку без сохранения", async () => {
		// Arrange: пользователь начал правку, но передумал.
		const user = userEvent.setup();
		const { onSave, onOpenChange } = renderDialog();
		await user.type(screen.getByRole("textbox"), " и правка");

		// Act: отмена.
		await user.click(screen.getByRole("button", { name: "Отмена" }));

		// Assert: текст не сохранён, диалог закрылся.
		expect(onSave).not.toHaveBeenCalled();
		expect(onOpenChange).toHaveBeenCalledWith(false);
	});

	it("сбрасывает черновик при повторном открытии", async () => {
		// Arrange: первый сеанс с правкой и отменой.
		const user = userEvent.setup();
		const props = {
			title: "Комментарий",
			defaultText: "исходный текст",
			onSave: vi.fn(),
			onOpenChange: vi.fn(),
		};
		const { rerender } = render(<MarkdownEditDialog open {...props} />);
		await user.type(screen.getByRole("textbox"), " временная");
		await user.click(screen.getByRole("button", { name: "Отмена" }));

		// Act: закрытие и повторное открытие того же комментария.
		rerender(<MarkdownEditDialog open={false} {...props} />);
		rerender(<MarkdownEditDialog open {...props} />);

		// Assert: отмена не протекла в новый сеанс редактирования.
		expect(screen.getByRole("textbox")).toHaveValue("исходный текст");
	});
});

// Тематизация @uiw/react-md-editor по дизайн-фреймам «Стенд/MD-редактор
// (split)» (Header nBfEb / Toolbar f1i0Gf / Split veYUd / Footer H5zNRM):
// проверяются стабильные в jsdom швы — класс-хук CSS-темы на обёртке
// редактора, состав тулбара и рендер footer-кнопок примитивами ui/button.
// Визуальная часть (радиусы, шрифты, цвета) принимается скриншот-сверкой.
// Traceability: change:reconcile-frontend-with-design/design#D5
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
describe("MarkdownEditDialog: тематизация дизайн-системы", () => {
	it("оборачивает виджет редактора классом-хуком темы", () => {
		// Act: открытие модального редактора.
		renderDialog();

		// Assert: корень виджета несёт класс journal-md-editor, на который
		// опираются переопределения .w-md-editor-* в markdown-editor-theme.css.
		expect(
			document.body.querySelector(".journal-md-editor.w-md-editor"),
		).not.toBeNull();
	});

	it("показывает тулбар дизайн-набора: 8 команд с иконками", () => {
		// Act: открытие модального редактора с тулбаром.
		renderDialog();

		// Assert: ровно 8 кнопок тулбара — дизайн-набор фрейма Toolbar
		// (f1i0Gf); стандартные команды @uiw (зачёркивание, цитата,
		// картинка, справка…) не рендерятся.
		const toolbarButtons =
			document.body.querySelectorAll(".w-md-editor-toolbar li > button");
		expect(toolbarButtons).toHaveLength(8);

		// Каждая команда имеет русскую доступную подпись и иконку lucide
		// (svg), цвет которой наследуется стилями кнопки.
		const labels = [
			"Полужный",
			"Курсив",
			"Заголовок 2",
			"Список",
			"Нумерованный список",
			"Ссылка",
			"Таблица",
			"Код",
		];
		for (const label of labels) {
			const button = document.body.querySelector(
				`.w-md-editor-toolbar button[aria-label="${label}"]`,
			);
			expect(button, `нет кнопки «${label}»`).not.toBeNull();
			expect(button?.querySelector("svg")).not.toBeNull();
		}
	});

	it("footer: подсказка сочетания и компактные кнопки-примитивы", () => {
		// Act: открытие модального редактора.
		renderDialog();

		// Assert: слева в футере подсказка из фрейма Footer (H5zNRM).
		expect(
			screen.getByText("Ctrl+Enter — сохранить · live-превью"),
		).toBeInTheDocument();

		// Кнопки футера — примитивы ui/button в компактном кегле 12
		// (паддинги [7,14] инстансов Cancel/Save фрейма H5zNRM).
		const cancel = screen.getByRole("button", { name: "Отмена" });
		const save = screen.getByRole("button", { name: "Сохранить" });
		for (const button of [cancel, save]) {
			expect(button).toHaveAttribute("data-slot", "button");
			expect(button.className).toContain("text-xs");
		}
		// Отмена — Secondary-инстанс (белая поверхность с бордером).
		expect(cancel.className).toContain("bg-card");
	});
});
