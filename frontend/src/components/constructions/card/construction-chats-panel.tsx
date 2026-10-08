import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronRight, MessageSquarePlus, RotateCcw, CheckCheck } from "lucide-react";
import { listChats, createChat } from "@/chat/api/chat-api";
import { chatKeys } from "@/chat/chat-queries";
import { chatParamsFromForm } from "@/chat/chat-params";
import { useCompleteChatMutation, useResumeChatMutation } from "@/chat/chat-mutations";
import type { ChatDto } from "@/chat/types";
import { AgentChatThread } from "@/chat/components/agent-chat-thread";
import { useAgentChatRuntime } from "@/chat/runtime/use-agent-chat-runtime";
import { Button } from "@/components/ui/button";
import { formatMoment } from "@/lib/format/display-time";
import { cn } from "@/lib/utils";
import { useIsMobile } from "@/lib/use-mobile";

// Правая скрываемая область чатов карточки по концепции §4: полный жизненный
// цикл чатов, привязанных к конструкции, — создание нового чата с
// автопривязкой к ней первым сообщением, продолжение, завершение (скрытие в
// завершённые); закрытая конструкция ведёт те же чаты конвейером
// пост-мортема. Бэкенд-эндпоинтов чата ещё нет (задача 5.3): интеграция идёт
// по зафиксированному контракту chat-api, недоступность бэкенда деградирует
// честным сообщением без пустой панели.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
// На мобильном чаты открываются в drawer-панели поверх карточки.
// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
// Traceability: adr:docs/adr/0010-frontend-spa-react-stack.md

/** Пропсы панели чатов конструкции. */
export interface ConstructionChatsPanelProps {
	/** Идентификатор конструкции — автопривязка новых чатов. */
	readonly constructionId: number;
	/** Ручной статус конструкции: закрытая/архивная ведёт чаты как пост-мортем. */
	readonly isClosed: boolean;
}

/** Чат выбранной строки с runtime assistant-ui. */
function ChatThreadView({ chat }: { chat: ChatDto }) {
	const controller = useAgentChatRuntime(chat.id, chat.params.model);
	return (
		<div className="flex h-full min-h-0 flex-col">
			<div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-md border">
				<AgentChatThread chat={controller} />
			</div>
		</div>
	);
}

export function ConstructionChatsPanel({ constructionId, isClosed }: ConstructionChatsPanelProps) {
	const isMobile = useIsMobile();
	const [open, setOpen] = React.useState(false);
	const [selectedChatId, setSelectedChatId] = React.useState<string | null>(null);
	const [draft, setDraft] = React.useState("");
	const queryClient = useQueryClient();

	// Списки чатов по контракту 4.1: фильтрация по привязке к конструкции
	// выполняется на клиенте — контракт отдаёт списки целиком.
	const activeQuery = useQuery({ queryKey: chatKeys.list("active"), queryFn: ({ signal }) => listChats("active", signal) });
	const completedQuery = useQuery({ queryKey: chatKeys.list("completed"), queryFn: ({ signal }) => listChats("completed", signal) });

	const boundChats = (status: "active" | "completed"): ChatDto[] => {
		const list = status === "active" ? activeQuery.data : completedQuery.data;
		const key = String(constructionId);
		return (list ?? []).filter((chat) => chat.params.constructionId === key);
	};

	const active = boundChats("active");
	const completed = boundChats("completed");
	const selected = [...active, ...completed].find((chat) => chat.id === selectedChatId) ?? null;

	// Новый чат создаётся первым сообщением с автопривязкой к конструкции:
	// параметры фиксируются при создании (модель GLM-5.3, все источники).
	const createMutation = useMutation({
		mutationFn: (text: string) => createChat(text, chatParamsFromForm({ constructionId: String(constructionId) })),
		onSuccess: (chat) => {
			void queryClient.invalidateQueries({ queryKey: chatKeys.lists() });
			setSelectedChatId(chat.id);
			setDraft("");
		},
	});

	const completeMutation = useCompleteChatMutation();
	const resumeMutation = useResumeChatMutation();

	// Честная деградация: бэкенд чата ещё не подключён — панель сообщает
	// причину и не рисует пустые списки; интерфейс оживает без правок в 5.3.
	const chatsUnavailable =
		activeQuery.isError || completedQuery.isError
			? (activeQuery.error ?? completedQuery.error)?.message ?? "неизвестная причина"
			: null;

	return (
		<aside data-slot="construction-chats" className="flex flex-col gap-2">
			<Button variant="outline" size="sm" aria-expanded={open} onClick={() => setOpen((value) => !value)} className="self-start">
				<ChevronRight aria-hidden className={cn("transition-transform", open && "rotate-90")} />
				Чаты конструкции
			</Button>

			{open && (
				<div className={cn(isMobile ? "fixed inset-0 z-40" : "flex h-[32rem] w-80 flex-col gap-2 rounded-lg border bg-card p-2 shadow-sm xl:w-96")}>
					{isMobile && (
						<button
							type="button"
							aria-label="Закрыть чаты конструкции"
							className="absolute inset-0 bg-black/40"
							onClick={() => setOpen(false)}
						/>
					)}
					<div className={cn("flex flex-col gap-2 rounded-lg border bg-card p-2 shadow-sm", isMobile && "absolute right-0 top-0 h-full w-[min(30rem,100vw)] overflow-y-auto border-l")}>
					{chatsUnavailable !== null ? (
						<div className="text-muted-foreground flex flex-col gap-2 p-2 text-sm" role="status">
							<p role="alert">Чаты агента недоступны: {chatsUnavailable}.</p>
							<p>
								Бэкенд-эндпоинты чата подключаются задачей 5.3 — панель заработает без
								изменений интерфейса карточки.
							</p>
						</div>
					) : (
						<>
							{/* Новый чат с автопривязкой: первое сообщение создаёт чат с
							    параметрами по умолчанию; закрытая конструкция помечает
							    конвейер пост-мортемом. */}
							<div className="flex flex-col gap-1">
								<span className="text-muted-foreground text-xs font-semibold tracking-wide uppercase">
									{isClosed ? "пост-мортем конструкции" : "новый чат"}
								</span>
								<textarea
									rows={2}
									className="border-input bg-background min-h-9 flex-1 resize-none rounded-md border px-2 py-1.5 text-sm outline-none placeholder:text-muted-foreground"
									placeholder="Спросите ИИ-помощника о конструкции…"
									value={draft}
									onChange={(event) => setDraft(event.target.value)}
								/>
								<Button
									size="sm"
									className="self-start"
									disabled={createMutation.isPending || draft.trim() === ""}
									onClick={() => createMutation.mutate(draft.trim())}
								>
									<MessageSquarePlus aria-hidden />
									Создать чат
								</Button>
								{createMutation.isError && (
									<p className="text-destructive text-xs" role="alert">
										Чат не создан: {createMutation.error.message}
									</p>
								)}
							</div>

							<div className="flex min-h-0 flex-1 flex-col gap-2">
								<ChatListSection
									title="активные"
									chats={active}
									selectedChatId={selectedChatId}
									onSelect={setSelectedChatId}
									action={(chat) => (
										<Button
											variant="ghost"
											size="icon"
											className="size-6"
											title="Завершить чат"
											aria-label={`завершить чат ${chat.id}`}
											disabled={completeMutation.isPending}
											onClick={() => completeMutation.mutate(chat.id)}
										>
											<CheckCheck aria-hidden />
										</Button>
									)}
								/>
								<ChatListSection
									title="завершённые"
									chats={completed}
									selectedChatId={selectedChatId}
									onSelect={setSelectedChatId}
									action={(chat) => (
										<Button
											variant="ghost"
											size="icon"
											className="size-6"
											title="Продолжить чат"
											aria-label={`продолжить чат ${chat.id}`}
											disabled={resumeMutation.isPending}
											onClick={() => resumeMutation.mutate(chat.id)}
										>
											<RotateCcw aria-hidden />
										</Button>
									)}
								/>
							</div>

							{selected !== null ? (
								<div className="border-t pt-2" style={{ height: "60%" }}>
									<ChatThreadView chat={selected} />
								</div>
							) : (
								<p className="text-muted-foreground py-4 text-center text-xs">
									{active.length + completed.length === 0
										? "Чатов конструкции ещё нет — задайте первый вопрос"
										: "Выберите чат из списков"}
								</p>
							)}
						</>
					)}
					</div>
				</div>
			)}
		</aside>
	);
}

/** Секция списка чатов с действием строки. */
function ChatListSection({
	title,
	chats,
	selectedChatId,
	onSelect,
	action,
}: {
	title: string;
	chats: readonly ChatDto[];
	selectedChatId: string | null;
	onSelect: (chatId: string) => void;
	action: (chat: ChatDto) => React.ReactNode;
}) {
	if (chats.length === 0) {
		return null;
	}

	return (
		<div className="flex min-h-0 flex-col gap-1">
			<span className="text-muted-foreground text-xs font-semibold tracking-wide uppercase">{title}</span>
			<ul className="flex flex-col gap-1 overflow-y-auto">
				{chats.map((chat) => (
					<li key={chat.id} className="flex items-center gap-1">
						<button
							type="button"
							className={cn(
								"flex-1 cursor-pointer rounded-md px-2 py-1.5 text-left text-xs transition-colors hover:bg-accent",
								chat.id === selectedChatId && "bg-accent font-medium",
							)}
							onClick={() => onSelect(chat.id)}
						>
							<span className="block truncate">{chat.params.model}</span>
							<span className="text-muted-foreground block text-[11px]">
								{formatMoment(chat.lastMessageAt)}
							</span>
						</button>
						{action(chat)}
					</li>
				))}
			</ul>
		</div>
	);
}
