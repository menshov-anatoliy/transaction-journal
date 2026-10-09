import { ChevronDown, ExternalLink, Globe, Link, Search } from "lucide-react";
import * as React from "react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { AgentChatThread } from "@/chat/components/agent-chat-thread";
import { chatParamsFromForm, CHAT_DATA_SOURCES, DEFAULT_CHAT_MODEL } from "@/chat/chat-params";
import { useCompleteChatMutation, useResumeChatMutation } from "@/chat/chat-mutations";
import { chatKeys } from "@/chat/chat-queries";
import { createChat, listChats } from "@/chat/api/chat-api";
import { useAgentChatRuntime } from "@/chat/runtime/use-agent-chat-runtime";
import type { ChatDataSource, ChatDto } from "@/chat/types";
import { SessionItem, SourceChip, StatusChip } from "@/components/design";
import { Button } from "@/components/ui/button";
import { listRules, readRule, type AgentRuleCard, type AgentRuleListItem } from "@/lib/api/agent-rules";
import { formatMoment } from "@/lib/format/display-time";
import { useIsMobile } from "@/lib/use-mobile";
import { cn } from "@/lib/utils";

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

/*
	Подписи таксономии корпуса правил для пилюль-атрибутов и чипов фильтров
	мастера Body #7: характер — тот же закрытый справочник десяти значений,
	что и в подсказках (HintCharacterLabels); чёткость crisp/fuzzy — пара
	«Однозначное»/«Формальное» мастера; субъект construction/portfolio —
	«конструкция»/«журнал». Незнакомое значение показывается как есть.
*/
const RULE_CHARACTER_LABELS: Readonly<Record<string, string>> = {
	"risk-mode": "лимиты и режим риска",
	"profit-target": "цель по прибыли",
	"profit-protection": "защита прибыли",
	"risk-reduction": "снижение риска",
	rolling: "роллирование",
	entry: "возможность входа",
	exit: "возможность выхода",
	"futures-leg": "фьючерсная нога",
	"rebuild-dismantle": "перестройка и разборка",
	other: "прочее",
};

const RULE_CHARACTER_ORDER = Object.keys(RULE_CHARACTER_LABELS);

const RULE_CLARITY_LABELS: Readonly<Record<string, string>> = {
	crisp: "Однозначное",
	fuzzy: "Формальное",
};

const RULE_SUBJECT_LABELS: Readonly<Record<string, string>> = {
	construction: "конструкция",
	portfolio: "журнал",
};

function ruleCharacterLabel(character: string): string {
	return RULE_CHARACTER_LABELS[character] ?? character;
}

function ruleClarityLabel(clarity: string): string {
	return RULE_CLARITY_LABELS[clarity] ?? clarity;
}

function ruleSubjectWord(subject: string): string {
	return RULE_SUBJECT_LABELS[subject] ?? subject;
}

/** Русская форма множественного числа для счётчика корпуса (нода Cnt). */
function ruleCountWord(count: number): string {
	const mod10 = count % 10;
	const mod100 = count % 100;
	if (mod10 === 1 && mod100 !== 11)
		return "правило";
	if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
		return "правила";
	return "правил";
}

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

	/*
		Тело карточки каталога («Карточка правила/Полная», нода Body GjcwT)
		приходит полной карточкой GET /rules/{id}: список каталога несёт
		только атрибуты, а описание — часть мастера. Запросы идут по одному
		на правило кэшем react-query и включаются только на вкладке правил.
	*/
	const ruleItems = rulesQuery.data?.items ?? [];
	const ruleCardQueries = useQueries({
		queries: ruleItems.map((rule) => ({
			queryKey: ["agent-rule-card", rule.id],
			queryFn: ({ signal }) => readRule(rule.id, signal),
			enabled: tab === "rules",
		})),
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
		<section className="flex min-h-0 flex-col gap-3.5 p-6">
			{/*
				Шапка мастера Body #7 (SAYaX «Заголовок и табы», инвариантна
				Body #3): титул «Агент» 21/600 + сегмент-контейнер табов z5Fqgz
				($surface, кайма $border, радиус 9, паддинги [3], гэп 4) и
				счётчик корпуса zYZRw «корпус · N правил · только чтение»
				12/normal $textMuted. Активный таб (k42Hl) — заливка $accentSoft,
				радиус 7, [6,14], 12.5/600 $accentStrong; неактивный (VBxP8) —
				12.5/normal $textSecondary. Секции accentSoft мастера — активный
				таб и выбранные чипы фильтров (4 заливки $accentSoft Body #7).
			*/}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<header className="flex flex-wrap items-center gap-4">
				<h1 className="page-title">Агент</h1>
				<nav className="inline-flex items-center gap-1 rounded-[9px] border bg-card p-[3px]" aria-label="Разделы агента">
					<button
						type="button"
						aria-pressed={tab === "chats"}
						className={cn(
							"cursor-pointer rounded-[7px] px-3.5 py-1.5 text-[12.5px] transition-colors outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
							tab === "chats" ? "bg-accent-soft font-semibold text-accent-strong" : "text-text-secondary hover:text-foreground",
						)}
						onClick={() => setTab("chats")}
					>
						Чаты
					</button>
					<button
						type="button"
						aria-pressed={tab === "rules"}
						className={cn(
							"cursor-pointer rounded-[7px] px-3.5 py-1.5 text-[12.5px] transition-colors outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
							tab === "rules" ? "bg-accent-soft font-semibold text-accent-strong" : "text-text-secondary hover:text-foreground",
						)}
						onClick={() => setTab("rules")}
					>
						Правила
					</button>
				</nav>
				{tab === "rules" && rulesQuery.data !== undefined && (
					<span className="text-xs text-text-muted">
						корпус · {rulesQuery.data.total} {ruleCountWord(rulesQuery.data.total)} · только чтение
					</span>
				)}
			</header>

			{tab === "chats" ? (
				<div className="grid min-h-0 grid-cols-1 gap-4 xl:grid-cols-[22rem_minmax(0,1fr)_24rem]">
					{/*
						Форма нового чата собрана по мастеру Body #3 — композеру
						«Композер/Агент» (Z14sH): поле модели — чип aF5vM
						(surface2, радиус 8, паддинги [5,10], подпись 12/500 +
						шеврон textMuted), поле привязки к конструкции — чип
						bCLlQ (infoSoft, пилюля 999, иконка link 12 $info),
						ввод сообщения — плейсхолдер dkwDP (13.5/normal
						$textMuted). Нативные select/input/textarea остаются
						управляемыми полями формы, но оформлены по этим
						дизайн-нодам вместо сырых rounded-md-контролов.
					*/}
					{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
					{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
					{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
					<section className="flex flex-col gap-3 rounded-lg border p-3">
						<h2 className="text-base font-semibold">Новый чат</h2>
						<label className="flex flex-col gap-1">
							<span className="text-[12.5px] font-semibold text-foreground">Модель</span>
							<span className="relative block">
								<select
									aria-label="Модель"
									className="w-full cursor-pointer appearance-none rounded-sm bg-secondary px-2.5 py-[5px] pr-8 text-[12px] font-medium text-foreground outline-none"
									value={model}
									onChange={(event) => setModel(event.target.value)}
								>
									<option value={DEFAULT_CHAT_MODEL}>{DEFAULT_CHAT_MODEL}</option>
									<option value="glm-5.3-flash">glm-5.3-flash</option>
								</select>
								<ChevronDown
									aria-hidden="true"
									className="pointer-events-none absolute right-2.5 top-1/2 size-3 -translate-y-1/2 text-text-muted"
								/>
							</span>
						</label>
						<label className="flex flex-col gap-1">
							<span className="text-[12.5px] font-semibold text-foreground">Опциональная конструкция</span>
							<span className="relative block">
								<Link
									aria-hidden="true"
									className="pointer-events-none absolute left-2.5 top-1/2 size-3 -translate-y-1/2 text-info"
								/>
								<input
									aria-label="Опциональная конструкция"
									className="w-full rounded-full bg-info-soft py-[5px] pl-7 pr-2.5 text-[12px] text-text-secondary outline-none placeholder:text-text-muted"
									placeholder="например, 7"
									value={constructionIdDraft}
									onChange={(event) => setConstructionIdDraft(event.target.value)}
								/>
							</span>
						</label>
						{/*
							Источники данных — примитив «Чип/Источник» (fYadZ)
							вместо чекбоксов-филдсета (§3.4:2 аудита): инстансы
							композера Body #3 — «журнал» и «корпус правил» с
							иконкой check, «рынок Bybit» — с globe. Клик по чипу
							переключает выбор (toggle-кнопка с aria-pressed),
							выбранный источник несёт вид мастера — иконка
							accent; невыбранный приглушается до textMuted.
							Пояснение источника — в подсказке title.
						*/}
						{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
						{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
						<div role="group" aria-label="Источники данных" className="flex flex-col gap-1.5">
							<span aria-hidden="true" className="text-[12.5px] font-semibold text-foreground">
								Источники данных
							</span>
							<div className="flex flex-wrap gap-1.5">
								{CHAT_DATA_SOURCES.map((source) => (
									<button
										key={source.id}
										type="button"
										aria-pressed={sourceSelections[source.id]}
										title={source.hint}
										className="cursor-pointer rounded-full outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
										onClick={() =>
											setSourceSelections((current) => ({ ...current, [source.id]: current[source.id] == false }))
										}
									>
										<SourceChip
											icon={source.id === "market" ? Globe : undefined}
											selected={sourceSelections[source.id]}
										>
											{/* Подписи инстансов мастера — со строчной первой буквы:
												«журнал», «корпус правил», «рынок Bybit». */}
											{source.label[0].toLowerCase() + source.label.slice(1)}
										</SourceChip>
									</button>
								))}
							</div>
						</div>
						<label className="flex flex-col gap-1">
							<span className="text-[12.5px] font-semibold text-foreground">Первое сообщение</span>
							<textarea
								aria-label="Первое сообщение"
								rows={4}
								className="rounded-sm border bg-card px-2.5 py-[7px] text-[13.5px] text-foreground outline-none placeholder:text-text-muted"
								placeholder="Спросите агента о конструкции, правилах или рынке…"
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
								<div className="relative min-h-0 flex-1 overflow-hidden">
									<ChatThreadPane
										chat={selectedChat}
										onRuleHover={(ruleId) => setHoveredRuleId(ruleId)}
										onRuleLeave={() => setHoveredRuleId(null)}
										onRuleOpen={(ruleId) => setOpenedRuleId(ruleId)}
									/>
									{/*
										Попап при наведении на карточку правила в следе источников —
										мастер «Попап правила/Краткий» (Lpcap, инстанс IBheJ Body #4
										плавает поверх ленты, layoutPosition absolute): геометрия и
										состав — в RuleBriefPopup; поведение наведения сохранено.
									*/}
									{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
									{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
									{hoveredRuleQuery.data !== undefined && <RuleBriefPopup card={hoveredRuleQuery.data} />}
								</div>
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
				<section className="grid grid-cols-1 items-start gap-3 xl:grid-cols-[minmax(0,1fr)_26rem]">
					{/*
						Вкладка «Правила» перенесена с мастера Body #7 (R3kdzS,
						расхождение §3.8:1 аудита): вместо строк списка li/p-2 —
						сетка «Каталог» (cRRlh: ряды по 2 карточки, гэп 12) с
						«Карточкой правила/Полной» (reusable Zes7z). Поиск (Q7Ur0)
						и фильтры-чипы (viuZI) — геометрия мастера на доменной
						таксономии корпуса (Non-Goals: данные на месте). Поведение
						сохранено: клик по заголовку открывает полную карточку в
						правой панели, ссылка футера — в новом окне.
					*/}
					{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
					{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
					{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
					<div className="flex min-w-0 flex-col gap-3.5">
						{/*
							Поиск мастера Q7Ur0: $surface, кайма $border, радиус 10,
							паддинги [10,14], иконка search 15×15 $textMuted слева,
							плейсхолдер 13/normal $textMuted.
						*/}
						<div className="flex items-center gap-2.5 rounded-[10px] border bg-card px-3.5 py-2.5">
							<Search aria-hidden="true" className="size-[15px] shrink-0 text-text-muted" />
							<input
								aria-label="Поиск правил"
								className="min-w-0 flex-1 bg-transparent text-[13px] text-foreground outline-none placeholder:text-text-muted"
								placeholder="Поиск по корпусу правил…"
								value={rulesFilters.search}
								onChange={(event) => setRulesFilters({ ...rulesFilters, search: event.target.value })}
							/>
						</div>

						{/*
							Фильтры мастера viuZI: группы «Характер/Чёткость/Субъект»
							(гэп 16), в группе метка 11.5/normal $textMuted (гэп 7) и
							пилюли-чипы (радиус 999, [4,10], 11.5): выбранный —
							$accentSoft/$accentStrong 600 (секция accentSoft мастера),
							невыбранный — $surface/$border $textSecondary. Состав
							опций — доменная таксономия корпуса.
						*/}
						<div className="flex flex-col gap-2.5">
							<FilterChipGroup
								label="Характер:"
								value={rulesFilters.character}
								options={RULE_CHARACTER_ORDER.map((value) => ({ value, label: ruleCharacterLabel(value) }))}
								onChange={(character) => setRulesFilters({ ...rulesFilters, character })}
							/>
							<FilterChipGroup
								label="Чёткость:"
								value={rulesFilters.clarity}
								options={Object.entries(RULE_CLARITY_LABELS).map(([value, label]) => ({ value, label }))}
								onChange={(clarity) => setRulesFilters({ ...rulesFilters, clarity })}
							/>
							<FilterChipGroup
								label="Субъект:"
								value={rulesFilters.subject}
								options={Object.entries(RULE_SUBJECT_LABELS).map(([value, label]) => ({
									value,
									label: label[0].toUpperCase() + label.slice(1),
								}))}
								onChange={(subject) => setRulesFilters({ ...rulesFilters, subject })}
							/>
						</div>

						{rulesQuery.isPending && <p className="text-muted-foreground text-sm">чтение правил…</p>}
						{rulesQuery.isError && (
							<p className="text-destructive text-sm" role="alert">
								Правила недоступны: {rulesQuery.error.message}
							</p>
						)}
						{rulesQuery.data !== undefined && (
							<ul className="grid max-h-[34rem] items-start gap-3 overflow-y-auto sm:grid-cols-2">
								{rulesQuery.data.items.map((rule, index) => (
									<li key={rule.id}>
										<RuleFullCard rule={rule} detail={ruleCardQueries[index]?.data} onOpen={() => setOpenedRuleId(rule.id)} />
									</li>
								))}
							</ul>
						)}
					</div>
					<div>
						{openedRuleQuery.data !== undefined ? (
							<RuleFullCard rule={openedRuleQuery.data} detail={openedRuleQuery.data} />
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
			{/*
				Заголовок группы — капс мастера Body #3 (ноды Cap1 «АКТИВНЫЕ» /
				Cap2 «ЗАВЕРШЁННЫЕ» фрейма dy6On «Список сессий»): 10/normal,
				letter-spacing 0.5, $textMuted.
			*/}
			<h3 className="text-[10px] font-normal tracking-[0.5px] text-text-muted uppercase">{title}</h3>
			{chats.length === 0 ? (
				<p className="text-muted-foreground text-xs">пусто</p>
			) : (
				<ul className="flex flex-col gap-1">
					{chats.map((chat) => (
						<li key={chat.id}>
							{/*
								Пункт списка сессий — примитив «Сессия/пункт»
								(мастер s9J3h, инстансы S1–S4 Body #3): заголовок —
								модель чата, время — момент последнего сообщения;
								превью ChatDto чат-слоя не несёт, активная сессия —
								заливка surface2 по инстансу Cur. Выбор сессии —
								кликом по пункту, поведение сохранено.
							*/}
							{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
							{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
							<SessionItem
								active={selectedChatId === chat.id}
								title={chat.params.model}
								time={formatMoment(chat.lastMessageAt)}
								onClick={() => onSelect(chat.id)}
							/>
							<Button variant="ghost" size="sm" className="mt-0.5 h-auto justify-start px-0 text-xs" onClick={() => onToggle(chat.id)}>
								{actionLabel}
							</Button>
						</li>
					))}
				</ul>
			)}
		</section>
	);
}

/*
	Группа фильтров-чипов мастера viuZI (инвариант euAq5 «G:Характер:»):
	метка L 11.5/normal $textMuted + пилюли C:* (радиус 999, [4,10], 11.5).
	Выбранный чип — заливка/кайма $accentSoft, текст $accentStrong 600
	(инстанс Ib62d «C:Все»); невыбранный — $surface/$border $textSecondary
	(инстанс DwR5J «C:Мягкое»). Выбор эксклюзивен в группе, «Все» снимает.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
function FilterChipGroup({
	label,
	value,
	options,
	onChange,
}: {
	label: string;
	value: string;
	options: ReadonlyArray<{ value: string; label: string }>;
	onChange: (next: string) => void;
}) {
	return (
		<div className="flex flex-wrap items-center gap-[7px]">
			<span className="text-[11.5px] text-text-muted">{label}</span>
			<FilterChip label="Все" active={value === ""} onClick={() => onChange("")} />
			{options.map((option) => (
				<FilterChip
					key={option.value}
					label={option.label}
					active={value === option.value}
					onClick={() => onChange(option.value)}
				/>
			))}
		</div>
	);
}

function FilterChip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
	return (
		<button
			type="button"
			aria-pressed={active}
			className={cn(
				"cursor-pointer rounded-full border px-2.5 py-1 text-[11.5px] transition-colors outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
				active
					? "border-accent-soft bg-accent-soft font-semibold text-accent-strong"
					: "border-border bg-card font-normal text-text-secondary hover:text-foreground",
			)}
			onClick={onClick}
		>
			{label}
		</button>
	);
}

/*
	«Карточка правила/Полная» (reusable Zes7z, инстансы R:1–R:4 Body #7):
	$surface, кайма $border, радиус 12, паддинги 18, вертикальный гэп 12.
	Шапка HLLTo (гэп 10): заголовок 14.5/600 $textPrimary + id 11/normal
	$textMuted. Пилюли-атрибуты Meta V3si6 (гэп 8) — примитив «Чип/Статус»
	(aa6cK, радиус 999, [4,10], 11.5/500): характер — пара $riskSoft/$risk
	(инстанс nrPzY «Мягкое»; пара $negSoft/$neg «Жёсткое» не имеет аналога
	в таксономии корпуса), чёткость — $infoSoft/$info (U6mVZ
	«Однозначное»), субъект — surface2/$textSecondary (u9Vm0h). Тело
	GjcwT — 13/normal $textSecondary, межстрочный 1.55. Футер p4Ot2 (гэп
	6): иконка external-link 12×12 + ссылка 12 $accentStrong «Открыть в
	каталоге в новом окне».
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
function RuleFullCard({
	rule,
	detail,
	onOpen,
}: {
	rule: Pick<AgentRuleListItem, "id" | "title" | "character" | "clarity" | "subject">;
	detail?: AgentRuleCard;
	onOpen?: () => void;
}) {
	return (
		<article data-slot="rule-full-card" className="flex flex-col gap-3 rounded-lg border bg-card p-[18px]">
			<header className="flex items-center gap-2.5">
				{onOpen === undefined ? (
					<h3 className="min-w-0 grow text-[14.5px] leading-snug font-semibold text-foreground">{rule.title}</h3>
				) : (
					<button
						type="button"
						className="min-w-0 grow cursor-pointer text-left text-[14.5px] leading-snug font-semibold text-foreground underline-offset-2 outline-none focus-visible:underline hover:underline"
						onClick={onOpen}
					>
						{rule.title}
					</button>
				)}
				<span className="shrink-0 text-[11px] text-text-muted">{rule.id}</span>
			</header>
			<div className="flex flex-wrap gap-2">
				<StatusChip tone="risk">{ruleCharacterLabel(rule.character)}</StatusChip>
				<StatusChip tone="info">{ruleClarityLabel(rule.clarity)}</StatusChip>
				<StatusChip tone="neutral">Субъект: {ruleSubjectWord(rule.subject)}</StatusChip>
			</div>
			{detail !== undefined && <RuleCardBody detail={detail} full={onOpen === undefined} />}
			<footer className="flex items-center gap-1.5">
				<ExternalLink aria-hidden="true" className="size-3 shrink-0 text-accent-strong" />
				<a
					href={`/spa/agent?rule=${encodeURIComponent(rule.id)}`}
					target="_blank"
					rel="noreferrer"
					className="text-xs text-accent-strong underline-offset-2 hover:underline"
				>
					Открыть в каталоге в новом окне
				</a>
			</footer>
		</article>
	);
}

/*
	Тело карточки: в каталоге — краткое содержание мастера GjcwT (одно
	описание действия/триггера), в правой панели — полные данные правила
	(триггер, действие, пороги, источники) той же типографикой мастера
	13/1.55 $textSecondary — доменное расширение состава карточки при
	сохранении вида мастера (Non-Goals: состав данных на месте).
*/
function RuleCardBody({ detail, full }: { detail: AgentRuleCard; full: boolean }) {
	const gist = detail.actionDescription ?? detail.triggerDescription;
	if (full === false) {
		return gist !== null ? <p className="text-[13px] leading-[1.55] text-text-secondary">{gist}</p> : null;
	}

	return (
		<div className="flex flex-col gap-2 text-[13px] leading-[1.55] text-text-secondary">
			{detail.triggerDescription !== null && (
				<p>
					<b>Триггер:</b> {detail.triggerDescription}
				</p>
			)}
			{detail.actionDescription !== null && (
				<p>
					<b>Действие:</b> {detail.actionDescription}
				</p>
			)}
			{detail.thresholds.length > 0 && (
				<ul className="list-disc pl-4">
					{detail.thresholds.map((threshold) => (
						<li key={`${threshold.name}-${threshold.unit}`}>
							{threshold.name}: {threshold.value} {threshold.unit}
						</li>
					))}
				</ul>
			)}
			{detail.sources.length > 0 && (
				<ul className="flex flex-col gap-1">
					{detail.sources.map((source) => (
						<li key={`${source.tag}-${source.file}`} className="text-[11px] text-text-muted">
							<span className="rounded bg-surface-2 px-1 py-0.5 font-medium">{source.tag}</span> {source.file}
						</li>
					))}
				</ul>
			)}
		</div>
	);
}

/*
	«Попап правила/Краткий» (reusable Lpcap, инстанс IBheJ Body #4 плавает
	над лентой чата): $surface, кайма $border, радиус 10, паддинги 12,
	гэп 7, тень мастера 0 8 24 #17171E20 ($textPrimary при 12% непрозрач-
	ности — значение эффекта мастера, отдельного токена нет). Заголовок
	oYr9W — 12.5/600 $textPrimary; мета rlwtL — 11/normal $textMuted
	«характер · чёткость · субъект · id»; суть lidNU — 12/normal
	$textSecondary, межстрочный 1.45.
*/
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
function RuleBriefPopup({ card }: { card: AgentRuleCard }) {
	const gist = card.actionDescription ?? card.triggerDescription;
	return (
		<aside
			aria-label="Карточка правила (hover из чата)"
			className="absolute right-3 top-3 z-10 flex w-[280px] flex-col gap-[7px] rounded-[10px] border bg-card p-3 shadow-[0_8px_24px] shadow-text-primary/12"
		>
			<p className="text-[12.5px] font-semibold text-foreground">{card.title}</p>
			<p className="text-[11px] text-text-muted">
				{ruleCharacterLabel(card.character)} · {ruleClarityLabel(card.clarity)} · {ruleSubjectWord(card.subject)} · {card.id}
			</p>
			{gist !== null && <p className="text-xs leading-[1.45] text-text-secondary">{gist}</p>}
		</aside>
	);
}
