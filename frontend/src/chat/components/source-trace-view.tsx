import { BookOpen, Cog, Globe, Notebook, type LucideIcon } from "lucide-react";
import type { SourceReference, SourceTrace, ToolCallTrace } from "@/chat/types";
import { SourceChip } from "@/components/design";
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
	"rule-card": "Правило",
	journal: "Журнал",
};

const referenceKindIcons: Record<SourceReference["kind"], LucideIcon> = {
	"rule-card": BookOpen,
	journal: Notebook,
};

/**
 * Рендер-примитив следа источников: список вызванных инструментов и ссылки
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
			/*
				Контейнер следа — мастер bUrOy «След источников» (инстанс Tr1
				Body #4): мягкая зелёная панель $accentSofter #EFF9F4 со stroke
				$accentSoft #E2F4EB, радиус 10, паддинги [10,12], зазор 5;
				нейтральная плашка bg-muted/40 (аудит §3.5:5) убрана.
			*/
			// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
			className="mt-2 flex flex-col gap-[5px] rounded-[10px] border-accent-soft bg-accent-softer px-3 py-2.5"
		>
			{/* Заголовок панели — нода nFpGY «TraceTitle» мастера bUrOy:
			    капс 10/normal, letter-spacing 0.5, $textMuted. */}
			<p className="text-[10px] font-normal tracking-[0.5px] text-text-muted uppercase">
				Источники ответа
			</p>
			{hasTools && (
				<ul className="flex flex-wrap gap-[7px]" aria-label="Использованные инструменты">
					{trace.toolCalls.map((call, index) => (
						<li key={toolCallKey(call, index)} className="min-w-0 max-w-full">
							{/*
								Вызванные инструменты — пилюли-источники (примитив
								«Чип/Источник» fYadZ, pill 999): у мастера bUrOy
								статичного списка инструментов нет — в дизайне
								инструменты живут состоянием Tool-статуса, поэтому
								завершённые вызовы читаются чипом источника.
							*/}
							{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
							{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
							{/* Длинные аргументы и as-of переносятся, чтобы кэш-метка не скрывалась за краем ленты. */}
							{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
							<SourceChip
								icon={toolIcon(call.tool)}
								iconClassName="text-accent-strong"
								className="max-w-full whitespace-normal"
							>
								<span className="min-w-0 [overflow-wrap:anywhere]">
									{call.tool} · {call.argument}
									{call.asOf && (
										<span className="text-text-muted">, as-of {formatMoment(call.asOf)}</span>
									)}
									{/* Рыночный след из кэша: биржа была недоступна, данные устарели. */}
									{/* Предупреждение использует семантическую пару risk/riskSoft. */}
									{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
									{call.degraded === true && (
										<span className="ml-1 rounded bg-risk-soft px-1 text-risk">
											кэш
										</span>
									)}
								</span>
							</SourceChip>
						</li>
					))}
				</ul>
			)}
			{hasReferences && (
				<ul className="flex flex-col gap-[5px]" aria-label="Ссылки на источники">
					{trace.references.map((reference) => {
						const Icon = referenceKindIcons[reference.kind];
						// Строка ссылки мастера bUrOy: «Правило №14 · …» (нода
						// H4mjmQ, book-open) и «Журнал · сделки …, as-of …»
						// (нода RD9JT, journal) — иконка 12 и текст 12 цветом
						// $accentStrong; отметка as-of — частью строки.
						const rowContent = (
							<>
								<Icon aria-hidden="true" className="size-3 shrink-0" />
								{referenceKindLabels[reference.kind]} · {reference.title}, as-of{" "}
								{formatMoment(reference.asOf)}
							</>
						);
						return (
							<li
								key={`${reference.kind}:${reference.id}`}
								className="flex flex-wrap items-center gap-[7px]"
							>
								{reference.kind === "rule-card" ? (
									// Ссылка на карточку правила — клик и наведение
									// открывают карточку: поведение сохранено.
									<button
										type="button"
										className="flex cursor-pointer items-center gap-[7px] text-left text-[12px] text-accent-strong underline-offset-2 hover:underline"
										onMouseEnter={() => onRuleHover?.(reference.id)}
										onMouseLeave={() => onRuleLeave?.()}
										onClick={() => onRuleOpen?.(reference.id)}
									>
										{rowContent}
									</button>
								) : (
									<span className="flex items-center gap-[7px] text-[12px] text-accent-strong">
										{rowContent}
									</span>
								)}
							</li>
						);
					})}
				</ul>
			)}
		</aside>
	);
}

/** Иконка вызванного инструмента: корпус правил — книга, рынок — глобус. */
function toolIcon(tool: string): LucideIcon {
	if (tool.includes("rule"))
		return BookOpen;
	if (tool.includes("market") || tool.includes("option"))
		return Globe;
	return Cog;
}

function toolCallKey(call: ToolCallTrace, index: number): string {
	// Один инструмент мог зваться повторно — ключ различает вызовы индексом.
	return `${call.tool}:${call.argument}:${index}`;
}
