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
// Дизайн-слой задачи 7.3: фильтры на примитивах-полях, чекбоксы
// дизайн-примитивов, панель целей и выделение строк по Body #5 (TECU5).
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

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
		// Титул раздела — Inter 21/600 дизайн-системы, не дефолтный text-2xl.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		expect(screen.getByRole("heading", { name: "Входящие", level: 1 }).className).toContain("page-title");
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

	it("рендерит строку фильтров примитивами дизайн-системы", async () => {
		renderPage();
		await screen.findByText("exec-buy");

		// Поля дат — примитивы-поля мастера Body #5: радиус 8, [7,10], Inter 12.
		// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
		const from = screen.getByLabelText("Дата с");
		expect(from).toHaveAttribute("data-slot", "input");
		expect(from.className).toContain("rounded-sm");
		expect(from.className).toContain("py-[7px]");
		expect(from.className).toContain("text-xs");

		// Чекбоксы направления — дизайн-примитив: 16×16, радиус 4.
		const buy = screen.getByLabelText(/покупка/i);
		expect(buy.closest("[data-slot='checkbox']")).not.toBeNull();
		expect(buy.className).toContain("rounded-[4px]");
		expect(buy.className).toContain("checked:bg-primary");

		// Кнопка сброса — Ghost-инстанс мастера (b0ZLfV).
		expect(screen.getByRole("button", { name: "Сбросить" })).toBeInTheDocument();
	});

	it("сбрасывает фильтры кнопкой «Сбросить»", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("exec-buy");

		// Arrange: снимаем инструмент BTCUSDT — сделка скрывается фильтром.
		await user.click(screen.getByLabelText("BTCUSDT"));
		expect(screen.queryByText("exec-buy")).not.toBeInTheDocument();

		// Act: сброс фильтров к окну по умолчанию.
		await user.click(screen.getByRole("button", { name: "Сбросить" }));

		// Assert: фильтр инструментов снова «все», сделка видна.
		expect(await screen.findByText("exec-buy")).toBeInTheDocument();
	});

	it("тонирование направления и выбор строки по мастеру Body #5", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("exec-buy");

		// Направление тонировано: покупка — $info, продажа — $risk.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		expect(screen.getByText("покупка", { selector: "span" }).className).toContain("text-info");
		expect(screen.getByText("продажа", { selector: "span" })).toHaveClass("text-risk");

		// Act: выбор строки чекбоксом дизайн-примитива.
		const row = screen.getByRole("row", { name: /exec-buy/i });
		await user.click(within(row).getByRole("checkbox"));

		// Assert: строка получает выделение $accentSofter (Row:1 мастера).
		expect(row).toHaveAttribute("data-state", "selected");
		expect(row.className).toContain("data-[state=selected]:bg-accent-softer");

		// Блок выбранного мастера: счётчик 12/600 и объём 11 textMuted.
		expect(screen.getByText("Выбрано: 1 сделка")).toBeInTheDocument();
		expect(screen.getByText("+450 объём")).toBeInTheDocument();
	});

	it("оформляет панель целей по мастеру: подпись, пилюли радиуса 9, мета", async () => {
		renderPage();
		await screen.findByText("exec-buy");

		// Заголовок-подпись (m9kwpl): Inter 10, letterSpacing 0.5, textMuted.
		const caption = screen.getByRole("heading", { name: "КОНСТРУКЦИИ-ЦЕЛИ" });
		expect(caption.className).toContain("tracking-[0.5px]");
		expect(caption.className).toContain("text-text-muted");

		// Пилюля-цель (VZPxZ): радиус 9, паддинги [9,11], имя 12.5/500.
		const pill = screen.getByRole("button", { name: /Календарь ETH/i });
		expect(pill.className).toContain("rounded-[9px]");
		expect(pill.className).toContain("py-[9px]");
		expect(pill.className).toContain("px-[11px]");
		expect(within(pill).getByText("Календарь ETH").className).toContain("text-[12.5px]");
		expect(within(pill).getByText(/открыта · итог \+100\.5/)).toBeInTheDocument();
	});

	it("в мобильном режиме оставляет только просмотр без тяжёлых действий", async () => {
		// Мобильная вёрстка проверяется последней: setViewport подменяет
		// window.matchMedia на 390px для всех последующих тестов файла.
		setViewport(390);
		renderPage();

		expect(await screen.findByText("exec-buy")).toBeInTheDocument();
		expect(screen.getByText(/мобильный режим:/i)).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: /Создать конструкцию из выбранного/i })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: /Собрать из Входящих/i })).not.toBeInTheDocument();
	});
});
