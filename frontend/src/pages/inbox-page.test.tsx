import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { InboxPage } from "./inbox-page";

// Экран «Входящие» проверяется как пользовательский сценарий: фильтры,
// выбор непривязанных сделок, привязка к цели, создание конструкции и
// инкрементальная сборка из входящих.
// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

vi.mock("@/lib/api/inbox", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/inbox")>()),
	fetchInboxOverview: vi.fn(),
	bindInboxTrades: vi.fn(),
	createConstructionFromInbox: vi.fn(),
	assembleInbox: vi.fn(),
}));

const api = vi.mocked(await import("@/lib/api/inbox"));

function renderPage() {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<MemoryRouter initialEntries={["/inbox?from=2026-09-01&to=2026-10-08&sides=buy,sell"]}>
				<Routes>
					<Route path="/inbox" element={<InboxPage />} />
				</Routes>
			</MemoryRouter>
		</QueryClientProvider>,
	);
}

function clickSelectAllInTable(user: ReturnType<typeof userEvent.setup>) {
	const table = screen.getByRole("table");
	const selectAll = within(table).getAllByRole("checkbox")[0];
	return user.click(selectAll);
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
	api.fetchInboxOverview.mockResolvedValue({
		items: [
			{
				execId: "exec-buy",
				symbol: "BTCUSDT",
				executedAt: "2026-10-08T09:00:00Z",
				isBuy: true,
				quantity: 0.01,
				price: 45000,
				amountUsdt: 450,
				fee: 0.5,
				feeCurrency: "USDT",
			},
			{
				execId: "exec-sell",
				symbol: "ETHUSDT",
				executedAt: "2026-10-08T10:00:00Z",
				isBuy: false,
				quantity: -0.5,
				price: 2400,
				amountUsdt: 1200,
				fee: -0.1,
				feeCurrency: "USDT",
			},
		],
		targets: [{ constructionId: 7, name: "Календарь ETH", status: "open", totalPnL: 100.5 }],
	});
	api.bindInboxTrades.mockResolvedValue(undefined);
	api.createConstructionFromInbox.mockResolvedValue({ constructionId: 9 });
	api.assembleInbox.mockResolvedValue({ constructionsCount: 1, boundCount: 2, tradesInInbox: 0 });
});

describe("раздел «Входящие»", () => {
	it("показывает непривязанные сделки и конструкции-цели", async () => {
		renderPage();
		expect(await screen.findByRole("heading", { name: "Входящие", level: 1 })).toBeInTheDocument();
		expect(await screen.findByText("exec-buy")).toBeInTheDocument();
		expect(screen.getByText("Календарь ETH")).toBeInTheDocument();
	});

	it("фильтрует сделки по направлению и привязывает выбранные кнопкой", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("exec-buy");

		await user.click(screen.getByLabelText(/покупка/i));
		expect(screen.queryByText("exec-buy")).not.toBeInTheDocument();
		expect(screen.getByText("exec-sell")).toBeInTheDocument();

		await clickSelectAllInTable(user);
		await user.click(screen.getByRole("button", { name: /Календарь ETH/i }));
		await waitFor(() => expect(api.bindInboxTrades).toHaveBeenCalledWith(7, ["exec-sell"]));
	});

	it("создаёт конструкцию из выбранного и запускает сборку из входящих", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("exec-buy");

		await clickSelectAllInTable(user);
		await user.type(screen.getByLabelText(/Имя конструкции/i), "Новая конструкция");
		await user.type(screen.getByLabelText(/Капитал/i), "3000");
		await user.click(screen.getByRole("button", { name: /Создать конструкцию из выбранного/i }));

		await waitFor(() => expect(api.createConstructionFromInbox).toHaveBeenCalled());
		expect(api.createConstructionFromInbox.mock.calls[0]?.[0]).toEqual({
			name: "Новая конструкция",
			allocatedCapitalUsdt: 3000,
			execIds: ["exec-buy", "exec-sell"],
		});

		await user.click(screen.getByRole("button", { name: /Собрать из Входящих/i }));
		await waitFor(() => expect(api.assembleInbox).toHaveBeenCalled());
		expect(await screen.findByText(/создано конструкций 1/i)).toBeInTheDocument();
	});

	it("в мобильном режиме оставляет только просмотр без тяжёлых действий", async () => {
		setViewport(390);
		renderPage();

		expect(await screen.findByText("exec-buy")).toBeInTheDocument();
		expect(screen.getByText(/мобильный режим:/i)).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: /Создать конструкцию из выбранного/i })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: /Собрать из Входящих/i })).not.toBeInTheDocument();
	});
});
