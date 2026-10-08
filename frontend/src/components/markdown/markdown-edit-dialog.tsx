import MDEditor, { commands, type ICommand } from "@uiw/react-md-editor";
import "@uiw/react-md-editor/markdown-editor.css";
import "./markdown-editor-theme.css";
import { useEffect, useState, type KeyboardEvent } from "react";
import {
	Bold,
	Code,
	Heading2,
	Italic,
	Link,
	List,
	ListOrdered,
	Table,
	X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import {
	Dialog,
	DialogClose,
	DialogContent,
	DialogDescription,
	DialogFooter,
	DialogHeader,
	DialogTitle,
} from "@/components/ui/dialog";
import { MarkdownViewer } from "./markdown-viewer";

export interface MarkdownEditDialogProps {
	/** Управление видимостью попапа со стороны раздела. */
	open: boolean;
	/** Заголовок окна: «Комментарий конструкции», «Комментарий позиции»… */
	title: string;
	/** Пояснение под заголовком, опционально. */
	description?: string;
	/** Исходный текст комментария до правки. */
	defaultText: string;
	/** Сохранение текста: Ctrl+Enter или кнопка «Сохранить». */
	onSave: (text: string) => void;
	/** Закрытие попапа: отмена, ESC, клик по оверлею. */
	onOpenChange: (open: boolean) => void;
}

// Модальный split-редактор журнального текста: тулбар и textarea слева,
// live-превью справа — тем же единым MD-рендером, что и чтение текста.
// WYSIWYG отсутствует: правится исходник Markdown. Ctrl+Enter — сохранить.
// Live-превью подменяется собственным компонентом, чтобы политика
// рендера §10 действовала и в окне редактирования.
// Визуальный слой собран по дизайн-стенду «MD-редактор (split)» (LeiDq):
// Header → Toolbar → Split → Footer, тематизация виджета @uiw — в
// markdown-editor-theme.css (design.md D5).
// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
// Traceability: change:reconcile-frontend-with-design/design#D5
//
// Тулбар дизайн-набора фрейма «Toolbar» (f1i0Gf): восемь команд — полужный,
// курсив, заголовок 2, два списка, ссылка, таблица, код. Исполняет их
// встроенная в @uiw логика обёртки выделения (префикс/суффикс); от виджета
// берутся только исполнение и сочетания клавиш, а подписи и иконки — наши:
// русские доступные имена и lucide-иконки 14×14, цвет которых наследует
// кнопка тулбара ($textSecondary в markdown-editor-theme.css).
// Traceability: change:reconcile-frontend-with-design/design#D5
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
const editorCommands: ICommand[] = [
	{
		...commands.bold,
		icon: <Bold size={14} />,
		buttonProps: { "aria-label": "Полужный", title: "Полужный" },
	},
	{
		...commands.italic,
		icon: <Italic size={14} />,
		buttonProps: { "aria-label": "Курсив", title: "Курсив" },
	},
	{
		...commands.title2,
		icon: <Heading2 size={14} />,
		buttonProps: { "aria-label": "Заголовок 2", title: "Заголовок 2" },
	},
	{
		...commands.unorderedListCommand,
		icon: <List size={14} />,
		buttonProps: { "aria-label": "Список", title: "Список" },
	},
	{
		...commands.orderedListCommand,
		icon: <ListOrdered size={14} />,
		buttonProps: {
			"aria-label": "Нумерованный список",
			title: "Нумерованный список",
		},
	},
	{
		...commands.link,
		icon: <Link size={14} />,
		buttonProps: { "aria-label": "Ссылка", title: "Ссылка" },
	},
	{
		...commands.table,
		icon: <Table size={14} />,
		buttonProps: { "aria-label": "Таблица", title: "Таблица" },
	},
	{
		...commands.code,
		icon: <Code size={14} />,
		buttonProps: { "aria-label": "Код", title: "Код" },
	},
];

// Компактный кегль footer-кнопок: инстансы Cancel/Save фрейма «Footer»
// (H5zNRM) — паддинги [7,14] и текст 12 поверх примитива ui/button
// (базовые 9/16 · 13).
const footerButtonClassName = "h-auto gap-1.5 px-3.5 py-[7px] text-xs";

export function MarkdownEditDialog({
	open,
	title,
	description,
	defaultText,
	onSave,
	onOpenChange,
}: MarkdownEditDialogProps) {
	const [draft, setDraft] = useState(defaultText);

	// Каждый сеанс начинается с исходного текста: отмена предыдущей правки
	// не должна протекать в новое открытие окна.
	useEffect(() => {
		if (open) {
			setDraft(defaultText);
		}
	}, [open, defaultText]);

	const save = () => {
		onSave(draft);
		onOpenChange(false);
	};

	// Ctrl+Enter работает из любой точки окна, включая textarea и кнопки.
	const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
		if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
			event.preventDefault();
			save();
		}
	};

	return (
		<Dialog open={open} onOpenChange={onOpenChange}>
			{/* Стенд «MD-редактор (split)» (LeiDq): 960×680, радиус 16, тень
			    0/16/48 поверх текстового primary. Попап собран колонкой
			    Header → Toolbar+Split → Footer без внутренних полей. */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D5 */}
			<DialogContent
				showCloseButton={false}
				className="flex h-[680px] max-h-[90vh] w-full max-w-[calc(100%-2rem)] flex-col gap-0 overflow-hidden rounded-2xl p-0 shadow-[0_16px_48px_#17171E26] sm:max-w-[960px]"
				onKeyDown={handleKeyDown}
			>
				{/* Header (nBfEb): [14,18], нижний divider, заголовок Inter
				    14/600, закрывашка 28×28 r8 справа. */}
				<DialogHeader className="flex-row items-center gap-2.5 border-b border-divider px-[18px] py-3.5">
					<DialogTitle className="text-sm font-semibold">
						{title}
					</DialogTitle>
					{description !== undefined && (
						<DialogDescription className="min-w-0 flex-1 truncate text-xs">
							{description}
						</DialogDescription>
					)}
					{description === undefined && (
						<span aria-hidden="true" className="flex-1" />
					)}
					<DialogClose className="inline-flex size-7 shrink-0 items-center justify-center rounded-lg text-text-secondary transition-colors hover:bg-surface-2 hover:text-foreground focus-visible:outline-2">
						<X size={14} />
						<span className="sr-only">Закрыть</span>
					</DialogClose>
				</DialogHeader>

				{/* Toolbar + Split: виджет @uiw; класс journal-md-editor — хук
				    переопределений markdown-editor-theme.css. Превью — единый
				    MD-рендер журнала (Inter 13/1.6 по veYUd/hrCte). */}
				<MDEditor
					value={draft}
					onChange={(value) => setDraft(value ?? "")}
					preview="live"
					height="100%"
					className="journal-md-editor min-h-0 flex-1"
					visibleDragbar={false}
					highlightEnable={false}
					commands={editorCommands}
					extraCommands={[]}
					data-color-mode="light"
					components={{
						preview: (source) => (
							<MarkdownViewer
								text={source}
								className="text-[13px] leading-[1.6]"
							/>
						),
					}}
				/>

				{/* Footer (H5zNRM): [12,18], верхний divider, подсказка
				    сочетания слева, кнопки-примитивы справа. */}
				<DialogFooter className="flex-row items-center gap-2.5 border-t border-divider px-[18px] py-3">
					<span className="text-[11.5px] text-text-muted">
						Ctrl+Enter — сохранить · live-превью
					</span>
					<span aria-hidden="true" className="flex-1" />
					<Button
						variant="secondary"
						className={footerButtonClassName}
						onClick={() => onOpenChange(false)}
					>
						Отмена
					</Button>
					<Button className={footerButtonClassName} onClick={save}>
						Сохранить
					</Button>
				</DialogFooter>
			</DialogContent>
		</Dialog>
	);
}
