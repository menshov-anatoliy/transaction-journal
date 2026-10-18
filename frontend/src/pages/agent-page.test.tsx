import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useMemo, useState } from "react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ContextPanelRegistration } from "@/components/layout/context-panel";
import type { PageHeaderRegistration } from "@/components/layout/page-header";
import type { AppOutletContext } from "@/components/layout/outlet-context";
import { AgentPage } from "./agent-page";

// Раздел «Агент» по концепции §7: форма нового чата, списки active/completed,
// лента выбранного чата и read-only каталог правил с фильтрами и поиском.
// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

const chatsApi = vi.hoisted(() => ({
	listChats: vi.fn(),
	createChat: vi.fn(),
	completeChat: vi.fn(),
	resumeChat: vi.fn(),
	deleteChat: vi.fn(),
	listChatMessages: vi.fn(),
	streamChatReply: vi.fn(),
}));

const rulesApi = vi.hoisted(() => ({
	listRules: vi.fn(),
	readRule: vi.fn(),
}));

// Обзор конструкций: источник имён для бейджей привязки списка сессий.
const constructionsApi = vi.hoisted(() => ({
	fetchConstructionsOverview: vi.fn(),
}));

vi.mock("@/chat/api/chat-api", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/chat/api/chat-api")>()),
	listChats: chatsApi.listChats,
	createChat: chatsApi.createChat,
	completeChat: chatsApi.completeChat,
	resumeChat: chatsApi.resumeChat,
	deleteChat: chatsApi.deleteChat,
	listChatMessages: chatsApi.listChatMessages,
	streamChatReply: chatsApi.streamChatReply,
}));

vi.mock("@/lib/api/agent-rules", () => ({
	listRules: rulesApi.listRules,
	readRule: rulesApi.readRule,
}));

vi.mock("@/lib/api/constructions", () => ({
	fetchConstructionsOverview: constructionsApi.fetchConstructionsOverview,
}));

/*
	Стенд регистраций сегментов каркаса: вне AppLayout хуки usePageHeader и
	useContextPanel превращаются в no-op, поэтому стенд подменяет
	Outlet-контекст каркаса и рендерит зарегистрированные шапку страницы и
	контекст-панель — так страница проверяет состав регистраций без каркаса.
*/
// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
// Traceability: change:rework-contextual-topbar/tasks#8.0
function TopbarRegistrationOutlet() {
	const [header, setHeader] = useState<PageHeaderRegistration | null>(null);
	const [context, setContext] = useState<ContextPanelRegistration | null>(null);
	const [mobileFocus, setMobileFocus] = useState(false);
	const outletContext = useMemo<AppOutletContext>(
		() => ({
			pageHeader: { setRegistration: setHeader },
			contextPanel: { setRegistration: setContext },
			mobileFocus: { setActive: setMobileFocus },
		}),
		[],
	);
	return (
		<>
			<div data-testid="page-header-registration">
				{header?.title != null && <span data-testid="registered-page-title">{header.title}</span>}
				{header?.breadcrumbTail != null && <span data-testid="registered-crumb-tail">{header.breadcrumbTail}</span>}
			</div>
			{/* Индикатор запроса фокусного мобильного хрома: страница просит
			    каркас скрыть мобильный топбар (раунд-сверка 10.0). */}
			{/* Traceability: change:rework-contextual-topbar/tasks#10.0 */}
			<div data-testid="registered-mobile-focus" data-active={mobileFocus} />
			<Outlet context={outletContext} />
			{context !== null && (
				<div data-testid="registered-context-panel" data-chip-label={context.chipLabel}>
					{context.content}
				</div>
			)}
		</>
	);
}

function renderPage(path = "/agent") {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<MemoryRouter initialEntries={[path]}>
				<Routes>
					<Route path="/" element={<TopbarRegistrationOutlet />}>
						<Route path="agent" element={<AgentPage />} />
					</Route>
				</Routes>
			</MemoryRouter>
		</QueryClientProvider>,
	);
}

// Контейнер подгруппы привязки ищется от заголовка со счётчиком — в нём
// проверяется перечень пунктов именно этой подгруппы.
function subgroupContainer(section: HTMLElement, name: RegExp): HTMLElement {
	return within(section).getByRole("heading", { name }).closest("div") as HTMLElement;
}

/** Мобильный вьюпорт 390 (мастера iGlhI/OvuY9): matchMedia подменяется до рендера. */
function setViewport(width: number) {
	Object.defineProperty(window, "innerWidth", { value: width, writable: true, configurable: true });
	window.matchMedia = vi.fn().mockImplementation((query: string) => ({
		matches: query.includes("max-width") ? width <= 1023 : false,
		media: query,
		onchange: null,
		addEventListener: vi.fn(),
		removeEventListener: vi.fn(),
		addListener: vi.fn(),
		removeListener: vi.fn(),
		dispatchEvent: vi.fn(),
	})) as typeof window.matchMedia;
}

beforeEach(() => {
	vi.clearAllMocks();
	// Обзор конструкций по умолчанию пуст: существующие сценарии работают
	// с портфельными чатами, бейджи привязки им не нужны.
	constructionsApi.fetchConstructionsOverview.mockResolvedValue({ items: [] });
	chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
		status === "active"
			? [
					{
						id: "chat-1",
						status: "active",
						params: { model: "GLM-5.3", constructionId: null, sources: ["journal", "rules-corpus", "market"] },
						createdAt: "2026-10-08T09:00:00Z",
						lastMessageAt: "2026-10-08T09:01:00Z",
						firstMessagePreview: "Оцени риск стреддла",
					},
				]
			: [],
	);
	chatsApi.listChatMessages.mockResolvedValue([]);
	chatsApi.createChat.mockResolvedValue({
		id: "chat-new",
		status: "active",
		params: { model: "GLM-5.3", constructionId: null, sources: ["journal", "rules-corpus", "market"] },
		createdAt: "2026-10-08T09:02:00Z",
		lastMessageAt: "2026-10-08T09:02:00Z",
	});
	chatsApi.completeChat.mockResolvedValue({ id: "chat-1", status: "completed" });
	chatsApi.resumeChat.mockResolvedValue({ id: "chat-1", status: "active" });
	chatsApi.deleteChat.mockResolvedValue(undefined);

	rulesApi.listRules.mockResolvedValue({
		items: [
			{
				id: "ac-01",
				title: "Лимиты риска на период",
				character: "risk-mode",
				clarity: "crisp",
				subject: "construction",
			},
		],
		total: 1,
	});

	// Полная карточка правила (GET /rules/{id}) для тела карточек каталога,
	// правой панели и краткого попапа из чата.
	rulesApi.readRule.mockImplementation(async (ruleId: string) => ({
		id: ruleId,
		title: "Лимиты риска на период",
		character: "risk-mode",
		clarity: "crisp",
		subject: "construction",
		status: "active",
		scope: "open-constructions",
		technique: null,
		triggerDescription: "Убыток недели приближается к лимиту периода 1% капитала.",
		actionDescription: "Остановить наращивание риска до конца периода.",
		thresholds: [{ name: "weeklyRiskLimit", value: "1", unit: "percent" }],
		sources: [{ tag: "ПИ", file: "vanna.md", quotes: ["лимит недели"] }],
	}));
});

describe("страница агента", () => {
	// Переход из источника в новом окне раскрывает именно выбранное правило:
	// полная карточка открывается в контекст-панели каркаса с чипом
	// «Правило · <код правила>» (раунд-сверка 8.0, мастер ZUmpr).
	// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
	// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
	// Traceability: change:rework-contextual-topbar/tasks#8.0
	it("TryIfRuleLinkOpensCatalogWithFullCard", async () => {
		renderPage("/agent?rule=ac-01");
		const panel = await screen.findByTestId("registered-context-panel");
		expect(panel).toHaveAttribute("data-chip-label", "Правило · ac-01");
		expect(await within(panel).findByText("Триггер:")).toBeInTheDocument();
		expect(within(panel).getByText("лимит недели")).toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Правила" })).toHaveAttribute("aria-pressed", "true");
		expect(rulesApi.readRule).toHaveBeenCalledWith("ac-01", expect.any(AbortSignal));
	});

	// Недоступность источника по прямой ссылке не маскируется приглашением
	// выбрать правило: сбой чтения показывается состоянием ошибки внутри
	// контекст-панели, чип кода при этом сохраняется.
	// Traceability: change:rework-contextual-topbar/tasks#8.0
	it("ThrowOnRuleLinkReadFailure", async () => {
		rulesApi.readRule.mockRejectedValue(new Error("HTTP 404"));
		renderPage("/agent?rule=missing");
		const panel = await screen.findByTestId("registered-context-panel");
		expect(panel).toHaveAttribute("data-chip-label", "Правило · missing");
		expect(await within(panel).findByRole("alert")).toHaveTextContent("Правило недоступно: HTTP 404");
	});

	// Шапка вкладки «Правила» (мастер ZUmpr, текст cbGOV): страница
	// регистрирует заголовок «Правила» и хвост трёхуровневой крошки —
	// сегмент страницы показывает «Журнал / Агент / Правила».
	// Traceability: openspec:ui/screens#scenario-frame-page-header-breadcrumb-tail
	// Traceability: change:rework-contextual-topbar/tasks#8.0
	it("TryIfRulesTabRegistersTitleAndBreadcrumbTail", async () => {
		const user = userEvent.setup();
		renderPage();

		// Act: переход на вкладку «Правила».
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		// Assert: регистрация шапки — заголовок и хвост крошки «Правила».
		expect(await screen.findByTestId("registered-page-title")).toHaveTextContent("Правила");
		expect(screen.getByTestId("registered-crumb-tail")).toHaveTextContent("Правила");
	});

	// Контекст-панель вкладки правил (мастер ZUmpr, сегмент ot9ii disabled
	// без выбора): без выбора панель не регистрируется; клик по заголовку
	// карточки каталога открывает правило в панели с чипом кода; уход на
	// вкладку чатов снимает регистрацию — панель скрывается.
	// Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
	// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
	// Traceability: change:rework-contextual-topbar/tasks#8.0
	it("TryIfRulesContextRegistersOnlyWithSelectionAndClearsOnChatsTab", async () => {
		const user = userEvent.setup();
		renderPage();
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		// Assert: без выбора контекст не зарегистрирован.
		expect(screen.queryByTestId("registered-context-panel")).not.toBeInTheDocument();

		// Act: клик по заголовку карточки каталога — выбор правила.
		await user.click(await screen.findByText(/Лимиты риска на период/i, { selector: "button" }));

		// Assert: панель зарегистрирована с чипом кода и полной карточкой.
		const panel = await screen.findByTestId("registered-context-panel");
		expect(panel).toHaveAttribute("data-chip-label", "Правило · ac-01");
		expect(await within(panel).findByText("Триггер:")).toBeInTheDocument();

		// Act: возврат на вкладку чатов.
		await user.click(screen.getByRole("button", { name: "Чаты" }));

		// Assert: регистрация снята — панель скрыта.
		expect(screen.queryByTestId("registered-context-panel")).not.toBeInTheDocument();
	});
	/*
		Таб «Чаты» перенесён на мастера D811M (раунд-сверка 6.0): слева
		«Список сессий» с «+ Новый чат» и капсами, в центре пустое состояние
		с композером; вкладка правил сохраняет каталог Body #7.
	*/
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	it("показывает список сессий, композер и каталог правил", async () => {
		const user = userEvent.setup();
		renderPage();

		expect(await screen.findByRole("heading", { name: "Агент" })).toBeInTheDocument();
		// Титул раздела — Inter 21/600 дизайн-системы, не дефолтный text-2xl.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		expect(screen.getByRole("heading", { name: "Агент", level: 1 }).className).toContain("page-title");
		expect(screen.getByLabelText(/модель/i)).toBeInTheDocument();
		// Источники данных — группа чипов в тулбаре композера.
		expect(screen.getByRole("group", { name: /источники данных/i })).toBeInTheDocument();
		// Кнопка «+ Новый чат» — primary левой колонки «Список сессий».
		expect(screen.getByRole("button", { name: "+ Новый чат" })).toBeInTheDocument();
		expect(await screen.findByRole("heading", { name: /активные/i })).toBeInTheDocument();
		expect(screen.getAllByRole("button", { name: /завершить чат/i }).length).toBeGreaterThan(0);
		await user.click(screen.getByRole("button", { name: /правила/i }));
		expect(await screen.findByText(/Лимиты риска на период/i)).toBeInTheDocument();
	});

	it("рендерит сессии примитивом SessionItem, сохраняя выбор и завершение (§3.4:1)", async () => {
		// Arrange: активный чат один; завершённый — с другой моделью, чтобы
		// пункты списка различимы по доступному имени.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? [
						{
							id: "chat-1",
							status: "active",
							params: { model: "GLM-5.3", constructionId: null, sources: ["journal", "rules-corpus", "market"] },
							createdAt: "2026-10-08T09:00:00Z",
							lastMessageAt: "2026-10-08T09:01:00Z",
						},
					]
				: [
						{
							id: "chat-2",
							status: "completed",
							params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-07T09:00:00Z",
							lastMessageAt: "2026-10-07T09:30:00Z",
						},
					],
		);
		const user = userEvent.setup();
		renderPage();

		// Assert: активная сессия — пункт мастера s9J3h (инстанс Cur):
		// заливка surface2 и доступная метка текущей сессии.
		const activeItem = await screen.findByRole("button", { name: /GLM-5\.3/ });
		expect(activeItem.getAttribute("aria-current")).toBe("true");
		expect(activeItem.className).toContain("bg-surface-2");
		expect(activeItem.className).toContain("rounded-md");

		// Assert: время последнего сообщения видно в пункте (нода SqhBW).
		expect(activeItem.textContent).toMatch(/\d{4}-\d{2}-\d{2} \d{2}:\d{2}/);

		// Assert: завершённая сессия — тем же примитивом, без активной заливки.
		const completedItem = screen.getByRole("button", { name: /glm-5\.3-flash/ });
		expect(completedItem.className).not.toContain("bg-surface-2");

		// Act: выбор завершённой сессии кликом по пункту.
		await user.click(completedItem);

		// Assert: выбранная сессия стала текущей (инстанс Cur).
		await waitFor(() => {
			expect(completedItem.getAttribute("aria-current")).toBe("true");
		});

		// Act: завершение активного чата кнопкой из списка.
		await user.click(screen.getByRole("button", { name: /завершить чат/i }));

		// Assert: команда завершения ушла выбранной сессии.
		await waitFor(() => {
			expect(chatsApi.completeChat).toHaveBeenCalledWith("chat-1");
		});
	});

	/*
		Опознаваемость пункта сессии: чат с привязкой показывает имя конструкции,
		разрешённое из кэша обзора конструкций по params.constructionId; id без
		записи в обзоре деградирует до «конструкция {id}», а портфельный чат
		бейджа не получает вовсе.
	*/
	// Traceability: openspec:ui/screens#scenario-ui-agent-session-shows-construction-name
	// Traceability: openspec:ui/screens#scenario-ui-agent-session-portfolio-no-binding-badge
	// Traceability: change:agent-chat-list-recognition/design#D3
	it("TryIfBoundSessionShowsConstructionNameAndPortfolioStaysWithoutBadge", async () => {
		// Arrange: активные чаты — привязанный к известной конструкции 8,
		// привязанный к неразрешённой 99 и портфельный; обзор знает имя 8.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? [
						{
							id: "chat-bound",
							status: "active",
							params: { model: "GLM-5.3", constructionId: "8", sources: ["journal"] },
							createdAt: "2026-10-08T09:00:00Z",
							lastMessageAt: "2026-10-08T09:01:00Z",
							firstMessagePreview: "Оцени риск стреддла",
						},
						{
							id: "chat-orphan",
							status: "active",
							params: { model: "glm-4-air", constructionId: "99", sources: ["journal"] },
							createdAt: "2026-10-08T08:00:00Z",
							lastMessageAt: "2026-10-08T08:05:00Z",
							firstMessagePreview: "Разбери сделку",
						},
						{
							id: "chat-portfolio",
							status: "active",
							params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-08T07:00:00Z",
							lastMessageAt: "2026-10-08T07:30:00Z",
							firstMessagePreview: "Сводка по портфелю за неделю",
						},
					]
				: [],
		);
		constructionsApi.fetchConstructionsOverview.mockResolvedValue({
			summary: {},
			items: [{ constructionId: 8, name: "Стреддл BTC" }],
		});
		renderPage();

		// Assert: привязанный пункт показывает имя конструкции из обзора бейджем.
		const boundItem = await screen.findByRole("button", { name: /GLM-5\.3/ });
		const boundBadge = within(boundItem).getByText("Стреддл BTC");
		expect(boundBadge.closest('[data-slot="session-item-badge"]')).not.toBeNull();

		// Assert: неразрешённый id даёт fallback «конструкция 99» вместо имени.
		const orphanItem = screen.getByRole("button", { name: /glm-4-air/ });
		expect(within(orphanItem).getByText("конструкция 99")).toBeInTheDocument();

		// Assert: портфельный пункт без признака конструкции.
		const portfolioItem = screen.getByRole("button", { name: /glm-5\.3-flash/ });
		expect(portfolioItem.querySelector('[data-slot="session-item-badge"]')).toBeNull();
		expect(within(portfolioItem).queryByText(/конструкция/)).toBeNull();
	});

	/*
		Превью пункта сессии — текст первого сообщения владельца одной строкой:
		сервер отдаёт текст целиком, ужимание до строки происходит вёрсткой
		(truncate) на пункте списка.
	*/
	// Traceability: openspec:ui/screens#scenario-ui-agent-session-shows-preview
	// Traceability: change:agent-chat-list-recognition/design#D2
	it("TryIfSessionShowsFirstOwnerMessagePreviewTruncatedToLine", async () => {
		// Arrange: активный чат с длинным текстом первого сообщения владельца.
		const longPreview =
			"Очень длинный текст первого сообщения владельца, который обязан ужиматься до одной строки вёрсткой пункта списка сессий";
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? [
						{
							id: "chat-1",
							status: "active",
							params: { model: "GLM-5.3", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-08T09:00:00Z",
							lastMessageAt: "2026-10-08T09:01:00Z",
							firstMessagePreview: longPreview,
						},
					]
				: [],
		);
		renderPage();

		// Assert: превью первого сообщения владельца видно на пункте сессии.
		const item = await screen.findByRole("button", { name: /GLM-5\.3/ });
		const preview = within(item).getByText(longPreview);
		expect(preview.getAttribute("data-slot")).toBe("session-item-preview");

		// Assert: длинный текст ужимается вёрсткой до одной строки (truncate).
		expect(preview.className).toContain("truncate");
	});

	/*
		Свежесть сверху: внутри каждой статусной группы пункты упорядочены
		по моменту последнего сообщения по убыванию — выше расположен чат
		с более поздним моментом последнего сообщения; серверный порядок
		перечня (по возрастанию Id) порядок отображения не задаёт.
	*/
	// Traceability: openspec:ui/screens#scenario-ui-agent-sessions-recently-updated-first
	// Traceability: change:agent-chat-list-recognition/design#D4
	it("TryIfSessionsOrderedNewestFirstInBothStatusGroups", async () => {
		// Arrange: активные чаты приходят в серверном порядке по возрастанию Id
		// (утренний → свежий → дневной), завершённые — вперемешку; моменты
		// последнего сообщения у всех различаются.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? [
						{
							id: "chat-morning",
							status: "active",
							params: { model: "модель-утренняя", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-08T08:00:00Z",
							lastMessageAt: "2026-10-08T09:00:00Z",
							firstMessagePreview: "",
						},
						{
							id: "chat-fresh",
							status: "active",
							params: { model: "модель-свежая", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-08T09:00:00Z",
							lastMessageAt: "2026-10-08T10:00:00Z",
							firstMessagePreview: "",
						},
						{
							id: "chat-noon",
							status: "active",
							params: { model: "модель-дневная", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-08T08:30:00Z",
							lastMessageAt: "2026-10-08T09:30:00Z",
							firstMessagePreview: "",
						},
					]
				: [
						{
							id: "chat-yesterday",
							status: "completed",
							params: { model: "модель-вчерашняя", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-07T08:00:00Z",
							lastMessageAt: "2026-10-07T10:00:00Z",
							firstMessagePreview: "",
						},
						{
							id: "chat-evening",
							status: "completed",
							params: { model: "модель-вечерняя", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-07T08:30:00Z",
							lastMessageAt: "2026-10-07T09:00:00Z",
							firstMessagePreview: "",
						},
					],
		);
		renderPage();

		// Assert: в «Активные» сверху свежие: свежая (10:00) → дневная (09:30) →
		// утренняя (09:00), хотя сервер прислал свежий вторым.
		const activeSection = (await screen.findByRole("heading", { name: /активные/i })).closest("section") as HTMLElement;
		const activeItems = within(activeSection).getAllByRole("button", { name: /модель-/ });
		expect(activeItems).toHaveLength(3);
		expect(activeItems[0].textContent).toContain("модель-свежая");
		expect(activeItems[1].textContent).toContain("модель-дневная");
		expect(activeItems[2].textContent).toContain("модель-утренняя");

		// Assert: в «Завершённые» тот же порядок свежести: вчерашняя (10:00)
		// выше вечерней (09:00).
		const completedSection = screen.getByRole("heading", { name: /завершённые/i }).closest("section") as HTMLElement;
		const completedItems = within(completedSection).getAllByRole("button", { name: /модель-/ });
		expect(completedItems).toHaveLength(2);
		expect(completedItems[0].textContent).toContain("модель-вчерашняя");
		expect(completedItems[1].textContent).toContain("модель-вечерняя");
	});

	/*
		Короткий перечень привязанных: привязанные чаты собираются в подгруппу
		«Конструкции» со счётчиком, портфельные — в «Портфель»; чат конструкции
		ищется в коротком перечне подгруппы, не просматривая портфельные.
	*/
	// Traceability: openspec:ui/screens#scenario-ui-agent-sessions-binding-split-shortlist
	// Traceability: change:agent-chat-list-recognition/design#D5
	it("TryIfBoundChatFoundInShortConstructionSubgroupWithCounters", async () => {
		// Arrange: активные — два привязанных чата и портфельный между ними;
		// обзор конструкций знает имя для привязки 8.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? [
						{
							id: "chat-bound-1",
							status: "active",
							params: { model: "GLM-5.3", constructionId: "8", sources: ["journal"] },
							createdAt: "2026-10-08T09:00:00Z",
							lastMessageAt: "2026-10-08T09:01:00Z",
							firstMessagePreview: "Оцени риск стреддла",
						},
						{
							id: "chat-portfolio",
							status: "active",
							params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-08T08:00:00Z",
							lastMessageAt: "2026-10-08T08:30:00Z",
							firstMessagePreview: "Сводка по портфелю",
						},
						{
							id: "chat-bound-2",
							status: "active",
							params: { model: "glm-4-air", constructionId: "99", sources: ["journal"] },
							createdAt: "2026-10-08T07:00:00Z",
							lastMessageAt: "2026-10-08T07:30:00Z",
							firstMessagePreview: "Разбери сделку",
						},
					]
				: [],
		);
		constructionsApi.fetchConstructionsOverview.mockResolvedValue({
			summary: {},
			items: [{ constructionId: 8, name: "Стреддл BTC" }],
		});
		renderPage();

		// Assert: подгруппы статусной группы «Активные» со счётчиками:
		// «Конструкции · 2» и «Портфель · 1».
		const activeSection = (await screen.findByRole("heading", { name: /активные/i })).closest("section") as HTMLElement;
		expect(within(activeSection).getByRole("heading", { name: /Конструкции\s*·\s*2/ })).toBeInTheDocument();
		expect(within(activeSection).getByRole("heading", { name: /Портфель\s*·\s*1/ })).toBeInTheDocument();

		// Assert: чат конструкции найден в коротком перечне «Конструкции» —
		// оба привязанных пункта там, портфельный отсутствует.
		const boundSubgroup = subgroupContainer(activeSection, /Конструкции\s*·\s*2/);
		expect(within(boundSubgroup).getByRole("button", { name: /GLM-5\.3/ })).toBeInTheDocument();
		expect(within(boundSubgroup).getByRole("button", { name: /glm-4-air/ })).toBeInTheDocument();
		expect(within(boundSubgroup).queryByRole("button", { name: /glm-5\.3-flash/ })).toBeNull();

		// Assert: портфельный чат — в перечне «Портфель», там же без привязанных.
		const portfolioSubgroup = subgroupContainer(activeSection, /Портфель\s*·\s*1/);
		expect(within(portfolioSubgroup).getByRole("button", { name: /glm-5\.3-flash/ })).toBeInTheDocument();
		expect(within(portfolioSubgroup).queryByRole("button", { name: /GLM-5\.3/ })).toBeNull();
	});

	/*
		Разделение по привязке действует в обеих статусных группах: и в
		активных, и в завершённых портфельные и привязанные чаты различаются
		подгруппами; пустая подгруппа показывается с нулевым счётчиком.
	*/
	// Traceability: openspec:ui/screens#scenario-ui-agent-sessions-binding-split-both-statuses
	// Traceability: change:agent-chat-list-recognition/design#D5
	it("TryIfBindingSplitAppliesToBothStatusGroupsAndShowsZeroCounter", async () => {
		// Arrange: в активных один привязанный и один портфельный чат;
		// в завершённых только портфельный — подгруппа «Конструкции» показывается с нулём.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? [
						{
							id: "chat-bound",
							status: "active",
							params: { model: "GLM-5.3", constructionId: "8", sources: ["journal"] },
							createdAt: "2026-10-08T09:00:00Z",
							lastMessageAt: "2026-10-08T09:01:00Z",
							firstMessagePreview: "Оцени риск стреддла",
						},
						{
							id: "chat-portfolio",
							status: "active",
							params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-08T08:00:00Z",
							lastMessageAt: "2026-10-08T08:30:00Z",
							firstMessagePreview: "Сводка по портфелю",
						},
					]
				: [
						{
							id: "chat-done",
							status: "completed",
							params: { model: "glm-4-air", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-07T08:00:00Z",
							lastMessageAt: "2026-10-07T09:00:00Z",
							firstMessagePreview: "Итоги недели",
						},
					],
		);
		constructionsApi.fetchConstructionsOverview.mockResolvedValue({
			summary: {},
			items: [{ constructionId: 8, name: "Стреддл BTC" }],
		});
		renderPage();

		// Assert: активные — обе подгруппы со счётчиками 1/1, привязанный
		// и портфельный чаты в разных перечнях.
		const activeSection = (await screen.findByRole("heading", { name: /активные/i })).closest("section") as HTMLElement;
		expect(within(activeSection).getByRole("heading", { name: /Конструкции\s*·\s*1/ })).toBeInTheDocument();
		expect(within(activeSection).getByRole("heading", { name: /Портфель\s*·\s*1/ })).toBeInTheDocument();
		const boundActive = subgroupContainer(activeSection, /Конструкции\s*·\s*1/);
		expect(within(boundActive).getByRole("button", { name: /GLM-5\.3/ })).toBeInTheDocument();
		const portfolioActive = subgroupContainer(activeSection, /Портфель\s*·\s*1/);
		expect(within(portfolioActive).getByRole("button", { name: /glm-5\.3-flash/ })).toBeInTheDocument();

		// Assert: завершённые — разделение действует и здесь; пустая подгруппа
		// «Конструкции» показана с нулевым счётчиком, портфельный — в «Портфель».
		const completedSection = screen.getByRole("heading", { name: /завершённые/i }).closest("section") as HTMLElement;
		expect(within(completedSection).getByRole("heading", { name: /Конструкции\s*·\s*0/ })).toBeInTheDocument();
		expect(within(completedSection).getByRole("heading", { name: /Портфель\s*·\s*1/ })).toBeInTheDocument();
		const portfolioCompleted = subgroupContainer(completedSection, /Портфель\s*·\s*1/);
		expect(within(portfolioCompleted).getByRole("button", { name: /glm-4-air/ })).toBeInTheDocument();
	});

	/*
		Композер нового чата оформлен по мастеру Z14sH «Композер/Агент»
		(перенос D811M, раунд-сверка 6.0): модель — чип aF5vM (surface2,
		радиус 8, [5,10], 12/500, шеврон), привязка конструкции — чип bCLlQ
		(infoSoft, пилюля 999, иконка link $info), сообщение — многострочное
		поле 13.5/normal с плейсхолдером мастера; источники — чипы
		«Чип/Источник» с переключением выбора.
	*/
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	it("оформляет композер по дизайн-нодам мастера Z14sH", async () => {
		// Act: рендер вкладки «Чаты»; композер доступен в режиме нового чата.
		const user = userEvent.setup();
		renderPage();
		await screen.findByRole("heading", { name: "Оцени риск стреддла" });
		await user.click(screen.getByRole("button", { name: "+ Новый чат" }));
		await screen.findByText("Чем помочь с журналом?");

		// Assert: модель — чип aF5vM: surface2, радиус 8, паддинги [5,10], 12/500.
		const modelSelect = screen.getByLabelText(/модель/i);
		expect(modelSelect.className).toContain("bg-secondary");
		expect(modelSelect.className).toContain("rounded-sm");
		expect(modelSelect.className).toContain("px-2.5");
		expect(modelSelect.className).toContain("py-[5px]");
		expect(modelSelect.className).toContain("text-[12px]");
		expect(modelSelect.className).toContain("font-medium");
		expect(modelSelect.closest("span")?.querySelector("svg")?.getAttribute("class")).toContain("text-text-muted");

		// Assert: конструкция — чип bCLlQ: infoSoft, пилюля 999, link $info.
		const constructionInput = screen.getByLabelText(/опциональная конструкция/i);
		expect(constructionInput.className).toContain("bg-info-soft");
		expect(constructionInput.className).toContain("rounded-full");
		expect(constructionInput.className).toContain("text-[12px]");
		expect(constructionInput.closest("span")?.querySelector("svg")?.getAttribute("class")).toContain("text-info");

		// Assert: первое сообщение — плейсхолдер dkwDP мастера: 13.5/normal.
		const messageField = screen.getByLabelText(/первое сообщение/i);
		expect(messageField.className).toContain("text-[13.5px]");
		expect(messageField.getAttribute("placeholder")).toBe("Спросите агента о конструкции, правилах или рынке…");
	});

	it("переключает выбор источников чипами вместо чекбоксов (§3.4:2)", async () => {
		// Arrange: чат-источники по умолчанию выбраны (инвариант справочника);
		// композер доступен в режиме нового чата — автовыбор чата гасим.
		const user = userEvent.setup();
		renderPage();
		await screen.findByRole("heading", { name: "Оцени риск стреддла" });
		await user.click(screen.getByRole("button", { name: "+ Новый чат" }));
		await screen.findByText("Чем помочь с журналом?");

		// Assert: все три чипа — toggle-кнопки в выбранном состоянии мастера.
		const journalChip = screen.getByRole("button", { name: "журнал" });
		const rulesChip = screen.getByRole("button", { name: "корпус правил" });
		const marketChip = screen.getByRole("button", { name: "рынок Bybit" });
		for (const chip of [journalChip, rulesChip, marketChip]) {
			expect(chip.getAttribute("aria-pressed")).toBe("true");
			expect(chip.querySelector('[data-slot="source-chip"]')?.getAttribute("data-selected")).toBe("true");
		}
		expect(screen.queryByRole("checkbox")).toBeNull();

		// Act: снятие выбора рынка кликом по чипу.
		await user.click(marketChip);

		// Assert: чип погашен до textMuted, выбор снят.
		expect(marketChip.getAttribute("aria-pressed")).toBe("false");
		expect(marketChip.querySelector('[data-slot="source-chip"]')?.getAttribute("data-selected")).toBe("false");

		// Act: повторный клик возвращает выбор.
		await user.click(marketChip);
		expect(marketChip.getAttribute("aria-pressed")).toBe("true");
	});

	/*
		Пустое состояние мастера D811M (раунд-сверка 6.0): титул «Чем помочь
		с журналом?», подпись и три карточки-подсказки с примерами мастера;
		клик по карточке вставляет текст примера в поле композера (без
		кавычек-ёлочек) и возвращает в него фокус.
	*/
	// Traceability: openspec:ui/design-system#requirement-design-composer-multiline
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	it("TryIfHintCardInsertsExampleIntoComposerAndFocuses", async () => {
		// Arrange: автовыбор чата закрывает центр лентой; переход в режим
		// нового чата открывает пустое состояние с композером.
		const user = userEvent.setup();
		renderPage();
		await screen.findByRole("heading", { name: "Оцени риск стреддла" });
		await user.click(screen.getByRole("button", { name: "+ Новый чат" }));

		// Assert: пустое состояние мастера видно, пока чат не выбран.
		expect(await screen.findByText("Чем помочь с журналом?")).toBeInTheDocument();
		expect(screen.getByText("Новый чат создаётся первым сообщением — параметры можно задать сразу.")).toBeInTheDocument();
		expect(screen.getByText("«Какой риск у железной бабочки ETH?»")).toBeInTheDocument();
		expect(screen.getByText("«Что говорит корпус про голый стреддл?»")).toBeInTheDocument();
		expect(screen.getByText("«Покажи премии опционов BTC на неделю»")).toBeInTheDocument();

		// Act: клик по карточке-подсказке «Спросите о конструкции».
		await user.click(screen.getByRole("button", { name: /Спросите о конструкции/ }));

		// Assert: пример вставлен в поле композера, поле в фокусе.
		const messageField = screen.getByLabelText(/первое сообщение/i);
		expect(messageField).toHaveValue("Какой риск у железной бабочки ETH?");
		expect(messageField).toHaveFocus();
	});

	/*
		«+ Новый чат» (раунд-сверка 6.0): сбрасывает выбранный чат — центр
		показывает пустое состояние даже при наличии чатов — и очищает
		композер: поле, привязку к конструкции, источники к дефолту.
	*/
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	it("TryIfNewChatButtonResetsSelectionAndComposerToEmptyState", async () => {
		const user = userEvent.setup();
		renderPage();

		// Arrange: активный чат автовыбран (в центре его шапка); переходим
		// в режим нового чата и заполняем композер, рынок снят из источников.
		expect(await screen.findByRole("heading", { name: "Оцени риск стреддла" })).toBeInTheDocument();
		await user.click(screen.getByRole("button", { name: "+ Новый чат" }));
		await screen.findByText("Чем помочь с журналом?");
		await user.type(screen.getByLabelText(/первое сообщение/i), "Черновик сообщения");
		await user.type(screen.getByLabelText(/опциональная конструкция/i), "7");
		await user.click(screen.getByRole("button", { name: "рынок Bybit" }));
		expect(screen.getByRole("button", { name: "рынок Bybit" }).getAttribute("aria-pressed")).toBe("false");

		// Act: сброс кнопкой «+ Новый чат» левой колонки.
		await user.click(screen.getByRole("button", { name: "+ Новый чат" }));

		// Assert: пустое состояние вместо шапки чата, композер очищен,
		// источники вернулись к дефолту.
		expect(await screen.findByText("Чем помочь с журналом?")).toBeInTheDocument();
		expect(screen.queryByRole("heading", { name: "Оцени риск стреддла" })).not.toBeInTheDocument();
		expect(screen.getByLabelText(/первое сообщение/i)).toHaveValue("");
		expect(screen.getByLabelText(/опциональная конструкция/i)).toHaveValue("");
		expect(screen.getByRole("button", { name: "рынок Bybit" }).getAttribute("aria-pressed")).toBe("true");
	});

	/*
		Шапка активного чата — мастер qhGhn «ChatHead» (QP9Co, раунд-сверка
		7.0): колонка «Чат» прозрачна — карточная рамка секции убрана;
		заголовок — превью первого сообщения владельца
		(ChatDto.firstMessagePreview) 16/600 $textPrimary с truncate; рядом
		чип модели — инстанс «Чип/Источник» fYadZ (высота 25, заливка
		$surface, радиус 999, кайма $border, иконка cpu 12 $textSecondary,
		подпись 12/normal $textPrimary); строки «последнее сообщение» нет.
		Кнопки — ghost r8 12/500: «Завершить» ($textSecondary) и «Удалить»
		(текст $neg #D14B41).
	*/
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	// Traceability: change:rework-contextual-topbar/tasks#7.0
	it("оформляет шапку активного чата по мастеру ChatHead qhGhn", async () => {
		renderPage();

		// Assert: заголовок шапки — превью первого сообщения владельца,
		// 16/600 $textPrimary с truncate; прежняя строка «последнее
		// сообщение» удалена, карточной рамки у колонки «Чат» нет.
		const title = await screen.findByRole("heading", { name: "Оцени риск стреддла" });
		expect(title.className).toContain("text-[16px]");
		expect(title.className).toContain("font-semibold");
		expect(title.className).toContain("text-text-primary");
		expect(title.className).toContain("truncate");
		expect(screen.queryByText(/последнее сообщение:/i)).not.toBeInTheDocument();
		const chatColumn = title.closest("section") as HTMLElement;
		expect(chatColumn.className).not.toContain("border");
		expect(chatColumn.className).not.toContain("rounded-lg");

		// Assert: чип модели — инстанс fYadZ: высота 25, заливка $surface,
		// радиус 999, кайма $border, подпись 12/normal $textPrimary.
		const chip = screen.getByText("GLM-5.3", { selector: "[data-slot='source-chip']" });
		expect(chip.className).toContain("h-[25px]");
		expect(chip.className).toContain("rounded-full");
		expect(chip.className).toContain("border");
		expect(chip.className).toContain("bg-card");
		expect(chip.className).toContain("text-[12px]");
		expect(chip.className).toContain("text-text-primary");

		// Assert: иконка чипа — cpu 12 $textSecondary.
		const icon = chip.querySelector("svg");
		expect(icon?.getAttribute("class")).toContain("lucide-cpu");
		expect(icon?.getAttribute("class")).toContain("text-text-secondary");

		// Assert: кнопки ghost r8 12/500 — «Завершить» ($textSecondary)
		// и «Удалить» (текст $neg #D14B41); у активного чата «Продолжить» нет.
		const finish = screen.getByRole("button", { name: "Завершить" });
		expect(finish.className).toContain("rounded-[8px]");
		expect(finish.className).toContain("text-[12px]");
		expect(finish.className).toContain("font-medium");
		expect(finish.className).toContain("text-text-secondary");
		const remove = screen.getByRole("button", { name: "Удалить" });
		expect(remove.className).toContain("rounded-[8px]");
		expect(remove.className).toContain("text-[12px]");
		expect(remove.className).toContain("text-neg");
		expect(screen.queryByRole("button", { name: "Продолжить" })).not.toBeInTheDocument();
	});

	/*
		Удаление выбранного чата (раунд-сверка 7.0): кнопка «Удалить» шапки
		вызывает существующий chat-api deleteChat; после успеха списки
		перечитываются, а выбор переходит на следующий чат.
	*/
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	// Traceability: change:rework-contextual-topbar/tasks#7.0
	it("TryIfDeleteButtonRemovesChatAndSelectsNextSession", async () => {
		// Arrange: два активных чата; автовыбор — свежий chat-1; после
		// удаления список активных перечитывается уже без chat-1.
		const chat1 = {
			id: "chat-1",
			status: "active",
			params: { model: "GLM-5.3", constructionId: null, sources: ["journal"] },
			createdAt: "2026-10-08T09:00:00Z",
			lastMessageAt: "2026-10-08T10:00:00Z",
			firstMessagePreview: "Оцени риск стреддла",
		};
		const chat2 = {
			id: "chat-2",
			status: "active",
			params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
			createdAt: "2026-10-08T08:00:00Z",
			lastMessageAt: "2026-10-08T09:00:00Z",
			firstMessagePreview: "Разбери сделку",
		};
		const active: Array<typeof chat1> = [chat1, chat2];
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active" ? active : [],
		);
		chatsApi.deleteChat.mockImplementation(async () => {
			active.splice(active.indexOf(chat1), 1);
			return undefined;
		});
		const user = userEvent.setup();
		renderPage();
		expect(await screen.findByRole("heading", { name: "Оцени риск стреддла" })).toBeInTheDocument();

		// Act: кнопка «Удалить» шапки выбранного чата.
		await user.click(screen.getByRole("button", { name: "Удалить" }));

		// Assert: вызвался deleteChat существующего chat-api; после
		// перечитывания списков выбран следующий чат.
		await waitFor(() => {
			expect(chatsApi.deleteChat).toHaveBeenCalledWith("chat-1");
		});
		expect(await screen.findByRole("heading", { name: "Разбери сделку" })).toBeInTheDocument();
	});

	/*
		Удаление последнего чата оставляет пустое состояние (раунд-сверка
		7.0): если после удаления чатов не осталось, центр показывает
		«Чем помочь с журналом?» вместо шапки удалённого чата.
	*/
	// Traceability: change:rework-contextual-topbar/tasks#7.0
	it("TryIfDeletingLastChatShowsEmptyState", async () => {
		// Arrange: один активный чат; после удаления список активных пуст.
		const chat1 = {
			id: "chat-1",
			status: "active",
			params: { model: "GLM-5.3", constructionId: null, sources: ["journal"] },
			createdAt: "2026-10-08T09:00:00Z",
			lastMessageAt: "2026-10-08T10:00:00Z",
			firstMessagePreview: "Оцени риск стреддла",
		};
		const active: Array<typeof chat1> = [chat1];
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active" ? active : [],
		);
		chatsApi.deleteChat.mockImplementation(async () => {
			active.length = 0;
			return undefined;
		});
		const user = userEvent.setup();
		renderPage();
		expect(await screen.findByRole("heading", { name: "Оцени риск стреддла" })).toBeInTheDocument();

		// Act: удаление единственного чата кнопкой шапки.
		await user.click(screen.getByRole("button", { name: "Удалить" }));

		// Assert: вместо шапки удалённого чата — пустое состояние.
		expect(await screen.findByText("Чем помочь с журналом?")).toBeInTheDocument();
		expect(screen.queryByRole("heading", { name: "Оцени риск стреддла" })).not.toBeInTheDocument();
	});

	/*
		Сбой удаления не теряет выбранный чат: ошибка команды показывается
		баннером под шапкой, чат остаётся выбранным до успеха команды.
	*/
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	// Traceability: change:rework-contextual-topbar/tasks#7.0
	it("ThrowOnDeleteChatFailureKeepsChatSelectedWithAlert", async () => {
		// Arrange: команда удаления отклоняется.
		chatsApi.deleteChat.mockRejectedValue(new Error("HTTP 500"));
		const user = userEvent.setup();
		renderPage();
		expect(await screen.findByRole("heading", { name: "Оцени риск стреддла" })).toBeInTheDocument();

		// Act: попытка удаления выбранного чата.
		await user.click(screen.getByRole("button", { name: "Удалить" }));

		// Assert: баннер сбоя с человекочитаемой причиной; чат остался выбран.
		expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось удалить чат: HTTP 500");
		expect(screen.getByRole("heading", { name: "Оцени риск стреддла" })).toBeInTheDocument();
	});

	/*
		Шапка завершённого чата (раунд-сверка 7.0): вместо «Завершить» —
		«Продолжить», кнопка «Удалить» сохраняется; продолжение вызывает
		существующую команду resumeChat.
	*/
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	// Traceability: change:rework-contextual-topbar/tasks#7.0
	it("TryIfCompletedChatHeaderShowsResumeAndDelete", async () => {
		// Arrange: активных чатов нет — автовыбор берёт первый завершённый.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? []
				: [
						{
							id: "chat-done",
							status: "completed",
							params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-07T08:00:00Z",
							lastMessageAt: "2026-10-07T09:00:00Z",
							firstMessagePreview: "Итоги недели",
						},
					],
		);
		const user = userEvent.setup();
		renderPage();

		// Assert: в шапке завершённого чата «Продолжить» и «Удалить»,
		// кнопки «Завершить» нет.
		expect(await screen.findByRole("heading", { name: "Итоги недели" })).toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Продолжить" })).toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Удалить" })).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Завершить" })).not.toBeInTheDocument();

		// Act: продолжение завершённого чата кнопкой шапки.
		await user.click(screen.getByRole("button", { name: "Продолжить" }));

		// Assert: команда продолжения ушла выбранному чату.
		await waitFor(() => {
			expect(chatsApi.resumeChat).toHaveBeenCalledWith("chat-done");
		});
	});

	/*
		Таб «Сводки · soon» — disabled-заглушка мастера D811M (TabSummaries,
		12.5/normal $textMuted): раздел сводок не реализован, клик невозможен.
	*/
	// Traceability: change:rework-contextual-topbar/tasks#6.0
	it("TryIfSummariesTabIsDisabledPlaceholder", () => {
		renderPage();

		// Assert: таб недоступен и стилизован цветом заглушки мастера.
		const summariesTab = screen.getByRole("button", { name: "Сводки · soon" });
		expect(summariesTab).toBeDisabled();
		expect(summariesTab.className).toContain("text-text-muted");
		expect(summariesTab.className).toContain("cursor-not-allowed");
	});

	it("создаёт чат первым сообщением с выбранными источниками", async () => {
		const user = userEvent.setup();
		renderPage();

		// Arrange: композер пустого состояния после «+ Новый чат».
		await screen.findByRole("heading", { name: "Оцени риск стреддла" });
		await user.click(screen.getByRole("button", { name: "+ Новый чат" }));
		await screen.findByText("Чем помочь с журналом?");

		// Act: снимаем рынок Bybit, вводим сообщение и отправляем композер
		// кнопкой-стрелкой мастера Z14sH.
		await user.click(screen.getByRole("button", { name: "рынок Bybit" }));
		await user.type(screen.getByLabelText(/первое сообщение/i), "Собери сводку по лимитам");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: в создании чата остались только выбранные источники.
		await waitFor(() =>
			expect(chatsApi.createChat).toHaveBeenCalledWith("Собери сводку по лимитам", {
				model: "GLM-5.3",
				constructionId: null,
				sources: ["journal", "rules-corpus"],
			}),
		);
	});

	it("применяет фильтры каталога правил и поиск", async () => {
		const user = userEvent.setup();
		renderPage();
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		// Act: выбор характера чипом-пилюлей мастера (фильтры Body #7 —
		// группы чипов viuZI вместо нативных select).
		await user.click(await screen.findByRole("button", { name: "лимиты и режим риска" }));
		await user.type(screen.getByLabelText(/поиск правил/i), "лимит");

		await waitFor(() => {
			expect(rulesApi.listRules).toHaveBeenCalledWith(
				expect.objectContaining({ character: "risk-mode", search: "лимит" }),
				expect.anything(),
			);
		});
	});

	/*
		Вкладка «Правила» оформлена по мастеру Body #7 (R3kdzS) design.pen
		(§3.8 аудита): табы — сегмент-контейнер z5Fqgz ($surface, r9, [3],
		gap 4) с активным табом accentSoft/accentStrong r7 [6,14] 12.5/600
		и счётчиком «корпус · N … · только чтение» (Cnt zYZRw); поиск —
		Q7Ur0 ($surface, r10, [10,14], иконка search 15 $textMuted, плейс-
		холдер 13); фильтры — группы чипов viuZI (метка 11.5 $textMuted +
		пилюли 999 [4,10] 11.5: выбранный accentSoft/accentStrong 600,
		невыбранный $surface/$border $textSecondary); каталог — сетка cRRlh
		рядами по 2 с гэпом 12; карточка — «Карточка правила/Полная» Zes7z.
	*/
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	// Traceability: change:reconcile-frontend-with-design/design#D2
	it("оформляет вкладку правил по мастеру Body #7: табы, счётчик, поиск, фильтры-чипы", async () => {
		// Arrange: в каталоге одно правило (total 1) — счётчик в единственном
		// числе «1 правило».
		const user = userEvent.setup();
		renderPage();
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		// Assert: активный таб «Правила» — заливка accentSoft секции мастера.
		const rulesTab = screen.getByRole("button", { name: "Правила" });
		expect(rulesTab.className).toContain("bg-accent-soft");
		expect(rulesTab.className).toContain("text-accent-strong");
		expect(rulesTab.className).toContain("font-semibold");
		expect(rulesTab.className).toContain("rounded-[7px]");

		// Assert: неактивный таб погашен до $textSecondary.
		const chatsTab = screen.getByRole("button", { name: "Чаты" });
		expect(chatsTab.className).toContain("text-text-secondary");
		expect(chatsTab.className).not.toContain("bg-accent-soft");

		// Assert: счётчик корпуса (нода Cnt zYZRw): 12/normal $textMuted.
		const counter = await screen.findByText(/корпус · 1 правило · только чтение/i);
		expect(counter.className).toContain("text-text-muted");
		expect(counter.className).toContain("text-xs");

		// Assert: поиск — поле мастера Q7Ur0: r10, [10,14], иконка search
		// 15×15 $textMuted, плейсхолдер 13 $textMuted.
		const searchField = screen.getByLabelText(/поиск правил/i);
		expect(searchField.className).toContain("text-[13px]");
		expect(searchField.getAttribute("placeholder")).toBe("Поиск по корпусу правил…");
		const searchIcon = searchField.parentElement?.querySelector("svg");
		expect(searchIcon?.getAttribute("class")).toContain("text-text-muted");

		// Assert: фильтры — пилюли 999 [4,10] 11.5; выбранные «Все» (по
		// одному в каждой группе) несут accentSoft/accentStrong — секции
		// accentSoft мастера.
		const allChips = screen.getAllByRole("button", { name: /^Все$/ });
		expect(allChips.length).toBe(3);
		for (const chip of allChips) {
			expect(chip.getAttribute("aria-pressed")).toBe("true");
			expect(chip.className).toContain("rounded-full");
			expect(chip.className).toContain("bg-accent-soft");
			expect(chip.className).toContain("text-accent-strong");
		}

		// Assert: невыбранный чип характера — $surface/$border $textSecondary.
		const characterChip = screen.getByRole("button", { name: "лимиты и режим риска" });
		expect(characterChip.getAttribute("aria-pressed")).toBe("false");
		expect(characterChip.className).toContain("bg-card");
		expect(characterChip.className).toContain("text-text-secondary");
	});

	it("рендерит карточки каталога правил по ноде Zes7z: пилюли-атрибуты 999, тело, футер", async () => {
		const user = userEvent.setup();
		renderPage();
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		// Assert: карточка «Карточка правила/Полная» (Zes7z): $surface,
		// кайма $border, r12, паддинги 18, гэп 12.
		const card = await screen.findByText(/Лимиты риска на период/i, { selector: "button" });
		const article = card.closest("article");
		expect(article).not.toBeNull();
		expect(article?.className).toContain("rounded-lg");
		expect(article?.className).toContain("border");
		expect(article?.className).toContain("bg-card");
		expect(article?.className).toContain("p-[18px]");
		expect(article?.className).toContain("gap-3");

		// Assert: идентификатор правила — 11/normal $textMuted (нода Id NLBwE).
		expect(within(article!).getByText("ac-01").className).toContain("text-text-muted");

		// Assert: пилюли-атрибуты — примитив «Чип/Статус» (aa6cK, r999
		// [4,10] 11.5/500): характер — пара $riskSoft/$risk (инстанс
		// nrPzY «Мягкое» Body #7), чёткость — $infoSoft/$info (U6mVZ),
		// субъект — surface2/$textSecondary (u9Vm0h).
		const characterPill = within(article!).getByText("лимиты и режим риска");
		expect(characterPill.className).toContain("rounded-full");
		expect(characterPill.className).toContain("bg-risk-soft");
		expect(characterPill.className).toContain("text-risk");
		const clarityPill = within(article!).getByText("Однозначное");
		expect(clarityPill.className).toContain("bg-info-soft");
		expect(clarityPill.className).toContain("text-info");
		const subjectPill = within(article!).getByText("Субъект: конструкция");
		expect(subjectPill.className).toContain("bg-surface-2");
		expect(subjectPill.className).toContain("text-text-secondary");

		// Assert: тело карточки (нода Body GjcwT: 13/normal $textSecondary,
		// межстрочный 1.55) приходит полной карточкой GET /rules/{id}.
		const body = await within(article!).findByText(/Остановить наращивание риска до конца периода/i);
		expect(body.closest("div")?.className).toContain("text-[13px]");
		expect(body.closest("div")?.className).toContain("leading-[1.55]");
		expect(body.closest("div")?.className).toContain("text-text-secondary");

		// Assert: футер (p4Ot2): иконка external-link 12 $accentStrong и
		// ссылка «Открыть в каталоге в новом окне» 12 $accentStrong.
		const footer = article!.querySelector("footer");
		const openLink = within(footer as HTMLElement).getByRole("link", { name: /открыть в каталоге в новом окне/i });
		expect(openLink.className).toContain("text-accent-strong");
		expect(openLink.className).toContain("text-xs");
		expect(footer?.querySelector("svg")?.getAttribute("class")).toContain("text-accent-strong");

		// Act: клик по заголовку открывает правило в правой панели
		// (поведение сохранено).
		await user.click(card);

		// Assert: правая панель несёт полную карточку с порогами.
		await waitFor(() => {
			expect(rulesApi.readRule).toHaveBeenCalledWith("ac-01", expect.anything());
		});
	});

	it("показывает краткий попап правила по ноде Lpcap при наведении в следе источников (§3.5:7)", async () => {
		// Arrange: активный чат с ответом ассистента, ссылающимся на
		// карточку правила в следе источников.
		chatsApi.listChatMessages.mockResolvedValue([
			{
				id: "m2",
				role: "assistant",
				text: "Ответ по лимитам риска",
				asOf: "2026-10-08T09:01:00Z",
				sourceTrace: {
					toolCalls: [],
					references: [{ kind: "rule-card", id: "ac-01", title: "Лимиты риска на период", asOf: "2026-10-08T09:00:00Z" }],
				},
			},
		]);
		const user = userEvent.setup();
		renderPage();

		// Act: наведение на ссылку карточки правила в следе источников.
		const ruleRef = await screen.findByRole("button", { name: /Лимиты риска на период/ });
		await user.hover(ruleRef);

		// Assert: попап «Попап правила/Краткий» (Lpcap): ширина 280, r10,
		// тень мастера 0 8 24 #17171E20, паддинги 12, гэп 7.
		const popup = await screen.findByLabelText(/карточка правила \(hover из чата\)/i);
		expect(popup.className).toContain("w-[280px]");
		expect(popup.className).toContain("rounded-[10px]");
		expect(popup.className).toContain("shadow-[0_8px_24px]");
		expect(popup.className).toContain("shadow-text-primary/12");
		expect(popup.className).toContain("p-3");
		expect(popup.className).toContain("gap-[7px]");

		// Assert: заголовок 12.5/600 $textPrimary, мета 11 $textMuted,
		// суть 12/1.45 $textSecondary (ноды oYr9W/rlwtL/lidNU).
		const popupTitle = screen.getByText("Лимиты риска на период", { selector: "p" });
		expect(popupTitle.className).toContain("text-[12.5px]");
		expect(popupTitle.className).toContain("font-semibold");
		const metaLine = screen.getByText(/лимиты и режим риска · Однозначное · конструкция · ac-01/i);
		expect(metaLine.className).toContain("text-[11px]");
		expect(metaLine.className).toContain("text-text-muted");
		const gist = screen.getByText(/Остановить наращивание риска/i);
		expect(gist.closest("div")?.className).toContain("text-xs");
		expect(gist.closest("div")?.className).toContain("leading-[1.45]");
	});
});

/*
	Мобильный фокусный режим активного чата (раунд-сверка 10.0, мастер
	OvuY9 «Экран/Mobile · Агент · Чат»): при выбранном чате экран занимает
	только чат — страница запрашивает у каркаса скрытие глобального
	мобильного топбара (useMobileFocus), шапка фокусного режима 52 px несёт
	«Назад» (гасит автовыбор — возврат к пустому состоянию «Агент · Чаты»),
	превью первого сообщения 13/600, чип модели и меню «More» с операциями
	шапки чата раунда 7.0 («Завершить»/«Продолжить», «Удалить»).
*/
// Traceability: openspec:chats/history#requirement-chat-manual-completion-and-deletion
// Traceability: change:rework-contextual-topbar/tasks#7.0
// Traceability: change:rework-contextual-topbar/tasks#10.0
describe("мобильный фокусный режим чата", () => {
	it("показывает шапку мастера OvuY9 и запрашивает скрытие глобального топбара", async () => {
		setViewport(390);
		renderPage();

		// Assert: активный чат автовыбран — страница запросила у каркаса
		// скрытие мобильного топбара и показывает только чат.
		const header = await screen.findByTestId("chat-focus-header");
		expect(screen.getByTestId("registered-mobile-focus")).toHaveAttribute("data-active", "true");
		expect(screen.queryByRole("heading", { name: "Агент", level: 1 })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Правила" })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Список сессий" })).not.toBeInTheDocument();

		// Assert: шапка 52 px ($surface, border-b 1 $divider, [0,14], гэп 10).
		expect(header.className).toContain("h-13");
		expect(header.className).toContain("bg-surface");
		expect(header.className).toContain("border-divider");
		expect(header.className).toContain("px-3.5");
		expect(header.className).toContain("gap-2.5");

		// Assert: «Назад» — chevron-left 18×18 $textPrimary.
		const back = within(header).getByRole("button", { name: "Назад к списку чатов" });
		const backIcon = back.querySelector("svg");
		expect(backIcon?.getAttribute("class")).toContain("lucide-chevron-left");
		expect(backIcon?.getAttribute("class")).toContain("size-4.5");
		expect(backIcon?.getAttribute("class")).toContain("text-text-primary");

		// Assert: заголовок — превью первого сообщения 13/600 $textPrimary
		// с truncate.
		const title = within(header).getByRole("heading", { name: "Оцени риск стреддла" });
		expect(title.className).toContain("text-[13px]");
		expect(title.className).toContain("font-semibold");
		expect(title.className).toContain("text-text-primary");
		expect(title.className).toContain("truncate");

		// Assert: чип модели — $surface2, радиус 999, паддинги [3,8],
		// 10 normal $textSecondary.
		const chip = within(header).getByText("GLM-5.3");
		expect(chip.className).toContain("bg-surface-2");
		expect(chip.className).toContain("rounded-full");
		expect(chip.className).toContain("px-2");
		expect(chip.className).toContain("py-[3px]");
		expect(chip.className).toContain("text-[10px]");
		expect(chip.className).toContain("text-text-secondary");

		// Assert: «More» — more-horizontal 16×16 $textSecondary.
		const more = within(header).getByRole("button", { name: "Действия чата" });
		const moreIcon = more.querySelector("svg");
		expect(moreIcon?.getAttribute("class")).toContain("lucide-more-horizontal");
		expect(moreIcon?.getAttribute("class")).toContain("size-4");
		expect(moreIcon?.getAttribute("class")).toContain("text-text-secondary");
	});

	it("«Назад» гасит автовыбор и возвращает пустое состояние с глобальным топбаром", async () => {
		setViewport(390);
		const user = userEvent.setup();
		renderPage();
		await screen.findByTestId("chat-focus-header");

		// Act: «Назад» в шапке фокусного режима.
		await user.click(screen.getByRole("button", { name: "Назад к списку чатов" }));

		// Assert: фокус снят — запрос каркасу погашен, страница вернулась
		// к состоянию «Агент · Чаты»: пустое состояние с композером,
		// автовыбор чата не сработал (шапки чата больше нет).
		expect(screen.queryByTestId("chat-focus-header")).not.toBeInTheDocument();
		expect(screen.getByTestId("registered-mobile-focus")).toHaveAttribute("data-active", "false");
		expect(screen.getByText("Чем помочь с журналом?")).toBeInTheDocument();
		expect(screen.getByLabelText(/первое сообщение/i)).toBeInTheDocument();
		expect(screen.queryByRole("heading", { name: "Оцени риск стреддла" })).not.toBeInTheDocument();
		expect(screen.getByRole("heading", { name: "Агент", level: 1 })).toBeInTheDocument();
	});

	it("меню «More» завершает выбранный чат операцией шапки раунда 7.0", async () => {
		setViewport(390);
		const user = userEvent.setup();
		renderPage();
		await screen.findByTestId("chat-focus-header");

		// Act: открытие меню «More» и выбор «Завершить».
		await user.click(screen.getByRole("button", { name: "Действия чата" }));
		const menu = screen.getByRole("menu", { name: "Действия чата" });
		await user.click(within(menu).getByRole("menuitem", { name: "Завершить" }));

		// Assert: команда завершения ушла выбранному чату.
		await waitFor(() => {
			expect(chatsApi.completeChat).toHaveBeenCalledWith("chat-1");
		});
	});

	it("«Удалить» из меню «More» удаляет последний чат в пустое состояние", async () => {
		// Arrange: единственный активный чат; после удаления список пуст.
		const chat1 = {
			id: "chat-1",
			status: "active",
			params: { model: "GLM-5.3", constructionId: null, sources: ["journal"] },
			createdAt: "2026-10-08T09:00:00Z",
			lastMessageAt: "2026-10-08T10:00:00Z",
			firstMessagePreview: "Оцени риск стреддла",
		};
		const active: Array<typeof chat1> = [chat1];
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active" ? active : [],
		);
		chatsApi.deleteChat.mockImplementation(async () => {
			active.length = 0;
			return undefined;
		});
		setViewport(390);
		const user = userEvent.setup();
		renderPage();
		await screen.findByTestId("chat-focus-header");

		// Act: «Удалить» из меню «More».
		await user.click(screen.getByRole("button", { name: "Действия чата" }));
		await user.click(within(screen.getByRole("menu", { name: "Действия чата" })).getByRole("menuitem", { name: "Удалить" }));

		// Assert: deleteChat вызван, чатов не осталось — пустое состояние,
		// шапки фокусного режима больше нет.
		await waitFor(() => {
			expect(chatsApi.deleteChat).toHaveBeenCalledWith("chat-1");
		});
		expect(await screen.findByText("Чем помочь с журналом?")).toBeInTheDocument();
		expect(screen.queryByTestId("chat-focus-header")).not.toBeInTheDocument();
	});

	it("меню «More» завершённого чата предлагает «Продолжить» вместо «Завершить»", async () => {
		// Arrange: активных чатов нет — автовыбор берёт завершённый.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? []
				: [
						{
							id: "chat-done",
							status: "completed",
							params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-07T08:00:00Z",
							lastMessageAt: "2026-10-07T09:00:00Z",
							firstMessagePreview: "Итоги недели",
						},
					],
		);
		setViewport(390);
		const user = userEvent.setup();
		renderPage();
		await screen.findByTestId("chat-focus-header");

		// Act: открытие меню «More».
		await user.click(screen.getByRole("button", { name: "Действия чата" }));

		// Assert: в меню «Продолжить» вместо «Завершить», «Удалить» на месте.
		const menu = screen.getByRole("menu", { name: "Действия чата" });
		expect(within(menu).getByRole("menuitem", { name: "Продолжить" })).toBeInTheDocument();
		expect(within(menu).queryByRole("menuitem", { name: "Завершить" })).not.toBeInTheDocument();
		expect(within(menu).getByRole("menuitem", { name: "Удалить" })).toBeInTheDocument();
	});
});
