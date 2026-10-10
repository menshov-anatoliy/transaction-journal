import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionsOverview, ConstructionsSummary } from "@/lib/api/constructions";
import { appSections } from "@/config/sections";
import { AppLayout } from "./AppLayout";
import { useContextPanel } from "./context-panel";

vi.mock("@/lib/api/inbox", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/inbox")>()),
	fetchInboxCount: vi.fn(),
}));

vi.mock("@/lib/api/constructions", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/constructions")>()),
	fetchConstructionsOverview: vi.fn(),
}));

const inboxApi = vi.mocked(await import("@/lib/api/inbox"));
const constructionsApi = vi.mocked(await import("@/lib/api/constructions"));

// Геометрия панелей переживает перезагрузку в localStorage, поэтому между
// тестами файла накопленное состояние обязательно сбрасывается, иначе
// свёрнутость из прошлого теста искажает следующий рендер.
beforeEach(() => {
	localStorage.clear();
	inboxApi.fetchInboxCount.mockResolvedValue(0);
	constructionsApi.fetchConstructionsOverview.mockResolvedValue(makeOverview());
	setViewport(1280);
});

// Проверяется каркас приложения по концепции §2: тонкий топбар с брендом
// «Журнал Bybit» и компактным итогом журнала (разбивка P&L видна на любом
// экране), левая панель с пятью разделами и сворачивание панели до иконок.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
// Слой сверки с design.pen (задача 3.1): топбар, бренд и нав-пункты «Каркаса»
// g0z20 перенесены по мастер-нодам jGvOM/WBYbU (54, [0,24], 14/600) и
// примитиву NavItem (apcZH/Vx9C2/q35Tj9), бейдж — на акцентной паре мастера.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
// Traceability: change:reconcile-frontend-with-design/design#D2

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

/**
 * Экран-проверка: регистрирует контекст правой панели, чтобы каркас
 * показал контекстную зону.
 */
function ContextRegisteringScreen() {
	useContextPanel({ content: <div>контекст экрана</div> });
	return <div>главная</div>;
}

function renderLayout(withContext = false) {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	const router = createMemoryRouter(
		[{
			element: <AppLayout />,
			children: [
				{ index: true, element: withContext ? <ContextRegisteringScreen /> : <div>главная</div> },
				{ path: "hints", element: <h1>Подсказки</h1> },
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
	it("показывает бренд «Журнал Bybit» в тонком топбаре", () => {
		inboxApi.fetchInboxCount.mockResolvedValue(0);
		setViewport(1280);
		renderLayout();

		expect(screen.getByText("Журнал Bybit")).toBeInTheDocument();
	});

	it("несёт геометрию топбара мастера g0z20: высота 54, паддинги [0,24], фон surface", () => {
		inboxApi.fetchInboxCount.mockResolvedValue(0);
		setViewport(1280);
		renderLayout();

		// Assert: топбар по мастер-ноде jGvOM «Каркаса» (высота 54, [0,24],
		// заливка $surface, нижний разделитель $divider).
		const header = screen.getByRole("banner");
		expect(header.className).toContain("h-[54px]");
		expect(header.className).toContain("px-6");
		expect(header.className).toContain("bg-surface");
		expect(header.className).toContain("border-divider");
	});

	it("показывает бренд типографикой мастера WBYbU: Inter 14/600", () => {
		inboxApi.fetchInboxCount.mockResolvedValue(0);
		setViewport(1280);
		renderLayout();

		// Assert: бренд 14/600 без приписного трекинга (text-sm = 14px).
		const brand = screen.getByText("Журнал Bybit");
		expect(brand.className).toContain("text-sm");
		expect(brand.className).toContain("font-semibold");
		expect(brand.className).not.toContain("tracking-tight");
	});

	it("рендерит пункты панели примитивом NavItem по мастер-ноде apcZH", () => {
		inboxApi.fetchInboxCount.mockResolvedValue(0);
		setViewport(1280);
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
		inboxApi.fetchInboxCount.mockResolvedValue(0);
		setViewport(1280);
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
		inboxApi.fetchInboxCount.mockResolvedValue(0);
		setViewport(1280);
		renderLayout();

		expect(screen.getByRole("link", { name: section.label })).toBeInTheDocument();
	});

	it("сворачивает панель навигации до иконок", async () => {
		inboxApi.fetchInboxCount.mockResolvedValue(0);
		setViewport(1280);
		const user = userEvent.setup();
		renderLayout();

		await user.click(screen.getByRole("button", { name: /панель навигации/i }));

		for (const section of appSections) {
			expect(screen.queryByText(section.label)).not.toBeInTheDocument();
		}
		expect(screen.getAllByRole("link")).toHaveLength(appSections.length);
	});

	it("показывает бейдж входящих при ненулевом счётчике", async () => {
		inboxApi.fetchInboxCount.mockResolvedValue(3);
		setViewport(1280);
		renderLayout();

		expect(await screen.findByText("3")).toBeInTheDocument();
	});

	it("рисует бейдж входящих примитивом NavItem по мастер-ноде q35Tj9", async () => {
		inboxApi.fetchInboxCount.mockResolvedValue(7);
		setViewport(1280);
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
		inboxApi.fetchInboxCount.mockResolvedValue(0);
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

// Итог журнала в топбаре каркаса: разбивка на реализованный и
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

// Геометрия и управление панелями каркаса: сворачивание живёт иконочной
// колонкой у внутреннего края панели, ширины ограничены диапазонами и
// восстанавливаются из localStorage между сессиями.
// Traceability: openspec:ui/screens#scenario-frame-collapse-icon-in-panel-column
// Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
// Traceability: change:rework-spa-shell-and-screens/design#D1
describe("панели каркаса", () => {
	it("кнопка сворачивания левой панели — последняя иконка её колонки", () => {
		renderLayout();

		// Assert: в иконочной колонке левой панели кнопка сворачивания стоит
		// последней; в топбаре панельных кнопок больше нет.
		const column = document.querySelector('[data-slot="panel-icon-column"][data-side="left"]');
		expect(column).not.toBeNull();
		const buttons = Array.from(column!.querySelectorAll("button"));
		const toggle = screen.getByRole("button", { name: "Свернуть панель навигации" });
		expect(buttons).not.toHaveLength(0);
		expect(buttons[buttons.length - 1]).toBe(toggle);
	});

	it("топбар не содержит кнопок сворачивания панелей", () => {
		renderLayout();

		// Assert: у топбара не осталось ни одной панельной кнопки — сворачивание
		// переехало в иконочные колонки панелей.
		const bannerButtons = Array.from(screen.getByRole("banner").querySelectorAll("button"));
		for (const button of bannerButtons) {
			expect(button.textContent).not.toMatch(/панел[ьи]/i);
			expect(button.getAttribute("aria-label") ?? "").not.toMatch(/панел[ьи]/i);
		}
	});

	it("восстанавливает геометрию левой панели из localStorage на монтировании", async () => {
		const user = userEvent.setup();
		// Arrange: в прошлой сессии панель сужена до 10 rem и свёрнута.
		localStorage.setItem("tj.layout.left", JSON.stringify({ width: 10, collapsed: true }));

		renderLayout();

		// Assert: панель снова свёрнута (подписи скрыты), после разворачивания
		// восстанавливается и сохранённая ширина 10 rem.
		const panel = screen.getByTestId("layout-panel-left");
		expect(panel.style.width).toBe("6.5rem");
		for (const section of appSections) {
			expect(screen.queryByText(section.label)).not.toBeInTheDocument();
		}
		await user.click(screen.getByRole("button", { name: "Развернуть панель навигации" }));
		expect(panel.style.width).toBe("10rem");
	});

	it("сплиттер левой панели меняет ширину стрелками и сохраняет её", async () => {
		const user = userEvent.setup();
		renderLayout();

		// Act: растягиваем левую панель клавиатурой (drag в jsdom недоступен).
		const panel = screen.getByTestId("layout-panel-left");
		expect(panel.style.width).toBe("16rem");
		const separator = screen.getByRole("separator", { name: "Изменить ширину панели навигации" });
		separator.focus();
		await user.keyboard("{ArrowRight}");

		// Assert: ширина выросла на шаг 0.5 rem, значение сохранено, URL чист.
		expect(panel.style.width).toBe("16.5rem");
		const stored = JSON.parse(localStorage.getItem("tj.layout.left") ?? "{}") as { width?: number };
		expect(stored.width).toBe(16.5);
		expect(window.location.search).toBe("");
	});

	it("ограничивает ширину правой панели диапазоном 20–40 rem стрелками", async () => {
		const user = userEvent.setup();
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 20, collapsed: false }));
		renderLayout(true);

		// Act: панель уже на нижней границе 20 rem, пробуем сузить дальше.
		const panel = screen.getByTestId("context-panel");
		const separator = screen.getByRole("separator", { name: "Изменить ширину контекстной панели" });
		separator.focus();
		await user.keyboard("{ArrowRight}");

		// Assert: для правой панели ArrowRight сужает, clamp не даёт уйти ниже 20.
		expect(panel.style.width).toBe("20rem");
	});
});
