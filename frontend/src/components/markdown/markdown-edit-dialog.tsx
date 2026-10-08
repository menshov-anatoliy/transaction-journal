import MDEditor from "@uiw/react-md-editor";
import "@uiw/react-md-editor/markdown-editor.css";
import { useEffect, useState, type KeyboardEvent } from "react";
import { Button } from "@/components/ui/button";
import {
	Dialog,
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
// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
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
			<DialogContent
				className="max-h-[90vh] max-w-3xl overflow-y-auto sm:max-w-3xl"
				onKeyDown={handleKeyDown}
			>
				<DialogHeader>
					<DialogTitle>{title}</DialogTitle>
					{description !== undefined && (
						<DialogDescription>{description}</DialogDescription>
					)}
				</DialogHeader>

				<MDEditor
					value={draft}
					onChange={(value) => setDraft(value ?? "")}
					preview="live"
					height={360}
					visibleDragbar={false}
					highlightEnable={false}
					extraCommands={[]}
					data-color-mode="light"
					components={{
						preview: (source) => <MarkdownViewer text={source} />,
					}}
				/>

				<DialogFooter>
					<Button
						variant="outline"
						onClick={() => onOpenChange(false)}
					>
						Отмена
					</Button>
					<Button onClick={save}>
						Сохранить
						{/* Подсказка сочетания не входит в доступное имя кнопки. */}
						<span
							aria-hidden="true"
							className="text-xs text-primary-foreground/70"
						>
							Ctrl+Enter
						</span>
					</Button>
				</DialogFooter>
			</DialogContent>
		</Dialog>
	);
}
