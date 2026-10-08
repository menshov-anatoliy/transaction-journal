import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AgentChatThread } from "@/chat/components/agent-chat-thread";
import { chatParamsFromForm, CHAT_DATA_SOURCES, DEFAULT_CHAT_MODEL } from "@/chat/chat-params";
import { useCompleteChatMutation, useResumeChatMutation } from "@/chat/chat-mutations";
import { chatKeys } from "@/chat/chat-queries";
import { createChat, listChats } from "@/chat/api/chat-api";
import { useAgentChatRuntime } from "@/chat/runtime/use-agent-chat-runtime";
import type { ChatDataSource, ChatDto } from "@/chat/types";
import { Button } from "@/components/ui/button";
import { listRules, readRule, type AgentRuleCard } from "@/lib/api/agent-rules";
import { formatMoment } from "@/lib/format/display-time";
import { useIsMobile } from "@/lib/use-mobile";

// Раздел «Агент» маршрута /agent по концепции §7: форма нового чата с
// параметрами, история active/completed, лента чата на инфраструктуре 4.1,
// read-only каталог правил с фильтрами/поиском и попап карточки правила
// при наведении из следа источников.
// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
// На мобильном чат и история работают через drawer-компоновку.
// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
// Текущий раздел интегрирован с доменным направлением add-agent-chat:
// жизненный цикл active/completed и параметры чата (модель/источники/конструкция).
// Traceability: change:add-agent-chat/proposal#what-changes
// Все данные и команды раздела идут через единый версионированный API /api/v1.
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

type AgentTab = "chats" | "rules";

type RuleFilters = {
	character: string;
	clarity: string;
	subject: string;
	search: string;
};

const initialRuleFilters: RuleFilters = {
	character: "",
	clarity: "",
	subject: "",
	search: "",
};

export function AgentPage() {
	const isMobile = useIsMobile();
	const [tab, setTab] = React.useState<AgentTab>("chats");
	const [mobileHistoryOpen, setMobileHistoryOpen] = React.useState(false);
	const [selectedChatId, setSelectedChatId] = React.useState<string | null>(null);
	const [hoveredRuleId, setHoveredRuleId] = React.useState<string | null>(null);
	const [openedRuleId, setOpenedRuleId] = React.useState<string | null>(null);
	const [rulesFilters, setRulesFilters] = React.useState<RuleFilters>(initialRuleFilters);
	const [constructionIdDraft, setConstructionIdDraft] = React.useState("");
	const [model, setModel] = React.useState(DEFAULT_CHAT_MODEL);
	const [firstMessage, setFirstMessage] = React.useState("");
	const [sourceSelections, setSourceSelections] = React.useState<Record<ChatDataSource, boolean>>({
		journal: true,
		"rules-corpus": true,
		market: true,
	});
	const queryClient = useQueryClient();

	const activeQuery = useQuery({ queryKey: chatKeys.list("active"), queryFn: ({ signal }) => listChats("active", signal) });
	const completedQuery = useQuery({ queryKey: chatKeys.list("completed"), queryFn: ({ signal }) => listChats("completed", signal) });

	const activeChats = activeQuery.data ?? [];
	const completedChats = completedQuery.data ?? [];
	const selectedChat = [...activeChats, ...completedChats].find((chat) => chat.id === selectedChatId) ?? activeChats[0] ?? completedChats[0] ?? null;

	const completeMutation = useCompleteChatMutation();
	const resumeMutation = useResumeChatMutation();
	const createMutation = useMutation({
		mutationFn: (payload: { text: string; chatModel: string; constructionId: string | null; sources: readonly ChatDataSource[] }) =>
			createChat(
				payload.text,
				chatParamsFromForm({
					model: payload.chatModel,
					constructionId: payload.constructionId,
					sources: payload.sources,
				}),
			),
		onSuccess: async (created) => {
			await queryClient.invalidateQueries({ queryKey: chatKeys.lists() });
			setSelectedChatId(created.id);
			setFirstMessage("");
			setTab("chats");
		},
	});

	const rulesQuery = useQuery({
		queryKey: ["agent-rules", rulesFilters.character, rulesFilters.clarity, rulesFilters.subject, rulesFilters.search],
		queryFn: ({ signal }) =>
			listRules(
				{
					character: rulesFilters.character || undefined,
					clarity: rulesFilters.clarity || undefined,
					subject: rulesFilters.subject || undefined,
					search: rulesFilters.search || undefined,
				},
				signal,
			),
	});

	const hoveredRuleQuery = useQuery({
		queryKey: ["agent-rule-hover", hoveredRuleId],
		queryFn: ({ signal }) => readRule(hoveredRuleId!, signal),
		enabled: hoveredRuleId !== null,
	});
	const openedRuleQuery = useQuery({
		queryKey: ["agent-rule-opened", openedRuleId],
		queryFn: ({ signal }) => readRule(openedRuleId!, signal),
		enabled: openedRuleId !== null,
	});

	React.useEffect(() => {
		if (selectedChat !== null)
			setSelectedChatId(selectedChat.id);
	}, [selectedChat?.id]);

	const onCreateChat = () => {
		const trimmedMessage = firstMessage.trim();
		if (trimmedMessage === "")
			return;
		const selectedSources = CHAT_DATA_SOURCES
			.map((source) => source.id)
			.filter((id) => sourceSelections[id]);
		createMutation.mutate({
			text: trimmedMessage,
			chatModel: model,
			constructionId: constructionIdDraft.trim() === "" ? null : constructionIdDraft.trim(),
			sources: selectedSources,
		});
	};

	return (
		<section className="flex min-h-0 flex-col gap-4 p-6">
			<h1 className="text-2xl font-semibold tracking-tight">Агент</h1>

			<div className="flex items-center gap-2">
				<Button variant={tab === "chats" ? "default" : "outline"} onClick={() => setTab("chats")}>
					Чаты
				</Button>
				<Button variant={tab === "rules" ? "default" : "outline"} onClick={() => setTab("rules")}>
					Правила
				</Button>
			</div>

			{tab === "chats" ? (
				<div className="grid min-h-0 grid-cols-1 gap-4 xl:grid-cols-[22rem_minmax(0,1fr)_24rem]">
					<section className="flex flex-col gap-3 rounded-lg border p-3">
						<h2 className="text-base font-semibold">Новый чат</h2>
						<label className="flex flex-col gap-1 text-sm">
							<span>Модель</span>
							<select aria-label="Модель" className="rounded-md border px-2 py-1.5" value={model} onChange={(event) => setModel(event.target.value)}>
								<option value={DEFAULT_CHAT_MODEL}>{DEFAULT_CHAT_MODEL}</option>
								<option value="glm-5.3-flash">glm-5.3-flash</option>
							</select>
						</label>
						<label className="flex flex-col gap-1 text-sm">
							<span>Опциональная конструкция</span>
							<input
								aria-label="Опциональная конструкция"
								className="rounded-md border px-2 py-1.5"
								placeholder="например, 7"
								value={constructionIdDraft}
								onChange={(event) => setConstructionIdDraft(event.target.value)}
							/>
						</label>
						<fieldset className="flex flex-col gap-1 text-sm">
							<legend className="pb-1 text-sm font-medium">Источники данных</legend>
							<div aria-label="Источники данных" className="flex flex-col gap-1">
								{CHAT_DATA_SOURCES.map((source) => (
									<label key={source.id} className="flex cursor-pointer items-start gap-2 text-sm">
										<input
											type="checkbox"
											checked={sourceSelections[source.id]}
											onChange={(event) =>
												setSourceSelections((current) => ({ ...current, [source.id]: event.target.checked }))
											}
										/>
										<span>
											<b>{source.label}</b>
											<span className="text-muted-foreground block text-xs">{source.hint}</span>
										</span>
									</label>
								))}
							</div>
						</fieldset>
						<label className="flex flex-col gap-1 text-sm">
							<span>Первое сообщение</span>
							<textarea
								aria-label="Первое сообщение"
								rows={4}
								className="rounded-md border px-2 py-1.5"
								placeholder="Сформулируйте первый вопрос для нового чата…"
								value={firstMessage}
								onChange={(event) => setFirstMessage(event.target.value)}
							/>
						</label>
						<Button disabled={createMutation.isPending || firstMessage.trim() === ""} onClick={onCreateChat}>
							Создать чат
						</Button>
						{createMutation.isError && (
							<p className="text-destructive text-sm" role="alert">
								Не удалось создать чат: {createMutation.error.message}
							</p>
						)}
					</section>

					<section className="flex min-h-[30rem] min-w-0 flex-col rounded-lg border p-3">
						{selectedChat === null ? (
							<p className="text-muted-foreground py-10 text-center text-sm">
								Чатов пока нет — создайте новый чат первым сообщением.
							</p>
						) : (
							<>
								<header className="mb-2 flex items-center justify-between gap-2 border-b pb-2">
									<div className="min-w-0">
										<p className="truncate text-sm font-semibold">{selectedChat.params.model}</p>
										<p className="text-muted-foreground text-xs">
											последнее сообщение: {formatMoment(selectedChat.lastMessageAt)}
										</p>
									</div>
									<div className="flex gap-2">
										{selectedChat.status === "active" ? (
											<Button
												size="sm"
												variant="outline"
												disabled={completeMutation.isPending}
												onClick={() => completeMutation.mutate(selectedChat.id)}
											>
												Завершить чат
											</Button>
										) : (
											<Button
												size="sm"
												variant="outline"
												disabled={resumeMutation.isPending}
												onClick={() => resumeMutation.mutate(selectedChat.id)}
											>
												Продолжить чат
											</Button>
										)}
									</div>
								</header>
								{isMobile && (
									<Button variant="outline" size="sm" className="mb-2 self-start" onClick={() => setMobileHistoryOpen(true)}>
										Открыть историю чатов
									</Button>
								)}
								<div className="min-h-0 flex-1 overflow-hidden">
									<ChatThreadPane
										chat={selectedChat}
										onRuleHover={(ruleId) => setHoveredRuleId(ruleId)}
										onRuleLeave={() => setHoveredRuleId(null)}
										onRuleOpen={(ruleId) => setOpenedRuleId(ruleId)}
									/>
								</div>
								{hoveredRuleQuery.data !== undefined && (
									<RulePopupCard card={hoveredRuleQuery.data} title="Карточка правила (hover из чата)" />
								)}
							</>
						)}
					</section>

					{isMobile == false && (
						<aside className="flex min-h-[30rem] flex-col gap-3 rounded-lg border p-3">
							<h2 className="text-base font-semibold">История чатов</h2>
							{activeQuery.isPending || completedQuery.isPending ? (
								<p className="text-muted-foreground text-sm">чтение истории…</p>
							) : (
								<>
									<ChatList
										title="Активные"
										chats={activeChats}
										selectedChatId={selectedChat?.id ?? null}
										onSelect={setSelectedChatId}
										onToggle={(chatId) => completeMutation.mutate(chatId)}
										actionLabel="Завершить чат"
									/>
									<ChatList
										title="Завершённые"
										chats={completedChats}
										selectedChatId={selectedChat?.id ?? null}
										onSelect={setSelectedChatId}
										onToggle={(chatId) => resumeMutation.mutate(chatId)}
										actionLabel="Продолжить чат"
									/>
								</>
							)}
							{activeQuery.isError && (
								<p className="text-destructive text-sm" role="alert">
									Активные чаты недоступны: {activeQuery.error.message}
								</p>
							)}
							{completedQuery.isError && (
								<p className="text-destructive text-sm" role="alert">
									Завершённые чаты недоступны: {completedQuery.error.message}
								</p>
							)}
						</aside>
					)}
					{isMobile && mobileHistoryOpen && (
						<div className="fixed inset-0 z-40">
							<button type="button" aria-label="Закрыть историю чатов" className="absolute inset-0 bg-black/40" onClick={() => setMobileHistoryOpen(false)} />
							<aside className="bg-background absolute right-0 top-0 h-full w-[min(26rem,100vw)] overflow-y-auto border-l p-3">
								<div className="mb-2 flex items-center justify-between">
									<h2 className="text-base font-semibold">История чатов</h2>
									<Button variant="outline" size="sm" onClick={() => setMobileHistoryOpen(false)}>
										Закрыть
									</Button>
								</div>
								{activeQuery.isPending || completedQuery.isPending ? (
									<p className="text-muted-foreground text-sm">чтение истории…</p>
								) : (
									<>
										<ChatList
											title="Активные"
											chats={activeChats}
											selectedChatId={selectedChat?.id ?? null}
											onSelect={(chatId) => {
												setSelectedChatId(chatId);
												setMobileHistoryOpen(false);
											}}
											onToggle={(chatId) => completeMutation.mutate(chatId)}
											actionLabel="Завершить чат"
										/>
										<ChatList
											title="Завершённые"
											chats={completedChats}
											selectedChatId={selectedChat?.id ?? null}
											onSelect={(chatId) => {
												setSelectedChatId(chatId);
												setMobileHistoryOpen(false);
											}}
											onToggle={(chatId) => resumeMutation.mutate(chatId)}
											actionLabel="Продолжить чат"
										/>
									</>
								)}
							</aside>
						</div>
					)}
				</div>
			) : (
				<section className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_26rem]">
					<div className="flex flex-col gap-3 rounded-lg border p-3">
						<h2 className="text-base font-semibold">Каталог корпуса правил (read-only)</h2>
						<RulesFilters filters={rulesFilters} onChange={setRulesFilters} />
						{rulesQuery.isPending && <p className="text-muted-foreground text-sm">чтение правил…</p>}
						{rulesQuery.isError && (
							<p className="text-destructive text-sm" role="alert">
								Правила недоступны: {rulesQuery.error.message}
							</p>
						)}
						{rulesQuery.data !== undefined && (
							<ul className="flex max-h-[34rem] flex-col gap-1 overflow-y-auto">
								{rulesQuery.data.items.map((rule) => (
									<li key={rule.id} className="rounded-md border p-2 text-sm">
										<div className="flex items-center justify-between gap-2">
											<button
												type="button"
												className="cursor-pointer text-left font-medium underline-offset-2 hover:underline"
												onClick={() => setOpenedRuleId(rule.id)}
											>
												{rule.title}
											</button>
											<a
												href={`/spa/agent?rule=${encodeURIComponent(rule.id)}`}
												target="_blank"
												rel="noreferrer"
												className="text-xs underline-offset-2 hover:underline"
											>
												В новом окне
											</a>
										</div>
										<p className="text-muted-foreground text-xs">
											{rule.id} · {rule.character} · {rule.clarity} · {rule.subject}
										</p>
									</li>
								))}
							</ul>
						)}
					</div>
					<div className="rounded-lg border p-3">
						{openedRuleQuery.data !== undefined ? (
							<RulePopupCard card={openedRuleQuery.data} title="Карточка правила" />
						) : (
							<p className="text-muted-foreground text-sm">Выберите карточку правила в списке.</p>
						)}
					</div>
				</section>
			)}
		</section>
	);
}

function ChatThreadPane({
	chat,
	onRuleHover,
	onRuleLeave,
	onRuleOpen,
}: {
	chat: ChatDto;
	onRuleHover: (ruleId: string) => void;
	onRuleLeave: () => void;
	onRuleOpen: (ruleId: string) => void;
}) {
	const controller = useAgentChatRuntime(chat.id, chat.params.model);
	return <AgentChatThread chat={controller} onRuleHover={onRuleHover} onRuleLeave={onRuleLeave} onRuleOpen={onRuleOpen} />;
}

function ChatList({
	title,
	chats,
	selectedChatId,
	onSelect,
	onToggle,
	actionLabel,
}: {
	title: string;
	chats: readonly ChatDto[];
	selectedChatId: string | null;
	onSelect: (chatId: string) => void;
	onToggle: (chatId: string) => void;
	actionLabel: string;
}) {
	return (
		<section className="flex flex-col gap-1">
			<h3 className="text-sm font-semibold">{title}</h3>
			{chats.length === 0 ? (
				<p className="text-muted-foreground text-xs">пусто</p>
			) : (
				<ul className="flex flex-col gap-1">
					{chats.map((chat) => (
						<li key={chat.id} className="rounded-md border p-2 text-sm">
							<button
								type="button"
								className={`w-full cursor-pointer text-left ${selectedChatId === chat.id ? "font-semibold" : ""}`}
								onClick={() => onSelect(chat.id)}
							>
								<p>{chat.params.model}</p>
								<p className="text-muted-foreground text-xs">{formatMoment(chat.lastMessageAt)}</p>
							</button>
							<Button variant="ghost" size="sm" className="mt-1 h-auto px-0 text-xs" onClick={() => onToggle(chat.id)}>
								{actionLabel}
							</Button>
						</li>
					))}
				</ul>
			)}
		</section>
	);
}

function RulesFilters({
	filters,
	onChange,
}: {
	filters: RuleFilters;
	onChange: (next: RuleFilters) => void;
}) {
	return (
		<div className="grid grid-cols-1 gap-2 md:grid-cols-2">
			<label className="flex flex-col gap-1 text-sm">
				<span>Характер действия</span>
				<select
					aria-label="Характер действия"
					className="rounded-md border px-2 py-1.5"
					value={filters.character}
					onChange={(event) => onChange({ ...filters, character: event.target.value })}
				>
					<option value="">все</option>
					<option value="risk-mode">risk-mode</option>
					<option value="profit-target">profit-target</option>
					<option value="profit-protection">profit-protection</option>
					<option value="risk-reduction">risk-reduction</option>
					<option value="rolling">rolling</option>
					<option value="entry">entry</option>
					<option value="exit">exit</option>
					<option value="futures-leg">futures-leg</option>
					<option value="rebuild-dismantle">rebuild-dismantle</option>
					<option value="other">other</option>
				</select>
			</label>
			<label className="flex flex-col gap-1 text-sm">
				<span>Чёткость</span>
				<select
					aria-label="Чёткость"
					className="rounded-md border px-2 py-1.5"
					value={filters.clarity}
					onChange={(event) => onChange({ ...filters, clarity: event.target.value })}
				>
					<option value="">все</option>
					<option value="crisp">crisp</option>
					<option value="fuzzy">fuzzy</option>
				</select>
			</label>
			<label className="flex flex-col gap-1 text-sm">
				<span>Субъект</span>
				<select
					aria-label="Субъект"
					className="rounded-md border px-2 py-1.5"
					value={filters.subject}
					onChange={(event) => onChange({ ...filters, subject: event.target.value })}
				>
					<option value="">все</option>
					<option value="construction">construction</option>
					<option value="portfolio">portfolio</option>
				</select>
			</label>
			<label className="flex flex-col gap-1 text-sm">
				<span>Поиск правил</span>
				<input
					aria-label="Поиск правил"
					className="rounded-md border px-2 py-1.5"
					value={filters.search}
					onChange={(event) => onChange({ ...filters, search: event.target.value })}
				/>
			</label>
		</div>
	);
}

function RulePopupCard({ card, title }: { card: AgentRuleCard; title: string }) {
	return (
		<article className="flex flex-col gap-2 text-sm">
			<h3 className="font-semibold">{title}</h3>
			<p className="text-base font-medium">{card.title}</p>
			<p className="text-muted-foreground text-xs">
				{card.id} · {card.character} · {card.clarity} · {card.subject}
			</p>
			{card.triggerDescription !== null && (
				<p>
					<b>Триггер:</b> {card.triggerDescription}
				</p>
			)}
			{card.actionDescription !== null && (
				<p>
					<b>Действие:</b> {card.actionDescription}
				</p>
			)}
			{card.thresholds.length > 0 && (
				<ul className="list-inside list-disc">
					{card.thresholds.map((threshold) => (
						<li key={`${threshold.name}-${threshold.unit}`}>
							{threshold.name}: {threshold.value} {threshold.unit}
						</li>
					))}
				</ul>
			)}
		</article>
	);
}
