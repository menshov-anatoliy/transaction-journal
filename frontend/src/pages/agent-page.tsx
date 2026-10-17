import { ArrowUp, BookOpen, Bot, ChevronDown, Globe, LineChart, Link, Plus, Search, type LucideIcon } from "lucide-react";
import * as React from "react";
import { useSearchParams } from "react-router";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { AgentChatThread } from "@/chat/components/agent-chat-thread";
import { chatParamsFromForm, CHAT_DATA_SOURCES, DEFAULT_CHAT_MODEL } from "@/chat/chat-params";
import { useCompleteChatMutation, useResumeChatMutation } from "@/chat/chat-mutations";
import { chatKeys } from "@/chat/chat-queries";
import { createChat, listChats } from "@/chat/api/chat-api";
import { useAgentChatRuntime } from "@/chat/runtime/use-agent-chat-runtime";
import type { ChatDataSource, ChatDto } from "@/chat/types";
import { SessionItem, SourceChip } from "@/components/design";
import { usePageHeader } from "@/components/layout/page-header";
import { RuleFullCard, RuleBriefPopup, RULE_CHARACTER_LABELS, RULE_CLARITY_LABELS, RULE_SUBJECT_LABELS, ruleCharacterLabel } from "@/components/agent/rule-cards";
import { Button } from "@/components/ui/button";
import { listRules, readRule } from "@/lib/api/agent-rules";
import { useConstructionsOverviewQuery } from "@/lib/api/constructions-overview-query";
import { formatMoment } from "@/lib/format/display-time";
import { useIsMobile } from "@/lib/use-mobile";
import { cn } from "@/lib/utils";

// Раздел «Агент» маршрута /agent по концепции §7: список сессий
// active/completed, композер нового чата в пустом состоянии с
// карточками-подсказками, лента чата на инфраструктуре 4.1,
// read-only каталог правил с фильтрами/поиском и попап карточки правила
// при наведении из следа источников.
// Таб «Чаты» перенесён на мастера D811M «Экран/Агент · Чаты (новый)»
// (change rework-contextual-topbar): список сессий — левая колонка,
// пустое состояние с композером — центр, disabled-таб «Сводки · soon».
// Traceability: change:rework-contextual-topbar/tasks#6.0
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

const RULE_CHARACTER_ORDER = Object.keys(RULE_CHARACTER_LABELS);

/*
	Карточки-подсказки пустого состояния (мастер D811M «Карточки-подсказки»,
	220, r12, pad 14): иконки мастера — search/book-open/line-chart 16
	$accentStrong, титул 12.5/600 $textPrimary, пример 11.5 $textMuted в
	кавычках-ёлочках; в композер вставляется текст примера без кавычек.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:rework-contextual-topbar/tasks#6.0
const CHAT_HINT_CARDS: ReadonlyArray<{ icon: LucideIcon; title: string; example: string }> = [
	{ icon: Search, title: "Спросите о конструкции", example: "Какой риск у железной бабочки ETH?" },
	{ icon: BookOpen, title: "Проверьте правило корпуса", example: "Что говорит корпус про голый стреддл?" },
	{ icon: LineChart, title: "Сводка по рынку", example: "Покажи премии опционов BTC на неделю" },
];

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
	const [searchParams] = useSearchParams();
	// Ссылка из полной карточки источника открывает каталог с выбранным правилом.
	// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
	const initialRuleId = searchParams.get("rule");
	const [tab, setTab] = React.useState<AgentTab>(initialRuleId === null ? "chats" : "rules");
	const [mobileHistoryOpen, setMobileHistoryOpen] = React.useState(false);
	// Режим нового чата (раунд-сверка 6.0): «+ Новый чат» гасит автовыбор
	// первого чата, пока пользователь не выберет сессию или не создаст чат.
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	const [forceNewChat, setForceNewChat] = React.useState(false);
	const [selectedChatId, setSelectedChatId] = React.useState<string | null>(null);
	const [hoveredRuleId, setHoveredRuleId] = React.useState<string | null>(null);
	const [openedRuleId, setOpenedRuleId] = React.useState<string | null>(initialRuleId);
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
	// Поле композера: карточки-подсказки вставляют пример и возвращают сюда фокус.
	const composerInputRef = React.useRef<HTMLTextAreaElement | null>(null);

	// Сегмент «страница» топбара (раунд-сверка 6.0): крошка «Журнал / Агент»
	// и бейдж «Входящих» приходят из fallback конфигурации разделов, а
	// заголовок «Агент · Чаты» регистрирует вкладка чатов; чипы страница
	// не задаёт, контекст не регистрирует — контекстная панель и сегмент
	// контекста скрыты. Вкладка «Правила» пока остаётся на fallback
	// «Агент»: состав её шапки решит раунд-сверка 8.0.
	// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	const pageHeaderRegistration = React.useMemo(
		() => ({ title: tab === "chats" ? "Агент · Чаты" : undefined }),
		[tab],
	);
	usePageHeader(pageHeaderRegistration);

	const activeQuery = useQuery({ queryKey: chatKeys.list("active"), queryFn: ({ signal }) => listChats("active", signal) });
	const completedQuery = useQuery({ queryKey: chatKeys.list("completed"), queryFn: ({ signal }) => listChats("completed", signal) });

	// Имена конструкций для бейджей списка сессий: резолв по привязке чата
	// из кэша обзора конструкций — имя принадлежит агрегату конструкций,
	// ChatDto его не дублирует; при переименовании бейджи обновляются сами.
	// Traceability: openspec:ui/screens#scenario-ui-agent-session-shows-construction-name
	// Traceability: change:agent-chat-list-recognition/design#D3
	const constructionsQuery = useConstructionsOverviewQuery();
	const constructionNames = React.useMemo(() => {
		const names = new Map<string, string>();
		for (const item of constructionsQuery.data?.items ?? [])
			names.set(String(item.constructionId), item.name);
		return names;
	}, [constructionsQuery.data]);

	const activeChats = activeQuery.data ?? [];
	const completedChats = completedQuery.data ?? [];
	// Режим нового чата (раунд-сверка 6.0): при активном «+ Новый чат»
	// автовыбор первого чата погашен и центр показывает пустое состояние.
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	const selectedChat = forceNewChat
		? null
		: ([...activeChats, ...completedChats].find((chat) => chat.id === selectedChatId) ?? activeChats[0] ?? completedChats[0] ?? null);

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

	// «+ Новый чат» (раунд-сверка 6.0): сбрасывает выбранный чат и композер
	// — поле, привязку к конструкции и источники к дефолту (модель не
	// трогаем) — и показывает пустое состояние; существующие сессии
	// остаются в списке.
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	const onNewChat = () => {
		setForceNewChat(true);
		setSelectedChatId(null);
		setFirstMessage("");
		setConstructionIdDraft("");
		setSourceSelections({ journal: true, "rules-corpus": true, market: true });
	};

	// Выбор сессии снимает режим нового чата — центр показывает ленту чата.
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	const onSelectSession = (chatId: string) => {
		setForceNewChat(false);
		setSelectedChatId(chatId);
	};

	// Клик по карточке-подсказке (раунд-сверка 6.0) вставляет текст примера
	// в поле композера и возвращает в него фокус.
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	const onHintSelect = (example: string) => {
		setFirstMessage(example);
		composerInputRef.current?.focus();
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
					{/* Таб «Сводки» — disabled-заглушка мастера D811M (TabSummaries,
						12.5/normal $textMuted): раздел сводок ещё не реализован;
						состав сегмента страницы для него решит раунд-сверка 8.0. */}
					{/* Traceability: change:rework-contextual-topbar/tasks#6.0 */}
					<button
						type="button"
						disabled
						aria-disabled="true"
						className="cursor-not-allowed rounded-[7px] px-3.5 py-1.5 text-[12.5px] text-text-muted outline-none"
					>
						Сводки · soon
					</button>
				</nav>
				{tab === "rules" && rulesQuery.data !== undefined && (
					<span className="text-xs text-text-muted">
						корпус · {rulesQuery.data.total} {ruleCountWord(rulesQuery.data.total)} · только чтение
					</span>
				)}
			</header>

			{tab === "chats" ? (
				<div className="grid min-h-0 grid-cols-1 gap-4 xl:grid-cols-[22rem_minmax(0,1fr)]">
					{/*
						Левая колонка мастера D811M — «Список сессий» (dy6On):
						карточка $surface r12 pad 12 с primary-кнопкой «+ Новый
						чат» (инстанс NewChat: pad [8,12], на всю ширину), капсами
						«АКТИВНЫЕ»/«ЗАВЕРШЁННЫЕ» (10/0.5 $textMuted) и пунктами
						примитива «Сессия/пункт» (s9J3h) с завершением/продолжением.
						Форма «Новый чат» и правый aside «История чатов» удалены по
						решению раунд-сверки 6.0: их функции перешли композеру
						центра и этому списку — createChat/listChats, завершение и
						продолжение сохранены; ширина колонки — 22rem текущей сетки.
					*/}
					{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
					{/* Traceability: change:rework-contextual-topbar/tasks#6.0 */}
					{isMobile == false && (
						<section aria-label="Список сессий" className="flex min-h-[30rem] flex-col gap-2 rounded-xl bg-card p-3">
							<SessionsList
								createPending={createMutation.isPending}
								onCreateChat={onNewChat}
								pendingLists={activeQuery.isPending || completedQuery.isPending}
								activeChats={activeChats}
								completedChats={completedChats}
								activeError={activeQuery.isError ? activeQuery.error : null}
								completedError={completedQuery.isError ? completedQuery.error : null}
								constructionNames={constructionNames}
								selectedChatId={selectedChat?.id ?? null}
								onSelect={onSelectSession}
								onComplete={(chatId) => completeMutation.mutate(chatId)}
								onResume={(chatId) => resumeMutation.mutate(chatId)}
							/>
						</section>
					)}

					<div className="flex min-h-[30rem] min-w-0 flex-col">
						{/* На мобильном список сессий доступен drawer-шторкой (группа 10
							меняет мобильную механику, подход сохранён). */}
						{isMobile && (
							<Button variant="outline" size="sm" className="mb-2 self-start" onClick={() => setMobileHistoryOpen(true)}>
								Список сессий
							</Button>
						)}
						{selectedChat === null ? (
							<div className="flex min-h-0 flex-1 flex-col items-center justify-end gap-5 px-4 pb-2 pt-6 sm:px-10 xl:px-20">
								{/*
									Пустое состояние мастера D811M: знак 52×52 r16
									$accentSoft с bot 26 $accentStrong, титул «Чем помочь
									с журналом?» 20/600 $textPrimary и подпись 13
									$textSecondary; центр мастера — колонка с выравниванием
									по низу и центру (justify-end/items-center, gap 20,
									боковые паддинги 80 на десктопе).
								*/}
								{/* Traceability: change:rework-contextual-topbar/tasks#6.0 */}
								<div className="flex flex-col items-center gap-3.5">
									<span aria-hidden="true" className="flex size-[52px] items-center justify-center rounded-[16px] bg-accent-soft">
										<Bot className="size-[26px] text-accent-strong" />
									</span>
									<h2 className="text-[20px] font-semibold text-text-primary">Чем помочь с журналом?</h2>
									<p className="text-[13px] text-text-secondary">
										Новый чат создаётся первым сообщением — параметры можно задать сразу.
									</p>
								</div>
								{/*
									Карточки-подсказки мастера: клик вставляет текст примера
									в композер и фокусирует поле (раунд-сверка 6.0).
								*/}
								{/* Traceability: change:rework-contextual-topbar/tasks#6.0 */}
								<div className="flex flex-wrap justify-center gap-3">
									{CHAT_HINT_CARDS.map((card) => (
										<button
											key={card.title}
											type="button"
											className="flex w-[220px] cursor-pointer flex-col items-start gap-2 rounded-xl bg-card p-3.5 text-left outline-none transition-colors hover:bg-surface-2 focus-visible:ring-[3px] focus-visible:ring-ring/50"
											onClick={() => onHintSelect(card.example)}
										>
											<card.icon aria-hidden="true" className="size-4 text-accent-strong" />
											<span className="text-[12.5px] font-semibold text-text-primary">{card.title}</span>
											<span className="text-[11.5px] leading-[1.4] text-text-muted">«{card.example}»</span>
										</button>
									))}
								</div>
								{/*
									Композер мастера Z14sH «Композер/Агент»: рамка $surface
									с каймой $border, r14, pad 14, колонка gap 8 —
									многострочное поле (базовые ~2 строки, авторост,
									плейсхолдер dkwDP 13.5 $textMuted) и тулбар gap 8:
									чип привязки bCLlQ (infoSoft, пилюля, link 12 $info),
									чип модели aF5vM (surface2, r8, 12/500 + шеврон), чипы
									источников fYadZ с кружком «+» (плейсхолдер мастера:
									справочник источников закрыт тремя категориями) и
									кнопка отправки 36×36 r10 $accent со стрелкой вверх.
									Отправка — прежняя команда createChat с параметрами
									формы (модель/привязка/источники).
								*/}
								{/* Traceability: openspec:ui/design-system#requirement-design-composer-multiline */}
								{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
								{/* Traceability: change:rework-contextual-topbar/tasks#6.0 */}
								<section aria-label="Композер нового чата" className="flex w-full max-w-[640px] flex-col gap-2 rounded-[14px] border bg-card p-3.5">
									<textarea
										ref={composerInputRef}
										aria-label="Первое сообщение"
										className="field-sizing-content max-h-40 min-h-[44px] resize-none bg-transparent text-[13.5px] text-foreground outline-none placeholder:text-text-muted"
										placeholder="Спросите агента о конструкции, правилах или рынке…"
										value={firstMessage}
										onChange={(event) => setFirstMessage(event.target.value)}
									/>
									<div className="flex flex-wrap items-center gap-2">
										<span className="relative block shrink-0">
											<Link
												aria-hidden="true"
												className="pointer-events-none absolute left-2.5 top-1/2 size-3 -translate-y-1/2 text-info"
											/>
											<input
												aria-label="Опциональная конструкция"
												className="w-36 rounded-full bg-info-soft py-[5px] pl-7 pr-2.5 text-[12px] text-text-secondary outline-none placeholder:text-text-secondary"
												placeholder="Конструкция"
												value={constructionIdDraft}
												onChange={(event) => setConstructionIdDraft(event.target.value)}
											/>
										</span>
										<span className="relative block shrink-0">
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
										{/*
											Источники данных — примитив «Чип/Источник» (fYadZ):
											клик по чипу переключает выбор (toggle-кнопка с
											aria-pressed), выбранный источник несёт вид мастера;
											невыбранный приглушается до textMuted. Пояснение
											источника — в подсказке title.
										*/}
										{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
										{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
										<div role="group" aria-label="Источники данных" className="flex flex-wrap items-center gap-1.5">
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
											{/* Кружок «+» — плейсхолдер мастера AddSrc (26, r999,
												plus 12 $textMuted): справочник источников закрыт —
												ровно три категории, элемент декоративный. */}
											<span
												aria-hidden="true"
												title="Справочник источников закрыт — журнал, корпус правил и рынок"
												className="inline-flex size-[26px] items-center justify-center rounded-full border bg-card"
											>
												<Plus className="size-3 text-text-muted" />
											</span>
										</div>
										<Button
											type="button"
											size="icon"
											aria-label="Отправить"
											title="Отправить"
											className="ms-auto size-9 rounded-[10px]"
											disabled={createMutation.isPending || firstMessage.trim() === ""}
											onClick={onCreateChat}
										>
											<ArrowUp className="size-4" />
										</Button>
									</div>
									{createMutation.isError && (
										<p className="text-destructive text-sm" role="alert">
											Не удалось создать чат: {createMutation.error.message}
										</p>
									)}
								</section>
							</div>
						) : (
							<section className="flex min-h-[30rem] min-w-0 flex-col rounded-lg border p-3">
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
							</section>
						)}
					</div>

					{isMobile && mobileHistoryOpen && (
						<div className="fixed inset-0 z-40">
							<button type="button" aria-label="Закрыть список сессий" className="absolute inset-0 bg-black/40" onClick={() => setMobileHistoryOpen(false)} />
							<aside className="bg-background absolute right-0 top-0 h-full w-[min(26rem,100vw)] overflow-y-auto border-l p-3">
								{/*
									Drawer списка сессий (раунд-сверка 6.0, механика Mobile —
									группа 10): подход сохранён — тот же оверлей с правой
									шторкой; вместо «Истории чатов» внутри тот же состав,
									что у левой колонки: «+ Новый чат», капсы и пункты
									сессий с завершением/продолжением.
								*/}
								{/* Traceability: change:rework-contextual-topbar/tasks#6.0 */}
								<div className="mb-2 flex items-center justify-between">
									<h2 className="text-base font-semibold">Список сессий</h2>
									<Button variant="outline" size="sm" onClick={() => setMobileHistoryOpen(false)}>
										Закрыть
									</Button>
								</div>
								<SessionsList
									createPending={createMutation.isPending}
									onCreateChat={() => {
										onNewChat();
										setMobileHistoryOpen(false);
									}}
									pendingLists={activeQuery.isPending || completedQuery.isPending}
									activeChats={activeChats}
									completedChats={completedChats}
									activeError={activeQuery.isError ? activeQuery.error : null}
									completedError={completedQuery.isError ? completedQuery.error : null}
									constructionNames={constructionNames}
									selectedChatId={selectedChat?.id ?? null}
									onSelect={(chatId) => {
										onSelectSession(chatId);
										setMobileHistoryOpen(false);
									}}
									onComplete={(chatId) => completeMutation.mutate(chatId)}
									onResume={(chatId) => resumeMutation.mutate(chatId)}
								/>
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
						{/* Прямая ссылка на источник различает загрузку, недоступность и отсутствие выбора. */}
						{/* Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent */}
						{openedRuleId !== null && openedRuleQuery.isError ? (
							<p className="text-destructive text-sm" role="alert">Правило недоступно: {openedRuleQuery.error.message}</p>
						) : openedRuleQuery.data !== undefined ? (
							<RuleFullCard rule={openedRuleQuery.data} detail={openedRuleQuery.data} />
						) : openedRuleId !== null ? (
							<p className="text-muted-foreground text-sm" role="status">Чтение полной карточки правила…</p>
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

/*
	Список сессий мастера D811M (dy6On): primary-кнопка «+ Новый чат»
	(инстанс NewChat: pad [8,12], на всю ширину), капсы статусных групп и
	пункты примитива «Сессия/пункт» с завершением/продолжением. Один состав
	используется в левой колонке и в мобильном drawer; смена выбора и
	создание чата сохраняют прежнюю логику createChat/listChats.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:rework-contextual-topbar/tasks#6.0
function SessionsList({
	createPending,
	onCreateChat,
	pendingLists,
	activeChats,
	completedChats,
	activeError,
	completedError,
	constructionNames,
	selectedChatId,
	onSelect,
	onComplete,
	onResume,
}: {
	createPending: boolean;
	onCreateChat: () => void;
	pendingLists: boolean;
	activeChats: readonly ChatDto[];
	completedChats: readonly ChatDto[];
	activeError: Error | null;
	completedError: Error | null;
	/** Имена конструкций из кэша обзора: резолв бейджа по привязке чата. */
	constructionNames: ReadonlyMap<string, string>;
	selectedChatId: string | null;
	onSelect: (chatId: string) => void;
	onComplete: (chatId: string) => void;
	onResume: (chatId: string) => void;
}) {
	return (
		<>
			<Button disabled={createPending} className="w-full px-3" onClick={onCreateChat}>
				+ Новый чат
			</Button>
			{pendingLists ? (
				<p className="text-muted-foreground text-sm">чтение сессий…</p>
			) : (
				<>
					<ChatList
						title="Активные"
						chats={activeChats}
						constructionNames={constructionNames}
						selectedChatId={selectedChatId}
						onSelect={onSelect}
						onToggle={onComplete}
						actionLabel="Завершить чат"
					/>
					<ChatList
						title="Завершённые"
						chats={completedChats}
						constructionNames={constructionNames}
						selectedChatId={selectedChatId}
						onSelect={onSelect}
						onToggle={onResume}
						actionLabel="Продолжить чат"
					/>
				</>
			)}
			{activeError !== null && (
				<p className="text-destructive text-sm" role="alert">
					Активные чаты недоступны: {activeError.message}
				</p>
			)}
			{completedError !== null && (
				<p className="text-destructive text-sm" role="alert">
					Завершённые чаты недоступны: {completedError.message}
				</p>
			)}
		</>
	);
}

function ChatList({
	title,
	chats,
	constructionNames,
	selectedChatId,
	onSelect,
	onToggle,
	actionLabel,
}: {
	title: string;
	chats: readonly ChatDto[];
	/** Имена конструкций из кэша обзора: резолв бейджа по привязке чата. */
	constructionNames: ReadonlyMap<string, string>;
	selectedChatId: string | null;
	onSelect: (chatId: string) => void;
	onToggle: (chatId: string) => void;
	actionLabel: string;
}) {
	// Свежие пункты вверху группы: сортировка по моменту последнего
	// сообщения по убыванию внутри каждой статусной группы.
	// Traceability: openspec:ui/screens#requirement-ui-agent-sessions-newest-first
	// Traceability: change:agent-chat-list-recognition/design#D4
	const sortedChats = React.useMemo(() => sortChatsByFreshness(chats), [chats]);
	// Разделение по привязке: привязанные чаты — подгруппа «Конструкции»,
	// портфельные — «Портфель»; обе подгруппы вычисляются в каждой статусной
	// группе по привязке чата (params.constructionId).
	// Traceability: openspec:ui/screens#requirement-ui-agent-sessions-split-by-binding
	// Traceability: change:agent-chat-list-recognition/design#D5
	const constructionChats = sortedChats.filter((chat) => chat.params.constructionId != null);
	const portfolioChats = sortedChats.filter((chat) => chat.params.constructionId == null);
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
				<>
					{/*
						Разделение группы по привязке: подгруппы «Портфель» и
						«Конструкции» со счётчиками в каждой статусной группе;
						пустая подгруппа показывается с нулевым счётчиком, чтобы
						разделение было предсказуемым, а чат конструкции искался
						в коротком перечне, не в общем списке.
					*/}
					{/* Traceability: openspec:ui/screens#requirement-ui-agent-sessions-split-by-binding */}
					{/* Traceability: change:agent-chat-list-recognition/design#D5 */}
					<ChatSubgroup
						heading="Портфель"
						count={portfolioChats.length}
						chats={portfolioChats}
						constructionNames={constructionNames}
						selectedChatId={selectedChatId}
						onSelect={onSelect}
						onToggle={onToggle}
						actionLabel={actionLabel}
					/>
					<ChatSubgroup
						heading="Конструкции"
						count={constructionChats.length}
						chats={constructionChats}
						constructionNames={constructionNames}
						selectedChatId={selectedChatId}
						onSelect={onSelect}
						onToggle={onToggle}
						actionLabel={actionLabel}
					/>
				</>
			)}
		</section>
	);
}

/*
	Подгруппа привязки внутри статусной группы: заголовок со счётчиком и
	перечень пунктов сессий в порядке свежести; вёрстка подгруппы не
	отличается от вёрстки статусной группы.
*/
// Traceability: openspec:ui/screens#requirement-ui-agent-sessions-split-by-binding
// Traceability: change:agent-chat-list-recognition/design#D5
function ChatSubgroup({
	heading,
	count,
	chats,
	constructionNames,
	selectedChatId,
	onSelect,
	onToggle,
	actionLabel,
}: {
	heading: string;
	count: number;
	chats: readonly ChatDto[];
	/** Имена конструкций из кэша обзора: резолв бейджа по привязке чата. */
	constructionNames: ReadonlyMap<string, string>;
	selectedChatId: string | null;
	onSelect: (chatId: string) => void;
	onToggle: (chatId: string) => void;
	actionLabel: string;
}) {
	return (
		<div className="flex flex-col gap-1">
			<h4 className="flex items-baseline gap-1 text-[10px] font-normal tracking-[0.5px] text-text-muted uppercase">
				<span>{heading}</span>
				{/* Счётчик подгруппы: нулевой счётчик виден наравне с ненулевым. */}
				<span className="text-[10px]">· {count}</span>
			</h4>
			{chats.length === 0 ? null : (
				<ul className="flex flex-col gap-1">
					{chats.map((chat) => (
						<li key={chat.id}>
							{/*
								Пункт списка сессий — примитив «Сессия/пункт»
								(мастер s9J3h, инстансы S1–S4 Body #3): заголовок —
								модель чата, время — момент последнего сообщения;
								превью — текст первого сообщения владельца одной
								строкой (ChatDto.firstMessagePreview), бейдж — имя
								привязанной конструкции. Активная сессия — заливка
								surface2 по инстансу Cur. Выбор сессии — кликом по
								пункту, поведение сохранено.
							*/}
							{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
							{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
							<SessionItem
								active={selectedChatId === chat.id}
								title={chat.params.model}
								time={formatMoment(chat.lastMessageAt)}
								badge={constructionLabel(chat, constructionNames)}
								onClick={() => onSelect(chat.id)}
							>
								{chat.firstMessagePreview === "" ? null : chat.firstMessagePreview}
							</SessionItem>
							<Button variant="ghost" size="sm" className="mt-0.5 h-auto justify-start px-0 text-xs" onClick={() => onToggle(chat.id)}>
								{actionLabel}
							</Button>
						</li>
					))}
				</ul>
			)}
		</div>
	);
}

/*
	Порядок статусной группы по свежести: сортировка по моменту последнего
	сообщения по убыванию выполняется на фронте, контракт бэкенда (OrderBy Id)
	не меняется; при равном моменте устойчивая сортировка сохраняет серверный
	порядок по возрастанию Id.
*/
// Traceability: openspec:ui/screens#requirement-ui-agent-sessions-newest-first
// Traceability: change:agent-chat-list-recognition/design#D4
function sortChatsByFreshness(chats: readonly ChatDto[]): ChatDto[] {
	return [...chats].sort((left, right) => new Date(right.lastMessageAt).getTime() - new Date(left.lastMessageAt).getTime());
}

/*
	Бейдж привязки пункта сессии: имя конструкции резолвится из кэша обзора
	конструкций по params.constructionId; неразрешённый id деградирует до
	«конструкция {id}» (как в панели чатов карточки), а портфельный чат
	бейджа не получает вовсе — признак конструкции отсутствует.
*/
// Traceability: openspec:ui/screens#scenario-ui-agent-session-shows-construction-name
// Traceability: openspec:ui/screens#scenario-ui-agent-session-portfolio-no-binding-badge
// Traceability: change:agent-chat-list-recognition/design#D3
function constructionLabel(chat: ChatDto, names: ReadonlyMap<string, string>): string | null {
	if (chat.params.constructionId == null)
		return null;
	return names.get(chat.params.constructionId) ?? `конструкция ${chat.params.constructionId}`;
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
