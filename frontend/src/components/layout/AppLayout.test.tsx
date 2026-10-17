import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionsOverview, ConstructionsSummary } from "@/lib/api/constructions";
import type { SyncRunJournalEntry } from "@/lib/api/sync";
import { appSections } from "@/config/sections";
import { AgentPage } from "@/pages/agent-page";
import { AppLayout } from "./AppLayout";
import { useContextPanel } from "./context-panel";
import { usePageHeader } from "./page-header";

vi.mock("@/lib/api/inbox", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/inbox")>()),
	fetchInboxCount: vi.fn(),
}));

vi.mock("@/lib/api/constructions", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/constructions")>()),
	fetchConstructionsOverview: vi.fn(),
}));

vi.mock("@/lib/api/sync", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/sync")>()),
	fetchSyncRuns: vi.fn(),
}));

// Маршрут /agent рендерит реальную страницу агента: чаты пусты, каталог
// правил не нужен для каркаса, но запросы обязаны разрешаться.
const agentChatApi = vi.hoisted(() => ({ listChats: vi.fn() }));
const agentRulesApi = vi.hoisted(() => ({ listRules: vi.fn() }));

vi.mock("@/chat/api/chat-api", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/chat/api/chat-api")>()),
	listChats: agentChatApi.listChats,
}));

vi.mock("@/lib/api/agent-rules", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/agent-rules")>()),
	listRules: agentRulesApi.listRules,
}));

const inboxApi = vi.mocked(await import("@/lib/api/inbox"));
const constructionsApi = vi.mocked(await import("@/lib/api/constructions"));
const syncApi = vi.mocked(await import("@/lib/api/sync"));

// Каркас приложения по концепции К1 «Контекстный топбар» (ADR-0016): топбар
// 64 px из трёх сегментов, выровненных по колонкам панелей; сегмент
// навигации — лого, бренд и кнопка сворачивания; сегмент страницы — крошка
// с бейджем «Входящих», шапка из регистрации, итог журнала и статус
// синхронизации; сегмент контекста — кнопка сворачивания и чип короткого
// названия. Футеров у панелей нет: свёрнутая панель — рейка миниатюр на
// всю высоту, сохранённая геометрия восстанавливается между сессиями.
// Слой сверки с design.pen: сегменты, паддинги [0,16]/[0,20] и кнопки по
// мастер-ноде jGvOM «Каркаса» g0z20, чип — примитив StatusChip тона info.
// Traceability: openspec:ui/screens#requirement-app-frame-navigation
// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
// Traceability: openspec:ui/screens#scenario-frame-marks-failure-degrades-unrealized-only
// Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-topbar-segment
// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
// Traceability: openspec:ui/screens#scenario-frame-topbar-segments-align-to-columns
// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
// Traceability: openspec:ui/screens#scenario-frame-sync-status-last-successful-run
// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
// Traceability: change:reconcile-frontend-with-design/design#D2
// Traceability: change:rework-contextual-topbar/design#D1

// Геометрия панелей переживает перезагрузку в localStorage, поэтому между
// тестами файла накопленное состояние обязательно сбрасывается, иначе
// свёрнутость из прошлого теста искажает следующий рендер.
beforeEach(() => {
	localStorage.clear();
	inboxApi.fetchInboxCount.mockResolvedValue(0);
	constructionsApi.fetchConstructionsOverview.mockResolvedValue(makeOverview());
	syncApi.fetchSyncRuns.mockResolvedValue([]);
	agentChatApi.listChats.mockResolvedValue([]);
	agentRulesApi.listRules.mockResolvedValue({ items: [], total: 0 });
	setViewport(1280);
});

/** Обзор раздела «Конструкции» с настраиваемой сводкой для моков сети. */
function makeOverview(summary: Partial<ConstructionsSummary> = {}): ConstructionsOverview {
	return {
		summary: {
			totalPnL: 214.32,
			realizedPnL: 156.1,
			unrealizedPnL: 58.22,
			marksAsOf: "2026-10-10T12:00:00+00:00",
			hasMarkFailure: false,
			constructionCount: 3,
			openCount: 2,
			...summary,
		},
		items: [],
	};
}

/** Строка журнала синхронизаций с настраиваемым статусом для моков сети. */
function makeSyncRun(status: SyncRunJournalEntry["status"], finishedAt: string | null): SyncRunJournalEntry {
	return {
		id: 1,
		startedAt: "2026-10-16T10:00:00",
		finishedAt,
		mode: "incremental",
		status,
		error: null,
		newExecutions: 0,
		newDeliveries: 0,
		newInstruments: 0,
		skippedAreas: [],
		unresolvedInstruments: [],
		uncoveredBaseCoins: [],
	};
}

/**
 * Экран-проверка: регистрирует контекст правой панели, чтобы каркас
 * показал контекстную панель и сегмент контекста.
 */
function ContextRegisteringScreen() {
	useContextPanel({ chipLabel: "Конструкция", content: <div>контекст экрана</div> });
	return <div>главная</div>;
}

const overflowingRegistration = {
	chipLabel: "Конструкция",
	content: <div>контекст экрана</div>,
	iconActions: Array.from({ length: 20 }, (_, index) => (
		<button key={index} type="button" title={`Действие ${index + 1}`}>
			{`Действие ${index + 1}`}
		</button>
	)),
};

function OverflowContextScreen() {
	useContextPanel(overflowingRegistration);
	return <div>главная</div>;
}

/**
 * Экран-проверка: регистрирует шапку страницы (заголовок, хвост крошки и
 * чип) в сегменте «страница» топбара.
 */
function HeaderRegisteringScreen() {
	usePageHeader({
		title: "Карточка конструкции",
		breadcrumbTail: "Карточка",
		chips: <span data-testid="page-chip">чип страницы</span>,
	});
	return <div>главная</div>;
}

function renderLayout(withContext = false, overflowContext = false, headerRegistered = false) {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	const router = createMemoryRouter(
		[{
			element: <AppLayout />,
			children: [
				{
					index: true,
					element: overflowContext ? (
						<OverflowContextScreen />
					) : withContext ? (
						<ContextRegisteringScreen />
					) : headerRegistered ? (
						<HeaderRegisteringScreen />
					) : (
						<div>главная</div>
					),
				},
				{ path: "hints", element: <h1>Подсказки</h1> },
				{ path: "inbox", element: <h1>Входящие</h1> },
				{ path: "agent", element: <AgentPage /> },
			],
		}],
		{ initialEntries: ["/"] },
	);
	return render(
		<QueryClientProvider client={queryClient}>
			<RouterProvider router={router} />
		</QueryClientProvider>,
	);
}

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

describe("раскладка каркаса приложения", () => {
	it("показывает бренд «Журнал Bybit» в сегменте навигации топбара", () => {
		renderLayout();

		const segment = screen.getByTestId("topbar-segment-nav");
		expect(within(segment).getByText("Журнал Bybit")).toBeInTheDocument();
		expect(within(segment).getByTestId("topbar-logo")).toBeInTheDocument();
	});

	it("рисует лого-глиф токенами мастера srW2i", () => {
		renderLayout();

		// Assert: квадрат 26×26 (size-6.5) с радиусом 8 на паре
		// accentSoft/accentStrong; по центру — иконка line-chart 14×14
		// цветом $accentStrong вместо текстового глифа.
		// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
		const logo = screen.getByTestId("topbar-logo");
		expect(logo.className).toContain("size-6.5");
		expect(logo.className).toContain("rounded-sm");
		expect(logo.className).toContain("bg-accent-soft");
		const glyph = logo.querySelector("svg");
		expect(glyph).toHaveClass("size-3.5");
		expect(glyph).toHaveClass("text-accent-strong");
	});

	it("несёт геометрию мастера g0z20: высота 64, сегментные паддинги, фон surface", () => {
		renderLayout();

		// Assert: топбар по мастер-ноде jGvOM «Каркаса» (высота 64, заливка
		// $surface, нижний разделитель $divider); паддинги по сегментам:
		// навигация/контекст [0,16] (px-4), страница [0,20] (px-5).
		const header = screen.getByRole("banner");
		expect(header.className).toContain("h-16");
		expect(header.className).toContain("bg-surface");
		expect(header.className).toContain("border-divider");
		expect(screen.getByTestId("topbar-segment-nav").className).toContain("px-4");
		expect(screen.getByTestId("topbar-segment-page").className).toContain("px-5");
	});

	it("показывает бренд типографикой мастера WBYbU: Inter 14/600", () => {
		renderLayout();

		// Assert: бренд 14/600 без приписного трекинга (text-sm = 14px).
		const brand = screen.getByText("Журнал Bybit");
		expect(brand.className).toContain("text-sm");
		expect(brand.className).toContain("font-semibold");
		expect(brand.className).not.toContain("tracking-tight");
	});

	it("рендерит пункты панели примитивом NavItem по мастер-ноде apcZH", () => {
		renderLayout();

		// Assert: каждая ссылка панели — инстанс примитива NavItem с геометрией
		// мастера: радиус 8, паддинги [8,10], подпись 13/500.
		const links = screen.getAllByRole("link");
		expect(links).toHaveLength(appSections.length);
		for (const link of links) {
			expect(link.getAttribute("data-slot")).toBe("nav-item");
			expect(link.className).toContain("rounded-sm");
			expect(link.className).toContain("px-2.5");
			expect(link.className).toContain("py-2");
			expect(link.className).toContain("text-[13px]");
		}
	});

	it("помечает текущий раздел активным состоянием мастера Vx9C2", () => {
		renderLayout();

		// Assert: на «/» активен раздел «Конструкции» — surface + бордер +
		// accentStrong 13/600 и aria-current="page".
		const active = screen.getByRole("link", { name: "Конструкции" });
		expect(active.getAttribute("aria-current")).toBe("page");
		expect(active.className).toContain("bg-card");
		expect(active.className).toContain("font-semibold");
		expect(active.className).toContain("text-accent-strong");
		const idle = screen.getByRole("link", { name: "Подсказки" });
		expect(idle.getAttribute("aria-current")).toBeNull();
		expect(idle.className).toContain("text-text-secondary");
	});

	it.each(appSections)("выводит пункт панели «$label»", (section) => {
		renderLayout();

		expect(screen.getByRole("link", { name: section.label })).toBeInTheDocument();
	});

	it("сворачивает панель навигации до иконок кнопкой сегмента топбара", async () => {
		const user = userEvent.setup();
		renderLayout();

		await user.click(screen.getByRole("button", { name: /панель навигации/i }));

		// Assert: подписи разделов скрыты в панели (крошка сегмента страницы
		// своё слово «Конструкции» сохраняет), миниатюры-ссылки остаются.
		const panel = screen.getByTestId("layout-panel-left");
		for (const section of appSections) {
			expect(within(panel).queryByText(section.label)).not.toBeInTheDocument();
		}
		expect(screen.getAllByRole("link")).toHaveLength(appSections.length);
	});

	it("показывает бейдж входящих при ненулевом счётчике", async () => {
		inboxApi.fetchInboxCount.mockResolvedValue(3);
		renderLayout();

		expect(await screen.findByText("3")).toBeInTheDocument();
	});

	it("рисует бейдж входящих примитивом NavItem по мастер-ноде q35Tj9", async () => {
		inboxApi.fetchInboxCount.mockResolvedValue(7);
		renderLayout();

		// Assert: бейдж — pill 999 на акцентной паре мастера (заливка $accent,
		// белый текст 11/600, [2,8]); отклонение от текста задачи 3.1
		// «accentSoft/accentStrong» зафиксировано прямым чтением ноды q35Tj9.
		const badge = await screen.findByText("7");
		expect(badge.getAttribute("data-slot")).toBe("nav-item-badge");
		expect(badge.className).toContain("rounded-full");
		expect(badge.className).toContain("bg-primary");
		expect(badge.className).toContain("text-primary-foreground");
		expect(badge.className).toContain("px-2");
		expect(badge.className).toContain("py-0.5");
		expect(badge.className).toContain("text-[11px]");
		expect(badge.className).toContain("font-semibold");
	});

	it("в мобильном режиме открывает навигацию в drawer и закрывает после перехода", async () => {
		setViewport(390);
		const user = userEvent.setup();
		renderLayout();

		await user.click(screen.getByRole("button", { name: "Открыть меню" }));
		expect(screen.getByRole("navigation", { name: "Разделы журнала" })).toBeInTheDocument();

		await user.click(screen.getByRole("link", { name: "Подсказки" }));
		expect(await screen.findByRole("heading", { name: "Подсказки" })).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Закрыть меню" })).not.toBeInTheDocument();
	});
});

// Сегменты топбара выровнены по колонкам каркаса: ширины сегментов
// навигации и контекста равны ширинам панелей, сегмент страницы занимает
// центр; свёрнутая панель сжимает свой сегмент до 44 px.
// Traceability: openspec:ui/screens#scenario-frame-topbar-segments-align-to-columns
// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-topbar-segment
describe("сегменты топбара", () => {
	it("ширины сегментов навигации и контекста равны ширинам панелей", async () => {
		// Arrange: пользователь ранее задал левой панели 10 rem, правой 28 rem.
		localStorage.setItem("tj.layout.left", JSON.stringify({ width: 10, collapsed: false }));
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 28, collapsed: false }));
		renderLayout(true);

		// Assert: сегмент над панелью повторяет её ширину — колонка топбара
		// продолжается в панель; сегмент страницы занимает центр (flex-1).
		expect(screen.getByTestId("topbar-segment-nav").style.width).toBe("10rem");
		expect(screen.getByTestId("layout-panel-left").style.width).toBe("10rem");
		expect(await screen.findByTestId("topbar-segment-context")).toHaveStyle({ width: "28rem" });
		expect(await screen.findByTestId("context-panel")).toHaveStyle({ width: "28rem" });
		expect(screen.getByTestId("topbar-segment-page").className).toContain("flex-1");
	});

	it("кнопки сворачивания живут в сегментах топбара у внутренних краёв", async () => {
		// Развёрнутые панели не содержат ни футера, ни собственных кнопок
		// управления свёрнутостью — обе кнопки в сегментах топбара.
		// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-topbar-segment
		renderLayout(true);
		expect(await screen.findByTestId("context-panel")).toBeInTheDocument();

		const navSegment = screen.getByTestId("topbar-segment-nav");
		const collapseLeft = within(navSegment).getByRole("button", { name: "Свернуть панель навигации" });
		expect(collapseLeft).toHaveAttribute("title", "Свернуть панель навигации");
		// Кнопки панелей — 28×28 по мастеру J8C2x/z6tizc (иконка 15 сохраняется).
		expect(collapseLeft.className).toContain("size-7");
		// Внутренний (правый) край сегмента навигации: кнопка — последний элемент.
		expect(navSegment.lastElementChild).toBe(collapseLeft);

		const contextSegment = screen.getByTestId("topbar-segment-context");
		const collapseRight = within(contextSegment).getByRole("button", { name: "Свернуть контекстную панель" });
		expect(collapseRight).toHaveAttribute("title", "Свернуть контекстную панель");
		expect(collapseRight.className).toContain("size-7");
		// Внутренний (левый) край сегмента контекста: кнопка — первый элемент.
		expect(contextSegment.firstElementChild).toBe(collapseRight);

		expect(document.querySelector('[data-slot="panel-footer"]')).toBeNull();
		expect(screen.getByTestId("layout-panel-left").querySelectorAll("button")).toHaveLength(0);
		expect(screen.getByTestId("context-panel").querySelectorAll("button")).toHaveLength(0);
	});

	it("свёрнутая панель сжимает сегмент до 44 px с кнопкой разворачивания", async () => {
		const user = userEvent.setup();
		renderLayout(true);
		await screen.findByTestId("context-panel");

		await user.click(screen.getByRole("button", { name: "Свернуть панель навигации" }));
		await user.click(screen.getByRole("button", { name: "Свернуть контекстную панель" }));

		// Assert: сегменты и панели сжаты до 44 px; в сегменте навигации
		// остались только кнопка разворачивания (лого и бренд скрыты), в
		// сегменте контекста — кнопка без чипа.
		expect(screen.getByTestId("topbar-segment-nav")).toHaveStyle({ width: "2.75rem" });
		expect(screen.getByTestId("layout-panel-left")).toHaveStyle({ width: "2.75rem" });
		expect(screen.queryByTestId("topbar-logo")).not.toBeInTheDocument();
		expect(screen.queryByText("Журнал Bybit")).not.toBeInTheDocument();
		const contextSegment = screen.getByTestId("topbar-segment-context");
		expect(contextSegment).toHaveStyle({ width: "2.75rem" });
		expect(within(contextSegment).queryByTestId("context-chip")).not.toBeInTheDocument();
		const expandRight = within(contextSegment).getByRole("button", { name: "Развернуть контекстную панель" });
		const expandLeft = within(screen.getByTestId("topbar-segment-nav")).getByRole(
			"button",
			{ name: "Развернуть панель навигации" },
		);
		// Кнопки разворачивания — те же 28×28 по мастеру J8C2x/z6tizc.
		expect(expandLeft.className).toContain("size-7");
		expect(expandRight.className).toContain("size-7");
		expect(screen.getByTestId("context-panel")).toHaveStyle({ width: "2.75rem" });
	});
});

// Сегмент страницы топбара: крошка «Журнал / <раздел>» всегда из
// конфигурации разделов, заголовок и чипы — из регистрации шапки страницы
// (без регистрации — заголовок раздела); бейдж «Входящих» дублируется у
// крошки на её экране.
// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
// Traceability: openspec:ui/screens#scenario-inbox-badge-counts-unassigned
describe("сегмент страницы", () => {
	it("без регистрации показывает крошку и заголовок раздела из конфигурации", () => {
		renderLayout();

		// Assert: крошка «Журнал / Конструкции» и заголовок «Конструкции»
		// берутся из appSections — индексному экрану регистрация не нужна.
		const segment = screen.getByTestId("topbar-segment-page");
		const crumb = within(segment).getByRole("navigation", { name: "Крошка раздела" });
		expect(crumb).toHaveTextContent("Журнал");
		expect(crumb).toHaveTextContent("/");
		expect(crumb).toHaveTextContent("Конструкции");
		expect(within(segment).getByTestId("topbar-page-title")).toHaveTextContent("Конструкции");
	});

	it("экран «Подсказки» без регистрации показывает крошку и заголовок раздела, контекст скрыт", async () => {
		// Решения раунд-сверки 5.0 (мастер h4jqRW): состав сегмента страницы
		// «Подсказок» — крошка «Журнал / Подсказки» и заголовок «Подсказки»
		// из fallback appSections, чипов нет; страница контекст не
		// регистрирует, поэтому контекстная панель и сегмент контекста
		// скрыты (чип мастера «Карточка · BTC-280324» — нестыковка мастера,
		// design D9).
		// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
		// Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
		// Traceability: change:rework-contextual-topbar/tasks#5.0
		const user = userEvent.setup();
		renderLayout();

		// Act: переход в раздел «Подсказки».
		await user.click(screen.getByRole("link", { name: "Подсказки" }));
		expect(await screen.findByRole("heading", { name: "Подсказки" })).toBeInTheDocument();

		// Assert: крошка и заголовок — из конфигурации разделов, чипов и
		// контекстного сегмента с панелью нет.
		const segment = screen.getByTestId("topbar-segment-page");
		const crumb = within(segment).getByRole("navigation", { name: "Крошка раздела" });
		expect(crumb.textContent).toBe("Журнал/Подсказки");
		expect(within(segment).getByTestId("topbar-page-title")).toHaveTextContent("Подсказки");
		expect(within(segment).queryByTestId("page-chip")).not.toBeInTheDocument();
		expect(screen.queryByTestId("topbar-segment-context")).not.toBeInTheDocument();
		expect(screen.queryByTestId("context-panel")).not.toBeInTheDocument();
	});

	it("рисует крошку и заголовок типографикой мастера jGvOM", () => {
		renderLayout();

		// Assert: крошка — одна строка 12px $textMuted (text-xs) без
		// выделения хвоста раздела; заголовок страницы — 15/600
		// (text-[15px]).
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		const segment = screen.getByTestId("topbar-segment-page");
		const crumb = within(segment).getByRole("navigation", { name: "Крошка раздела" });
		expect(crumb.className).toContain("text-xs");
		expect(crumb.className).toContain("text-text-muted");
		const tail = within(crumb).getByText("Конструкции");
		expect(tail.className).not.toContain("font-medium");
		expect(tail.className).not.toContain("text-text-secondary");
		const title = within(segment).getByTestId("topbar-page-title");
		expect(title.className).toContain("text-[15px]");
	});

	it("регистрация шапки выводит заголовок и чипы страницы у крошки", () => {
		renderLayout(false, false, true);

		// Assert: заголовок и чип из регистрации появляются в сегменте
		// страницы рядом с крошкой раздела.
		const segment = screen.getByTestId("topbar-segment-page");
		expect(within(segment).getByTestId("topbar-page-title")).toHaveTextContent("Карточка конструкции");
		expect(within(segment).getByTestId("page-chip")).toBeInTheDocument();
	});

	it("хвост регистрации продолжает крошку третьим уровнем", () => {
		renderLayout(false, false, true);

		// Assert: зарегистрированный хвост «Карточка» продолжает крошку
		// «Журнал / Конструкции» трёхуровневой — порядок «Журнал /
		// Конструкции / Карточка» в одной строке.
		// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
		// Traceability: change:rework-contextual-topbar/tasks#3.0
		const segment = screen.getByTestId("topbar-segment-page");
		const crumb = within(segment).getByRole("navigation", { name: "Крошка раздела" });
		expect(crumb.textContent).toBe("Журнал/Конструкции/Карточка");
	});

	it("экран «Агент» показывает зарегистрированный заголовок «Агент · Чаты»", async () => {
		// Решения раунд-сверки 6.0 (мастер D811M): крошка «Журнал / Агент» и
		// бейдж «Входящих» — fallback из appSections; заголовок «Агент · Чаты»
		// регистрирует страница через usePageHeader; чипов нет, контекст
		// страница не регистрирует — сегмент контекста и панель скрыты.
		// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
		// Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
		// Traceability: change:rework-contextual-topbar/tasks#6.0
		const user = userEvent.setup();
		renderLayout();

		// Act: переход в раздел «Агент».
		await user.click(screen.getByRole("link", { name: "Агент" }));
		expect(await screen.findByRole("heading", { name: "Агент", level: 1 })).toBeInTheDocument();

		// Assert: крошка из конфигурации разделов, заголовок «Агент · Чаты»
		// из регистрации страницы; чипов и контекстного сегмента нет.
		const segment = screen.getByTestId("topbar-segment-page");
		const crumb = within(segment).getByRole("navigation", { name: "Крошка раздела" });
		expect(crumb.textContent).toBe("Журнал/Агент");
		expect(within(segment).getByTestId("topbar-page-title")).toHaveTextContent("Агент · Чаты");
		expect(within(segment).queryByTestId("page-chip")).not.toBeInTheDocument();
		expect(screen.queryByTestId("topbar-segment-context")).not.toBeInTheDocument();
		expect(screen.queryByTestId("context-panel")).not.toBeInTheDocument();
	});

	it("бейдж входящих дублируется у крошки на экране «Входящие»", async () => {
		// Arrange: три непривязанные сделки; счётчик общий с пунктом меню.
		inboxApi.fetchInboxCount.mockResolvedValue(3);
		const user = userEvent.setup();
		renderLayout();
		await screen.findByText("3");

		// Act: переход в раздел «Входящие».
		await user.click(screen.getByRole("link", { name: "Входящие" }));
		expect(await screen.findByRole("heading", { name: "Входящие" })).toBeInTheDocument();

		// Assert: у крошки раздела тот же бейдж-счётчик, что и в пункте меню.
		const segment = screen.getByTestId("topbar-segment-page");
		const badge = within(segment).getByTestId("topbar-badge");
		expect(badge).toHaveTextContent("3");
		expect(badge.className).toContain("bg-primary");
		expect(
			within(screen.getByRole("navigation", { name: "Разделы журнала" })).getByText("3"),
		).toBeInTheDocument();
	});
});

// Статус синхронизации в сегменте страницы: «Обновлено HH:MM» по последнему
// успешному запуску журнала в зоне браузера; без успешных запусков, при
// пустом журнале или сбое загрузки блок скрыт без деградации остального.
// Traceability: openspec:ui/screens#scenario-frame-sync-status-last-successful-run
describe("статус синхронизации", () => {
	it("показывает «Обновлено HH:MM» по последнему успешному запуску", async () => {
		// Arrange: последний успешный запуск завершился в 11:05, более свежий
		// запуск сбойный, ранее был ещё один успешный.
		syncApi.fetchSyncRuns.mockResolvedValue([
			makeSyncRun("failed", "2026-10-16T12:00:00"),
			makeSyncRun("succeeded", "2026-10-16T11:05:00"),
			makeSyncRun("succeeded", "2026-10-16T09:30:00"),
		]);
		renderLayout();

		// Assert: время берётся из последнего успешного запуска, а не из
		// более свежего сбойного и не из более раннего успешного; формат
		// HH:MM в зоне браузера.
		const status = await screen.findByTestId("sync-status");
		expect(status).toHaveTextContent("Обновлено 11:05");
		// 11.5px $textMuted по мастеру (нода SyncStatus).
		expect(status.className).toContain("text-[11.5px]");
	});

	it("скрыт, когда успешных запусков нет", async () => {
		// Arrange: журнал есть, но все запуски сбойные.
		syncApi.fetchSyncRuns.mockResolvedValue([
			makeSyncRun("failed", "2026-10-16T12:00:00"),
			makeSyncRun("failed", "2026-10-16T09:00:00"),
		]);
		renderLayout();

		// Assert: блока нет, итог журнала продолжает работать.
		await screen.findByTestId("journal-total");
		expect(screen.queryByTestId("sync-status")).not.toBeInTheDocument();
	});

	it("скрыт при пустом журнале синхронизаций", async () => {
		syncApi.fetchSyncRuns.mockResolvedValue([]);
		renderLayout();

		// Assert: блока нет, остальные элементы топбара на месте.
		await screen.findByTestId("journal-total");
		expect(screen.queryByTestId("sync-status")).not.toBeInTheDocument();
	});

	it("скрыт при сбое загрузки журнала без деградации топбара", async () => {
		// Arrange: журнал синхронизаций недоступен.
		syncApi.fetchSyncRuns.mockRejectedValue(new Error("API ответил ошибкой 500"));
		renderLayout();

		// Assert: блока нет, итог журнала и бренд видны — деградации нет.
		await screen.findByTestId("journal-total");
		expect(screen.queryByTestId("sync-status")).not.toBeInTheDocument();
		expect(screen.getByText("Журнал Bybit")).toBeInTheDocument();
	});
});

// Сегмент контекста топбара: чип показывает зарегистрированное короткое
// название содержимого на паре $infoSoft/$info; без регистрации контекста
// сегмент скрыт вместе с панелью.
// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
// Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
describe("сегмент контекста", () => {
	it("чип показывает короткое название зарегистрированного содержимого", async () => {
		renderLayout(true);

		// Assert: чип примитива StatusChip тона info стоит в сегменте рядом
		// с кнопкой сворачивания; клик по чипу не даёт действий.
		const chip = await screen.findByTestId("context-chip");
		expect(chip).toHaveTextContent("Конструкция");
		expect(chip.getAttribute("data-slot")).toBe("status-chip");
		expect(chip.className).toContain("bg-info-soft");
		expect(chip.className).toContain("text-info");
		const segment = screen.getByTestId("topbar-segment-context");
		expect(
			within(segment).getByRole("button", { name: "Свернуть контекстную панель" }),
		).toBeInTheDocument();
		await userEvent.click(chip);
		expect(chip).toBeInTheDocument();
	});

	it("без регистрации контекста сегмент контекста скрыт", () => {
		renderLayout();

		// Assert: сегмента нет, центр с инвариантами топбара остался.
		expect(screen.queryByTestId("topbar-segment-context")).not.toBeInTheDocument();
		expect(screen.getByTestId("topbar-segment-page")).toBeInTheDocument();
	});
});

// Итог журнала в сегменте страницы топбара: разбивка на реализованный и
// нереализованный P&L обязательна на любом экране, при сбое марок гаснет
// только нереализованная часть; детальная сводка остаётся в шапке списка.
// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
// Traceability: openspec:ui/screens#scenario-frame-marks-failure-degrades-unrealized-only
describe("итог журнала в топбаре", () => {
	it("показывает итог с разбивкой на реализованный и нереализованный P&L на любом экране", async () => {
		constructionsApi.fetchConstructionsOverview.mockResolvedValue(makeOverview());
		const user = userEvent.setup();
		renderLayout();

		// Assert: итог живёт в топбаре (banner) и несёт обе части разбивки;
		// детальные счётчики и отметка марок остаются сводке списка.
		const banner = screen.getByRole("banner");
		expect(await within(banner).findByText("+214.32 USDT")).toBeInTheDocument();
		const total = within(banner).getByTestId("journal-total");
		expect(total).toHaveTextContent("+214.32 USDT");
		expect(total).toHaveTextContent("реализов. +156.1 / нереализов. +58.22");
		expect(total).not.toHaveTextContent("конструкци");

		// Act: переход на другой экран («Подсказки») — итог остаётся видимым
		// из общего кэша без повторного запроса.
		await user.click(screen.getByRole("link", { name: "Подсказки" }));
		expect(await screen.findByRole("heading", { name: "Подсказки" })).toBeInTheDocument();
		expect(within(screen.getByRole("banner")).getByTestId("journal-total")).toHaveTextContent("+214.32 USDT");
	});

	it("при сбое марок помечает итог неполным, нереализованный P&L — признаком сбоя, реализованный остаётся", async () => {
		// Arrange: провайдер котировок не оценил остатки — нереализованная
		// часть и итог непостроены, реализованный P&L известен.
		constructionsApi.fetchConstructionsOverview.mockResolvedValue(
			makeOverview({ totalPnL: null, unrealizedPnL: null, marksAsOf: null, hasMarkFailure: true }),
		);
		renderLayout();

		// Assert: итог помечен неполным, нереализованная часть получила
		// признак сбоя, реализованная часть и сам итог остаются видимыми.
		const banner = screen.getByRole("banner");
		expect(await within(banner).findByText("сбой котировок")).toBeInTheDocument();
		const total = within(banner).getByTestId("journal-total");
		expect(total).toHaveTextContent("недоступен");
		expect(total).toHaveTextContent("неполный");
		expect(total).toHaveTextContent("сбой котировок");
		expect(total).toHaveTextContent("реализов. +156.1");
	});
});

// Панели: футеров нет — управление свёрнутостью в сегментах топбара,
// свёрнутая панель — рейка миниатюр на всю высоту; сохранённая геометрия
// восстанавливается между сессиями.
// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-topbar-segment
// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
// Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
// Traceability: change:rework-contextual-topbar/design#D6
describe("панели каркаса", () => {
	it("свёрнутая панель — рейка миниатюр на всю высоту без футера", async () => {
		const user = userEvent.setup();
		renderLayout(true);
		await screen.findByTestId("context-panel");
		await user.click(screen.getByRole("button", { name: "Свернуть панель навигации" }));
		await user.click(screen.getByRole("button", { name: "Свернуть контекстную панель" }));

		// Assert: рейка — единственное содержимое свёрнутой панели: она
		// растянута на всю высоту (flex-1), футеров у каркаса нет, кнопка
		// разворачивания осталась в сегменте топбара.
		// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
		for (const [testId, side] of [
			["layout-panel-left", "left"],
			["context-panel", "right"],
		] as const) {
			const panel = screen.getByTestId(testId);
			const rail = panel.querySelector(`[data-slot="panel-miniature-rail"][data-side="${side}"]`);
			expect(rail).not.toBeNull();
			expect(rail?.className).toContain("w-11");
			expect(rail?.className).toContain("flex-1");
			expect(panel.querySelectorAll('[data-slot="panel-footer"]')).toHaveLength(0);
			expect(within(panel).queryByRole("button", { name: /развернуть/i })).not.toBeInTheDocument();
		}
		expect(within(screen.getByTestId("layout-panel-left")).getAllByRole("link")).toHaveLength(appSections.length);
	});

	it("при переполнении миниатюр прокручивается рейка, разворачивание — из топбара", async () => {
		// Ограниченная высота не ломает рейку: она занимает всю панель и
		// прокручивается вместе с множеством действий.
		// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 24, collapsed: true }));
		renderLayout(false, true);
		const panel = await screen.findByTestId("context-panel");
		panel.style.height = "90px";
		const rail = panel.querySelector('[data-slot="panel-miniature-rail"]') as HTMLElement;
		expect(within(rail).getAllByRole("button")).toHaveLength(20);
		expect(rail.className).toContain("min-h-0");
		expect(rail.className).toContain("flex-1");
		expect(rail.className).toContain("overflow-y-auto");
		expect(panel.querySelector('[data-slot="panel-footer"]')).toBeNull();

		// Кнопка разворачивания в сегменте топбара, не в самой панели.
		// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-topbar-segment
		const expand = screen.getByRole("button", { name: "Развернуть контекстную панель" });
		expect(panel.contains(expand)).toBe(false);
		expect(screen.getByTestId("topbar-segment-context").contains(expand)).toBe(true);
	});

	it("восстанавливает геометрию левой панели из localStorage на монтировании", async () => {
		const user = userEvent.setup();
		// Arrange: в прошлой сессии панель сужена до 10 rem и свёрнута.
		localStorage.setItem("tj.layout.left", JSON.stringify({ width: 10, collapsed: true }));

		renderLayout();

		// Assert: панель снова свёрнута (подписи скрыты, сегмент сжат до
		// 44 px), после разворачивания восстанавливается ширина 10 rem.
		const panel = screen.getByTestId("layout-panel-left");
		expect(panel).toHaveStyle({ width: "2.75rem" });
		for (const section of appSections) {
			expect(within(panel).queryByText(section.label)).not.toBeInTheDocument();
			expect(screen.getByRole("link", { name: section.label })).toHaveAttribute("title", section.label);
		}
		const rail = document.querySelector('[data-slot="panel-miniature-rail"][data-side="left"]');
		expect(rail).not.toBeNull();
		expect(rail?.className).toContain("w-11");
		expect(screen.queryByRole("button", { name: "Свернуть панель навигации" })).not.toBeInTheDocument();
		await user.click(screen.getByRole("button", { name: "Развернуть панель навигации" }));
		expect(panel).toHaveStyle({ width: "10rem" });
	});

	it("рейка сохраняет навигацию и не показывает бейдж «Входящих»", async () => {
		// Миниатюры остаются ссылками, но текст и бейдж при сворачивании скрыты.
		// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
		const user = userEvent.setup();
		inboxApi.fetchInboxCount.mockResolvedValue(7);
		renderLayout();
		expect(await screen.findByText("7")).toBeInTheDocument();
		await user.click(screen.getByRole("button", { name: "Свернуть панель навигации" }));
		expect(screen.queryByText("7")).not.toBeInTheDocument();
		await user.click(screen.getByRole("link", { name: "Подсказки" }));
		expect(await screen.findByRole("heading", { name: "Подсказки" })).toBeInTheDocument();
		expect(screen.getByRole("link", { name: "Подсказки" })).toHaveAttribute("aria-current", "page");
	});

	it("контент панелей сохраняет боковые отступы 12 px", async () => {
		// Контент обеих панелей отступает на 12 px; вертикали следуют макету.
		// Traceability: openspec:ui/screens#scenario-frame-panel-content-uniform-padding
		renderLayout(true);
		const right = await screen.findByTestId("context-panel");
		expect(screen.getByRole("navigation", { name: "Разделы журнала" }).className).toContain("px-3 py-4");
		const content = right.querySelector(".overflow-y-auto");
		expect(content?.className).toContain("px-3 pt-3.5 pb-3");
	});

	it("сплиттер левой панели меняет ширину стрелками и сохраняет её", async () => {
		const user = userEvent.setup();
		renderLayout();

		// Act: растягиваем левую панель клавиатурой (drag в jsdom недоступен).
		const panel = screen.getByTestId("layout-panel-left");
		expect(panel).toHaveStyle({ width: "16rem" });
		const separator = screen.getByRole("separator", { name: "Изменить ширину панели навигации" });
		separator.focus();
		await user.keyboard("{ArrowRight}");

		// Assert: ширина выросла на шаг 0.5 rem, значение сохранено, URL чист.
		expect(panel).toHaveStyle({ width: "16.5rem" });
		const stored = JSON.parse(localStorage.getItem("tj.layout.left") ?? "{}") as { width?: number };
		expect(stored.width).toBe(16.5);
		expect(window.location.search).toBe("");
	});

	it("ограничивает ширину правой панели диапазоном 20–40 rem стрелками", async () => {
		const user = userEvent.setup();
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 20, collapsed: false }));
		renderLayout(true);

		// Act: панель уже на нижней границе 20 rem, пробуем сузить дальше.
		const panel = await screen.findByTestId("context-panel");
		const separator = screen.getByRole("separator", { name: "Изменить ширину контекстной панели" });
		separator.focus();
		await user.keyboard("{ArrowRight}");

		// Assert: для правой панели ArrowRight сужает, clamp не даёт уйти ниже 20.
		expect(panel).toHaveStyle({ width: "20rem" });
	});
});
