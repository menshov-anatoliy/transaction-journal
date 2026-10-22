import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
	ConstructionCard,
	SyntheticCloseDisputeInput,
	SyntheticCloseManualLinkInput,
} from "@/lib/api/construction-card";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ConstructionMetricStrip } from "./metric-strip";

// Проверяется строка «зафиксировано синтетикой» сводки деталей конструкции
// (мастер design.pen Z5uVG в карточке «Сводка метрик» h69OG): величина
// паритетного лока к экспирации и логический размер закрываемой ноги
// «0.5 → 0.2»; индикатор финрезультата и инструментальные величины
// строкой не пересчитываются. Строка живёт, пока есть распознанные связки:
// она несёт переключатель раскрывающегося списка связок с действиями
// «Оспорить»/«Вернуть», а величины считаются только по неоспоренным
// связкам; без связок строка отсутствует целиком.
// Traceability: openspec:ui/screens#requirement-detail-synthetic-close-line
// Traceability: openspec:ui/screens#requirement-detail-synthetic-close-dispute-list

/** Базовый снимок карточки: открытый стреддл с одной опционной ногой. */
function makeCard(overrides: Partial<ConstructionCard> = {}): ConstructionCard {
	return {
		constructionId: 7,
		name: "ETH-28MAR-1600-C+P",
		status: "open",
		allocatedCapitalUsdt: 3000,
		riskPercent: 3,
		riskUsdt: 90,
		riskUnit: "percent",
		profitPercent: 8,
		profitUsdt: 240,
		profitUnit: "usdt",
		comment: null,
		hasOpenResidual: true,
		hasQuoteFailure: false,
		quotesAsOf: "2026-10-15T14:30:00+00:00",
		metrics: {
			realizedPnL: 60.25,
			unrealizedPnL: 40.25,
			adjustmentsPnL: 5,
			totalPnL: 105.5,
			totalPnLPercent: 3.5,
			realizedPnLPercent: 2,
			unrealizedPnLPercent: 1.34,
			adjustmentsPnLPercent: 0.17,
			quoteValue: 500,
			capitalUsagePercent: 16.7,
			realRiskUsdt: 45,
			worstCaseUsdt: 15.25,
			syntheticAdjustedTotalUsdt: null,
			realRiskStatus: "finite",
			openedAt: "2026-10-01T09:05:00+00:00",
			closedAt: null,
			durationSeconds: 3600,
			daysToNearestExpiry: 9,
			netDailyDecayUsdt: -6.8,
			totalTimeValueUsdt: 1240,
		},
		positions: [
			{
				symbol: "ETH-28MAR-1600-P",
				residual: 0.5,
				averageEntryPrice: 30,
				averageClosePrice: null,
				realizedPnL: 40,
				realizedPnLPercent: 1.33,
				unrealizedPnL: 30,
				unrealizedPnLPercent: 1,
				totalPnL: 70,
				totalPnLPercent: 2.33,
				accumulatedFees: 0.5,
				openedAt: "2026-10-01T09:05:00+00:00",
				closedAt: null,
				isOpen: true,
				comment: null,
				quoteValue: 300,
				priceChangePercent: 5,
				syntheticResidual: null,
				otherResidual: null,
				daysToExpiry: 9,
				dailyDecayUsdt: -6.8,
				intrinsicValueUsdt: 180.4,
				timeValueUsdt: 1059.6,
				decayDayAgoUsdt: -6.2,
				syntheticClosedQuantity: null,
				syntheticOpenQuantity: null,
			},
		],
		trades: [],
		closingEntries: [],
		closingWarnings: [],
		adjustments: [],
		syntheticCloses: [],
		riskWarnings: [],
		...overrides,
	};
}

// matchMedia-заглушка как в тестах каркаса: рабочий стол 1280px.
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
	setViewport(1280);
});

/** Состояние формы ручной связки и команд по умолчанию для помощника рендера. */
interface ManualLinkProps {
	readonly manualLinkFormOpen: boolean;
	readonly manualLinkPending: boolean;
	readonly manualLinkError: string | null;
	readonly onCreateManualLink: (input: SyntheticCloseManualLinkInput) => void;
	readonly onDeleteManualLink: (linkId: number) => void;
	readonly onManualLinkFormOpenChange: (open: boolean) => void;
}

/** Рендер сводки с заглушками команд спора, возврата и ручной связки. */
function renderStrip(
	card: ConstructionCard,
	onDispute: (key: SyntheticCloseDisputeInput) => void = () => undefined,
	onRevert: (key: SyntheticCloseDisputeInput) => void = () => undefined,
	manualLinkProps: Partial<ManualLinkProps> = {},
) {
	const defaults: ManualLinkProps = {
		manualLinkFormOpen: false,
		manualLinkPending: false,
		manualLinkError: null,
		onCreateManualLink: () => undefined,
		onDeleteManualLink: () => undefined,
		onManualLinkFormOpenChange: () => undefined,
		...manualLinkProps,
	};
	return render(
		// Подсказки метрик строятся на Radix Tooltip: провайдер-предок
		// обязателен, в тестах он же задаёт нулевую задержку открытия.
		<TooltipProvider delayDuration={0}>
			<ConstructionMetricStrip
				card={card}
				onDispute={onDispute}
				onRevert={onRevert}
				manualLinkPending={defaults.manualLinkPending}
				manualLinkError={defaults.manualLinkError}
				manualLinkFormOpen={defaults.manualLinkFormOpen}
				onCreateManualLink={defaults.onCreateManualLink}
				onDeleteManualLink={defaults.onDeleteManualLink}
				onManualLinkFormOpenChange={defaults.onManualLinkFormOpenChange}
			/>
		</TooltipProvider>,
	);
}

describe("ConstructionMetricStrip: строка «зафиксировано синтетикой»", () => {
	it("показывает лок к экспирации и логический размер ноги при неоспоренной связке", () => {
		// Сценарий «Сводка показывает зафиксированный синтетикой результат»:
		// связка 0.3 из путовой ноги 0.5 — строка с локом к экспирации,
		// нога показывается сокращённой до 0.2.
		// Traceability: openspec:ui/screens#scenario-detail-synthetic-locked-line-shown
		// Arrange: связка закрытия 0.3 путовой ноги из 0.5, лок 35.7 USDT.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});

		// Act: рендер сводки карточки.
		renderStrip(card);

		// Assert: строка выводится с величиной лока и сокращением ноги
		// 0.5 → 0.2; инструментальные величины в строке не повторяются.
		expect(screen.getByText("зафиксировано синтетикой")).toBeTruthy();
		expect(screen.getByText("+35.7 USDT")).toBeTruthy();
		expect(screen.getByText("ETH-28MAR-1600-P · нога 0.5 → 0.2")).toBeTruthy();
	});

	it("суммирует лок нескольких неоспоренных связок в одной строке", () => {
		// Arrange: две связки по одной и той же ноге: 0.1 + 0.2 из 0.5.
		const card = makeCard({
			syntheticCloses: [
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.1,
					lockedValueUsdt: 12,
					disputed: false,
					linearExecId: "exec-linear-1",
					counterOptionExecId: "exec-option-1",
					manualLinkId: null,
				},
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.2,
					lockedValueUsdt: 23.7,
					disputed: false,
					linearExecId: "exec-linear-2",
					counterOptionExecId: "exec-option-2",
					manualLinkId: null,
				},
			],
		});

		// Act: рендер сводки карточки.
		renderStrip(card);

		// Assert: лок 35.7 USDT, логический размер ноги 0.5 − 0.3 = 0.2.
		expect(screen.getByText("+35.7 USDT")).toBeTruthy();
		expect(screen.getByText("ETH-28MAR-1600-P · нога 0.5 → 0.2")).toBeTruthy();
	});

	it("не выводит строку без распознанных связок", () => {
		// Сценарий «Без связок и при споре строка отсутствует»: без связок
		// отображение не отличается от конструкции без синтетики.
		// Traceability: openspec:ui/screens#scenario-detail-synthetic-line-absent
		// Arrange: карточка без синтетических закрытий.
		const card = makeCard({ syntheticCloses: [] });

		// Act: рендер сводки карточки.
		renderStrip(card);

		// Assert: ни строки-переключателя, ни разделителя, ни списка в DOM
		// нет — отображение не отличается от конструкции без синтетики.
		expect(screen.queryByRole("button", { name: /зафиксировано синтетикой/ })).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-locked-line"]')).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-closes-list"]')).toBeNull();
	});

	it("оставляет строку-переключатель без величин, когда все связки оспорены", async () => {
		// Сценарий «Список доступен при всех оспоренных связках»: строка
		// с величиной лока отсутствует, но доступ к списку сохраняется,
		// и возврат связки остаётся достижим.
		// Traceability: openspec:ui/screens#scenario-dispute-list-available-when-all-disputed
		// Arrange: единственная связка оспорена.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: true,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});

		// Act: рендер сводки карточки, затем раскрытие списка связок.
		renderStrip(card);
		const user = userEvent.setup();
		await user.click(screen.getByRole("button", { name: /зафиксировано синтетикой/ }));

		// Assert: строка-переключатель осталась без величин лока и ног;
		// раскрытый список показывает оспоренную связку с признаком спора
		// и действием возврата.
		expect(screen.queryByText("+35.7 USDT")).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-locked-value"]')).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-locked-legs"]')).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-close-row"][data-disputed="true"]')).not.toBeNull();
		expect(screen.getByText("оспорена")).toBeTruthy();
		expect(screen.getByRole("button", { name: "Вернуть" })).toBeTruthy();
		expect(screen.queryByRole("button", { name: "Оспорить" })).toBeNull();
	});

	it("раскрытие списка показывает каждую связку и признак спора", async () => {
		// Сценарий «Раскрытие списка показывает каждую связку»: обе связки
		// с закрываемой ногой, количеством и локом к экспирации; оспоренная
		// помечена признаком спора.
		// Traceability: openspec:ui/screens#scenario-dispute-list-expands-shows-links
		// Arrange: две распознанные связки путовой ноги, вторая оспорена.
		const card = makeCard({
			syntheticCloses: [
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.2,
					lockedValueUsdt: 23.7,
					disputed: false,
					linearExecId: "exec-linear-2",
					counterOptionExecId: "exec-option-2",
					manualLinkId: null,
				},
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.3,
					lockedValueUsdt: 35.7,
					disputed: true,
					linearExecId: "exec-linear-1",
					counterOptionExecId: "exec-option-1",
					manualLinkId: null,
				},
			],
		});
		const user = userEvent.setup();
		renderStrip(card);

		// Act: до раскрытия список связок свёрнут; клик по переключателю
		// в строке синтетики разворачивает список.
		expect(document.querySelector('[data-slot="synthetic-closes-list"]')).toBeNull();
		await user.click(screen.getByRole("button", { name: /зафиксировано синтетикой/ }));

		// Assert: список показывает обе связки — ногу, количество и лок;
		// оспоренная связка приглушена признаком «оспорена», действия
		// соответствуют состояниям связок.
		const rows = document.querySelectorAll('[data-slot="synthetic-close-row"]');
		expect(rows.length).toBe(2);
		expect(screen.getAllByText("ETH-28MAR-1600-P").length).toBe(2);
		expect(screen.getByText("· 0.2")).toBeTruthy();
		expect(screen.getByText("· лок +23.7 USDT")).toBeTruthy();
		expect(screen.getByText("· 0.3")).toBeTruthy();
		expect(screen.getByText("· лок +35.7 USDT")).toBeTruthy();
		expect(screen.getByText("оспорена")).toBeTruthy();
		expect(screen.getByRole("button", { name: "Оспорить" })).toBeTruthy();
		expect(screen.getByRole("button", { name: "Вернуть" })).toBeTruthy();
	});

	it("нажатие «Вернуть» вызывает возврат оспоренной связки парой execId", async () => {
		// Сценарий «Возврат восстанавливает связку в логическом слое»:
		// действие «Вернуть» адресует оспоренную связку парой execId
		// и передаёт её команде возврата карточки.
		// Traceability: openspec:ui/screens#scenario-revert-action-restores-link
		// Arrange: раскрытый список с оспоренной связкой; замер возврата.
		const onRevert = vi.fn();
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: true,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});
		const user = userEvent.setup();
		renderStrip(card, () => undefined, onRevert);
		await user.click(screen.getByRole("button", { name: /зафиксировано синтетикой/ }));

		// Act: владелец возвращает оспоренную связку.
		await user.click(screen.getByRole("button", { name: "Вернуть" }));

		// Assert: команда возврата получила пару execId связки, спор
		// не был отправлен.
		expect(onRevert).toHaveBeenCalledWith({
			linearExecId: "exec-linear-1",
			counterOptionExecId: "exec-option-1",
		});
		expect(onRevert).toHaveBeenCalledTimes(1);
	});

	it("нажатие «Оспорить» вызывает оспаривание неоспоренной связки парой execId", async () => {
		// Arrange: раскрытый список с неоспоренной связкой; замер спора.
		const onDispute = vi.fn();
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.2,
				lockedValueUsdt: 23.7,
				disputed: false,
				linearExecId: "exec-linear-2",
				counterOptionExecId: "exec-option-2",
				manualLinkId: null,
			}],
		});
		const user = userEvent.setup();
		renderStrip(card, onDispute);
		await user.click(screen.getByRole("button", { name: /зафиксировано синтетикой/ }));

		// Act: владелец оспаривает ложную связку.
		await user.click(screen.getByRole("button", { name: "Оспорить" }));

		// Assert: команда спора получила пару execId связки, возврат
		// не был отправлен.
		expect(onDispute).toHaveBeenCalledWith({
			linearExecId: "exec-linear-2",
			counterOptionExecId: "exec-option-2",
		});
		expect(onDispute).toHaveBeenCalledTimes(1);
	});

	it("считает агрегаты строки только по неоспоренным связкам", () => {
		// Arrange: смесь связок — неоспоренная 0.2 с локом 23.7 и
		// оспоренная 0.3 с локом 35.7; спорная в логический слой не входит.
		const card = makeCard({
			syntheticCloses: [
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.2,
					lockedValueUsdt: 23.7,
					disputed: false,
					linearExecId: "exec-linear-2",
					counterOptionExecId: "exec-option-2",
					manualLinkId: null,
				},
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.3,
					lockedValueUsdt: 35.7,
					disputed: true,
					linearExecId: "exec-linear-1",
					counterOptionExecId: "exec-option-1",
					manualLinkId: null,
				},
			],
		});

		// Act: рендер сводки карточки со свёрнутым списком связок.
		renderStrip(card);

		// Assert: в строке лок только неоспоренной связки (+23.7 USDT),
		// нога сокращена только на неоспоренное количество 0.5 → 0.3;
		// спорный лок не суммируется и спорное количество не вычитается.
		expect(screen.getByText("+23.7 USDT")).toBeTruthy();
		expect(screen.queryByText("+59.4 USDT")).toBeNull();
		expect(screen.getByText("ETH-28MAR-1600-P · нога 0.5 → 0.3")).toBeTruthy();
		expect(screen.queryByText("ETH-28MAR-1600-P · нога 0.5 → 0")).toBeNull();
	});

	it("не пересчитывает инструментальные величины сводки логическим слоем", () => {
		// Arrange: связка уводит 0.3 из ноги с инструментальным остатком 0.5.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});

		// Act: рендер сводки карточки.
		renderStrip(card);

		// Assert: «Реализованный результат» остаётся инструментальным —
		// его величина совпадает с metrics.realizedPnL, лок в него не вмешался.
		expect(screen.getByText("+60.25")).toBeTruthy();
	});
});

describe("ConstructionMetricStrip: сводная величина «с учётом зафиксированного синтетикой»", () => {
	// Сводная величина зоны синтетики карточки: «Реализованный результат» +
	// Σ локов действующих связок; приходит из API nullable — число при
	// действующих (неоспоренных) связках, null без них. Величина не меняет
	// инструментальные метрики и индикатор финрезультата.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/ui/screens/spec#requirement-card-synthetic-adjusted-total-row
	it("показывает сводную рядом со строкой «зафиксировано синтетикой» при действующих связках", () => {
		// Сценарий «Величина показывается при действующих связках»: число из
		// API выводится в зоне синтетики со подписью и тоном по знаку.
		// Traceability: change:add-synthetic-adjusted-finresult/specs/ui/screens/spec#scenario-card-adjusted-total-shown-with-links
		// Arrange: неоспоренная связка с локом 35.7 USDT; сводная 96 USDT
		// приходит из API (реализованный 60.25 + лок 35.7 ≈ 95.95).
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});

		// Act: рендер сводки карточки со сводной из API.
		renderStrip({ ...card, metrics: { ...card.metrics, syntheticAdjustedTotalUsdt: 96 } });

		// Assert: сводная строка выведена — подпись и значение рядом со
		// строкой лока, тон значения зелёный по положительному знаку.
		expect(screen.getByText("зафиксировано синтетикой")).toBeTruthy();
		expect(screen.getByText("с учётом зафиксированного синтетикой")).toBeTruthy();
		expect(screen.getByText("+96 USDT")).toBeTruthy();
		const total = document.querySelector('[data-slot="synthetic-adjusted-total"]');
		expect(total).not.toBeNull();
		expect(total!.querySelector('[data-slot="synthetic-adjusted-total-value"]')).toHaveClass(
			"text-[color:var(--fin-positive-strong)]",
		);
	});

	it("не выводит сводную без действующих связок", () => {
		// Сценарий «Без связок величина отсутствует»: null из API — сводной
		// строки нет вместе со всей зоной синтетики.
		// Traceability: change:add-synthetic-adjusted-finresult/specs/ui/screens/spec#scenario-card-adjusted-total-absent-without-links
		// Arrange: карточка без связок; фикстура по умолчанию несёт null.
		const card = makeCard({ syntheticCloses: [] });

		// Act: рендер сводки карточки.
		renderStrip(card);

		// Assert: ни строки лока, ни сводной в DOM нет.
		expect(document.querySelector('[data-slot="synthetic-locked-line"]')).toBeNull();
		expect(document.querySelector('[data-slot="synthetic-adjusted-total"]')).toBeNull();
		expect(screen.queryByText("с учётом зафиксированного синтетикой")).toBeNull();
	});

	it("в раскрытом состоянии списка связок сводная стоит между переключателем и списком", async () => {
		// Порядок зоны синтетики по макету: строка-переключатель → сводная
		// величина → раскрытый список связок.
		// Traceability: change:add-synthetic-adjusted-finresult/specs/ui/screens/spec#requirement-card-synthetic-adjusted-total-row
		// Arrange: связка и сводная из API; раскрытие списка кликом.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});
		const user = userEvent.setup();
		renderStrip({ ...card, metrics: { ...card.metrics, syntheticAdjustedTotalUsdt: 96 } });
		await user.click(screen.getByRole("button", { name: /зафиксировано синтетикой/ }));

		// Act: список связок раскрыт.

		// Assert: в DOM сводная следует за строкой лока и стоит перед
		// раскрытым списком связок.
		const lockedLine = document.querySelector('[data-slot="synthetic-locked-line"]')!;
		const adjusted = document.querySelector('[data-slot="synthetic-adjusted-total"]')!;
		const list = document.querySelector('[data-slot="synthetic-closes-list"]')!;
		expect(lockedLine.compareDocumentPosition(adjusted) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
		expect(adjusted.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
	});
});

describe("ConstructionMetricStrip: саммари времени жизни", () => {
	// Саммари времени жизни в сводке карточки: остаток суток до ближайшей
	// экспирации открытых опционных ног, нетто-дневной распад и суммарная
	// временная стоимость; сутки в зоне предупреждения (≤ 14) — красные.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
	it("выводит саммари времени жизни с красными сутками в зоне предупреждения", () => {
		// Arrange: карточка с опционными ногами на 9 сутках.
		// Act: рендер сводки карточки.
		renderStrip(makeCard());

		// Assert: три метрики саммари — сутки со знаком зоны предупреждения,
		// распад красным по минусу и суммарная временная стоимость.
		const metrics = document.querySelectorAll('[data-slot="construction-metrics-card"] [data-slot="metric"]');
		const labels = [...metrics].map((metric) => metric.querySelector("span")?.textContent);
		expect(labels).toContain("до экспирации");
		expect(labels).toContain("распад / сутки");
		expect(labels).toContain("временная стоимость");
		const expiryMetric = metrics[metrics.length - 3];
		expect(expiryMetric).toHaveTextContent("9 дн.");
		expect(expiryMetric.querySelector('[data-slot="metric-value"]')).toHaveClass("text-destructive");
		const decayMetric = metrics[metrics.length - 2];
		expect(decayMetric).toHaveTextContent("-6.8");
		expect(decayMetric.querySelector('[data-slot="metric-value"]')).toHaveClass("text-[color:var(--fin-negative)]");
		expect(metrics[metrics.length - 1]).toHaveTextContent("+1240");
	});

	// Сутки дальше зоны предупреждения остаются нейтральными: красный —
	// признак зоны ≤ 14, а не постоянный стиль значения.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
	it("не красит сутки дальше зоны предупреждения", () => {
		// Arrange: ближайшая экспирация на 21 сутках.
		const card = makeCard();
		const metrics = { ...card.metrics, daysToNearestExpiry: 21, netDailyDecayUsdt: 2.5 };

		// Act: рендер сводки карточки.
		renderStrip({ ...card, metrics });

		// Assert: значение суток без красного тона, положительный распад —
		// в акцентно-зелёном тоне знака.
		const metricNodes = document.querySelectorAll('[data-slot="construction-metrics-card"] [data-slot="metric"]');
		const expiryMetric = metricNodes[metricNodes.length - 3];
		expect(expiryMetric).toHaveTextContent("21 дн.");
		expect(expiryMetric.querySelector('[data-slot="metric-value"]')).not.toHaveClass("text-destructive");
		expect(metricNodes[metricNodes.length - 2].querySelector('[data-slot="metric-value"]')).toHaveClass(
			"text-[color:var(--fin-positive-strong)]",
		);
	});

	// Без открытых опционных ног или без тикеров саммари времени жизни
	// деградирует прочерками во всех трёх метриках.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
	it("показывает прочерки саммари без опционных ног и тикеров", () => {
		// Arrange: карточка без метрик времени жизни (нет опционных ног).
		const card = makeCard();
		const metrics = { ...card.metrics, daysToNearestExpiry: null, netDailyDecayUsdt: null, totalTimeValueUsdt: null };

		// Act: рендер сводки карточки.
		renderStrip({ ...card, metrics });

		// Assert: три метрики саммари — прочерки.
		const metricNodes = document.querySelectorAll('[data-slot="construction-metrics-card"] [data-slot="metric"]');
		for (const metric of [...metricNodes].slice(-3)) {
			expect(metric).toHaveTextContent("—");
		}
	});
});

describe("ConstructionMetricStrip: ручная синтетическая связка", () => {
	// Точка входа формы ручной связки в сводке деталей: действие доступно
	// и без действующих связок — первую ручную связку иначе не создать;
	// при связках форма открывается из зоны синтетики.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	it("действие «Ручная связка» доступно без действующих связок, строка синтетики скрыта", async () => {
		// Arrange: карточка без связок; замер смены состояния формы.
		const onManualLinkFormOpenChange = vi.fn();
		const user = userEvent.setup();

		// Act: рендер сводки без связок и клик по действию.
		renderStrip(makeCard({ syntheticCloses: [] }), () => undefined, () => undefined, {
			onManualLinkFormOpenChange,
		});
		await user.click(screen.getByRole("button", { name: "Ручная связка" }));

		// Assert: строка «зафиксировано синтетикой» не выводится, но действие
		// открыло форму ручной связки.
		expect(document.querySelector('[data-slot="synthetic-locked-line"]')).toBeNull();
		expect(onManualLinkFormOpenChange).toHaveBeenCalledWith(true);
	});

	it("открытая форма ручной связки встроена в зону синтетики и показывает ошибку сервера", () => {
		// Сценарий «Рассогласованные инструменты или количества отклоняются»:
		// человекочитаемая причина отказа серверной валидации показывается
		// в самой форме.
		// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
		// Arrange: форма открыта, сервер ответил причиной отказа.
		renderStrip(makeCard(), () => undefined, () => undefined, {
			manualLinkFormOpen: true,
			manualLinkError: "Встречный опцион «ETH-26DEC-3000-C» не совпадает страйком с закрываемой ногой.",
		});

		// Assert: форма присутствует, причина отказа доступна как alert.
		expect(document.querySelector('[data-slot="manual-synthetic-link-form"]')).not.toBeNull();
		expect(screen.getByText("Ручная синтетическая связка")).toBeTruthy();
		expect(screen.getByRole("alert")).toHaveTextContent("не совпадает страйком");
	});

	it("ручная связка в списке удаляется по идентификатору из снимка, распознанная оспаривается", async () => {
		// Arrange: раскрытый список — ручная связка (идентификатор строки
		// связки приходит в строке снимка карточки) и распознанная; замер
		// команды удаления.
		const onDeleteManualLink = vi.fn();
		const card = makeCard({
			syntheticCloses: [
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.3,
					lockedValueUsdt: 35.7,
					disputed: false,
					linearExecId: "manual:5",
					counterOptionExecId: "manual:7",
					manualLinkId: 12,
				},
				{
					closedLegSymbol: "ETH-28MAR-1600-P",
					quantity: 0.2,
					lockedValueUsdt: 23.7,
					disputed: false,
					linearExecId: "exec-linear-2",
					counterOptionExecId: "exec-option-2",
					manualLinkId: null,
				},
			],
		});
		const user = userEvent.setup();
		renderStrip(card, () => undefined, () => undefined, { onDeleteManualLink });
		await user.click(screen.getByRole("button", { name: /зафиксировано синтетикой/ }));

		// Act: владелец удаляет ручную связку.
		await user.click(screen.getByRole("button", { name: "Удалить" }));

		// Assert: команда удаления получила идентификатор ручной связки из
		// строки снимка — действие доступно в любой сессии, без сессионного
		// реестра; у ручной строки спора нет, распознанная связка осталась
		// с действием «Оспорить».
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		expect(onDeleteManualLink).toHaveBeenCalledWith(12);
		expect(onDeleteManualLink).toHaveBeenCalledTimes(1);
		expect(screen.getByRole("button", { name: "Оспорить" })).toBeTruthy();
		expect(screen.queryByRole("button", { name: "Вернуть" })).toBeNull();
	});
});

// Подсказки числовых метрик сводки: триггер — само значение, текст попапа
// берётся из справочника; многопоказательные ячейки объясняют каждый
// показатель своим разделом; даты и времена подсказок не получают.
// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
describe("ConstructionMetricStrip: подсказки числовых метрик сводки", () => {
	it("числовые метрики открывают подсказку с заголовком справочника", async () => {
		// Каждая числовая метрика снабжается подсказкой: наведение на
		// значение открывает попап, называющий метрику из справочника.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
		// Arrange: карточка по умолчанию; значение → ключ справочника.
		const user = userEvent.setup();
		renderStrip(makeCard());

		for (const [value, key] of [
			["+105.5 USDT", "totalPnl"],
			["+3.5%", "capitalPercent"],
			["+500", "constructionValue"],
			["+16.7%", "capitalUsage"],
			["+60.25", "realizedPnl"],
			["+40.25", "unrealizedPnl"],
			["+5", "adjustmentsCount"],
			["9 дн.", "daysToExpiry"],
			["-6.8", "decayPerDay"],
			["+1240", "timeValue"],
		] as const) {
			// Act: наведение на значение очередной метрики.
			await user.hover(screen.getByText(value));

			// Assert: открыта ровно одна подсказка с заголовком справочника;
			// предыдущая закрыта уходом указателя.
			await waitFor(() => {
				const tooltips = screen.getAllByRole("tooltip");
				expect(tooltips).toHaveLength(1);
				expect(tooltips[0]).toHaveTextContent(METRIC_TOOLTIPS[key].title);
			});
		}
	});

	it("подсказка «% капитала» даёт раздел на процент и на капитал", async () => {
		// Многопоказательная ячейка объясняет оба показателя раздельно:
		// раздел о проценте результата и раздел о выделенном капитале.
		// Traceability: openspec:ui/screens#scenario-multi-indicator-sections
		// Arrange: карточка по умолчанию с капиталом и процентом.
		const user = userEvent.setup();
		renderStrip(makeCard());

		// Act: наведение на значение процента.
		await user.hover(screen.getByText("+3.5%"));

		// Assert: подсказка содержит оба раздела справочника.
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.capitalPercent.title);
		for (const section of METRIC_TOOLTIPS.capitalPercent.sections ?? []) {
			expect(tooltip).toHaveTextContent(section.label);
			expect(tooltip).toHaveTextContent(section.text);
		}
	});

	it("подсказка строки синтетики даёт разделы на лок и размер ноги", async () => {
		// Строка «зафиксировано синтетикой» — одна многопоказательная
		// ячейка: лок к экспирации и логический размер ноги поясняются
		// одной подсказкой с разделом на каждый показатель.
		// Traceability: openspec:ui/screens#scenario-multi-indicator-sections
		// Arrange: неоспоренная связка — строка с величинами выведена.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});
		const user = userEvent.setup();
		renderStrip(card);

		// Act: наведение на величину лока.
		await user.hover(screen.getByText("+35.7 USDT"));

		// Assert: подсказка называет строку и содержит оба раздела.
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS.syntheticLocked.title);
		for (const section of METRIC_TOOLTIPS.syntheticLocked.sections ?? []) {
			expect(tooltip).toHaveTextContent(section.label);
			expect(tooltip).toHaveTextContent(section.text);
		}
	});

	it("сводная «с учётом зафиксированного синтетикой» открывает подсказку", async () => {
		// Сводная величина зоны синтетики — числовая метрика: подсказка
		// объясняет её состав из справочника.
		// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
		// Arrange: действующая связка и сводная из API.
		const card = makeCard({
			syntheticCloses: [{
				closedLegSymbol: "ETH-28MAR-1600-P",
				quantity: 0.3,
				lockedValueUsdt: 35.7,
				disputed: false,
				linearExecId: "exec-linear-1",
				counterOptionExecId: "exec-option-1",
				manualLinkId: null,
			}],
		});
		const user = userEvent.setup();
		renderStrip({ ...card, metrics: { ...card.metrics, syntheticAdjustedTotalUsdt: 96 } });

		// Act: наведение на значение сводной.
		await user.hover(screen.getByText("+96 USDT"));

		// Assert: подсказка называет сводную из справочника.
		await waitFor(() => expect(screen.getByRole("tooltip")).toBeInTheDocument());
		expect(screen.getByRole("tooltip")).toHaveTextContent(METRIC_TOOLTIPS.adjustedTotal.title);
	});

	it("«Котировки на» и «Период» подсказок не имеют", async () => {
		// Даты и времена подсказками метрик не снабжаются: триггера нет
		// ни у подписи, ни у значения, наведение ничего не открывает.
		// Traceability: openspec:ui/screens#scenario-dates-skip-tooltip
		// Arrange: карточка по умолчанию с заполненными датами.
		const user = userEvent.setup();
		renderStrip(makeCard());

		for (const label of ["котировки на", "период"]) {
			// Assert (структура): у метрики-даты нет триггера подсказки.
			const metric = screen.getByText(label).closest('[data-slot="metric"]');
			expect(metric).not.toBeNull();
			expect(metric!.querySelector('[data-slot="tooltip-trigger"]')).toBeNull();

			// Act: наведение на всю метрику — и подпись, и значение.
			await user.hover(metric!);

			// Assert (поведение): подсказка не открылась.
			expect(screen.queryByRole("tooltip")).toBeNull();
		}
	});
});

describe("ConstructionMetricStrip: подсказка чтения индикатора", () => {
	// Подсказка чтения индикатора карточки: «?» в правом верхнем углу блока
	// индикатора, над текстовыми метками, открывает попап с пояснением
	// чтения шкалы; подсказка живёт в полном и среднем
	// виде индикатора карточки. Компактный вид таблиц и краткая карточка
	// контекстной панели подсказку не получают — это покрыто их собственными
	// тестами и не затрагивается здесь.
	// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-card-only
	it("«?» в правом верхнем углу блока индикатора открывает попап с заголовком и текстами зон", async () => {
		// Сценарий «Открытие подсказки триггером»: клик по «?» у индикатора
		// карточки открывает попап чтения с заголовком и пояснением зон.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-opens
		// Arrange: сводка карточки в полном виде; замер клика по триггеру.
		const user = userEvent.setup();
		renderStrip(makeCard());

		// Act: клик по кнопке «Как читать индикатор» в правом верхнем углу
		// блока индикатора.
		await user.click(screen.getByRole("button", { name: "Как читать индикатор" }));

		// Assert: попап открыт с заголовком и дословными строками зон
		// планового риска и сверхприбыли из макета.
		expect(screen.getByRole("dialog")).toBeInTheDocument();
		expect(screen.getByText("Как читать индикатор")).toBeTruthy();
		expect(screen.getByText(/плановый риск — красная зона от −Риск до 0/)).toBeTruthy();
		expect(screen.getByText(/сверхприбыль — золотая зона за \+Профит/)).toBeTruthy();
	});

	it("поясняет насечку худшего исхода и маркер результата без устаревших формулировок", async () => {
		// Сценарий «Пояснение отражает актуальную модель чтения»: подсказка
		// объясняет насечку как худший исход конструкции к экспирации и
		// маркер как текущий результат — реализованную часть, оценку
		// остатков «заберу при ликвидации сейчас» и внешние корректировки;
		// формулировки устаревшей модели («реальный риск минус
		// реализованная прибыль», «граница строится от нуля», «оценка
		// итога», «Общий P&L») в попапе отсутствуют.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-reading-tooltip-explains-current-model
		// Arrange: открытая подсказка чтения.
		const user = userEvent.setup();
		renderStrip(makeCard());
		await user.click(screen.getByRole("button", { name: "Как читать индикатор" }));
		const popup = screen.getByRole("dialog");

		// Assert: насечка объяснена худшим исходом конструкции к экспирации
		// с подписью от плановой границы зоны, маркер — текущим результатом
		// с разбивкой на части; устаревших формулировок во всём попапе нет.
		expect(within(popup).getByText(/насечка — худший исход конструкции к экспирации/)).toBeTruthy();
		expect(within(popup).getByText(/маркер — текущий результат: реализованный результат, оценка остатков «заберу при ликвидации сейчас» и внешние корректировки/)).toBeTruthy();
		expect(within(popup).queryByText(/оценка итога/)).toBeNull();
		expect(within(popup).queryByText(/Общий P&L/)).toBeNull();
		expect(within(popup).queryByText(/реальный риск минус реализованная прибыль/)).toBeNull();
		expect(within(popup).queryByText(/граница строится от нуля/)).toBeNull();
	});

	it("объясняет насечку правее маркера синтетической фиксацией остатков и отсылает к зоне синтетики", async () => {
		// Сценарии «Подсказка объясняет насечку правее маркера» и
		// «Подсказка отсылает к зоне синтетики»: случай «насечка правее
		// маркера» объясняется гарантией платежа остатков, в частности
		// синтетической фиксацией по правилу ac-43, а место
		// зафиксированной части названо зоной синтетики карточки.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-tooltip-explains-notch-right-of-marker
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-tooltip-points-to-synthetic-zone
		// Arrange: открытая подсказка чтения.
		const user = userEvent.setup();
		renderStrip(makeCard());
		await user.click(screen.getByRole("button", { name: "Как читать индикатор" }));
		const popup = screen.getByRole("dialog");

		// Assert: насечка правее маркера объяснена синтетической фиксацией
		// остатков по правилу ac-43; зафиксированная часть адресована
		// зоной синтетики карточки.
		expect(within(popup).getByText(/при фиксации остатков синтетикой — и правее маркера/)).toBeTruthy();
		expect(within(popup).getByText(/синтетическая фиксация по правилу ac-43/)).toBeTruthy();
		expect(within(popup).getByText(/величина зафиксированного видна в зоне синтетики карточки/)).toBeTruthy();
	});

	it("объясняет расхождение насечки и маркера при полной синтетической фиксации", async () => {
		// Сценарий «Подсказка объясняет расхождение при полной фиксации»:
		// маркер может стоять левее насечки, потому что рыночная оценка
		// «сейчас» несёт временную стоимость встречного опциона, спред и
		// свежесть котировок — к экспирации значения сходятся.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-tooltip-explains-full-fix-divergence
		// Arrange: открытая подсказка чтения.
		const user = userEvent.setup();
		renderStrip(makeCard());
		await user.click(screen.getByRole("button", { name: "Как читать индикатор" }));
		const popup = screen.getByRole("dialog");

		// Assert: причина расхождения названа через временную стоимость
		// встречного опциона и сходимость к экспирации.
		expect(within(popup).getByText(/маркер может стоять левее насечки/)).toBeTruthy();
		expect(within(popup).getByText(/временную стоимость встречного опциона, спред и свежесть котировок/)).toBeTruthy();
		expect(within(popup).getByText(/к экспирации значения сходятся/)).toBeTruthy();
	});

	it("закрывается по Esc", async () => {
		// Сценарий «Закрытие подсказки»: Esc — один из обязательных
		// способов убрать подсказку чтения.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-closes
		// Arrange: открытая подсказка чтения.
		const user = userEvent.setup();
		renderStrip(makeCard());
		await user.click(screen.getByRole("button", { name: "Как читать индикатор" }));
		expect(screen.getByRole("dialog")).toBeInTheDocument();

		// Act: нажатие Esc.
		await user.keyboard("{Escape}");

		// Assert: попап убран из портала.
		expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
	});

	it("показывает триггер и открывает попап в среднем виде на мобильной ширине", async () => {
		// Сценарий «Открытие подсказки триггером»: на узком экране карточка
		// рендерит средний вид индикатора, и триггер «?» там тоже открывает
		// попап чтения — подсказка охватывает оба вида.
		// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-opens
		// Arrange: мобильная ширина 375 — индикатор среднего вида.
		setViewport(375);
		const user = userEvent.setup();
		renderStrip(makeCard());
		expect(document.querySelector('[data-slot="fin-result-medium"]')).not.toBeNull();

		// Act: клик по триггеру «?» у среднего индикатора.
		await user.click(screen.getByRole("button", { name: "Как читать индикатор" }));

		// Assert: попап открыт с заголовком чтения шкалы.
		expect(screen.getByRole("dialog")).toBeInTheDocument();
		expect(screen.getByText("Как читать индикатор")).toBeTruthy();
	});

	it("держит равные видимые отступы вокруг кнопки подсказки: ряд 26px", () => {
		// Геометрия ряда подсказки: jsdom не считает layout, поэтому
		// фиксируются классы-носители размеров; полный расчёт «почему 26px,
		// а не 28px» — в комментарии у триггера metric-strip.tsx. Паттерн
		// с computeFinResultGeometry здесь не применяется: вертикальный
		// ритм ряда не зависит от ширины полосы. Равенство зазоров
		// подтверждено замером в реальном браузере (headless Chrome
		// на стенде с настоящими шрифтами): оба вида дают 4px и 4px,
		// зазор «метки → полоса» 11px.
		// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
		// Arrange: рабочая ширина — полный вид индикатора; ряд общий.
		setViewport(1280);
		renderStrip(makeCard());
		const trigger = screen.getByRole("button", { name: "Как читать индикатор" });
		const row = trigger.parentElement;

		// Assert: обёртка несёт компенсацию гэпа секции и 26px-ряд,
		// триггер — 20px в top 4px правого угла ряда.
		expect(row).not.toBeNull();
		expect(row?.className).toContain("-mt-4");
		expect(row?.className).toContain("pt-[26px]");
		expect(trigger.className).toContain("top-[4px]");
		expect(trigger.className).toContain("size-5");
	});
});

describe("ConstructionMetricStrip: метрика «текущий результат»", () => {
	it("называет сводную «текущий результат» и показывает полную сумму", () => {
		// Сценарий «Маркер результата включает внешние корректировки»:
		// метрика сводки «текущий результат» показывает то же число, что
		// маркер индикатора, — реализованную часть, нереализованную и
		// внешние корректировки; имя «общий P&L» больше не используется.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-includes-adjustments
		// Arrange: метрики карточки 60.25 + 40.25 + 5 = 105.5 USDT.
		renderStrip(makeCard());

		// Assert: метка переименована, значение — полная сумма, старой
		// метки в сводке нет.
		expect(screen.getByText("текущий результат")).toBeTruthy();
		expect(screen.getByText("+105.5 USDT")).toBeTruthy();
		expect(screen.queryByText("общий P&L")).toBeNull();
	});

	it("помечает неполный текущий результат сбоем котировок с пояснением в тайтле", () => {
		// Сценарий «Сбой котировок оставляет результат по доступной части»:
		// без нереализованной части метрика помечена неполной, тайтл
		// называет новую метку и причину неполноты.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-incomplete-with-adjustments
		// Arrange: сбой котировок — нереализованная часть и итог null.
		const base = makeCard();
		const card = makeCard({
			hasQuoteFailure: true,
			metrics: { ...base.metrics, unrealizedPnL: null, totalPnL: null },
		});

		// Act: рендер сводки карточки.
		renderStrip(card);

		// Assert: признак неполноты сопровождён тайтлом про текущий
		// результат и сбой котировок.
		const incomplete = screen.getByText("неполный (сбой котировок)");
		expect(incomplete.getAttribute("title")).toBe(
			"Текущий результат неполный: нереализованная часть не оценена из-за сбоя котировок",
		);
	});
});

describe("ConstructionMetricStrip: вход индикатора результата", () => {
	it("прокидывает внешние корректировки PnL во вход индикатора", () => {
		// Сценарий «Маркер результата включает внешние корректировки»:
		// маркер подписан полной суммой реализованной части, доступной
		// нереализованной и внешних корректировок — без прокидывания
		// adjustmentsPnL подпись была бы меньше на их величину.
		// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-includes-adjustments
		// Arrange: метрики карточки 60.25 + 40.25 + 5 = 105.5 USDT;
		// подпись маркера без корректировок была бы «+100.5».
		renderStrip(makeCard());

		// Assert: подпись маркера полного индикатора — полная сумма
		// текущего результата.
		const markerLabel = document.querySelector<HTMLElement>('[data-part="marker-label"]');
		expect(markerLabel).not.toBeNull();
		expect(markerLabel?.textContent).toBe("+105.5");
	});
});
