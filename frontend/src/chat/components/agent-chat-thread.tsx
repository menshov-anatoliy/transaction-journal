import type { TextMessagePartComponent } from "@assistant-ui/react";
import {
	AssistantRuntimeProvider,
	ComposerPrimitive,
	MessagePrimitive,
	ThreadPrimitive,
} from "@assistant-ui/react";
import type { SourceTrace } from "@/chat/types";
import type { AgentChatController } from "@/chat/runtime/use-agent-chat-runtime";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { SourceTraceView } from "./source-trace-view";

export interface AgentChatThreadProps {
	/** Контроллер чата агента из useAgentChatRuntime. */
	chat: AgentChatController;
}

// Журнальный текст чата рендерится единым MD-рендером — текстовая часть
// сообщения проходит через MarkdownViewer, как правила и комментарии.
// Примитив Parts спредит поля части (text) прямо в пропсы компонента.
const MarkdownText: TextMessagePartComponent = ({ text }) => (
	<MarkdownViewer text={text} />
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
export function AgentChatThread({ chat }: AgentChatThreadProps) {
	return (
		<AssistantRuntimeProvider runtime={chat.runtime}>
			<div className="flex h-full min-h-0 flex-col">
				<ThreadPrimitive.Root className="flex min-h-0 flex-1 flex-col">
					<ThreadPrimitive.Viewport className="min-h-0 flex-1 overflow-y-auto px-4 py-6">
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
									<AssistantRow trace={readSourceTrace(message)} />
								)
							}
						</ThreadPrimitive.Messages>
					</ThreadPrimitive.Viewport>
				</ThreadPrimitive.Root>

				{/* Баннер деградации: генерация не удалась, история сохранена. */}
				{chat.error !== null && (
					<div
						role="alert"
						className="mx-4 mb-2 rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive"
					>
						Генерация не удалась: {chat.error}. Ответ может быть неполным — история
						чата сохранена.
					</div>
				)}

				<ComposerPrimitive.Root className="mx-4 mb-4 flex items-end gap-2 rounded-xl border bg-card p-2 shadow-sm">
					<ComposerPrimitive.Input
						rows={2}
						placeholder="Спросите ИИ-помощника…"
						className="max-h-40 min-h-11 flex-1 resize-none bg-transparent px-2 py-1.5 text-sm outline-none placeholder:text-muted-foreground"
					/>
					<ComposerPrimitive.Send className="inline-flex h-9 items-center rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50">
						Отправить
					</ComposerPrimitive.Send>
					{chat.isRunning && (
						<ComposerPrimitive.Cancel className="inline-flex h-9 items-center rounded-lg border px-3 text-sm font-medium transition-colors hover:bg-accent">
							Остановить
						</ComposerPrimitive.Cancel>
					)}
				</ComposerPrimitive.Root>
			</div>
		</AssistantRuntimeProvider>
	);
}

function UserRow() {
	return (
		<MessagePrimitive.Root className="mb-4 flex justify-end">
			<div className="max-w-[80%] rounded-2xl rounded-br-md bg-primary px-4 py-2.5 text-sm text-primary-foreground">
				<MessagePrimitive.Parts components={{ Text: MarkdownText }} />
			</div>
		</MessagePrimitive.Root>
	);
}

function AssistantRow({ trace }: { trace: SourceTrace | undefined }) {
	return (
		<MessagePrimitive.Root className="mb-4 flex flex-col items-start">
			<div className="w-full max-w-[92%] rounded-2xl rounded-bl-md border bg-card px-4 py-2.5">
				<MessagePrimitive.Parts components={{ Text: MarkdownText }} />
				{trace !== undefined && <SourceTraceView trace={trace} />}
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
