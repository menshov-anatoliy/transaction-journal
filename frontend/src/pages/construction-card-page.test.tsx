import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
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

// Примитивы дизайн-системы в карточке (задача 7.2 change
// reconcile-frontend-with-design): сводка метрик — примитив Метрика по
// мастеру «Сводка метрик» (h69OG) Body #2, статус — примитив Чип/Статус.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("карточка конструкции: примитивы дизайн-системы", () => {
	it("рендерит все 9 показателей сводки примитивом Metric в карточке мастера h69OG", async () => {
		// Act: карточка открытой конструкции с оценёнными марками.
		renderCardPage();

		// Assert: 9 инстансов Метрики — как M1–M9 мастера «Сводка метрик» (h69OG).
		expect(await screen.findByText("общий P&L")).toBeInTheDocument();
		const metrics = document.querySelectorAll('[data-slot="metric"]');
		expect(metrics).toHaveLength(9);

		// Контейнер сводки — surface-карточка радиуса 12 с каймой, паддинги
		// [14,18], зазор между показателями 20 (нода h69OG).
		const card = document.querySelector('[data-slot="construction-metrics-card"]');
		expect(card?.className).toContain("rounded-lg");
		expect(card?.className).toContain("border");
		expect(card?.className).toContain("bg-surface");
		expect(card?.className).toContain("px-[18px]");
		expect(card?.className).toContain("py-3.5");
		expect(card?.className).toContain("gap-5");

		// Итог (M1) — значение 16/600: единственный акцентный кегль строки.
		const first = metrics[0]?.querySelector('[data-slot="metric-value"]');
		expect(first?.className).toContain("text-[16px]");
		expect(first?.className).toContain("tabular-nums");

		// Остальные показатели (M2–M9) — значение 14/600 поверх примитива.
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
