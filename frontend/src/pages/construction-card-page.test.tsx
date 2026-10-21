import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, createMemoryRouter, RouterProvider, useLocation } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppLayout } from "@/components/layout/AppLayout";
import type { ChatDto } from "@/chat/types";
import type { ConstructionsOverview } from "@/lib/api/constructions";
import type { ConstructionCard } from "@/lib/api/construction-card";
import type { HintPanel } from "@/lib/api/hints";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ConstructionCardPage } from "./construction-card-page";

// Проверяется карточка конструкции по концепции §4: шапка с именем, статусом
// и действиями, kstrip метрик с периодом, полный индикатор финрезультата,
// комментарий MD-рендером, четыре таблицы записей, панель подсказок
// конструкции и панель чатов в контекстной панели каркаса. Слой API подменён
// модулем-заглушкой.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

const card: ConstructionCard = {
	constructionId: 7,
	name: "ETH-240628-3200C+P",
	status: "open",
	allocatedCapitalUsdt: 3000,
	riskPercent: 3,
	riskUsdt: 90,
	riskUnit: "percent",
	profitPercent: 8,
	profitUsdt: 240,
	profitUnit: "usdt",
	comment: "## стреддл",
	hasOpenResidual: true,
	hasMarkFailure: false,
	marksAsOf: "2026-06-20T14:30:00+00:00",
	metrics: {
		realizedPnL: 60.25,
		unrealizedPnL: 40.25,
		adjustmentsPnL: 5,
		totalPnL: 105.5,
		totalPnLPercent: 3.5,
		realizedPnLPercent: 2,
		unrealizedPnLPercent: 1.34,
		adjustmentsPnLPercent: 0.17,
		markValue: 500,
		capitalUsagePercent: 16.7,
		realRiskUsdt: 45,
		worstCaseUsdt: 15.25,
		syntheticAdjustedTotalUsdt: null,
		realRiskStatus: "finite",
		openedAt: "2026-06-18T09:05:00+00:00",
		closedAt: null,
		durationSeconds: 53.4 * 3600,
		daysToNearestExpiry: 9,
		netDailyDecayUsdt: -6.8,
		totalTimeValueUsdt: 1240,
	},
	positions: [
		{
			symbol: "ETH-28JUN24-3200-C",
			residual: 2,
			averageEntryPrice: 30,
			averageClosePrice: null,
			realizedPnL: 40,
			realizedPnLPercent: 1.33,
			unrealizedPnL: 30,
			unrealizedPnLPercent: 1,
			totalPnL: 70,
			totalPnLPercent: 2.33,
			accumulatedFees: 0.5,
			openedAt: "2026-06-18T09:05:00+00:00",
			closedAt: null,
			isOpen: true,
			comment: "ножка **входа**",
			markValue: 300,
			priceChangePercent: 5,
			syntheticResidual: null,
			otherResidual: null,
			daysToExpiry: 9,
			dailyDecayUsdt: -6.8,
			intrinsicValueUsdt: 180.4,
			timeValueUsdt: 1059.6,
			decayDayAgoUsdt: -6.2,
		},
	],
	trades: [
		{
			execId: "exec-1",
			symbol: "ETH-28JUN24-3200-C",
			executedAt: "2026-06-18T09:05:00+00:00",
			isBuy: true,
			quantity: 2,
			price: 30,
			amountUsdt: 60,
			fee: 0.1,
			comment: null,
			source: "exchange",
			manualTradeId: null,
		},
	],
	closingEntries: [
		{
			closedAt: "2026-06-19T10:00:00+00:00",
			kind: "manual-mark",
			symbol: "ETHUSDT",
			quantity: -0.5,
			price: 3520,
			amountUsdt: -1760,
			manualMarkId: 21,
		},
	],
	closingWarnings: [
		{ kind: "manual-mark", symbol: "ETHUSDT", closedAt: "2026-06-19T10:00:00+00:00" },
	],
	adjustments: [
		{
			adjustmentId: 11,
			date: "2026-06-19T00:00:00+00:00",
			description: "PnL робота grid-ETH",
			source: "robot",
			amountUsdt: 5,
		},
	],
	syntheticCloses: [],
	riskWarnings: [],
};

const constructionPanel: HintPanel = {
	subject: { kind: "construction", constructionId: 7 },
	liveGroups: [],
	history: [],
	liveCount: 0,
};

const overview: ConstructionsOverview = {
	summary: {
		totalPnL: 105.5, realizedPnL: 60.25, unrealizedPnL: 40.25,
		marksAsOf: null, hasMarkFailure: false, constructionCount: 1, openCount: 1,
	},
	items: [],
};

const activeChat: ChatDto = {
	id: "active-1", status: "active",
	params: { constructionId: "7", model: "GLM-5.3", sources: ["journal", "rules-corpus"] },
	createdAt: "2026-10-08T09:00:00Z", lastMessageAt: "2026-10-08T09:00:00Z",
	firstMessagePreview: "Оцени риск конструкции",
};

const cardApi = vi.hoisted(() => ({
	fetchConstructionCard: vi.fn(),
	renameConstruction: vi.fn(),
	changeConstructionStatus: vi.fn(),
	changeAllocatedCapital: vi.fn(),
	changeRisk: vi.fn(),
	changeProfit: vi.fn(),
	deleteConstruction: vi.fn(),
	setConstructionComment: vi.fn(),
	setPositionComment: vi.fn(),
	setTradeComment: vi.fn(),
	fetchLastInstrumentMark: vi.fn(),
	addManualCloseMark: vi.fn(),
	editManualCloseMark: vi.fn(),
	deleteManualCloseMark: vi.fn(),
	fetchMoveTargets: vi.fn(),
	returnTradeToInbox: vi.fn(),
	moveTrade: vi.fn(),
	addAdjustment: vi.fn(),
	editAdjustment: vi.fn(),
	deleteAdjustment: vi.fn(),
}));

vi.mock("@/lib/api/construction-card", () => cardApi);

vi.mock("@/lib/api/hints", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/hints")>()),
	fetchConstructionHintsPanel: vi.fn(),
}));

vi.mock("@/chat/api/chat-api", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/chat/api/chat-api")>()),
	listChats: vi.fn(),
	listChatMessages: vi.fn(),
}));

// Данные каркаса AppLayout для проверки слота контекстной панели: счётчик
// входящих, общий обзор журнала и журнал синхронизаций — заглушки, к
// тестируемому слоту отношения не имеют.
const frameApi = vi.hoisted(() => ({
	fetchInboxCount: vi.fn(),
	fetchConstructionsOverview: vi.fn(),
	fetchSyncRuns: vi.fn(),
}));
vi.mock("@/lib/api/inbox", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/inbox")>()),
	fetchInboxCount: frameApi.fetchInboxCount,
}));
vi.mock("@/lib/api/constructions", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/constructions")>()),
	fetchConstructionsOverview: frameApi.fetchConstructionsOverview,
}));
vi.mock("@/lib/api/sync", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/sync")>()),
	fetchSyncRuns: frameApi.fetchSyncRuns,
}));
// Тред чата изолируется от assistant-ui runtime: для слота достаточно
// факта монтирования панели и восстановления выбора из URL.
vi.mock("@/chat/runtime/use-agent-chat-runtime", () => ({
	useAgentChatRuntime: () => ({ runtime: {}, isRunning: false, error: null, cancel: vi.fn() }),
}));
vi.mock("@/chat/components/agent-chat-thread", () => ({
	AgentChatThread: () => <div data-testid="agent-chat-thread" />,
}));

import { fetchConstructionHintsPanel } from "@/lib/api/hints";
import { listChats, listChatMessages } from "@/chat/api/chat-api";
import { ApiError } from "@/lib/api/http";

function renderCardPage(
	path = "/constructions/7",
	queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
): ReturnType<typeof render> {
	return render(
		// Сводка метрик и индикатор карточки строятся на Radix Tooltip —
		// провайдер-предок обязателен для рендера.
		<TooltipProvider delayDuration={0}>
			<QueryClientProvider client={queryClient}>
				<MemoryRouter initialEntries={[path]}>
					<Routes>
						<Route path="/constructions/:constructionId" element={<ConstructionCardPage />} />
					</Routes>
				</MemoryRouter>
			</QueryClientProvider>
		</TooltipProvider>,
	);
}

function LocationProbe() {
	return <span aria-label="URL">{useLocation().search}</span>;
}

// Карточка внутри каркаса: только здесь работает слот контекстной панели
// (useOutletContext), поэтому монтаж панели чатов проверяется в этой обвязке.
function renderCardPageInFrame(search = ""): () => void {
	const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
	const router = createMemoryRouter(
		[{
			element: <AppLayout />,
			children: [{
				path: "/constructions/:constructionId",
				element: <><LocationProbe /><ConstructionCardPage /></>,
			}],
		}],
		{ initialEntries: [`/constructions/7${search}`] },
	);
	const view = render(
		// Провайдер подсказок обязателен: сводка метрик и индикатор
		// карточки строятся на Radix Tooltip.
		<TooltipProvider delayDuration={0}>
			<QueryClientProvider client={queryClient}>
				<RouterProvider router={router} />
			</QueryClientProvider>
		</TooltipProvider>,
	);
	return () => view.unmount();
}

// Диалоги капитала, риска и профита открываются только через меню
// «Параметры» шапки карточки.
// Traceability: openspec:ui/screens#scenario-detail-params-menu
async function openParamsDialog(
	user: ReturnType<typeof userEvent.setup>,
	name: "Капитал…" | "Риск…" | "Профит…",
) {
	await user.click(await screen.findByRole("button", { name: "Параметры" }));
	await user.click(screen.getByRole("menuitem", { name }));
}

beforeEach(() => {
	vi.resetAllMocks();
	cardApi.fetchConstructionCard.mockResolvedValue(card);
	vi.mocked(fetchConstructionHintsPanel).mockResolvedValue(constructionPanel);
	// Недоступность чатов не блокирует остальные данные карточки.
	vi.mocked(listChats).mockRejectedValue(new Error("API ответил ошибкой 404"));
	vi.mocked(listChatMessages).mockResolvedValue([]);
	frameApi.fetchInboxCount.mockResolvedValue(0);
	frameApi.fetchConstructionsOverview.mockResolvedValue(overview);
	// Статус синхронизации топбара без успешных запусков скрыт — к проверкам
	// карточки отношения не имеет.
	frameApi.fetchSyncRuns.mockResolvedValue([]);
});

afterEach(() => vi.unstubAllGlobals());

describe("карточка конструкции: действия и ввод", () => {
	// Отдельного действия «В новом окне» в шапке карточки нет.
	// Traceability: openspec:ui/screens#scenario-detail-no-new-window-button
	it("TryIfHeaderHasNoNewWindowAction", async () => {
		renderCardPage();
		await screen.findByRole("heading", { name: /ETH-240628-3200C\+P\s*открыта/i });
		expect(screen.queryByRole("link", { name: "В новом окне" })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "В новом окне" })).not.toBeInTheDocument();
	});

	// Меню «Параметры» открывает существующие диалоги капитала, риска
	// и профита; отдельных кнопок этих параметров в шапке больше нет.
	// Traceability: openspec:ui/screens#scenario-detail-params-menu
	it("TryIfParamsMenuOpensExistingDialogs", async () => {
		const user = userEvent.setup();
		renderCardPage();

		await openParamsDialog(user, "Капитал…");
		expect(screen.getByLabelText("выделенный капитал, USDT")).toBeInTheDocument();

		await openParamsDialog(user, "Риск…");
		expect(screen.getByLabelText("%", { exact: true })).toBeInTheDocument();

		await openParamsDialog(user, "Профит…");
		expect(screen.getByLabelText("USDT", { exact: true })).toBeInTheDocument();

		expect(screen.queryByRole("button", { name: "Изменить капитал" })).not.toBeInTheDocument();
	});

	// Meta-строка под заголовком: плановые риск/профит, реальный риск числом
	// при конечном статусе, капитал и дата открытия.
	// Traceability: openspec:ui/screens#scenario-detail-header-meta-line
	it("TryIfHeaderMetaLineShowsPlannedAndRealValues", async () => {
		renderCardPage();
		await screen.findByRole("heading", { name: /ETH-240628-3200C\+P\s*открыта/i });
		const meta = document.querySelector('[data-slot="construction-header-meta"]');
		expect(meta?.textContent).toContain("плановый риск 90 / плановый профит 240");
		expect(meta?.textContent).toContain("реальный риск 45");
		expect(meta?.textContent).toContain("капитал 3000 USDT");
		expect(meta?.textContent).toMatch(/открыта 2026-06-18 \d{2}:05/);
	});

	// Неконечный реальный риск показывается канонической подписью статуса:
	// неограниченный хвост и сбой расчёта не превращаются в прочерк или ноль.
	// Traceability: openspec:ui/screens#scenario-detail-header-meta-line
	it.each([
		["unbounded", "реальный риск не ограничен"],
		["unavailable", "реальный риск не удалось рассчитать"],
	] as const)("TryIfHeaderMetaLineShowsNonFiniteRealRiskStatus %s", async (status, label) => {
		cardApi.fetchConstructionCard.mockResolvedValue({
			...card, metrics: { ...card.metrics, realRiskUsdt: null, realRiskStatus: status },
		});
		renderCardPage();
		await screen.findByRole("heading", { name: /ETH-240628-3200C\+P\s*открыта/i });
		const meta = document.querySelector('[data-slot="construction-header-meta"]');
		expect(meta?.textContent).toContain(label);
		expect(meta?.textContent).not.toContain("реальный риск 45");
	});

	// Невалидный маршрут показывает «не найдена», а не вечное чтение.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it.each(["abc", "0", "-1", "1.5"])("ThrowOnInvalidConstructionId %s", async (id) => {
		renderCardPage(`/constructions/${id}`);
		expect(await screen.findByText(/конструкция не найдена/i)).toBeInTheDocument();
		expect(cardApi.fetchConstructionCard).not.toHaveBeenCalled();
		expect(screen.queryByText("чтение конструкции…")).not.toBeInTheDocument();
	});

	// Эхо-пересчёт не меняет единицу ввода, сохраняем именно изменённое поле.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it.each([
		["Риск…", "%", "5", "USDT", "150", "percent", 5],
		["Профит…", "USDT", "150", "%", "5", "usdt", 150],
	] as const)("TryIfBoundEchoPreservesInputUnit %s", async (button, field, text, echo, expected, unit, value) => {
		cardApi.changeRisk.mockResolvedValue(undefined);
		cardApi.changeProfit.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, button);
		await user.clear(screen.getByLabelText(field, { exact: true }));
		await user.type(screen.getByLabelText(field, { exact: true }), text);
		expect(screen.getByLabelText(echo, { exact: true })).toHaveValue(expected);
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		await waitFor(() => expect(button === "Риск…" ? cardApi.changeRisk : cardApi.changeProfit)
			.toHaveBeenCalledWith(7, value, unit));
	});

	// Сохранение без правки не превращает исходную USDT-границу в проценты.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfExistingBoundKeepsOriginalUnit", async () => {
		cardApi.changeProfit.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, "Профит…");
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		await waitFor(() => expect(cardApi.changeProfit).toHaveBeenCalledWith(7, 240, "usdt"));
	});

	// Очистка любой единицы очищает эхо и снимает параметр целиком.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfEmptyBoundRemovesBothValues", async () => {
		cardApi.changeRisk.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, "Риск…");
		await user.clear(screen.getByLabelText("%", { exact: true }));
		expect(screen.getByLabelText("USDT", { exact: true })).toHaveValue("");
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		await waitFor(() => expect(cardApi.changeRisk).toHaveBeenCalledWith(7, null, null));
	});

	// При нулевом или отсутствующем капитале USDT сохраняются без деления на ноль.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it.each([0, null])("TryIfUsdtBoundWorksWithoutPositiveCapital %s", async (capital) => {
		cardApi.fetchConstructionCard.mockResolvedValue({ ...card, allocatedCapitalUsdt: capital });
		cardApi.changeRisk.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, "Риск…");
		await user.clear(screen.getByLabelText("USDT", { exact: true }));
		await user.type(screen.getByLabelText("USDT", { exact: true }), "75");
		expect(screen.getByLabelText("%", { exact: true })).toHaveValue("");
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		await waitFor(() => expect(cardApi.changeRisk).toHaveBeenCalledWith(7, 75, "usdt"));
	});

	// Ошибочный ввод не подменяется удалением границы.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it.each(["не число", "Infinity"])("ThrowOnInvalidBound %s", async (text) => {
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, "Риск…");
		await user.clear(screen.getByLabelText("%", { exact: true }));
		await user.type(screen.getByLabelText("%", { exact: true }), text);
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		expect(await screen.findByText(/Введите число для риска/)).toHaveAttribute("role", "alert");
		expect(cardApi.changeRisk).not.toHaveBeenCalled();
	});

	// Ноль — легитимный капитал, только пустое сохранение снимает его.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it.each([["0", 0], ["", null]] as const)("TryIfCapitalSupportsZeroAndRemoval %s", async (text, value) => {
		cardApi.changeAllocatedCapital.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, "Капитал…");
		const input = screen.getByLabelText("выделенный капитал, USDT");
		await user.clear(input);
		if (text !== "") await user.type(input, text);
		await user.click(screen.getByRole("button", { name: "Сохранить капитал" }));
		await waitFor(() => expect(cardApi.changeAllocatedCapital).toHaveBeenCalledWith(7, value));
	});

	// Ошибочное число не снимает капитал и не закрывает форму.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("ThrowOnInvalidCapital", async () => {
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, "Капитал…");
		const input = screen.getByLabelText("выделенный капитал, USDT");
		await user.clear(input);
		await user.type(input, "ошибка");
		await user.click(screen.getByRole("button", { name: "Сохранить капитал" }));
		expect(await screen.findByText(/Введите число для капитала/)).toHaveAttribute("role", "alert");
		expect(cardApi.changeAllocatedCapital).not.toHaveBeenCalled();
		expect(input).toHaveValue("ошибка");
	});

	// Отказ домена остаётся рядом с формой, введённое значение не теряется.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("ThrowOnRejectedBoundKeepsFormOpen", async () => {
		cardApi.changeRisk.mockRejectedValue(new Error("риск недопустим"));
		const user = userEvent.setup();
		renderCardPage();
		await openParamsDialog(user, "Риск…");
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		expect(await screen.findByText(/Действие не выполнено: риск недопустим/)).toHaveAttribute("role", "alert");
		expect(screen.getByLabelText("%", { exact: true })).toHaveValue("3");
	});

	// Возврат из архива всегда восстанавливает статус «закрыта».
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfArchivedConstructionRestoresClosed", async () => {
		cardApi.fetchConstructionCard.mockResolvedValue({ ...card, status: "archived" });
		cardApi.changeConstructionStatus.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "Вернуть из архива" }));
		await waitFor(() => expect(cardApi.changeConstructionStatus).toHaveBeenCalledWith(7, "closed"));
	});

	// Перенос использует первую видимую цель даже после холодной загрузки списка.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfTradeMovesToInitiallyLoadedTarget", async () => {
		cardApi.fetchMoveTargets.mockResolvedValue([{ constructionId: 8, name: "Цель" }]);
		cardApi.moveTrade.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "Перенести…" }));
		expect(await screen.findByRole("combobox", { name: "целевая конструкция" })).toHaveValue("8");
		await user.click(screen.getByRole("button", { name: "Перенести" }));
		await waitFor(() => expect(cardApi.moveTrade).toHaveBeenCalledWith("exec-1", 8));
	});

	// Сбой предзаполнения марки виден, неверная цена не отправляется как null.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("ThrowOnMarkLookupFailureAndInvalidPrice", async () => {
		cardApi.fetchLastInstrumentMark.mockRejectedValue(new Error("биржа недоступна"));
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "закрыть пометкой…" }));
		expect(await screen.findByText(/Последняя марка недоступна: биржа недоступна/)).toHaveAttribute("role", "alert");
		await user.type(screen.getByLabelText("цена закрытия"), "ошибка");
		await user.click(screen.getByRole("button", { name: "Поставить пометку" }));
		expect(screen.getByText(/Введите корректную цену/)).toHaveAttribute("role", "alert");
		expect(cardApi.addManualCloseMark).not.toHaveBeenCalled();
	});

	// Для ручной пометки сохраняется полная точность предзаполненной марки.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfManualMarkPreservesPricePrecision", async () => {
		cardApi.fetchLastInstrumentMark.mockResolvedValue(0.00012345);
		cardApi.addManualCloseMark.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "закрыть пометкой…" }));
		await waitFor(() => expect(screen.getByLabelText("цена закрытия")).toHaveValue("0.00012345"));
		await user.click(screen.getByRole("button", { name: "Поставить пометку" }));
		await waitFor(() => expect(cardApi.addManualCloseMark).toHaveBeenCalledWith(7,
			expect.objectContaining({ symbol: "ETH-28JUN24-3200-C", price: 0.00012345 })));
		// Форма скрывается только после успешной постановки пометки.
		expect(screen.queryByLabelText("цена закрытия")).toBeNull();
		expect(await screen.findByRole("button", { name: "закрыть пометкой…" })).toBeInTheDocument();
	});

	// Запоздавшая котировка не перезаписывает цену, которую владелец уже ввёл.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfManualPriceSurvivesLateMarkResponse", async () => {
		let resolveMark: (price: number) => void = () => {};
		cardApi.fetchLastInstrumentMark.mockReturnValue(new Promise<number>((resolve) => { resolveMark = resolve; }));
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "закрыть пометкой…" }));
		await user.type(screen.getByLabelText("цена закрытия"), "17");
		resolveMark(30);
		await waitFor(() => expect(screen.getByLabelText("цена закрытия")).toHaveValue("17"));
	});

	// Сценарий «Форма пометки подсказывает внутреннюю стоимость»: у опционной
	// ноги в деньгах форма показывает подсказку |расчётная цена − страйк| на
	// расчётной цене экспирации; компоненты клиент восстанавливает из
	// внутренней стоимости позиции (180.4 на остатке 2 → 90.2 на контракт)
	// и страйка символа.
	// Traceability: openspec:ui/screens#scenario-mark-form-intrinsic-hint
	it("TryIfCloseMarkFormShowsIntrinsicHintForItmLeg", async () => {
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "закрыть пометкой…" }));
		const hint = await screen.findByText("внутренняя стоимость на расчётной цене");
		expect(hint.closest('[data-slot="mark-intrinsic-hint"]')?.textContent).toContain("|3290.2 − 3200| = 90.2 USDT");
	});

	// Сценарий «Частичная пометка оставляет остаток» на стороне формы:
	// заданное количество уходит в команду постановки пометки, подсказка
	// ведёт остаток после частичного закрытия; незаданное количество остаётся
	// null — весь остаток.
	// Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity
	it("TryIfCloseMarkFormSendsPartialQuantity", async () => {
		cardApi.fetchLastInstrumentMark.mockResolvedValue(30);
		cardApi.addManualCloseMark.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "закрыть пометкой…" }));
		const quantity = screen.getByLabelText("количество пометки");
		expect(quantity.getAttribute("placeholder")).toBe("весь остаток — 2");
		await user.type(quantity, "0.5");
		expect(screen.getByText("частичное закрытие — останется 1.5")).toBeTruthy();
		await user.click(screen.getByRole("button", { name: "Поставить пометку" }));
		await waitFor(() => expect(cardApi.addManualCloseMark).toHaveBeenCalledWith(7,
			expect.objectContaining({ symbol: "ETH-28JUN24-3200-C", price: 30, quantity: 0.5 })));
	});

	// Правка пометки не округляет цену; неверный ввод не заменяет её пустым.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("ThrowOnInvalidEditedClosePrice", async () => {
		cardApi.fetchConstructionCard.mockResolvedValue({
			...card, closingEntries: [{ ...card.closingEntries[0], price: 0.00012345 }],
		});
		const user = userEvent.setup();
		renderCardPage();
		await screen.findByText("exec-1");
		// Кнопка правки выбирается по имени: в заголовках таблицы появились
		// триггеры подсказок метрик, индексы кнопок больше не стабильны.
		const closeTable = document.querySelector('[data-slot="closing-entries-table"]');
		const edit = closeTable && within(closeTable as HTMLElement).getByRole("button", { name: "править" });
		expect(edit).toBeDefined();
		await user.click(edit!);
		const input = screen.getByLabelText("цена закрытия");
		expect(input).toHaveValue("0.00012345");
		await user.clear(input);
		await user.type(input, "Infinity");
		await user.click(screen.getByRole("button", { name: "Сохранить пометку" }));
		expect(screen.getByText(/Введите корректную цену, время и количество/)).toHaveAttribute("role", "alert");
		expect(cardApi.editManualCloseMark).not.toHaveBeenCalled();
	});

	// При сохранении существующей корректировки без правок сумма не округляется.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfAdjustmentEditPreservesPrecision", async () => {
		cardApi.fetchConstructionCard.mockResolvedValue({
			...card, adjustments: [{ ...card.adjustments[0], amountUsdt: 1.23456789 }],
		});
		cardApi.editAdjustment.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await screen.findByText("exec-1");
		// Кнопка правки выбирается по имени: в заголовках таблицы появились
		// триггеры подсказок метрик, индексы кнопок больше не стабильны.
		const table = document.querySelector('[data-slot="adjustments-table"]');
		const edit = table && within(table as HTMLElement).getByRole("button", { name: "править" });
		expect(edit).toBeDefined();
		await user.click(edit!);
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		await waitFor(() => expect(cardApi.editAdjustment).toHaveBeenCalledWith(11,
			expect.objectContaining({ amountUsdt: 1.23456789 })));
	});

	// Пустая дата корректировки обрабатывается как ошибка формы, а не исключение.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("ThrowOnMissingAdjustmentDate", async () => {
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "Добавить корректировку…" }));
		await user.type(screen.getByLabelText("сумма, USDT"), "10");
		fireEvent.change(screen.getByLabelText("дата", { exact: true }), { target: { value: "" } });
		await user.click(screen.getByRole("button", { name: "Добавить корректировку" }));
		expect(await screen.findByText(/Введите корректную дату и сумму/)).toHaveAttribute("role", "alert");
		expect(cardApi.addAdjustment).not.toHaveBeenCalled();
	});

	// Правка карточки обновляет другие представления журнала без ручной перезагрузки.
	// Traceability: doc:.wf-research/ui-concept/concept.md#1-рамка-и-принципы
	it("TryIfCardMutationInvalidatesRelatedViews", async () => {
		// Arrange: кэш остальных экранов уже содержит прочитанные данные.
		const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
		const keys = [
			["construction-card", 8], ["constructions-overview"], ["construction-preview", 7],
			["construction-move-targets", 7], ["inbox-overview"], ["inbox-count"],
		];
		for (const key of keys) client.setQueryData(key, {});
		cardApi.changeAllocatedCapital.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage("/constructions/7", client);

		// Act: изменение капитала затрагивает сводные показатели.
		await openParamsDialog(user, "Капитал…");
		await user.click(screen.getByRole("button", { name: "Сохранить капитал" }));

		// Assert: каждое представление помечено для перечитывания при открытии.
		await waitFor(() => {
			for (const key of keys) expect(client.getQueryState(key)?.isInvalidated).toBe(true);
		});
	});

	// Мобильный профиль сохраняет чтение таблиц и средний индикатор,
	// но не предлагает правку пометок, корректировок или распределение сделок.
	// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
	it("TryIfMobileCardKeepsDataWithoutHeavyActions", async () => {
		vi.stubGlobal("matchMedia", vi.fn(() => ({
			matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn(),
		})));
		renderCardPage();

		expect(await screen.findByText("exec-1")).toBeInTheDocument();
		expect(screen.getByText("PnL робота grid-ETH")).toBeInTheDocument();
		expect(document.querySelector('[data-slot="fin-result-medium"]')).not.toBeNull();
		expect(screen.queryByRole("button", { name: "закрыть пометкой…" })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Добавить корректировку…" })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Перенести…" })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Во «Входящие»" })).not.toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "править" })).not.toBeInTheDocument();
		expect(screen.getByRole("button", { name: "изменить комментарий конструкции" })).toBeInTheDocument();
	});
});

describe("карточка конструкции", () => {
	it("показывает шапку с именем и статусом, kstrip метрик с периодом и комментарий MD", async () => {
		renderCardPage();

		// Шапка: имя и ручной статус словами в одном заголовке.
		expect(await screen.findByRole("heading", { name: /ETH-240628-3200C\+P\s*открыта/i })).toBeInTheDocument();
		// Титул карточки — Inter 21/600 дизайн-системы, не дефолтный text-2xl.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		expect(screen.getByRole("heading", { name: /ETH-240628-3200C\+P\s*открыта/i }).className).toContain("page-title");

		// Kstrip: итог с процентом капитала, разбивка, стоимость, занятость.
		expect(screen.getByText("общий P&L")).toBeInTheDocument();
		expect(screen.getByText("+105.5 USDT")).toBeInTheDocument();
		expect(screen.getByText("+3.5%")).toBeInTheDocument();
		expect(screen.getByText("котировки на")).toBeInTheDocument();
		// Момент показывается в локальной зоне браузера — сверяем дату и минуты.
		expect(screen.getByText(/2026-06-20 \d{2}:30/)).toBeInTheDocument();
		expect(screen.getByText("период")).toBeInTheDocument();

		// Комментарий рендерится из Markdown: заголовок второго уровня.
		expect(screen.getByRole("heading", { name: "стреддл" })).toBeInTheDocument();
	});

	it("показывает четыре таблицы записей с пустыми состояниями и предупреждение", async () => {
		renderCardPage();

		expect(await screen.findByRole("heading", { name: "Позиции" })).toBeInTheDocument();
		expect(screen.getByRole("heading", { name: "Сделки" })).toBeInTheDocument();
		expect(screen.getByRole("heading", { name: "Закрывающие записи" })).toBeInTheDocument();
		expect(screen.getByRole("heading", { name: "Корректировки PnL" })).toBeInTheDocument();

		// Строки таблиц: позиция, сделка, закрывающая запись, корректировка.
		// Инструмент встречается в таблицах позиций и сделок — сверяем вхождение.
		expect(screen.getAllByText("ETH-28JUN24-3200-C").length).toBeGreaterThanOrEqual(2);
		expect(screen.getByText("exec-1")).toBeInTheDocument();
		expect(screen.getAllByText("ручная пометка").length).toBeGreaterThan(0);
		expect(screen.getByText("PnL робота grid-ETH")).toBeInTheDocument();

		// Предупреждение об избыточной закрывающей записи — чип в головке
		// секции по мастеру design.pen, details унесены в подсказку.
		// Traceability: change:rework-spa-shell-and-screens/specs/ui/screens/spec#scenario-detail-grids-match-design
		expect(screen.getByRole("alert")).toHaveTextContent("⚠ 1 избыточная");
	});

	// Секция «Предупреждения» в карточке с предупреждениями: полный перечень
	// над таблицами записей — заголовок, счётчик записей и все тексты.
	// Traceability: openspec:ui/screens#scenario-detail-warnings-section-above-tables
	it("TryIfCardShowsWarningsSectionAboveRecordTables", async () => {
		cardApi.fetchConstructionCard.mockResolvedValue({
			...card,
			riskWarnings: [
				{ text: "ETHUSDT · 1 из 2 контрактов · убыток ограничен ценой входа" },
				{ text: "ETH-28JUN24-3200-P · 2 контракта · убыток не ограничен" },
			],
		});
		renderCardPage();

		await screen.findByRole("heading", { name: "Предупреждения" });
		const section = document.querySelector('[data-slot="construction-card-warnings"]');
		expect(section).not.toBeNull();
		// Счётчик записей в головке секции согласован с числом строк.
		expect(section).toHaveTextContent("2 записи");
		expect(section).toHaveTextContent("ETHUSDT · 1 из 2 контрактов · убыток ограничен ценой входа");
		expect(section).toHaveTextContent("ETH-28JUN24-3200-P · 2 контракта · убыток не ограничен");
		// Секция стоит раньше таблиц записей: «Позиции» идут ниже по DOM.
		const positions = document.querySelector('[data-slot="positions-table"]');
		expect(positions).not.toBeNull();
		expect(section!.compareDocumentPosition(positions!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
		// Записи тоном риска по мастер-секции design.pen.
		for (const row of section!.querySelectorAll("p")) {
			expect(row).toHaveClass("text-[color:var(--risk)]");
		}
	});

	// Конструкция без предупреждений не показывает секцию вовсе.
	// Traceability: openspec:ui/screens#scenario-detail-warnings-section-hidden
	it("TryIfCardHidesWarningsSectionWithoutWarnings", async () => {
		renderCardPage();

		await screen.findByRole("heading", { name: "Позиции" });
		expect(document.querySelector('[data-slot="construction-card-warnings"]')).toBeNull();
	});

	// Неограниченный реальный риск и предупреждения сосуществуют: индикатор
	// финрезультата сохраняет статус «риск не ограничен», секция предупреждений
	// показывается независимо от него.
	// Traceability: openspec:ui/screens#scenario-unbounded-badge-coexists-with-warnings
	it("TryIfUnboundedRiskStateCoexistsWithWarningsSection", async () => {
		cardApi.fetchConstructionCard.mockResolvedValue({
			...card,
			metrics: { ...card.metrics, realRiskUsdt: null, realRiskStatus: "unbounded" },
			riskWarnings: [{ text: "ETHUSDT · 2 контракта · убыток не ограничен" }],
		});
		renderCardPage();

		await screen.findByRole("heading", { name: "Предупреждения" });
		const riskState = document.querySelector('[data-slot="fin-result-full"] [data-part="risk-state"]');
		expect(riskState).not.toBeNull();
		expect(riskState).toHaveTextContent("риск не ограничен");
		const section = document.querySelector('[data-slot="construction-card-warnings"]');
		expect(section).not.toBeNull();
		expect(section).toHaveTextContent("ETHUSDT · 2 контракта · убыток не ограничен");
	});

	it("показывает панель подсказок конструкции", async () => {
		renderCardPage();

		expect(await screen.findByText("подсказок нет")).toBeInTheDocument();
	});

	it("показывает состояние «не найдена» по 404", async () => {
		cardApi.fetchConstructionCard.mockRejectedValue(new ApiError(404, "конструкции нет"));

		renderCardPage();

		expect(await screen.findByText(/конструкция не найдена/i)).toBeInTheDocument();
	});

	it("показывает состояние недоступности при сбое чтения", async () => {
		cardApi.fetchConstructionCard.mockRejectedValue(new Error("журнал повреждён"));

		renderCardPage();

		expect(await screen.findByText(/журнал недоступен/i)).toBeInTheDocument();
	});
});

// Примитивы дизайн-системы в карточке (задача 7.2 change
// reconcile-frontend-with-design): сводка метрик — примитив Метрика по
// мастеру «Сводка метрик» (h69OG) Body #2, статус — примитив Чип/Статус.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("карточка конструкции: примитивы дизайн-системы", () => {
	it("рендерит все 12 показателей сводки примитивом Metric в карточке мастера h69OG", async () => {
		// Act: карточка открытой конструкции с оценёнными марками.
		renderCardPage();

		// Assert: 12 инстансов Метрики — M1–M9 мастера «Сводка метрик» (h69OG)
		// и три метрики саммари времени жизни (ML1–ML3 макета).
		expect(await screen.findByText("общий P&L")).toBeInTheDocument();
		const metrics = document.querySelectorAll('[data-slot="metric"]');
		expect(metrics).toHaveLength(12);

		// Контейнер сводки — surface-карточка радиуса 12 с каймой, паддинги
		// [14,18]: колонка с зазором 12 (нода h69OG после 5.1 change
		// synthetic-close-logical-layer), внутри — строка метрик с зазором 20.
		const card = document.querySelector('[data-slot="construction-metrics-card"]');
		expect(card?.className).toContain("rounded-lg");
		expect(card?.className).toContain("border");
		expect(card?.className).toContain("bg-surface");
		expect(card?.className).toContain("px-[18px]");
		expect(card?.className).toContain("py-3.5");
		expect(card?.className).toContain("flex-col");
		expect(card?.className).toContain("gap-3");
		const row = card?.querySelector("div");
		expect(row?.className).toContain("gap-5");

		// Итог (M1) — значение 16/600: единственный акцентный кегль строки.
		const first = metrics[0]?.querySelector('[data-slot="metric-value"]');
		expect(first?.className).toContain("text-[16px]");
		expect(first?.className).toContain("tabular-nums");

		// Остальные показатели (M2–M9 и саммари времени жизни) — значение
		// 14/600 поверх примитива.
		const second = metrics[1]?.querySelector('[data-slot="metric-value"]');
		expect(second?.className).toContain("text-[14px]");
		expect(second?.className).not.toContain("text-[16px]");

		// Подписи — Caption мастера: 11/normal textMuted с трекингом 0.3.
		const caption = metrics[0]?.querySelector("span");
		expect(caption?.className).toContain("text-[11px]");
		expect(caption?.className).toContain("text-text-muted");
		expect(caption?.className).toContain("tracking-[0.3px]");
	});

	it.each([
		// Доменный статус → тон пилюли по инстансам дизайн-нод Body #2 и
		// фрейма «Примитивы»: open → pos (X7CR1q), closed → neutral (EIqx3),
		// archived → muted (dAcLW).
		["open", "открыта", "bg-accent-soft", "text-accent-strong"],
		["closed", "закрыта", "bg-surface-2", "text-text-secondary"],
		["archived", "архив", "bg-surface-2", "text-text-muted"],
	] as const)("статус %s рендерится StatusChip с тоном мастера", async (status, text, bg, fg) => {
		// Arrange: снимок карточки с проверяемым ручным статусом.
		cardApi.fetchConstructionCard.mockResolvedValue({ ...card, status });

		// Act: карточка конструкции.
		renderCardPage();

		// Assert: статус — пилюля Чип/Статус в строке титула, тон по мастеру
		// (текст статуса встречается и в таблице позиций — ищем сам примитив).
		const chip = await waitFor(() => {
			const el = document.querySelector('[data-slot="status-chip"]');
			expect(el).not.toBeNull();
			return el as HTMLElement;
		});
		expect(chip.textContent).toBe(text);
		expect(chip.className).toContain(bg);
		expect(chip.className).toContain(fg);
	});
});

// Панель чатов конструкции монтируется в слот useContextPanel страницы
// карточки (design.md D4): каркас показывает контекстную панель, панель
// развёрнута по умолчанию, геометрия задаётся каркасом; URL-параметры
// ?chats=* упразднены, выбор чата остаётся в ?chat= и переживает
// перезагрузку.
// Traceability: openspec:ui/screens#scenario-ui-chats-panel-in-context-panel
describe("карточка конструкции: панель чатов в контекстной панели", () => {
	it("TryIfChatsPanelMountedIntoContextPanelSlotExpandedByDefault", async () => {
		// Arrange: активный чат конструкции доступен.
		const user = userEvent.setup();
		vi.mocked(listChats).mockImplementation(async (requested) => requested === "active" ? [activeChat] : []);
		renderCardPageInFrame();
		const panel = await screen.findByTestId("context-panel");
		// Assert: панель в каркасном слоте, развёрнута, без toggle-кнопки
		// и без параметров ?chats=*.
		expect(await within(panel).findByRole("button", { name: "Создать чат" })).toBeInTheDocument();
		expect(within(panel).queryByRole("button", { name: "Чаты конструкции" })).not.toBeInTheDocument();
		const chatsRoot = document.querySelector('[data-slot="construction-chats"]');
		expect(chatsRoot?.className).toContain("h-full");
		expect(screen.getByLabelText("URL").textContent).toBe("");
		// Act: выбор чата.
		await user.click(within(panel).getByRole("button", { name: /GLM-5\.3/ }));
		// Assert: выбор остаётся в ?chat=, chats-параметров не появляется.
		expect(screen.getByLabelText("URL")).toHaveTextContent("chat=active-1");
		expect(screen.getByLabelText("URL")).not.toHaveTextContent("chats=");
	});

	it("TryIfChatSelectionSurvivesPageReload", async () => {
		// Arrange: чат выбран до перезагрузки, соседний параметр сохранён.
		vi.mocked(listChats).mockImplementation(async (requested) => requested === "active" ? [activeChat] : []);
		const unmount = renderCardPageInFrame("?chat=active-1&other=keep");
		const panel = await screen.findByTestId("context-panel");
		expect(await within(panel).findByTestId("agent-chat-thread")).toBeInTheDocument();
		// Act: «перезагрузка» — полное размонтирование и новый заход по тому же URL.
		unmount();
		renderCardPageInFrame("?chat=active-1&other=keep");
		// Assert: выбор восстановлен из URL вместе с соседними параметрами.
		const reloaded = await screen.findByTestId("context-panel");
		expect(await within(reloaded).findByTestId("agent-chat-thread")).toBeInTheDocument();
			// Выбранный пункт сессии — фон surface-2 по мастерам design.pen.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		expect(within(reloaded).getByRole("button", { name: /GLM-5\.3/ })).toHaveClass("bg-surface-2");
		expect(screen.getByLabelText("URL")).toHaveTextContent("chat=active-1");
		expect(screen.getByLabelText("URL")).toHaveTextContent("other=keep");
	});
});

// Шапка сегмента «страница» топбара на карточке (решение раунд-сверки 3.0,
// макет QbFRE): трёхуровневая крошка «Журнал / Конструкции / Карточка»,
// после загрузки заголовок «<имя конструкции> · Карточка», до загрузки —
// fallback «Карточка конструкции»; чипов сегмента страницы на экране нет.
// Traceability: openspec:ui/screens#scenario-frame-page-header-registration
// Traceability: change:rework-contextual-topbar/tasks#3.0
describe("карточка конструкции: шапка сегмента страницы топбара", () => {
	it("TryIfPageHeaderRegistersCardNameBreadcrumbTailAndChatsChipAfterLoad", async () => {
		// Act: карточка внутри каркаса — регистрация шапки работает только в слоте Outlet.
		renderCardPageInFrame();

		// Assert: заголовок сегмента — «<имя конструкции> · Карточка»; ждём
		// именно текст: сначала срабатывает fallback-регистрация, затем
		// перерегистрация по загруженной карточке.
		const title = await screen.findByText("ETH-240628-3200C+P · Карточка");
		expect(title).toHaveAttribute("data-testid", "topbar-page-title");

		// Assert: крошка трёхуровневая — хвост «Карточка» после раздела «Конструкции».
		const segment = screen.getByTestId("topbar-segment-page");
		const crumb = within(segment).getByRole("navigation", { name: "Крошка раздела" });
		expect(crumb.textContent).toBe("Журнал/Конструкции/Карточка");

		// Assert: чип сегмента контекста — «Чаты конструкции»; чипов сегмента страницы нет.
		// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
		expect(screen.getByTestId("context-chip")).toHaveTextContent("Чаты конструкции");
		expect(within(segment).queryByTestId("page-chip")).not.toBeInTheDocument();
	});

	it("TryIfPageHeaderFallsBackToCardTitleWhileCardLoading", async () => {
		// Arrange: чтение карточки не завершается — имени для заголовка нет.
		cardApi.fetchConstructionCard.mockReturnValue(new Promise(() => {}));

		// Act: карточка внутри каркаса.
		renderCardPageInFrame();

		// Assert: до загрузки сегмент показывает fallback «Карточка
		// конструкции» при уже зарегистрированном хвосте крошки; контекстная
		// панель с чипом чатов ещё не выведена.
		const title = await screen.findByTestId("topbar-page-title");
		expect(title).toHaveTextContent("Карточка конструкции");
		const segment = screen.getByTestId("topbar-segment-page");
		const crumb = within(segment).getByRole("navigation", { name: "Крошка раздела" });
		expect(crumb.textContent).toBe("Журнал/Конструкции/Карточка");
		expect(screen.queryByTestId("context-chip")).not.toBeInTheDocument();
	});
});
