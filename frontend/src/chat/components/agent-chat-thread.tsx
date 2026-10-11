import type { TextMessagePartComponent } from "@assistant-ui/react";
import {
	AssistantRuntimeProvider,
	ComposerPrimitive,
	MessagePrimitive,
	ThreadPrimitive,
} from "@assistant-ui/react";
import { ArrowUp, ChevronDown, Globe, Link, Square } from "lucide-react";
import { CHAT_DATA_SOURCES } from "@/chat/chat-params";
import type { ChatDataSource, SourceTrace } from "@/chat/types";
import type { AgentChatActiveTool, AgentChatController } from "@/chat/runtime/use-agent-chat-runtime";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { Button } from "@/components/ui/button";
import { SourceChip, ToolStatus } from "@/components/design";
import { SourceTraceView } from "./source-trace-view";

/**
 * Контекст чата для чипов тулбара композера: параметры, зафиксированные
 * при создании чата, показываются рядом с отправкой без возможности
 * изменения — сменить привязку или модель значит начать новый чат.
 */
export interface AgentChatThreadContext {
	/** ИИ-модель чата — чип модели. */
	readonly model: string;
	/** Подпись привязки чата к конструкции; у портфельного чата чипа нет. */
	readonly constructionLabel?: string;
	/** Источники данных чата — чипы источников. */
	readonly sources: readonly ChatDataSource[];
}

export interface AgentChatThreadProps {
	/** Контроллер чата агента из useAgentChatRuntime. */
	chat: AgentChatController;
	/** Контекст чата для чипов тулбара композера; без него тулбар — только отправка. */
	context?: AgentChatThreadContext;
	/** Наведение на ссылку карточки правила из следа источников. */
	onRuleHover?: (ruleId: string) => void;
	/** Уход курсора со ссылки карточки правила. */
	onRuleLeave?: () => void;
	/** Открытие карточки правила из следа источников. */
	onRuleOpen?: (ruleId: string) => void;
}

// Журнальный текст чата рендерится единым MD-рендером — текстовая часть
// сообщения проходит через MarkdownViewer, как правила и комментарии.
// Примитив Parts спредит поля части (text) прямо в пропсы компонента.
// Типографика сообщений — мастер Body #4 (design.pen, инстансы eja01/sENPF):
// Inter 13.5/normal, у владельца межстрочный 1.5 (нода uuYEd), у агента 1.55
// (нода L30TL); базовые text-sm/leading-relaxed перекрываются через cn.
// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
const UserMarkdownText: TextMessagePartComponent = ({ text }) => (
	<MarkdownViewer text={text} className="text-[13.5px] leading-[1.5]" />
);

const AssistantMarkdownText: TextMessagePartComponent = ({ text }) => (
	<MarkdownViewer text={text} className="text-[13.5px] leading-[1.55]" />
);

/**
 * Базовая обёртка чата агента: Thread/Composer assistant-ui поверх
 * ExternalStoreRuntime. Бизнес-экран раздела «Агент» (форма нового чата,
 * история, каталог правил) — задача 5.3; здесь только переиспользуемый
 * каркас ленты сообщений с MD-рендером, следом источников и деградацией
 * сбоя генерации баннером без потери истории.
 */
// Traceability: adr:docs/adr/0010-frontend-spa-react-stack.md
// Traceability: change:add-agent-chat/proposal#what-changes
export function AgentChatThread({ chat, context, onRuleHover, onRuleLeave, onRuleOpen }: AgentChatThreadProps) {
	return (
		<AssistantRuntimeProvider runtime={chat.runtime}>
			<div className="flex h-full min-h-0 flex-col">
				<ThreadPrimitive.Root className="flex min-h-0 flex-1 flex-col">
					{/* Лента сообщений — мастер Body #4, нода iXsp6 «Сообщения»:
					    асимметричные паддинги [4,60,4,4] и зазор между элементами 16
					    (mb-4 строк ниже); правый инсет 60 держит меру текста около
					    ширины композера. */}
					<ThreadPrimitive.Viewport className="min-h-0 flex-1 overflow-y-auto p-1 pr-15">
						<ThreadPrimitive.Empty>
							<p className="py-16 text-center text-sm text-muted-foreground">
								Задайте вопрос ИИ-помощнику — он ответит сценариями «если/то» по
								журналу, корпусу правил и рынку.
							</p>
						</ThreadPrimitive.Empty>
						<ThreadPrimitive.Messages>
							{({ message }) =>
								message.role === "user" ? (
									<UserRow />
								) : (
									<AssistantRow trace={readSourceTrace(message)} onRuleHover={onRuleHover} onRuleLeave={onRuleLeave} onRuleOpen={onRuleOpen} />
								)
							}
						</ThreadPrimitive.Messages>
						{/*
							Живые пилюли активности tool-вызовов — мастер ix8ma
							«Tool-статус» (инстанс Tool2/tMmi0 Body #4): в макете
							пилюля стоит в потоке сообщений между вопросом владельца
							и стримящимся ответом. Кадр tool start зажигает пилюлю
							конкретного инструмента, парный end гасит её; чистку по
							терминальным кадрам и отмене выполняет runtime. Статичная
							надпись «источники — собираю данные…» убрана: активность
							приходит событиями стрима, а не рисуется на весь ход;
							сами tool-call фрагменты в ленте не рендерятся.
						*/}
						{/* Traceability: openspec:ui/screens#scenario-ui-streaming-with-tool-status */}
						{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
						{chat.activeTools.length > 0 && (
							<div className="mb-4 flex flex-col items-start gap-2" data-slot="tool-status-run">
								{chat.activeTools.map((tool) => (
									<ToolStatus key={tool.name}>{toolStatusLabel(tool)}</ToolStatus>
								))}
							</div>
						)}
					</ThreadPrimitive.Viewport>
				</ThreadPrimitive.Root>

				{/* Баннер деградации: генерация не удалась, история сохранена. */}
				{chat.error !== null && (
					<div
						role="alert"
						className="mx-1 mb-2 rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive"
					>
						Генерация не удалась: {chat.error}. Ответ может быть неполным — история
						чата сохранена.
					</div>
				)}

				{/* Композер Body #4 — мастер Z14sH «Композер/Агент» (инстанс AcXEb):
				    заливка $surface, бордер $border, радиус 14, паддинг 14, зазор 8,
				    вертикаль «поле ввода → тулбар», ширина колонки до 720. */}
				{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
				{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
				{/* Практики ввода композера: поле растёт с текстом от одной до
				    восьми строк (TextareaAutosize примитива), Enter отправляет,
				    Shift+Enter переносит строку; отправка недоступна при пустом
				    поле и во время генерации — на месте отправки остаётся только
				    Stop, отменяющий стрим и возвращающий панель в состояние ввода.
				    Traceability: openspec:ui/screens#scenario-ui-composer-input-practices */}
				{/* Traceability: openspec:ui/screens#scenario-ui-cancel-generation */}
				<ComposerPrimitive.Root className="mx-1 mb-1 flex w-[min(100%,720px)] flex-col gap-2 rounded-[14px] border bg-surface p-3.5">
					<ComposerPrimitive.Input
						minRows={1}
						maxRows={8}
						placeholder="Спросите ИИ-помощника…"
						className="min-h-11 w-full resize-none bg-transparent text-[13.5px] leading-[1.5] outline-none placeholder:text-text-muted"
					/>
					{/* Тулбар под полем — одна строка мастера ZCrdN: чипы контекста
					    слева, spacer, отправка/остановка справа. */}
					<div className="flex items-center gap-2">
						{context !== undefined && <ComposerContextChips context={context} />}
						<div className="ml-auto flex items-center gap-2">
							{chat.isRunning ? (
								<ComposerPrimitive.Cancel asChild>
									<Button aria-label="Остановить" size="icon" title="Остановить генерацию" className="rounded-md">
										<Square aria-hidden />
									</Button>
								</ComposerPrimitive.Cancel>
							) : (
								/* Отправка — мастер-нода K7kSy «Send/Stop»: квадрат 36×36
								на токене $accent (вариант Primary примитива ui/button),
								радиус 10 (--radius-md лестницы --radius), иконка arrow-up
								белым; примитив Send сам блокирует кнопку при пустом поле. */
								<ComposerPrimitive.Send asChild>
									<Button aria-label="Отправить" size="icon" className="rounded-md">
										<ArrowUp aria-hidden />
									</Button>
								</ComposerPrimitive.Send>
							)}
						</div>
					</div>
				</ComposerPrimitive.Root>
			</div>
		</AssistantRuntimeProvider>
	);
}

/**
 * Чипы контекста тулбара композера — мастер Z14sH (тулбар ZCrdN): привязка
 * к конструкции — чип bCLlQ (пилюля 999 на $infoSoft, иконка link 12 $info),
 * модель — чип aF5vM ($surface2, радиус 8, подпись 12/500 + шеврон
 * $textMuted), источники — инстансы примитива «Чип/Источник» fYadZ:
 * «журнал» и «корпус правил» с check, «рынок Bybit» с globe. Чипы
 * информационные: параметры чата фиксируются при создании и не меняются.
 */
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
function ComposerContextChips({ context }: { context: AgentChatThreadContext }) {
	return (
		<div data-slot="composer-context-chips" className="flex min-w-0 flex-wrap items-center gap-1.5">
			{context.constructionLabel !== undefined && (
				<span
					data-slot="composer-bind-chip"
					title="Чат привязан к конструкции"
					className="inline-flex items-center gap-1.5 rounded-full bg-info-soft px-2.5 py-[5px] text-[12px] whitespace-nowrap text-text-secondary"
				>
					<Link aria-hidden className="size-3 shrink-0 text-info" />
					{context.constructionLabel}
				</span>
			)}
			<span
				data-slot="composer-model-chip"
				title="Модель чата"
				className="inline-flex items-center gap-1.5 rounded-sm bg-secondary px-2.5 py-[5px] text-[12px] font-medium whitespace-nowrap text-foreground"
			>
				{context.model}
				<ChevronDown aria-hidden className="size-3 shrink-0 text-text-muted" />
			</span>
			{context.sources.map((sourceId) => {
				const source = CHAT_DATA_SOURCES.find((item) => item.id === sourceId);
				if (source === undefined)
					return null;
				return (
					<SourceChip key={sourceId} icon={sourceId === "market" ? Globe : undefined} title={source.hint}>
						{/* Подписи инстансов мастера — со строчной первой буквы:
						    «журнал», «корпус правил», «рынок Bybit». */}
						{source.label[0].toLowerCase() + source.label.slice(1)}
					</SourceChip>
				);
			})}
		</div>
	);
}

function UserRow() {
	return (
		<MessagePrimitive.Root className="mb-4 flex justify-end">
			{/* Пузырь сообщения владельца — мастер eja01 «Сообщение/Пользователь»:
			    нейтральная заливка $surface2 с текстом $textPrimary, единый радиус 12
			    без «хвоста» мессенджера, паддинги [10,14], мера текста до ~400.
			    Индиго-пузырь bg-primary (аудит §3.5:1) убран как вне-токенный. */}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			<div className="max-w-[400px] rounded-lg bg-surface-2 px-3.5 py-2.5 text-text-primary">
				<MessagePrimitive.Parts components={{ Text: UserMarkdownText }} />
			</div>
		</MessagePrimitive.Root>
	);
}

function AssistantRow({
	trace,
	onRuleHover,
	onRuleLeave,
	onRuleOpen,
}: {
	trace: SourceTrace | undefined;
	onRuleHover?: (ruleId: string) => void;
	onRuleLeave?: () => void;
	onRuleOpen?: (ruleId: string) => void;
}) {
	return (
		<MessagePrimitive.Root className="mb-4 flex w-full flex-col items-start">
			{/* Ответ агента — мастер sENPF «Сообщение/Ассистент»: без карточки-плашки,
			    плоский текст $textPrimary на фоне колонки во всю ширину; радиусы чата
			    7–12 в дизайне живут на вложенных панелях (след источников — таск 5.2),
			    а не на самом сообщении. Карточка с радиусом 16 (аудит §3.5:2) убрана. */}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			<div className="w-full">
				<MessagePrimitive.Parts components={{ Text: AssistantMarkdownText }} />
				{trace !== undefined && (
					<SourceTraceView trace={trace} onRuleHover={onRuleHover} onRuleLeave={onRuleLeave} onRuleOpen={onRuleOpen} />
				)}
			</div>
		</MessagePrimitive.Root>
	);
}

// След источников ехал в метаданных сообщения конвертером runtime.
function readSourceTrace(message: { metadata?: { custom?: Record<string, unknown> } }): SourceTrace | undefined {
	const trace = message.metadata?.custom?.["sourceTrace"];
	if (trace === null || trace === undefined)
		return undefined;
	return trace as SourceTrace;
}

// Подпись пилюли — формат ноды PfMRJ «источник — что делаю…»: имя
// инструмента реестра и деталь вызова, если стрим её доставил.
function toolStatusLabel(tool: AgentChatActiveTool): string {
	return tool.detail === undefined ? `${tool.name}…` : `${tool.name} — ${tool.detail}…`;
}
