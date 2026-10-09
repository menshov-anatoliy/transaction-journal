import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor } from "@testing-library/react";
import { Profiler } from "react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ConstructionsOverview } from "@/lib/api/constructions";
import type { HintPanel } from "@/lib/api/hints";
import { ConstructionsPage } from "./constructions-page";

// Проверяется компоновка раздела «Конструкции» по концепции §3: шапка с итогом,
// счётчиками и кнопкой синхронизации, таблица с выделением строки, правая
// контекстная область — панель подсказок журнала или превью конструкции,
// ручной запуск прохода агента. Слой API подменён модулем-заглушкой.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

const overview: ConstructionsOverview = {
	summary: {
		totalPnL: 100.5,
		realizedPnL: 60.25,
		unrealizedPnL: 40.25,
		marksAsOf: "2026-06-20T14:30:00",
		hasMarkFailure: false,
		constructionCount: 2,
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
			markValue: 500,
			capitalUsagePercent: 16.7,
			openedAt: "2026-06-18T09:05:00",
			closedAt: null,
			liveHintCount: 2,
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
			markValue: null,
			capitalUsagePercent: null,
			openedAt: "2026-05-20T10:00:00",
			closedAt: "2026-05-31T12:00:00",
			liveHintCount: 0,
		},
	],
};

const journalPanel: HintPanel = {
	subject: { kind: "journal", constructionId: null },
	liveGroups: [
		{
			group: { id: "risk-mode", title: "Риск-режим" },
			hints: [
				{
					id: 5,
					ruleId: "risk-limit-week",
					character: "risk-mode",
					clarity: "crisp",
					sources: [],
					text: "Лимит риска недели достигнут: 2.1% использовано",
					facts: { weekPnl: "2.1%" },
					asOf: "2026-06-20T14:30:00",
					status: "new",
					firstSeenAt: null,
				},
			],
		},
	],
	history: [],
	liveCount: 1,
};

const constructionPanel: HintPanel = {
	subject: { kind: "construction", constructionId: 7 },
	liveGroups: [],
	history: [],
	liveCount: 0,
};

vi.mock("@/lib/api/constructions", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/constructions")>()),
	fetchConstructionsOverview: vi.fn(),
	fetchConstructionPreview: vi.fn(),
}));

vi.mock("@/lib/api/hints", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/hints")>()),
	fetchJournalHintsPanel: vi.fn(),
	fetchConstructionHintsPanel: vi.fn(),
	runHintsPass: vi.fn(),
	applyHint: vi.fn(),
	dismissHint: vi.fn(),
	markHintSeen: vi.fn(),
}));

vi.mock("@/lib/api/sync", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/sync")>()),
	runSync: vi.fn(),
}));

const api = {
	constructions: vi.mocked(await import("@/lib/api/constructions")),
	hints: vi.mocked(await import("@/lib/api/hints")),
	sync: vi.mocked(await import("@/lib/api/sync")),
};

function renderPage() {
	const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
	return render(
		<QueryClientProvider client={queryClient}>
			<MemoryRouter>
				<ConstructionsPage />
			</MemoryRouter>
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

beforeEach(() => {
	vi.clearAllMocks();
	api.constructions.fetchConstructionsOverview.mockResolvedValue(overview);
	api.constructions.fetchConstructionPreview.mockResolvedValue({
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
		markValue: 500,
		capitalUsagePercent: 16.7,
		openedAt: "2026-06-18T09:05:00",
		closedAt: null,
		marksAsOf: "2026-06-20T14:30:00",
		hasMarkFailure: false,
		counts: { positions: 2, openPositions: 1, trades: 3, adjustments: 0 },
	});
	api.hints.fetchJournalHintsPanel.mockResolvedValue(journalPanel);
	api.hints.fetchConstructionHintsPanel.mockResolvedValue(constructionPanel);
	api.hints.runHintsPass.mockResolvedValue({
		outcome: "completed",
		asOf: "2026-06-20T14:30:00",
		createdHints: 2,
		expiredHints: 1,
		diagnostics: null,
		unimplementedRuleIds: [],
	});
	api.hints.applyHint.mockResolvedValue({ hintId: 5, transitioned: true });
	api.hints.dismissHint.mockResolvedValue({ hintId: 5, transitioned: true });
	api.hints.markHintSeen.mockResolvedValue(undefined);
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
					<Profiler id="constructions" onRender={() => {
						commits += 1;
						if (commits > 30) {
							throw new Error("Выделение конструкции вызвало бесконечное обновление страницы");
						}
					}}>
						<ConstructionsPage />
					</Profiler>
				</MemoryRouter>
			</QueryClientProvider>,
		);
		await screen.findByRole("row", { name: /ETH-240628/ });

		// Act: клик и завершение отложенных обновлений таблицы и превью.
		await user.click(screen.getByRole("row", { name: /ETH-240628/ }));
		await screen.findByRole("link", { name: /открыть карточку/i });
		await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });
		const settledCommits = commits;
		await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });

		// Assert: выбранная строка и превью остаются доступны, рендер прекратился.
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

	it("шапка итога повторяет фрейм «Итог» oYD4G: 18/600 accentStrong, подписи 11, паддинги [14,16]", async () => {
		// Шапка итога журнала сверена с дизайн-фреймом «Итог» (oYD4G):
		// акцентное значение 18/600 $accentStrong, подписи разбивки и
		// счётчиков 11/normal $textSecondary/$textMuted, паддинги [14,16],
		// межстрочный зазор 3.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		// Traceability: change:reconcile-frontend-with-design/design#D2
		// Act: открытие раздела.
		renderPage();

		// Assert: значение итога — 18/600 на акцентном токене.
		const value = await screen.findByText("+100.5 USDT");
		expect(value.className).toContain("text-[18px]");
		expect(value.className).toContain("font-semibold");
		expect(value.className).toContain("text-accent-strong");

		// Блок итога: вертикальный стек с паддингами [14,16] и зазором 3.
		const summary = value.closest('[data-slot="constructions-summary"]');
		expect(summary).not.toBeNull();
		expect(summary?.className).toContain("px-4");
		expect(summary?.className).toContain("py-3.5");
		expect(summary?.className).toContain("gap-[3px]");

		// Подписи: разбивка и котировки 11/normal textSecondary, счётчики 11/normal textMuted.
		const breakdown = screen.getByText(/реализов\. \+60\.25 \/ нереализов\. \+40\.25 · котировки на/i);
		expect(breakdown.className).toContain("text-[11px]");
		expect(breakdown.className).toContain("text-text-secondary");
		const counters = screen.getByText("2 конструкции (1 открыта)");
		expect(counters.className).toContain("text-[11px]");
		expect(counters.className).toContain("text-text-muted");
	});

	it("в тулбаре — кнопка «Разобрать входящие» варианта Secondary, ведёт в раздел входящих", async () => {
		// Разбор входящих доступен из тулбара раздела: кнопка в варианте
		// Secondary дизайн-системы (поверхность + бордер) и ведёт в /inbox.
		// Traceability: change:reconcile-frontend-with-design/design#D2
		// Act: открытие раздела.
		renderPage();

		// Assert: Secondary-кнопка тулбара отправляет в раздел «Входящие».
		const link = await screen.findByRole("link", { name: /разобрать входящие/i });
		expect(link).toHaveAttribute("href", "/inbox");
		expect(link.className).toContain("border");
		expect(link.className).toContain("bg-card");
	});

	it("показывает шапку с итогом, разбивкой, котировками и счётчиками", async () => {
		// Act: открытие раздела.
		renderPage();

		// Assert: шапка несёт итог с разбивкой, отметку котировок и счётчики.
		expect(await screen.findByText("+100.5 USDT")).toBeInTheDocument();
		expect(screen.getByText("+60.25")).toBeInTheDocument();
		expect(screen.getByText("+40.25")).toBeInTheDocument();
		expect(screen.getByText(/котировки на/i)).toBeInTheDocument();
		expect(screen.getByText("2 конструкции (1 открыта)")).toBeInTheDocument();
		expect(screen.getByRole("button", { name: /синхронизировать/i })).toBeInTheDocument();
	});

	it("показывает панель подсказок журнала и помечает первый показ", async () => {
		// Act: открытие раздела без выделения.
		renderPage();

		// Assert: панель журнала с живой подсказкой и кнопкой прохода.
		expect(await screen.findByText(/Лимит риска недели достигнут/)).toBeInTheDocument();
		expect(screen.getByRole("button", { name: /запустить проход подсказок/i })).toBeInTheDocument();

		// Автопометка первого показа ушла командой для записи без отметки.
		await waitFor(() => expect(api.hints.markHintSeen).toHaveBeenCalledWith(5));
	});

	it("заменяет панель журнала превью конструкции по выделению строки", async () => {
		// Arrange: пользователь для клика по строке.
		const user = userEvent.setup();
		renderPage();

		// Act: выделение строки кликом.
		await user.click((await screen.findByRole("row", { name: /ETH-240628/ })));

		// Assert: превью с полным индикатором и панель подсказок конструкции.
		expect(await screen.findByText(/позиции: 2/)).toBeInTheDocument();
		expect(document.querySelector('[data-slot="fin-result-full"]')).not.toBeNull();
		expect(api.hints.fetchConstructionHintsPanel).toHaveBeenCalledWith(7);
		expect(screen.getByRole("link", { name: /открыть карточку/i })).toBeInTheDocument();
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

	it("запускает проход подсказок и показывает счётчики исхода", async () => {
		// Arrange: пользователь для нажатия кнопки.
		const user = userEvent.setup();
		renderPage();
		await screen.findByText(/Лимит риска недели достигнут/);

		// Act: команда ручного прохода.
		await user.click(screen.getByRole("button", { name: /запустить проход подсказок/i }));

		// Assert: проход выполнен, счётчики показаны.
		await waitFor(() => expect(api.hints.runHintsPass).toHaveBeenCalled());
		expect(await screen.findByText(/проход завершён: создано 2, погашено 1/i)).toBeInTheDocument();
	});

	it("применяет живую подсказку кнопкой панели", async () => {
		// Arrange: пользователь для нажатия кнопки.
		const user = userEvent.setup();
		renderPage();
		await screen.findByText(/Лимит риска недели достигнут/);

		// Act: команда «Применено».
		await user.click(screen.getByRole("button", { name: "Применено" }));

		// Assert: команда ушла идентификатором записи.
		await waitFor(() => expect(api.hints.applyHint).toHaveBeenCalledWith(5));
	});

	it("показывает состояние недоступности при сбое чтения журнала", async () => {
		// Arrange: обзор журнала отвечает ошибкой API.
		api.constructions.fetchConstructionsOverview.mockRejectedValue(
			Object.assign(new Error("сырьё повреждено"), { status: 503 }),
		);
		renderPage();

		// Assert: раздел показывает явное состояние недоступности.
		expect(await screen.findByText(/журнал недоступен/i)).toBeInTheDocument();
		expect(screen.queryByRole("table")).not.toBeInTheDocument();
	});

	it("в мобильном режиме открывает правую контекстную область в drawer", async () => {
		setViewport(390);
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("+100.5 USDT");

		await user.click(screen.getByRole("button", { name: /Открыть контекст раздела/i }));
		expect(await screen.findByRole("button", { name: /Запустить проход подсказок/i })).toBeInTheDocument();
	});
});
