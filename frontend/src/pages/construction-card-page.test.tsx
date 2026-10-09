import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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

function renderCardPage(
	path = "/constructions/7",
	queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
): ReturnType<typeof render> {
	return render(
		<QueryClientProvider client={queryClient}>
			<MemoryRouter initialEntries={[path]}>
				<Routes>
					<Route path="/constructions/:constructionId" element={<ConstructionCardPage />} />
				</Routes>
			</MemoryRouter>
		</QueryClientProvider>,
	);
}

beforeEach(() => {
	vi.resetAllMocks();
	cardApi.fetchConstructionCard.mockResolvedValue(card);
	vi.mocked(fetchConstructionHintsPanel).mockResolvedValue(constructionPanel);
	// Недоступность чатов не блокирует остальные данные карточки.
	vi.mocked(listChats).mockRejectedValue(new Error("API ответил ошибкой 404"));
});

afterEach(() => vi.unstubAllGlobals());

describe("карточка конструкции: действия и ввод", () => {
	// Карточка открывается отдельным окном без транзитной вкладки.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfCardCanOpenInNewWindow", async () => {
		renderCardPage();
		const link = await screen.findByRole("link", { name: "В новом окне" });
		expect(link).toHaveAttribute("href", "/constructions/7");
		expect(link).toHaveAttribute("target", "_blank");
		expect(link).toHaveAttribute("rel", "noopener noreferrer");
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
		await user.click(await screen.findByRole("button", { name: button }));
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
		await user.click(await screen.findByRole("button", { name: "Профит…" }));
		await user.click(screen.getByRole("button", { name: "Сохранить" }));
		await waitFor(() => expect(cardApi.changeProfit).toHaveBeenCalledWith(7, 240, "usdt"));
	});

	// Очистка любой единицы очищает эхо и снимает параметр целиком.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("TryIfEmptyBoundRemovesBothValues", async () => {
		cardApi.changeRisk.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCardPage();
		await user.click(await screen.findByRole("button", { name: "Риск…" }));
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
		await user.click(await screen.findByRole("button", { name: "Риск…" }));
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
		await user.click(await screen.findByRole("button", { name: "Риск…" }));
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
		await user.click(await screen.findByRole("button", { name: "Изменить капитал" }));
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
		await user.click(await screen.findByRole("button", { name: "Изменить капитал" }));
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
		await user.click(await screen.findByRole("button", { name: "Риск…" }));
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

	// Правка пометки не округляет цену; неверный ввод не заменяет её пустым.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	it("ThrowOnInvalidEditedClosePrice", async () => {
		cardApi.fetchConstructionCard.mockResolvedValue({
			...card, closingEntries: [{ ...card.closingEntries[0], price: 0.00012345 }],
		});
		const user = userEvent.setup();
		renderCardPage();
		await screen.findByText("exec-1");
		const closeTable = document.querySelector('[data-slot="closing-entries-table"]');
		const edit = closeTable?.querySelectorAll("button")[1];
		expect(edit).toBeDefined();
		await user.click(edit!);
		const input = screen.getByLabelText("цена закрытия");
		expect(input).toHaveValue("0.00012345");
		await user.clear(input);
		await user.type(input, "Infinity");
		await user.click(screen.getByRole("button", { name: "Сохранить пометку" }));
		expect(screen.getByText(/Введите корректную цену и время/)).toHaveAttribute("role", "alert");
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
		const table = document.querySelector('[data-slot="adjustments-table"]');
		const edit = table?.querySelectorAll("button")[1];
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
		await user.click(await screen.findByRole("button", { name: "Изменить капитал" }));
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
