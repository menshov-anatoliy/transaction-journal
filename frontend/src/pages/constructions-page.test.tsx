import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import { Profiler } from "react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, MemoryRouter, RouterProvider, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionsOverview } from "@/lib/api/constructions";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { AppLayout } from "@/components/layout/AppLayout";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ConstructionsPage } from "./constructions-page";

// Проверяется компоновка раздела «Конструкции» по концепции §3 и change
// rework-spa-shell-and-screens: тулбар с титулом, счётчиками конструкций и
// ручным проходом подсказок, строка итога сводки, таблица с
// выделением строки по URL, краткая карточка выделенной конструкции в
// правой контекстной панели каркаса (на мобильном — в drawer). Слой API
// подменён модулем-заглушкой.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
// Traceability: change:rework-spa-shell-and-screens/design#D3
// Traceability: change:rework-spa-shell-and-screens/design#D4
// Traceability: change:rework-spa-shell-and-screens/design#D5
// Traceability: change:rework-spa-shell-and-screens/design#D6

const overview: ConstructionsOverview = {
	summary: {
		totalPnL: 100.5,
		realizedPnL: 60.25,
		unrealizedPnL: 40.25,
		quotesAsOf: "2026-06-20T14:30:00",
		hasQuoteFailure: false,
		constructionCount: 3,
		openCount: 1,
	},
	items: [
		{
			constructionId: 7,
			name: "ETH-240628-3200C+P",
			status: "open",
			allocatedCapitalUsdt: 3000,
			riskPercent: 3,
			riskUsdt: 90,
			profitPercent: 8,
			profitUsdt: 240,
			realizedPnL: 60.25,
			unrealizedPnL: 40.25,
			adjustmentsPnL: 0,
			totalPnL: 100.5,
			totalPnLPercent: 3.35,
			quoteValue: 500,
			capitalUsagePercent: 16.7,
			realRiskUsdt: 45,
			worstCaseUsdt: 15.25,
			realRiskStatus: "finite",
			openedAt: "2026-06-18T09:05:00",
			closedAt: null,
			liveHintCount: 2,
			riskWarningsCount: 1,
			daysToNearestExpiry: 9,
			netDailyDecayUsdt: -6.8,
			totalIntrinsicValueUsdt: 640,
		},
		{
			constructionId: 8,
			name: "BTC-240531-60000C",
			status: "closed",
			allocatedCapitalUsdt: null,
			riskPercent: null,
			riskUsdt: null,
			profitPercent: null,
			profitUsdt: null,
			realizedPnL: 0,
			unrealizedPnL: 0,
			adjustmentsPnL: 0,
			totalPnL: 0,
			totalPnLPercent: null,
			quoteValue: null,
			capitalUsagePercent: null,
			realRiskUsdt: 0,
			worstCaseUsdt: 0,
			realRiskStatus: "finite",
			openedAt: "2026-05-20T10:00:00",
			closedAt: "2026-05-31T12:00:00",
			liveHintCount: 0,
			riskWarningsCount: 0,
			daysToNearestExpiry: null,
			netDailyDecayUsdt: null,
			totalIntrinsicValueUsdt: null,
		},
		{
			constructionId: 9,
			name: "SOL-240607-150P",
			status: "archived",
			allocatedCapitalUsdt: null,
			riskPercent: null,
			riskUsdt: null,
			profitPercent: null,
			profitUsdt: null,
			realizedPnL: -12.5,
			unrealizedPnL: null,
			adjustmentsPnL: 0,
			totalPnL: -12.5,
			totalPnLPercent: null,
			quoteValue: null,
			capitalUsagePercent: null,
			realRiskUsdt: 0,
			worstCaseUsdt: -12.5,
			realRiskStatus: "finite",
			openedAt: "2026-04-02T10:00:00",
			closedAt: "2026-06-07T16:00:00",
			liveHintCount: 0,
			riskWarningsCount: 0,
			daysToNearestExpiry: null,
			netDailyDecayUsdt: null,
			totalIntrinsicValueUsdt: null,
		},
	],
};

const preview = {
	constructionId: 7,
	name: "ETH-240628-3200C+P",
	status: "open" as const,
	allocatedCapitalUsdt: 3000,
	riskPercent: 3,
	riskUsdt: 90,
	profitPercent: 8,
	profitUsdt: 240,
	realizedPnL: 60.25,
	unrealizedPnL: 40.25,
	adjustmentsPnL: 0,
	totalPnL: 100.5,
	totalPnLPercent: 3.35,
	quoteValue: 500,
	capitalUsagePercent: 16.7,
	realRiskUsdt: 45,
	worstCaseUsdt: 15.25,
	realRiskStatus: "finite" as const,
	openedAt: "2026-06-18T09:05:00",
	closedAt: null,
	quotesAsOf: "2026-06-20T14:30:00",
	hasQuoteFailure: false,
	counts: { positions: 2, openPositions: 1, trades: 3, adjustments: 0 },
	riskWarningsCount: 0,
	riskWarnings: [],
};

vi.mock("@/lib/api/constructions", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/constructions")>()),
	fetchConstructionsOverview: vi.fn(),
	fetchConstructionPreview: vi.fn(),
}));

vi.mock("@/lib/api/hints", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/hints")>()),
	runHintsPass: vi.fn(),
}));

vi.mock("@/lib/api/inbox", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/inbox")>()),
	fetchInboxCount: vi.fn(),
}));

vi.mock("@/lib/api/sync", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/sync")>()),
	runSync: vi.fn(),
	fetchSyncRuns: vi.fn(),
}));

const api = {
	constructions: vi.mocked(await import("@/lib/api/constructions")),
	hints: vi.mocked(await import("@/lib/api/hints")),
	inbox: vi.mocked(await import("@/lib/api/inbox")),
	sync: vi.mocked(await import("@/lib/api/sync")),
};

/** Текущая строка параметров маршрута — для проверки URL-состояния раздела. */
let lastSearch: string;

function LocationProbe() {
	const location = useLocation();
	lastSearch = location.search;
	return null;
}

/** Рендер страницы автономно: контекстная панель каркаса недоступна. */
function renderPage() {
	const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
	return render(
		<QueryClientProvider client={queryClient}>
			{/* Провайдер подсказок с нулевой задержкой — для проверки попапов
			    метрик наведением, как в примитиве Tooltip. */}
			<TooltipProvider delayDuration={0}>
				<MemoryRouter>
					<LocationProbe />
					<ConstructionsPage />
				</MemoryRouter>
			</TooltipProvider>
		</QueryClientProvider>,
	);
}

/** Рендер страницы внутри каркаса: краткая карточка попадает в контекстную панель. */
function renderPageInFrame() {
	const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
	const router = createMemoryRouter(
		[
			{
				element: <AppLayout />,
				children: [
					{
						path: "/",
						element: (
							<>
								<LocationProbe />
								<ConstructionsPage />
							</>
						),
					},
				],
			},
		],
		{ initialEntries: ["/"] },
	);
	render(
		<QueryClientProvider client={queryClient}>
			{/* Radix Tooltip требует провайдера: сводка шапки и таблица рендерят
			    подсказки метрик. */}
			<TooltipProvider delayDuration={0}>
				<RouterProvider router={router} />
			</TooltipProvider>
		</QueryClientProvider>,
	);
	return { router };
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

beforeEach(() => {
	vi.clearAllMocks();
	lastSearch = "";
	api.constructions.fetchConstructionsOverview.mockResolvedValue(overview);
	api.constructions.fetchConstructionPreview.mockResolvedValue(preview);
	// Статус синхронизации топбара без успешных запусков скрыт — к проверкам
	// страницы отношения не имеет.
	api.sync.fetchSyncRuns.mockResolvedValue([]);
	api.hints.runHintsPass.mockResolvedValue({
		outcome: "completed",
		asOf: "2026-06-20T14:30:00",
		createdHints: 2,
		expiredHints: 1,
		diagnostics: null,
		unimplementedRuleIds: [],
	});
	api.inbox.fetchInboxCount.mockResolvedValue(0);
	api.sync.runSync.mockResolvedValue({
		mode: "incremental",
		status: "succeeded",
		startedAt: "2026-06-20T14:30:00",
		finishedAt: "2026-06-20T14:31:30",
		newExecutions: 5,
		newDeliveries: 2,
		newInstruments: 1,
		projectionError: null,
		reconciliationWarnings: [],
		skippedAreas: [],
		unresolvedInstruments: [],
		uncoveredBaseCoins: [],
	});
});

describe("раздел «Конструкции»", () => {
	// Выделение конструкции должно завершать обновление списка, а не запускать бесконечный рендер.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	it("ThrowOnSelectionRenderLoop: клик по конструкции не зацикливает страницу", async () => {
		// Arrange: ограничитель останавливает цикл до исчерпания памяти тестового процесса.
		const user = userEvent.setup();
		const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
		let commits = 0;
		render(
			<QueryClientProvider client={queryClient}>
				<MemoryRouter>
					{/* Radix Tooltip требует провайдера: страница рендерит подсказки
					    метрик сводки и заголовков таблицы. */}
					<TooltipProvider delayDuration={0}>
						<Profiler
							id="constructions"
							onRender={() => {
								commits += 1;
								if (commits > 30) {
									throw new Error("Выделение конструкции вызвало бесконечное обновление страницы");
								}
							}}
						>
							<ConstructionsPage />
						</Profiler>
					</TooltipProvider>
				</MemoryRouter>
			</QueryClientProvider>,
		);
		await screen.findByRole("row", { name: /ETH-240628/ });

		// Act: клик и завершение отложенных обновлений таблицы.
		await user.click(screen.getByRole("row", { name: /ETH-240628/ }));
		await act(async () => {
			await new Promise((resolve) => setTimeout(resolve, 50));
		});
		const settledCommits = commits;
		await act(async () => {
			await new Promise((resolve) => setTimeout(resolve, 50));
		});

		// Assert: строка осталась выделенной, рендер прекратился.
		expect(screen.getByRole("row", { name: /ETH-240628/ })).toHaveAttribute("aria-selected", "true");
		expect(commits).toBe(settledCommits);
	});

	it("оформляет титул раздела по дизайн-системе: Inter 21/600", async () => {
		// Титул раздела — H1 дизайн-экранов Body #1..#8 ($font 21/600),
		// а не дефолтный text-2xl (24px): кегль сведён к дизайн-токену.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		// Act: открытие раздела.
		renderPage();

		// Assert: титул несёт утилиту page-title (Inter 21/600) дизайн-системы.
		const heading = await screen.findByRole("heading", { name: "Конструкции", level: 1 });
		expect(heading.className).toContain("page-title");
		expect(heading.className).not.toContain("text-2xl");
	});

	it("шапка итога повторяет мастер ud07u: 20/600 accentStrong, подписи 12.5/11.5, паддинги [14,16]", async () => {
		// Шапка итога журнала сверена с мастером ud07u design.pen (после
		// синхронизации К1): акцентное значение 20/600 $accentStrong (J3jIaE),
		// разбивка 12.5 $textSecondary (g2EeYx), котировки 11.5 $textMuted
		// (otg5U), паддинги [14,16], межстрочный зазор 3.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		// Traceability: change:reconcile-frontend-with-design/design#D2
		// Act: открытие раздела.
		renderPage();

		// Assert: значение итога — 20/600 на акцентном токене; триггер подсказки
		// оборачивает значение целиком, типографика наследуется от абзаца.
		const value = await screen.findByText("+100.5 USDT");
		const valueParagraph = value.closest("p");
		expect(valueParagraph?.className).toContain("text-[20px]");
		expect(valueParagraph?.className).toContain("font-semibold");
		expect(valueParagraph?.className).toContain("text-accent-strong");

		// Блок итога: одна строка по референсу design.pen — паддинги [14,16],
		// сегменты по базовой линии, перенос на узком экране.
		// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
		const summary = value.closest('[data-slot="constructions-summary"]');
		expect(summary).not.toBeNull();
		expect(summary?.className).toContain("flex-wrap");
		expect(summary?.className).toContain("items-baseline");
		expect(summary?.className).toContain("px-4");
		expect(summary?.className).toContain("py-3.5");

		// Подписи: разбивка 12.5 $textSecondary (g2EeYx); котировки —
		// 11.5 $textMuted (otg5U) отдельным сегментом той же строки.
		const breakdown = screen.getByText(/реализов\. \+60\.25 \/ нереализов\. \+40\.25/);
		expect(breakdown.className).toContain("text-[12.5px]");
		expect(breakdown.className).toContain("text-text-secondary");
		const quotes = screen.getByText(/котировки на/i);
		expect(quotes.className).toContain("text-[11.5px]");
		expect(quotes.className).toContain("text-text-muted");
	});

	it("подсказка «Итога журнала» раскрывает назначение и разделы разбивки", async () => {
		// Числовая метрика сводки шапки — значение итога журнала: наведение
		// открывает попап с назначением и разделами реализованного и
		// нереализованного PnL из справочника; строка разбивки отдельного
		// триггера не имеет, её показатели раскрыты разделами попапа.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
		// Traceability: openspec:ui/screens#scenario-multi-indicator-sections
		// Arrange: пользователь для наведения, открытый раздел.
		const user = userEvent.setup();
		renderPage();

		// Act: наведение на значение итога журнала в шапке сводки.
		await user.hover(await screen.findByText("+100.5 USDT"));

		// Assert: попап с заголовком метрики и обоими разделами разбивки.
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.journalTotal.title);
		for (const section of METRIC_TOOLTIPS.journalTotal.sections ?? []) {
			expect(tooltip).toHaveTextContent(section.label);
			expect(tooltip).toHaveTextContent(section.text);
		}
	});

	it("счётчики шапки раскрывают подсказку с назначением счётчиков", async () => {
		// Счётчики «конструкций N · открыто N · архив N» — числовые
		// показатели шапки: наведение открывает подсказку с назначением
		// счётчиков из справочника.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
		// Arrange: пользователь для наведения, открытый раздел.
		const user = userEvent.setup();
		renderPage();

		// Act: наведение на строку счётчиков в шапке.
		await user.hover(await screen.findByText(/конструкций \d+ · открыто \d+ · архив \d+/));

		// Assert: попап с назначением счётчиков из справочника.
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.constructionCounters.title);
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.constructionCounters.purpose);
	});

	it("не показывает кнопку «Разобрать входящие» в тулбаре раздела", async () => {
		// По решению раунд-сверки формы «Конструкции» кнопка «Разобрать
		// входящие» из тулбара раздела убрана: вход в разбор — через раздел
		// «Входящие» панели навигации.
		// Traceability: change:rework-contextual-topbar/tasks#2.0
		// Act: открытие раздела.
		renderPage();

		// Assert: таблица открыта, ссылки «Разобрать входящие» в тулбаре нет.
		expect(await screen.findByRole("row", { name: /ETH-240628/ })).toBeInTheDocument();
		expect(screen.queryByRole("link", { name: /разобрать входящие/i })).not.toBeInTheDocument();
	});

	it("показывает счётчики конструкций рядом с титулом в формате макета", async () => {
		// Счётчики по решению раунд-сверки перенесены из строки итога сводки
		// к H1 тулбара в формате макета: «конструкций N · открыто N · архив N»,
		// архив — все минус открытые; 12.5 $textSecondary (мастер NwnY4).
		// Считаются по полному набору журнала, независимо от области
		// видимости списка.
		// Traceability: change:rework-contextual-topbar/tasks#2.0
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		// Act: открытие раздела.
		renderPage();

		// Assert: счётчики следуют сразу за титулом H1 и несут токены макета;
		// текст счётчиков обёрнут триггером подсказки — токены проверяются
		// на абзаце-контейнере.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
		const countersText = await screen.findByText("конструкций 3 · открыто 1 · архив 2");
		const counters = countersText.closest("p");
		expect(counters).toHaveAttribute("data-slot", "constructions-counters");
		expect(counters?.className).toContain("text-[12.5px]");
		expect(counters?.className).toContain("text-text-secondary");
		const heading = screen.getByRole("heading", { name: "Конструкции", level: 1 });
		expect(heading.nextElementSibling).toBe(counters);
	});

	it("показывает шапку с итогом, разбивкой и котировками без счётчиков", async () => {
		// Строка итога сводки после раунд-сверки несёт только итог с
		// разбивкой и отметку котировок; счётчики переехали к титулу тулбара.
		// Traceability: change:rework-contextual-topbar/tasks#2.0
		// Act: открытие раздела.
		renderPage();

		// Assert: шапка несёт итог с разбивкой и котировки, счётчиков в сводке нет.
		expect(await screen.findByText("+100.5 USDT")).toBeInTheDocument();
		expect(screen.getByText("+60.25")).toBeInTheDocument();
		expect(screen.getByText("+40.25")).toBeInTheDocument();
		expect(screen.getByText(/котировки на/i)).toBeInTheDocument();
		expect(screen.queryByText(/3 конструкции/)).not.toBeInTheDocument();
		expect(screen.getByRole("button", { name: /синхронизировать/i })).toBeInTheDocument();
	});

	it("по умолчанию показывает только открытые конструкции; счётчики — по полному набору", async () => {
		// Область видимости «Открытые» — дефолт раздела: закрытые и
		// архивные строки скрыты из таблицы, но счётчики и итог сводки
		// считаются по всему журналу.
		// Traceability: openspec:ui/screens#scenario-list-scope-open-default
		// Act: открытие раздела без параметров URL.
		renderPage();

		// Assert: открытая строка видна, закрытая и архиваемая — нет.
		expect(await screen.findByRole("row", { name: /ETH-240628/ })).toBeInTheDocument();
		expect(screen.queryByRole("row", { name: /BTC-240531/ })).not.toBeInTheDocument();
		expect(screen.queryByRole("row", { name: /SOL-240607/ })).not.toBeInTheDocument();

		// Сегмент: активна пилюля «Открытые», параметр scope в URL не записан.
		expect(screen.getByRole("button", { name: "Открытые" })).toHaveAttribute("aria-pressed", "true");
		expect(screen.getByRole("button", { name: "Все" })).toHaveAttribute("aria-pressed", "false");
		expect(lastSearch).not.toContain("scope");

		// Счётчики — по полному набору из API.
		expect(screen.getByText("конструкций 3 · открыто 1 · архив 2")).toBeInTheDocument();
	});

	it("переключение на «Все» показывает закрытые и архивные строки, не меняя счётчики", async () => {
		// Область видимости «Все» показывает весь журнал, включая
		// архивные конструкции; сводка раздела не пересчитывается.
		// Traceability: openspec:ui/screens#scenario-list-scope-all-shows-archived
		// Arrange: пользователь для переключения сегмента.
		const user = userEvent.setup();
		renderPage();
		await screen.findByRole("row", { name: /ETH-240628/ });

		// Act: переключение на «Все».
		await user.click(screen.getByRole("button", { name: "Все" }));

		// Assert: показаны все три строки, активна пилюля «Все».
		expect(await screen.findByRole("row", { name: /BTC-240531/ })).toBeInTheDocument();
		expect(screen.getByRole("row", { name: /SOL-240607/ })).toBeInTheDocument();
		expect(screen.getByRole("row", { name: /ETH-240628/ })).toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Все" })).toHaveAttribute("aria-pressed", "true");
		expect(screen.getByRole("button", { name: "Открытые" })).toHaveAttribute("aria-pressed", "false");

		// Область видимости записана в URL; счётчики не изменились.
		expect(lastSearch).toContain("scope=all");
		expect(screen.getByText("конструкций 3 · открыто 1 · архив 2")).toBeInTheDocument();

		// Обратное переключение убирает параметр scope из URL.
		await user.click(screen.getByRole("button", { name: "Открытые" }));
		await waitFor(() => expect(screen.queryByRole("row", { name: /SOL-240607/ })).not.toBeInTheDocument());
		expect(lastSearch).not.toContain("scope");
	});

	it("клик по строке выделяет её, пишет URL и разворачивает панель с краткой карточкой", async () => {
		// Клик по строке — выделение конструкции: параметр ?construction
		// в URL, правая контекстная панель каркаса разворачивается и
		// показывает краткую карточку выделенной конструкции.
		// Traceability: openspec:ui/screens#scenario-list-row-click-selects
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Arrange: пользователь для клика по строке.
		const user = userEvent.setup();
		renderPageInFrame();
		const row = await screen.findByRole("row", { name: /ETH-240628/ });

		// Act: клик по строке.
		await user.click(row);

		// Assert: строка выделена, URL несёт идентификатор конструкции.
		expect(screen.getByRole("row", { name: /ETH-240628/ })).toHaveAttribute("aria-selected", "true");
		expect(lastSearch).toContain("construction=7");

		// Панель каркаса развернулась с краткой карточкой: шапка, средний
		// индикатор финрезультата, метрики, период и единственное действие.
		const panel = screen.getByTestId("context-panel");
		expect(await within(panel).findByRole("heading", { name: /ETH-240628/ })).toBeInTheDocument();
		await waitFor(() => expect(panel.querySelector('[data-slot="fin-result-medium"]')).not.toBeNull());
		expect(within(panel).getByText("капитал")).toBeInTheDocument();
		expect(within(panel).getByText("реализов.")).toBeInTheDocument();
		expect(within(panel).getByText("нереализов.")).toBeInTheDocument();
		expect(within(panel).getByText(/живых подсказок: 2/)).toBeInTheDocument();
		const cardLink = within(panel).getByRole("link", { name: /открыть карточку/i });
		expect(cardLink).toHaveAttribute("href", "/constructions/7");
		// Открытие в новом окне — отдельная команда двойного клика по
		// строке; в карточке такой кнопки больше нет.
		expect(within(panel).queryByText(/в новом окне/i)).not.toBeInTheDocument();
	});

	it("повторный клик по выделенной строке снимает выделение и сворачивает панель", async () => {
		// Повторный клик по уже выделенной строке — снятие выделения:
		// параметр удаляется из URL, контекстная панель сворачивается.
		// Traceability: openspec:ui/screens#scenario-list-second-click-collapses-context-panel
		// Arrange: выделенная строка с развёрнутой панелью.
		const user = userEvent.setup();
		renderPageInFrame();
		await user.click(await screen.findByRole("row", { name: /ETH-240628/ }));
		await waitFor(() => expect(screen.getByTestId("context-panel").querySelector('[data-slot="fin-result-medium"]')).not.toBeNull());

		// Act: повторный клик по той же строке.
		await user.click(screen.getByRole("row", { name: /ETH-240628/ }));

		// Assert: выделение снято, панель каркаса скрыта.
		expect(screen.getByRole("row", { name: /ETH-240628/ })).toHaveAttribute("aria-selected", "false");
		expect(lastSearch).not.toContain("construction");
		await waitFor(() => expect(screen.queryByTestId("context-panel")).not.toBeInTheDocument());
	});

	it("клик по другой строке переключает выделение без сворачивания панели", async () => {
		// Клик по другой строке переключает выделение: панель остаётся
		// развёрнутой, карточка перечитывается для новой конструкции.
		// Traceability: openspec:ui/screens#scenario-list-row-click-selects
		// Arrange: пользователь, область видимости «Все» и две строки.
		const user = userEvent.setup();
		renderPageInFrame();
		await user.click(await screen.findByRole("row", { name: /ETH-240628/ }));
		await user.click(screen.getByRole("button", { name: "Все" }));
		await screen.findByRole("row", { name: /BTC-240531/ });
		api.constructions.fetchConstructionPreview.mockResolvedValue({ ...preview, constructionId: 8, name: "BTC-240531-60000C" });

		// Act: клик по другой строке.
		await user.click(screen.getByRole("row", { name: /BTC-240531/ }));

		// Assert: выделение переключилось, карточка в панели сменилась.
		expect(lastSearch).toContain("construction=8");
		const panel = screen.getByTestId("context-panel");
		expect(await within(panel).findByRole("heading", { name: /BTC-240531/ })).toBeInTheDocument();
	});

	it("двойной клик по строке открывает карточку конструкции в новом окне", async () => {
		// Двойной клик — отдельная команда навигации: карточка
		// конструкции открывается в новом окне браузера.
		// Traceability: openspec:ui/screens#scenario-list-double-click-opens-new-window
		// Arrange: шпион на window.open.
		const user = userEvent.setup();
		const openSpy = vi.spyOn(window, "open").mockReturnValue(null);
		renderPage();
		const row = await screen.findByRole("row", { name: /ETH-240628/ });

		// Act: двойной клик по строке.
		await user.dblClick(row);

		// Assert: карточка запрошена в новом окне по корне-относительному пути.
		// Traceability: adr:docs/adr/0013-spa-served-from-host-root.md#fixed-mechanics
		expect(openSpy).toHaveBeenCalledWith("/constructions/7", "_blank");
		openSpy.mockRestore();
	});

	it("иконная кнопка тулбара запускает проход подсказок и обновляет бейджи", async () => {
		// Ручной проход подсказок перенесён в тулбар рядом с
		// синхронизацией: команда POST /hints/pass, по завершению
		// перечитываются бейджи живых подсказок строк обзора.
		// Traceability: openspec:ui/screens#scenario-ui-manual-pass-runs-engine
		// Arrange: пользователь и исходное число чтений обзора.
		const user = userEvent.setup();
		renderPage();
		await screen.findByRole("row", { name: /ETH-240628/ });
		const readsBefore = api.constructions.fetchConstructionsOverview.mock.calls.length;

		// Act: нажатие иконной кнопки прохода.
		await user.click(screen.getByRole("button", { name: "Запустить проход подсказок" }));

		// Assert: команда выполнена, итог показан, обзор перечитан.
		await waitFor(() => expect(api.hints.runHintsPass).toHaveBeenCalled());
		expect(await screen.findByText(/проход завершён: создано 2, погашено 1/i)).toBeInTheDocument();
		expect(api.constructions.fetchConstructionsOverview.mock.calls.length).toBeGreaterThan(readsBefore);
	});

	it("показывает проблемы корпуса при неуспешном проходе подсказок", async () => {
		// Корпус правил не валиден — владелец видит агрегированный
		// список проблем карточек вместо счётчиков исхода.
		// Traceability: openspec:ui/screens#scenario-ui-manual-pass-corpus-error
		// Arrange: проход отвечает исходом corpus-invalid со списком проблем.
		const user = userEvent.setup();
		api.hints.runHintsPass.mockResolvedValue({
			outcome: "corpus-invalid",
			asOf: "2026-06-20T14:30:00",
			createdHints: 0,
			expiredHints: 0,
			diagnostics: ["Карточка 3: риск не согласован с границами", "Карточка 7: профит без плановой границы"],
			unimplementedRuleIds: [],
		});
		renderPage();
		await screen.findByRole("row", { name: /ETH-240628/ });

		// Act: нажатие кнопки прохода.
		await user.click(screen.getByRole("button", { name: "Запустить проход подсказок" }));

		// Assert: алерт с заголовком и списком проблем корпуса.
		await screen.findByText("Корпус правил невалиден:");
		const alert = screen.getByRole("alert");
		expect(within(alert).getByText("Карточка 3: риск не согласован с границами")).toBeInTheDocument();
		expect(within(alert).getByText("Карточка 7: профит без плановой границы")).toBeInTheDocument();
	});

	it("запускает синхронизацию из шапки и показывает итог запуска", async () => {
		// Arrange: пользователь для нажатия кнопки.
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("+100.5 USDT");

		// Act: команда синхронизации.
		await user.click(screen.getByRole("button", { name: /синхронизировать/i }));

		// Assert: запуск выполнен, итог закрытого запуска показан.
		await waitFor(() => expect(api.sync.runSync).toHaveBeenCalled());
		expect(await screen.findByText(/синхронизация завершена/i)).toBeInTheDocument();
	});

	it("показывает состояние недоступности при сбое чтения журнала", async () => {
		// Arrange: обзор журнала отвечает ошибкой API.
		api.constructions.fetchConstructionsOverview.mockRejectedValue(Object.assign(new Error("сырьё повреждено"), { status: 503 }));
		renderPage();

		// Assert: раздел показывает явное состояние недоступности.
		expect(await screen.findByText(/журнал недоступен/i)).toBeInTheDocument();
		expect(screen.queryByRole("table")).not.toBeInTheDocument();
	});

	it("в мобильном режиме открывает краткую карточку выделенной конструкции в drawer", async () => {
		// На мобильном правая контекстная панель каркаса скрыта:
		// краткая карточка выделенной строки открывается в drawer-шторке.
		// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
		// Arrange: мобильный вьюпорт, пользователь и выделенная строка.
		setViewport(390);
		const user = userEvent.setup();
		renderPage();
		// На мобильном строки таблицы — карточки-кнопки, а не строки таблицы.
		await user.click(await screen.findByRole("button", { name: /ETH-240628/ }));

		// Act: кнопка открытия краткой карточки.
		await user.click(screen.getByRole("button", { name: /открыть краткую карточку/i }));

		// Assert: drawer показывает карточку конструкции.
		expect(await screen.findByRole("heading", { name: "Краткая карточка" })).toBeInTheDocument();
		expect(screen.getByRole("heading", { name: /ETH-240628/ })).toBeInTheDocument();
		expect(screen.getByText("капитал")).toBeInTheDocument();

		// Закрытие шторки прячет карточку.
		await user.click(screen.getByRole("button", { name: "Закрыть" }));
		expect(screen.queryByRole("heading", { name: "Краткая карточка" })).not.toBeInTheDocument();
	});

	it("мобильный экран показывает блок «Итог» вместо H1-тулбара и шапки сводки", async () => {
		// Решения раунд-сверки 10.0 (мастер iGlhI «Экран/Mobile ·
		// Конструкции»): сразу под топбаром прозрачный блок «Итог» — итог
		// 18/600 $accentStrong, разбивка с котировками 11 $textSecondary,
		// счётчики «конструкций N · открыто N» 11 $textMuted без «архива»
		// (источник — сводка обзора, решение 2.0); H1-тулбар с областью
		// видимости и синхронизацией скрыт, список идёт сразу за блоком.
		// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
		// Traceability: change:rework-contextual-topbar/tasks#2.0
		// Traceability: change:rework-contextual-topbar/tasks#10.0
		// Arrange: мобильный вьюпорт 390.
		setViewport(390);
		renderPage();

		// Assert: блок «Итог» — три строки с токенами мастера iGlhI.
		const block = await screen.findByTestId("constructions-mobile-summary");
		const total = within(block).getByText("+100.5 USDT");
		// Триггер подсказки оборачивает значение целиком, типографика
		// наследуется от абзаца блока «Итог».
		const totalParagraph = total.closest("p");
		expect(totalParagraph?.className).toContain("text-[18px]");
		expect(totalParagraph?.className).toContain("font-semibold");
		expect(totalParagraph?.className).toContain("text-accent-strong");
		const breakdown = within(block).getByText(/реализов\. \+60\.25 \/ нереализов\. \+40\.25 · котировки \d{2}:\d{2}/);
		expect(breakdown.className).toContain("text-[11px]");
		expect(breakdown.className).toContain("text-text-secondary");
		// Текст счётчиков обёрнут триггером подсказки — токены проверяются
		// на абзаце-контейнере.
		const countersText = within(block).getByText("конструкций 3 · открыто 1");
		const counters = countersText.closest("p");
		expect(counters?.className).toContain("text-[11px]");
		expect(counters?.className).toContain("text-text-muted");

		// Assert: H1-тулбар скрыт — титул, счётчики-строка, сегмент области,
		// синхронизация и десктопная шапка сводки отсутствуют.
		expect(screen.queryByRole("heading", { name: "Конструкции", level: 1 })).not.toBeInTheDocument();
		expect(screen.queryByText("конструкций 3 · открыто 1 · архив 2")).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Открытые" })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: /синхронизировать/i })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Запустить проход подсказок" })).not.toBeInTheDocument();
		expect(screen.queryByTestId("constructions-summary")).not.toBeInTheDocument();

		// Assert: список следует сразу за блоком — мобильные карточки строк.
		expect(screen.getByRole("button", { name: /ETH-240628/ })).toBeInTheDocument();
	});
});
