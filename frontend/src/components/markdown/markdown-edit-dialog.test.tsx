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
