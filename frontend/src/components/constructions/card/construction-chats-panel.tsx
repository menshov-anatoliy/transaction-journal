import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { MessageSquarePlus, RotateCcw, CheckCheck, Trash2 } from "lucide-react";
import { listChats, createChat, deleteChat } from "@/chat/api/chat-api";
import { chatKeys, chatMessagesOptions } from "@/chat/chat-queries";
import { chatParamsFromForm } from "@/chat/chat-params";
import { useCompleteChatMutation, useResumeChatMutation } from "@/chat/chat-mutations";
import type { ChatDto } from "@/chat/types";
import { AgentChatThread } from "@/chat/components/agent-chat-thread";
import { useAgentChatRuntime } from "@/chat/runtime/use-agent-chat-runtime";
import { Button } from "@/components/ui/button";
import { formatMoment } from "@/lib/format/display-time";
import { cn } from "@/lib/utils";
import { readRule } from "@/lib/api/agent-rules";
import { RuleFullCard, RuleBriefPopup } from "@/components/agent/rule-cards";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";

// Панель чатов конструкции в правой контекстной панели каркаса по концепции
// §4: полный жизненный цикл чатов, привязанных к конструкции, — создание
// нового чата с автопривязкой к ней первым сообщением, продолжение,
// завершение (скрытие в завершённые) и удаление целиком. Панель монтируется
// страницей карточки в слот useContextPanel и развёрнута всегда (у карточки
// контекст существует); ширина и свёрнутость — геометрия каркаса, панель
// её не задаёт. Для закрытой конструкции сохраняется тот же текущий API.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
// Traceability: adr:docs/adr/0010-frontend-spa-react-stack.md

/** Пропсы панели чатов конструкции. */
export interface ConstructionChatsPanelProps {
	/** Идентификатор конструкции — автопривязка новых чатов. */
	readonly constructionId: number;
	/** Ручной статус конструкции: закрытая/архивная ведёт чаты как пост-мортем. */
	readonly isClosed: boolean;
	/** Имя конструкции для чипа привязки композера (мастер bCLlQ, BindLabel). */
	readonly constructionName?: string;
}

/** Чат выбранной строки с runtime assistant-ui. */
function ChatThreadView({ chat, constructionName }: { chat: ChatDto; constructionName?: string }) {
	const controller = useAgentChatRuntime(chat.id, chat.params.model);
	const history = useQuery(chatMessagesOptions(chat.id));
	const [hoveredRuleId, setHoveredRuleId] = React.useState<string | null>(null);
	const [openedRuleId, setOpenedRuleId] = React.useState<string | null>(null);
	const hoveredRule = useQuery({
		queryKey: ["agent-rule-card", hoveredRuleId],
		queryFn: ({ signal }) => readRule(hoveredRuleId!, signal),
		enabled: hoveredRuleId !== null,
	});
	const openedRule = useQuery({
		queryKey: ["agent-rule-card", openedRuleId],
		queryFn: ({ signal }) => readRule(openedRuleId!, signal),
		enabled: openedRuleId !== null,
	});

	// Источник ответа раскрывается кратким попапом и полной карточкой рядом с чатом.
	// Лента остаётся на общем Markdown-компоненте AgentChatThread.
	// Чипы тулбара композера — параметры чата, зафиксированные при создании:
	// модель, источники и привязка к конструкции, если чат ею связан
	// (у привязанных чатов карточки — всегда).
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
	// Traceability: openspec:ui/screens#scenario-ui-composer-input-practices
	return (
		<div className="flex h-full min-h-0 flex-col gap-2 xl:flex-row">
			<div className="relative flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden rounded-md border">
				{history.isPending ? <p role="status">Загрузка истории чата…</p>
					: history.isError ? <div className="p-2 text-sm">
						<p role="alert">История чата недоступна: {history.error.message}</p>
						<Button variant="outline" size="sm" onClick={() => void history.refetch()}>Повторить загрузку истории</Button>
					</div> : <AgentChatThread
					chat={controller}
					context={{
						model: chat.params.model,
						// Чип привязки показывает имя конструкции (мастер bCLlQ,
						// BindLabel «Стреддл BTC»); без имени — идентификатор.
						// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
						constructionLabel: chat.params.constructionId
							? (constructionName ?? `конструкция ${chat.params.constructionId}`)
							: undefined,
						sources: chat.params.sources,
					}}
					onRuleHover={setHoveredRuleId}
					onRuleLeave={() => setHoveredRuleId(null)}
					onRuleOpen={(ruleId) => {
						setHoveredRuleId(null);
						setOpenedRuleId(ruleId);
					}}
				/>}
				{hoveredRuleId !== null && (
					hoveredRule.data !== undefined ? <RuleBriefPopup card={hoveredRule.data} /> : (
						<div className="pointer-events-none absolute right-3 top-3 z-10 rounded-md border bg-card p-3 text-xs">
							{hoveredRule.isError
								? <p role="alert">Правило недоступно: {hoveredRule.error.message}</p>
								: <p role="status">Чтение правила…</p>}
						</div>
					)
				)}
			</div>
			{openedRuleId !== null && (
				<aside data-slot="rule-detail" aria-label="Полная карточка правила" className="max-h-full overflow-y-auto xl:w-72 xl:shrink-0">
					<Button variant="ghost" size="sm" onClick={() => setOpenedRuleId(null)}>Закрыть карточку правила</Button>
					{openedRule.isError ? <p role="alert">Правило недоступно: {openedRule.error.message}</p>
						: openedRule.data !== undefined ? <RuleFullCard rule={openedRule.data} detail={openedRule.data} />
							: <p role="status">Чтение полной карточки правила…</p>}
				</aside>
			)}
		</div>
	);
}

export function ConstructionChatsPanel({ constructionId, isClosed, constructionName }: ConstructionChatsPanelProps) {
	const [searchParams, setSearchParams] = useSearchParams();
	// Панель развёрнута всегда: собственная свёрнутость и параметр ?chats=*
	// упразднены — ширина и свёрнутость задаёт каркас (localStorage), панель
	// наполняет слот контекстной панели. Выбор чата (?chat=) и показ
	// завершённых переживают перезагрузку в URL.
	// Traceability: openspec:ui/screens#scenario-ui-chats-panel-in-context-panel
	const selectedChatId = searchParams.get("chat");
	const showCompleted = searchParams.get("completedChats") === "true";
	const updateQuery = (changes: Record<string, string | null>) => setSearchParams((current) => {
		const next = new URLSearchParams(current);
		for (const [key, value] of Object.entries(changes)) {
			if (value === null)
				next.delete(key);
			else
				next.set(key, value);
		}
		return next;
	}, { replace: true });
	const setSelectedChatId = (chatId: string | null) => updateQuery({ chat: chatId });
	const [deleteTarget, setDeleteTarget] = React.useState<string | null>(null);
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
	const selected = [...active, ...(showCompleted ? completed : [])].find((chat) => chat.id === selectedChatId) ?? null;

	// Новый чат создаётся первым сообщением с автопривязкой к конструкции:
	// параметры фиксируются при создании (модель GLM-5.3, все источники).
	const createMutation = useMutation({
		mutationFn: (text: string) => createChat(text, chatParamsFromForm({ constructionId: String(constructionId) })),
		onSuccess: async (chat) => {
			queryClient.setQueryData<ChatDto[]>(chatKeys.list("active"), (items = []) => [chat, ...items.filter((item) => item.id !== chat.id)]);
			setSelectedChatId(chat.id);
			setDraft("");
			await queryClient.invalidateQueries({ queryKey: chatKeys.lists() });
		},
	});

	const completeMutation = useCompleteChatMutation();
	const resumeMutation = useResumeChatMutation();
	// Удаление требует подтверждения и очищает кеш истории только после успеха API.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	const deleteMutation = useMutation({
		mutationFn: deleteChat,
		onSuccess: async (_, chatId) => {
			for (const status of ["active", "completed"] as const)
				queryClient.setQueryData<ChatDto[]>(chatKeys.list(status), (items = []) => items.filter((item) => item.id !== chatId));
			queryClient.removeQueries({ queryKey: chatKeys.messages(chatId) });
			if (selectedChatId === chatId)
				setSelectedChatId(null);
			setDeleteTarget(null);
			await queryClient.invalidateQueries({ queryKey: chatKeys.lists() });
		},
	});
	const lifecycleError = completeMutation.error ?? resumeMutation.error;

	// Пока список не прочитан, отсутствие данных не выдаётся за пустую историю.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	const chatsLoading = activeQuery.isPending || (showCompleted && completedQuery.isPending);
	const chatsUnavailable =
		activeQuery.isError || (showCompleted && completedQuery.isError)
			? (activeQuery.error ?? completedQuery.error)?.message ?? "неизвестная причина"
			: null;

	return (
		<div data-slot="construction-chats" className="flex h-full min-h-0 flex-col gap-2 p-2">
			<Button variant="outline" size="sm" aria-pressed={showCompleted} onClick={() => updateQuery({
				completedChats: showCompleted ? null : "true",
				...(showCompleted && completed.some((chat) => chat.id === selectedChatId) ? { chat: null } : {}),
			})}>Показать завершённые</Button>
			{chatsUnavailable !== null ? (
				<div className="text-muted-foreground flex flex-col gap-2 p-2 text-sm" role="status">
					<p role="alert">Чаты агента недоступны: {chatsUnavailable}.</p>
					<Button variant="outline" size="sm" onClick={() => {
						void activeQuery.refetch();
						void completedQuery.refetch();
					}}>Повторить загрузку</Button>
				</div>
			) : chatsLoading ? <p role="status">Загрузка чатов конструкции…</p> : (
				<>
					{/* Первое сообщение создаёт чат с привязкой и параметрами по умолчанию.
					    Для закрытой конструкции показывается контекст пост-мортема,
					    без подмены текущего конвейера ответа. */}
					<div className="flex flex-col gap-1">
					{/* Капшен секции — мастер D811M (Cap1 «АКТИВНЫЕ»): 10/normal,
					    $textMuted, капс; полужирный caption 12 (аудит) убран. */}
					{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
						<span className="text-text-muted text-[10px] font-normal tracking-wide uppercase">
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

					{lifecycleError !== null && <p role="alert" className="text-destructive text-xs">Статус чата не изменён: {lifecycleError.message}</p>}
					<Dialog open={deleteTarget !== null} onOpenChange={(value) => {
						if (value === false && deleteMutation.isPending === false)
							setDeleteTarget(null);
					}}>
						<DialogContent showCloseButton={deleteMutation.isPending === false}>
							<DialogTitle>Удалить чат целиком?</DialogTitle>
							<DialogDescription>Удалить чат целиком вместе со всеми сообщениями? Это действие нельзя отменить.</DialogDescription>
							<div className="flex gap-2">
								<Button variant="destructive" size="sm" disabled={deleteMutation.isPending} onClick={() => {
									if (deleteTarget !== null)
										deleteMutation.mutate(deleteTarget);
								}}>Подтвердить удаление</Button>
								<Button variant="outline" size="sm" disabled={deleteMutation.isPending} onClick={() => setDeleteTarget(null)}>Отмена</Button>
							</div>
							{deleteMutation.isError && <p role="alert" className="text-destructive text-xs">Чат не удалён: {deleteMutation.error.message}</p>}
						</DialogContent>
					</Dialog>
					<div className="flex min-h-0 flex-1 flex-col gap-2">
						<ChatListSection
							title="активные"
							chats={active}
							selectedChatId={selectedChatId}
							onSelect={setSelectedChatId}
							onDelete={(chatId) => { deleteMutation.reset(); setDeleteTarget(chatId); }}
							deletePending={deleteMutation.isPending}
							action={(chat) => (
								<Button
									variant="ghost"
									size="icon"
									className="size-6"
									title="Завершить чат"
									aria-label={`завершить чат ${chat.id}`}
									disabled={completeMutation.isPending}
									onClick={() => {
										resumeMutation.reset();
										completeMutation.mutate(chat.id, { onSuccess: () => {
											if (showCompleted === false && selectedChatId === chat.id)
												setSelectedChatId(null);
										} });
									}}
								>
									<CheckCheck aria-hidden />
								</Button>
							)}
						/>
						{showCompleted && <ChatListSection
							title="завершённые"
							chats={completed}
							selectedChatId={selectedChatId}
							onSelect={setSelectedChatId}
							onDelete={(chatId) => { deleteMutation.reset(); setDeleteTarget(chatId); }}
							deletePending={deleteMutation.isPending}
							action={(chat) => (
								<Button
									variant="ghost"
									size="icon"
									className="size-6"
									title="Продолжить чат"
									aria-label={`продолжить чат ${chat.id}`}
									disabled={resumeMutation.isPending}
									onClick={() => { completeMutation.reset(); resumeMutation.mutate(chat.id); }}
								>
									<RotateCcw aria-hidden />
								</Button>
							)}
						/>}
					</div>

					{selected !== null ? (
						<div className="border-t pt-2" style={{ height: "60%" }}>
							<ChatThreadView key={selected.id} chat={selected} constructionName={constructionName} />
						</div>
					) : (
						<p className="text-muted-foreground py-4 text-center text-xs">
							{selectedChatId !== null
								? "Выбранный чат не найден в доступном списке"
								: active.length + (showCompleted ? completed.length : 0) === 0
								? "Чатов конструкции ещё нет — задайте первый вопрос"
								: "Выберите чат из списков"}
						</p>
					)}
				</>
			)}
		</div>
	);
}

/** Секция списка чатов с действием строки. */
function ChatListSection({
	title,
	chats,
	selectedChatId,
	onSelect,
	action,
	onDelete,
	deletePending,
}: {
	title: string;
	chats: readonly ChatDto[];
	selectedChatId: string | null;
	onSelect: (chatId: string) => void;
	action: (chat: ChatDto) => React.ReactNode;
	onDelete: (chatId: string) => void;
	deletePending: boolean;
}) {
	if (chats.length === 0) {
		return null;
	}

	return (
		<div className="flex min-h-0 flex-col gap-1">
			{/* Капшен секции — мастер D811M (Cap1/Cap2): 10/normal, $textMuted, капс. */}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			<span className="text-text-muted text-[10px] font-normal tracking-wide uppercase">{title}</span>
			<ul className="flex flex-col gap-1 overflow-y-auto">
				{chats.map((chat) => (
					<li key={chat.id} className="flex items-center gap-1">
						{/* Пункт сессии — мастер s9J3h «Сессия/пункт»: радиус 10,
						    паддинги [8,10], название 12.5/600, время 11 $textMuted;
						    выбранный пункт — заливка $surface2 (D811M, S1 Cur).
						    Превью последнего сообщения из макета нет: в DTO чата
						    данных превью не передаётся — расширение контракта
						    вне визуальной сверки. */}
						{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
						<button
							type="button"
							className={cn(
								"flex-1 cursor-pointer rounded-md px-2.5 py-2 text-left text-[12.5px] font-semibold transition-colors hover:bg-accent",
								chat.id === selectedChatId && "bg-surface-2",
							)}
							onClick={() => onSelect(chat.id)}
						>
							<span className="block truncate">{chat.params.model}</span>
							<span className="text-text-muted block text-[11px]">
								{formatMoment(chat.lastMessageAt)}
							</span>
						</button>
						{action(chat)}
						<Button variant="ghost" size="icon" className="size-6" aria-label={`удалить чат ${chat.id}`} title="Удалить чат целиком" disabled={deletePending} onClick={() => onDelete(chat.id)}>
							<Trash2 aria-hidden />
						</Button>
					</li>
				))}
			</ul>
		</div>
	);
}
