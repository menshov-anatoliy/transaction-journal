import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { appSections } from "@/config/sections";
import { AppLayout } from "./AppLayout";

vi.mock("@/lib/api/inbox", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/inbox")>()),
	fetchInboxCount: vi.fn(),
}));

const inboxApi = vi.mocked(await import("@/lib/api/inbox"));

// Проверяется каркас приложения по концепции §2: тонкий топбар с брендом
// «Журнал Bybit» (итог журнала здесь не размещается), левая панель с пятью
// разделами и сворачивание панели до иконок.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
// Слой сверки с design.pen (задача 3.1): топбар, бренд и нав-пункты «Каркаса»
// g0z20 перенесены по мастер-нодам jGvOM/WBYbU (54, [0,24], 14/600) и
// примитиву NavItem (apcZH/Vx9C2/q35Tj9), бейдж — на акцентной паре мастера.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
// Traceability: change:reconcile-frontend-with-design/design#D2

function renderLayout() {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	const router = createMemoryRouter(
		[{
			element: <AppLayout />,
			children: [
				{ index: true, element: <div>главная</div> },
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
