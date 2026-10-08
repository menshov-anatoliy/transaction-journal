import type { SourceReference, SourceTrace, ToolCallTrace } from "@/chat/types";
import { formatMoment } from "@/lib/format/display-time";

export interface SourceTraceViewProps {
	/** След источников ответа ИИ-помощника: инструменты и ссылки с as-of. */
	trace: SourceTrace;
	/** Наведение на ссылку карточки правила в следе источников. */
	onRuleHover?: (ruleId: string) => void;
	/** Уход курсора со ссылки карточки правила. */
	onRuleLeave?: () => void;
	/** Открытие карточки правила в правой панели или новом окне. */
	onRuleOpen?: (ruleId: string) => void;
}

const referenceKindLabels: Record<SourceReference["kind"], string> = {
	"rule-card": "Карточка правила",
	journal: "Данные журнала",
};

/**
 * Рендер-примитив следа источников: список вызванных инструментов и ссылок
 * на карточки правил и данные журнала с их отметками as-of. Живёт под
 * MD-текстом ответа и не заменяет его: текст рендерит MarkdownViewer,
 * след только поясняет, на чём ответ построен.
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
export function SourceTraceView({ trace, onRuleHover, onRuleLeave, onRuleOpen }: SourceTraceViewProps) {
	const hasTools = trace.toolCalls.length > 0;
	const hasReferences = trace.references.length > 0;

	if (hasTools === false && hasReferences === false)
		return null;

	return (
		<aside
			aria-label="След источников ответа"
			className="mt-2 flex flex-col gap-1.5 rounded-lg border bg-muted/40 px-3 py-2 text-xs text-muted-foreground"
		>
			{hasTools && (
				<ul className="flex flex-wrap gap-1.5" aria-label="Использованные инструменты">
					{trace.toolCalls.map((call, index) => (
						<li key={toolCallKey(call, index)} className="rounded-md bg-card px-2 py-1">
							<span className="font-mono">{call.tool}</span>
							<span className="mx-1 text-border">·</span>
							<span>{call.argument}</span>
							{call.asOf && (
								<span className="ml-1">as-of {formatMoment(call.asOf)}</span>
							)}
							{/* Рыночный след из кэша: биржа была недоступна, данные устарели. */}
							{call.degraded === true && (
								<span className="ml-1 rounded bg-amber-100 px-1 text-amber-800">
									кэш
								</span>
							)}
						</li>
					))}
				</ul>
			)}
			{hasReferences && (
				<ul className="flex flex-col gap-1" aria-label="Ссылки на источники">
					{trace.references.map((reference) => (
						<li key={`${reference.kind}:${reference.id}`} className="flex flex-wrap gap-1">
							{reference.kind === "rule-card" ? (
								<button
									type="button"
									className="cursor-pointer rounded-md bg-card px-2 py-1 text-left underline-offset-2 hover:underline"
									onMouseEnter={() => onRuleHover?.(reference.id)}
									onMouseLeave={() => onRuleLeave?.()}
									onClick={() => onRuleOpen?.(reference.id)}
								>
									{referenceKindLabels[reference.kind]}: {reference.title}
								</button>
							) : (
								<span className="rounded-md bg-card px-2 py-1">
									{referenceKindLabels[reference.kind]}: {reference.title}
								</span>
							)}
							<span>as-of {formatMoment(reference.asOf)}</span>
						</li>
					))}
				</ul>
			)}
		</aside>
	);
}

function toolCallKey(call: ToolCallTrace, index: number): string {
	// Один инструмент мог зваться повторно — ключ различает вызовы индексом.
	return `${call.tool}:${call.argument}:${index}`;
}
