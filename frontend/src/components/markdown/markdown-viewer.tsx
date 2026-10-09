import type { ComponentPropsWithoutRef } from "react";
import type { Components } from "react-markdown";
import ReactMarkdown from "react-markdown";
import remarkBreaks from "remark-breaks";
import remarkGfm from "remark-gfm";
import { cn } from "@/lib/utils";
import { sanitizeUrl } from "./sanitize-url";

export interface MarkdownViewerProps {
	/** Журнальный текст в Markdown: сообщения чатов, правила, комментарии. */
	text: string;
	/** Дополнительные классы контейнера под раскладку раздела. */
	className?: string;
}

// Базовые классы элементов журнального текста: типографика без плагина
// prose, чтобы стиль жил в тех же токенах темы, что и остальной интерфейс.
const headingClass = "mt-4 mb-2 font-semibold tracking-tight first:mt-0";
const cellClass = "border px-2 py-1 align-top";

const journalComponents: Components = {
	h1: (props) => <h1 className={cn(headingClass, "text-xl")} {...props} />,
	h2: (props) => <h2 className={cn(headingClass, "text-lg")} {...props} />,
	h3: (props) => <h3 className={cn(headingClass, "text-base")} {...props} />,
	h4: (props) => <h4 className={cn(headingClass, "text-sm")} {...props} />,
	h5: (props) => <h5 className={cn(headingClass, "text-sm")} {...props} />,
	h6: (props) => <h6 className={cn(headingClass, "text-sm")} {...props} />,
	p: (props) => <p className="mb-2 last:mb-0" {...props} />,
	a: (props) => (
		<a
			className="text-primary underline underline-offset-2"
			target="_blank"
			rel="noreferrer"
			{...props}
		/>
	),
	ul: (props) => (
		<ul className="mb-2 list-disc pl-5 last:mb-0" {...props} />
	),
	ol: (props) => (
		<ol className="mb-2 list-decimal pl-5 last:mb-0" {...props} />
	),
	li: (props) => <li className="mb-1 last:mb-0" {...props} />,
	blockquote: (props) => (
		<blockquote
			className="mb-2 border-l-2 border-border pl-3 text-muted-foreground last:mb-0"
			{...props}
		/>
	),
	table: (props: ComponentPropsWithoutRef<"table">) => (
		<div className="mb-2 overflow-x-auto last:mb-0">
			<table className="w-full border-collapse text-sm" {...props} />
		</div>
	),
	th: (props) => (
		<th
			className={cn(cellClass, "bg-muted text-left font-medium")}
			{...props}
		/>
	),
	td: (props) => <td className={cellClass} {...props} />,
	img: (props) => (
		<img className="max-w-full rounded-md" {...props} />
	),
	hr: (props) => <hr className="my-3 border-border" {...props} />,
	// Журнальный просмотр использует Inter 11; JetBrains Mono остаётся в редакторе.
	// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
	code: (props) => (
		<code
			className="rounded bg-muted px-1 py-0.5 font-sans text-[11px]"
			{...props}
		/>
	),
	pre: (props) => (
		<pre
			className="mb-2 overflow-x-auto rounded-md bg-muted p-3 text-[0.875em] last:mb-0"
			{...props}
		/>
	),
};

// Единый рендер всего журнального текста (чат, правила, комментарии):
// GFM с таблицами и автоссылками, мягкие переносы строк, сырой HTML
// экранируется, небезопасные схемы ссылок вырезаются. Разделы 5.x
// переиспользуют только этот компонент, чтобы политика Markdown
// жила в одной точке.
// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
export function MarkdownViewer({ text, className }: MarkdownViewerProps) {
	return (
		<div className={cn("text-sm leading-relaxed", className)}>
			<ReactMarkdown
				remarkPlugins={[remarkGfm, remarkBreaks]}
				components={journalComponents}
				urlTransform={sanitizeUrl}
			>
				{text}
			</ReactMarkdown>
		</div>
	);
}
