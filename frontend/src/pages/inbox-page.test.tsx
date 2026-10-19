import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, RouterProvider, createMemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppLayout } from "@/components/layout/AppLayout";
import type { ConstructionsOverview } from "@/lib/api/constructions";
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
// Контекстный топбар (решения раунд-сверки 4.0): панель «Конструкции-цели»
// проверяется внутри каркаса — регистрация useContextPanel вне Outlet-контекста
// превращается в no-op, поэтому сценарии целей, формы создания и сборки
// рендерят страницу в AppLayout.
// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
// Traceability: change:rework-contextual-topbar/tasks#4.0

vi.mock("@/lib/api/inbox", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/inbox")>()),
	fetchInboxOverview: vi.fn(),
	bindInboxTrades: vi.fn(),
	createConstructionFromInbox: vi.fn(),
	assembleInbox: vi.fn(),
	// Счётчик каркаса (пункт меню и бейдж крошки) — заглушка к проверкам страницы.
	fetchInboxCount: vi.fn(),
}));

// Команды ручных сделок мокаются на уровне API: страница проверяет
// пользовательский сценарий создания, правки и удаления с подтверждением.
vi.mock("@/lib/api/manual-trades", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/manual-trades")>()),
	createManualTrade: vi.fn(),
	editManualTrade: vi.fn(),
	deleteManualTrade: vi.fn(),
}));

// Обзор журнала и журнал синхронизаций читает каркас AppLayout (итог в
// топбаре): заглушки каркасных данных, к проверкам «Входящих» отношения
// не имеют.
vi.mock("@/lib/api/constructions", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/constructions")>()),
	fetchConstructionsOverview: vi.fn(),
}));

vi.mock("@/lib/api/sync", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/sync")>()),
	fetchSyncRuns: vi.fn(),
}));

const api = vi.mocked(await import("@/lib/api/inbox"));
const manualApi = vi.mocked(await import("@/lib/api/manual-trades"));
const constructionsApi = vi.mocked(await import("@/lib/api/constructions"));
const syncApi = vi.mocked(await import("@/lib/api/sync"));

/** Пустой обзор журнала для топбара каркаса: итог и счётчики не проверяются. */
const frameOverview: ConstructionsOverview = {
	summary: {
		totalPnL: 0,
		realizedPnL: 0,
		unrealizedPnL: 0,
		marksAsOf: null,
		hasMarkFailure: false,
		constructionCount: 1,
		openCount: 1,
	},
	items: [],
};

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

/**
 * Страница внутри каркаса: только здесь работает слот контекстной панели
 * (useOutletContext), поэтому содержимое панели «Конструкции-цели»
 * проверяется в этой обвязке.
 */
function renderPageInFrame() {
	setViewport(1280);
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
	});
	const router = createMemoryRouter(
		[{ element: <AppLayout />, children: [{ path: "/inbox", element: <InboxPage /> }] }],
		{ initialEntries: ["/inbox?from=2026-09-01&to=2026-10-08&sides=buy,sell"] },
	);
	return render(
		<QueryClientProvider client={queryClient}>
			<RouterProvider router={router} />
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
	// Геометрия панелей каркаса хранится в localStorage: между тестами
	// файла накопленная свёрнутость/ширина искажают следующий рендер каркаса.
	localStorage.clear();
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
				source: "exchange",
				manualTradeId: null,
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
				source: "exchange",
				manualTradeId: null,
			},
			{
				// Ручная запись: execId отсутствует, ключ строки сырья адресует
				// правку и удаление, бейдж различает источник в таблице.
				execId: null,
				symbol: "BTCUSDT",
				executedAt: "2026-10-08T11:00:00Z",
				isBuy: true,
				quantity: 0.2,
				price: 43000,
				amountUsdt: 8600,
				fee: 0,
				feeCurrency: null,
				source: "manual",
				manualTradeId: 15,
			},
		],
		targets: [{ constructionId: 7, name: "Календарь ETH", status: "open", totalPnL: 100.5 }],
	});
	api.bindInboxTrades.mockResolvedValue(undefined);
	api.createConstructionFromInbox.mockResolvedValue({ constructionId: 9 });
	api.assembleInbox.mockResolvedValue({ constructionsCount: 1, boundCount: 2, tradesInInbox: 0 });
	manualApi.createManualTrade.mockResolvedValue({ id: 15, constructionId: null });
	manualApi.editManualTrade.mockResolvedValue({ id: 15, constructionId: null });
	manualApi.deleteManualTrade.mockResolvedValue(undefined);
	// Данные каркаса AppLayout: счётчик входящих, общий обзор журнала и
	// журнал синхронизаций (статус синхронизации топбара скрыт без запусков).
	api.fetchInboxCount.mockResolvedValue(0);
	constructionsApi.fetchConstructionsOverview.mockResolvedValue(frameOverview);
	syncApi.fetchSyncRuns.mockResolvedValue([]);
});

describe("раздел «Входящие»", () => {
	it("показывает непривязанные сделки и конструкции-цели", async () => {
		// Цели рендерятся в контекстной панели каркаса — страница в каркасе.
		renderPageInFrame();
		expect(await screen.findByRole("heading", { name: "Входящие", level: 1 })).toBeInTheDocument();
		// Титул раздела — Inter 21/600 дизайн-системы, не дефолтный text-2xl.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		expect(screen.getByRole("heading", { name: "Входящие", level: 1 }).className).toContain("page-title");
		expect(await screen.findByText("exec-buy")).toBeInTheDocument();
		expect(screen.getByText("Календарь ETH")).toBeInTheDocument();
	});

	// Регистрация панели «Конструкции-цели» в правой контекстной панели
	// каркаса: содержимое панели и чип короткого названия в сегменте
	// контекста (решение раунд-сверки 4.0, мастер B1GoG1).
	// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
	// Traceability: change:rework-contextual-topbar/tasks#4.0
	it("регистрирует цели в контекстной панели каркаса с чипом «Конструкции-цели»", async () => {
		renderPageInFrame();

		// Assert: содержимое целей живёт в панели каркаса, а не в теле страницы;
		// чип сегмента контекста несёт короткое название «Конструкции-цели».
		// Панель появляется до загрузки обзора, поэтому содержимое ждём
		// отдельно: регистрация обновляется вслед за целями из данных.
		await screen.findByText("exec-buy");
		const panel = await screen.findByTestId("context-panel");
		expect(await within(panel).findByText("Календарь ETH")).toBeInTheDocument();
		expect(within(panel).getByRole("heading", { name: "КОНСТРУКЦИИ-ЦЕЛИ" })).toBeInTheDocument();
		const chip = within(screen.getByTestId("topbar-segment-context")).getByTestId("context-chip");
		expect(chip).toHaveTextContent("Конструкции-цели");
	});

	// Кнопка «Собрать из Входящих» — в строке H1 шапки раздела, справа от
	// счётчика; из панели целей кнопка убрана (решение раунд-сверки 4.0).
	// Traceability: openspec:ui/screens#scenario-inbox-assembly-without-confirmation
	// Traceability: change:rework-contextual-topbar/tasks#4.0
	it("размещает «Собрать из Входящих» в строке H1 шапки раздела, а не в панели целей", async () => {
		renderPageInFrame();
		await screen.findByText("exec-buy");

		// Assert: кнопка — сосед H1 в одной строке шапки, вне контекстной панели.
		const headerRow = screen.getByRole("heading", { name: "Входящие", level: 1 }).closest("div");
		expect(headerRow).not.toBeNull();
		const assembleButton = within(headerRow as HTMLElement).getByRole("button", { name: "Собрать из Входящих" });
		// Вариант Secondary дизайн-системы: белая поверхность с бордером.
		expect(assembleButton.className).toContain("bg-card");
		expect(screen.getByTestId("context-panel").contains(assembleButton)).toBe(false);
		expect(within(screen.getByTestId("context-panel")).queryByRole("button", { name: /Собрать из Входящих/i })).not.toBeInTheDocument();
	});

	it("фильтрует сделки по направлению и привязывает выбранные кнопкой", async () => {
		const user = userEvent.setup();
		renderPageInFrame();
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
		renderPageInFrame();
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
		// Блок выбранного живёт в контекстной панели каркаса.
		renderPageInFrame();
		await screen.findByText("exec-buy");

		// Направление тонировано: покупка — $info, продажа — $risk.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		for (const buy of screen.getAllByText("покупка", { selector: "span" })) {
			expect(buy).toHaveClass("text-info");
		}

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
		// Панель целей монтируется в контекстную панель каркаса.
		renderPageInFrame();
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

	// Ручной ввод из «Входящих» по сценарию «Сделка создаётся из «Входящих»»:
	// форма открывается кнопкой шапки, серверная валидация возвращает записи
	// с бейджем «ручная» и без идентификатора исполнения.
	// Traceability: openspec:ui/screens#scenario-inbox-create-manual-trade
	// Traceability: openspec:ui/screens#scenario-source-badge-shown
	it("создаёт ручную сделку из формы шапки с бейджем источника", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("exec-buy");

		// Arrange: таблица различает источники бейджами; у ручной записи
		// execId — прочерк, у биржевых бейдж «биржа».
		const manualRow = screen.getByRole("row", { name: /ручная/i });
		expect(within(manualRow).getByText("ручная")).toBeInTheDocument();
		expect(within(manualRow).queryByText("exec-")).not.toBeInTheDocument();
		const exchangeRow = screen.getByRole("row", { name: /exec-buy/i });
		expect(within(exchangeRow).getByText("биржа")).toBeInTheDocument();

		// Act: открытие формы, заполнение и создание без конструкции.
		await user.click(screen.getByRole("button", { name: "Ручная сделка" }));
		await user.type(screen.getByLabelText("Символ инструмента"), "BTCUSDT");
		await user.type(screen.getByLabelText("Количество"), "0,3");
		await user.type(screen.getByLabelText("Цена, USDT"), "43000");
		await user.click(screen.getByRole("button", { name: "Создать" }));

		// Assert: сделка адресована общему пулу «Входящих».
		await waitFor(() => expect(manualApi.createManualTrade).toHaveBeenCalledTimes(1));
		expect(manualApi.createManualTrade.mock.calls[0][0]).toMatchObject({ constructionId: null, symbol: "BTCUSDT" });
	});

	it("показывает причину отказа серверной валидации у формы", async () => {
		// Arrange: сервер отклоняет будущую дату.
		const user = userEvent.setup();
		manualApi.createManualTrade.mockRejectedValue(new Error("Дата сделки не может быть в будущем."));
		renderPage();
		await screen.findByText("exec-buy");
		await user.click(screen.getByRole("button", { name: "Ручная сделка" }));
		await user.type(screen.getByLabelText("Символ инструмента"), "BTCUSDT");
		await user.type(screen.getByLabelText("Количество"), "0,3");
		await user.type(screen.getByLabelText("Цена, USDT"), "43000");

		// Act: отправка формы с некорректной датой.
		await user.click(screen.getByRole("button", { name: "Создать" }));

		// Assert: человекочитаемая причина показана формой.
		expect(await screen.findByRole("alert")).toHaveTextContent("Дата сделки не может быть в будущем.");
		expect(screen.getByRole("button", { name: "Создать" })).toBeEnabled();
	});

	// Правка и удаление ручной записи доступны из таблицы «Входящих»;
	// у биржевых записей этих действий нет — они неизменяемы.
	// Traceability: openspec:ui/screens#scenario-detail-edit-manual-trade
	// Traceability: openspec:ui/screens#scenario-detail-synced-trade-immutable
	it("адресует правку и удаление только ручной записи", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("exec-buy");

		// Assert: у биржевой строки действий правки/удаления нет.
		const exchangeRow = screen.getByRole("row", { name: /exec-buy/i });
		expect(within(exchangeRow).queryByRole("button", { name: "Изменить" })).not.toBeInTheDocument();
		expect(within(exchangeRow).queryByRole("button", { name: "Удалить" })).not.toBeInTheDocument();

		// Arrange: форма правки ручной записи предзаполнена атрибутами.
		const manualRow = screen.getByRole("row", { name: /ручная/i });
		await user.click(within(manualRow).getByRole("button", { name: "Изменить" }));
		expect(screen.getByLabelText("Символ инструмента")).toHaveValue("BTCUSDT");

		// Act: сохранение правки.
		await user.click(screen.getByRole("button", { name: "Сохранить" }));

		// Assert: правка адресована ключом строки сырья.
		await waitFor(() => expect(manualApi.editManualTrade).toHaveBeenCalledTimes(1));
		expect(manualApi.editManualTrade.mock.calls[0][0]).toBe(15);

		// Act: удаление с подтверждением.
		await user.click(within(screen.getByRole("row", { name: /ручная/i })).getByRole("button", { name: "Удалить" }));
		expect(screen.getByRole("dialog")).toBeInTheDocument();
		await user.click(screen.getByRole("button", { name: "Подтвердить удаление" }));

		// Assert: удаление адресовано ключом строки сырья.
		await waitFor(() => expect(manualApi.deleteManualTrade).toHaveBeenCalledWith(15));
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
		// Форма ручной сделки доступна и на мобильном: ввод сделки — лёгкий
		// сценарий, тяжёлые операции распределения остаются на desktop.
		expect(screen.getByRole("button", { name: "Ручная сделка" })).toBeInTheDocument();
	});
});
