import type { TextMessagePartComponent } from "@assistant-ui/react";
import {
	AssistantRuntimeProvider,
	ComposerPrimitive,
	MessagePrimitive,
	ThreadPrimitive,
} from "@assistant-ui/react";
import { ArrowUp } from "lucide-react";
import type { SourceTrace } from "@/chat/types";
import type { AgentChatController } from "@/chat/runtime/use-agent-chat-runtime";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { Button } from "@/components/ui/button";
import { ToolStatus } from "@/components/design";
import { SourceTraceView } from "./source-trace-view";

export interface AgentChatThreadProps {
	/** Контроллер чата агента из useAgentChatRuntime. */
	chat: AgentChatController;
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
export function AgentChatThread({ chat, onRuleHover, onRuleLeave, onRuleOpen }: AgentChatThreadProps) {
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
							Индикация выполнения tool-вызовов хода — мастер ix8ma
							«Tool-статус» (инстанс Tool2/tMmi0 Body #4): в макете
							пилюля стоит в потоке сообщений между вопросом владельца
							и стримящимся ответом. Транспорт SSE (задача 2.3) событий
							прогресса tool-вызовов не несёт — известных каналу данных
							нет, поэтому пилюля показывается на весь ход генерации с
							единым текстом источников; имя конкретного инструмента
							появится здесь же с приходом live-событий tool-прогресса.
						*/}
						{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
						{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
						{chat.isRunning && (
							<div className="mb-4" data-slot="tool-status-run">
								<ToolStatus>источники — собираю данные для ответа…</ToolStatus>
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
				    заливка $surface, бордер $border, радиус 14, паддинг 14, зазор 12,
				    вертикаль «поле ввода → строка действий», ширина колонки до 720.
				    «Паддинг 10» аудита §3.5:3 принадлежит обёртке ComposerWrap (PZoUP)
				    экрана Body #3 — в Body #4 композер идёт без обёртки, решение по
				    мастеру; радиус 14 тоже по мастеру, а не «7–12» из текста задачи. */}
				{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
				{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
				<ComposerPrimitive.Root className="mx-1 mb-1 flex w-[min(100%,720px)] flex-col gap-3 rounded-[14px] border bg-surface p-3.5">
					<ComposerPrimitive.Input
						rows={2}
						placeholder="Спросите ИИ-помощника…"
						className="max-h-40 min-h-11 w-full resize-none bg-transparent text-[13.5px] leading-[1.5] outline-none placeholder:text-text-muted"
					/>
					<div className="flex items-center justify-end gap-2">
						{chat.isRunning && (
							<ComposerPrimitive.Cancel asChild>
								<Button variant="secondary" size="sm">
									Остановить
								</Button>
							</ComposerPrimitive.Cancel>
						)}
						{/* Отправка — мастер-нода K7kSy «Send»: квадрат 36×36 на токене
						    $accent (вариант Primary примитива ui/button), радиус 10
						    (--radius-md лестницы --radius), иконка arrow-up белым. */}
						{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
						<ComposerPrimitive.Send asChild>
							<Button aria-label="Отправить" size="icon" className="rounded-md">
								<ArrowUp aria-hidden />
							</Button>
						</ComposerPrimitive.Send>
					</div>
				</ComposerPrimitive.Root>
			</div>
		</AssistantRuntimeProvider>
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
