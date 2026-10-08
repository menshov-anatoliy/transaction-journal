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
