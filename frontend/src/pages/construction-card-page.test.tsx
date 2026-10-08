import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionCard } from "@/lib/api/construction-card";
import type { HintPanel } from "@/lib/api/hints";
import { ConstructionCardPage } from "./construction-card-page";

// Проверяется карточка конструкции по концепции §4: шапка с именем, статусом
// и действиями, kstrip метрик с периодом, полный индикатор финрезультата,
// комментарий MD-рендером, четыре таблицы записей, панель подсказок
// конструкции и правая скрываемая область чатов. Слой API подменён
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
		openedAt: "2026-06-18T09:05:00+00:00",
		closedAt: null,
		durationSeconds: 53.4 * 3600,
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
};

const constructionPanel: HintPanel = {
	subject: { kind: "construction", constructionId: 7 },
	liveGroups: [],
	history: [],
	liveCount: 0,
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
}));

import { fetchConstructionHintsPanel } from "@/lib/api/hints";
import { listChats } from "@/chat/api/chat-api";
import { ApiError } from "@/lib/api/http";

function renderCardPage(): ReturnType<typeof render> {
	const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
	return render(
		<QueryClientProvider client={queryClient}>
			<MemoryRouter initialEntries={["/constructions/7"]}>
				<Routes>
					<Route path="/constructions/:constructionId" element={<ConstructionCardPage />} />
				</Routes>
			</MemoryRouter>
		</QueryClientProvider>,
	);
}

beforeEach(() => {
	vi.clearAllMocks();
	cardApi.fetchConstructionCard.mockResolvedValue(card);
	vi.mocked(fetchConstructionHintsPanel).mockResolvedValue(constructionPanel);
	// Бэкенд чатов ещё не существует (задача 5.3): список падает — панель
	// обязана деградировать честным сообщением, а не пустотой.
	vi.mocked(listChats).mockRejectedValue(new Error("API ответил ошибкой 404"));
});

describe("карточка конструкции", () => {
	it("показывает шапку с именем и статусом, kstrip метрик с периодом и комментарий MD", async () => {
		renderCardPage();

		// Шапка: имя и ручной статус словами в одном заголовке.
		expect(await screen.findByRole("heading", { name: /ETH-240628-3200C\+P\s*открыта/i })).toBeInTheDocument();

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

		// Предупреждение об избыточной закрывающей записи видно.
		expect(screen.getByText(/избыточная закрывающая запись/i)).toBeInTheDocument();
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
