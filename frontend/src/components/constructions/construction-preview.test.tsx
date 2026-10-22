import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import type { ConstructionPreview } from "@/lib/api/constructions";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ConstructionPreviewCard } from "./construction-preview";

// Проверяется краткая карточка конструкции в контекстной панели списка:
// read-only сводка метрик с цветом по знаку, средний индикатор
// финрезультата с приглушёнными плановыми границами, строка периода и
// единственное действие «Открыть карточку» (открытие в новом окне —
// команда двойного клика по строке списка).
// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card

const preview: ConstructionPreview = {
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
	adjustmentsPnL: 5,
	totalPnL: 105.5,
	totalPnLPercent: 3.5,
	quoteValue: 500,
	capitalUsagePercent: 16.7,
	realRiskUsdt: 45,
	worstCaseUsdt: 15.25,
	realRiskStatus: "finite",
	openedAt: "2026-06-18T09:05:00",
	closedAt: null,
	quotesAsOf: "2026-06-20T14:30:00",
	hasQuoteFailure: false,
	counts: { positions: 2, openPositions: 1, trades: 3, adjustments: 1 },
	riskWarningsCount: 0,
	riskWarnings: [],
};

function renderPreview(overrides: Partial<ConstructionPreview> = {}, liveHintCount?: number) {
	return render(
		// Средний индикатор несёт подсказки чисел на Radix Tooltip —
		// провайдер-предок обязателен для рендера.
		<TooltipProvider delayDuration={0}>
			<MemoryRouter>
				<ConstructionPreviewCard preview={{ ...preview, ...overrides }} liveHintCount={liveHintCount} />
			</MemoryRouter>
		</TooltipProvider>,
	);
}

describe("краткая карточка конструкции", () => {
	it("показывает имя со статусной пилюлей и сводку четырёх метрик", () => {
		// Act: карточка открытой конструкции.
		renderPreview();

		// Assert: шапка карточки — имя конструкции; метрики — примитив
		// Метрика (мастер «Карточка конструкции/Превью», N6abN).
		// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
		expect(screen.getByRole("heading", { name: "ETH-240628-3200C+P" })).toBeInTheDocument();
		const metrics = document.querySelectorAll('[data-slot="metric"]');
		expect(metrics).toHaveLength(4);
		expect(screen.getByText("капитал")).toBeInTheDocument();
		expect(screen.getByText("стоимость")).toBeInTheDocument();
		expect(screen.getByText("реализов.")).toBeInTheDocument();
		expect(screen.getByText("нереализов.")).toBeInTheDocument();
	});

	it("красит величины по знаку: плюс — зелёным, минус — красным", () => {
		// Цветовая индикация чисел краткой карточки по знаку величины:
		// реализованная и нереализованная части окрашиваются зелёным
		// или красным, капитал и стоимость остаются нейтральными.
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Act: карточка с прибылью.
		const { unmount } = renderPreview();

		// Assert: положительные величины — зелёные, нейтральные — без тона.
		// Тон несёт вложенный span значения внутри примитива Метрика.
		const grid = document.querySelector('[data-slot="metric"]')?.parentElement;
		expect(grid?.className).toContain("grid-cols-2");
		const realizedValue = grid?.querySelectorAll('[data-slot="metric"]')[2]?.querySelector('[data-slot="metric-value"]');
		expect(realizedValue?.textContent).toBe("+60.25");
		const realized = realizedValue?.querySelector("span");
		expect(realized?.className).toContain("text-[color:var(--fin-positive-strong)]");
		const capital = grid?.querySelectorAll('[data-slot="metric"]')[0]?.querySelector('[data-slot="metric-value"] span');
		expect(capital?.className).not.toContain("text-[color:var(--fin-negative)]");
		unmount();

		// Act: карточка с убытком по обеим частям.
		renderPreview({ realizedPnL: -12.5, unrealizedPnL: -3.5 });

		// Assert: отрицательные величины — красные.
		const lossGrid = document.querySelector('[data-slot="metric"]')?.parentElement;
		const realizedLoss = lossGrid?.querySelectorAll('[data-slot="metric"]')[2]?.querySelector('[data-slot="metric-value"] span');
		expect(realizedLoss?.className).toContain("text-[color:var(--fin-negative)]");
		const unrealizedLoss = lossGrid?.querySelectorAll('[data-slot="metric"]')[3]?.querySelector('[data-slot="metric-value"] span');
		expect(unrealizedLoss?.className).toContain("text-[color:var(--fin-negative)]");
	});

	it("показывает средний индикатор финрезультата вместо полного", () => {
		// Краткая карточка — средний вид индикатора (мастер rMxFZ) с
		// приглушёнными плановыми границами; полный вид остаётся в
		// карточке конструкции.
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Act: карточка открытой конструкции.
		renderPreview();

		// Assert: средний индикатор присутствует, полного нет.
		expect(document.querySelector('[data-slot="fin-result-medium"]')).not.toBeNull();
		expect(document.querySelector('[data-slot="fin-result-full"]')).toBeNull();
		// Приглушённые плановые границы: подписи риска и профита на
		// textMuted, цветными остаются граница реального риска и маркер.
		const riskScale = document.querySelector('[data-part="risk-scale"]');
		expect(riskScale?.className).toContain("text-text-muted");
		expect(riskScale?.className).not.toContain("fin-negative");
		const profitScale = document.querySelector('[data-part="profit-scale"]');
		expect(profitScale?.className).toContain("text-text-muted");
	});

	it("показывает период, счётчики записей и живые подсказки", () => {
		// Строка периода карточки: открытие, длительность, счётчики сделок
		// и корректировок; сегмент живых подсказок приходит из строки
		// обзора и опционален.
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Act: закрытая конструкция с известным концом периода и двумя живыми подсказками.
		renderPreview({ closedAt: "2026-06-20T09:05:00" }, 2);

		// Assert: открытие, длительность 2 дня, счётчики и сегмент подсказок.
		expect(screen.getByText("2026-06-18")).toBeInTheDocument();
		expect(screen.getByText(/2 дн\./)).toBeInTheDocument();
		expect(screen.getByText(/3 сделки/)).toBeInTheDocument();
		expect(screen.getByText(/1 корректировка/)).toBeInTheDocument();
		expect(screen.getByText(/живых подсказок: 2/)).toBeInTheDocument();
	});

	it("скрывает сегмент живых подсказок без значения или при нуле", () => {
		// Сегмент живых подсказок опционален: без числа из строки обзора
		// или при нуле строка периода не показывает сегмент.
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Act: карточка без живых подсказок.
		renderPreview();

		// Assert: сегмента подсказок нет.
		expect(screen.queryByText(/живых подсказок/)).not.toBeInTheDocument();
	});

	it("деградирует нереализованную оценку признаком сбоя котировок", () => {
		// Сбой котировок — видимое состояние карточки: вместо нереализованной
		// оценки показывается признак сбоя с пояснением.
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Act: карточка со сбоем котировок.
		renderPreview({ unrealizedPnL: null, hasQuoteFailure: true });

		// Assert: признак сбоя занимает место нереализованной величины.
		expect(screen.getByText("сбой котировок")).toBeInTheDocument();
	});

	it("перечисляет все предупреждения полностью тоном риска", () => {
		// Краткая карточка показывает полный перечень записей диагностики,
		// а не счётчик: заголовок «Предупреждения» и каждый текст тоном
		// риска (фрейм Warnings мастера N6abN).
		// Traceability: openspec:ui/screens#scenario-preview-lists-all-warnings
		// Act: карточка конструкции с двумя предупреждениями.
		renderPreview({
			riskWarningsCount: 2,
			riskWarnings: [
				{ text: "ETHUSDT · 1 из 2 контрактов · убыток ограничен ценой входа" },
				{ text: "ETHUSDT · 1 контракт · убыток не ограничен" },
			],
		});

		// Assert: секция присутствует, показаны все записи по порядку,
		// заголовок и записи несут тон риска.
		const section = document.querySelector('[data-slot="construction-preview-warnings"]');
		expect(section).not.toBeNull();
		expect(screen.getByText("Предупреждения")).toBeInTheDocument();
		expect(screen.getByText("ETHUSDT · 1 из 2 контрактов · убыток ограничен ценой входа")).toBeInTheDocument();
		expect(screen.getByText("ETHUSDT · 1 контракт · убыток не ограничен")).toBeInTheDocument();
		const lines = [...(section?.querySelectorAll("p") ?? [])];
		expect(lines).toHaveLength(3);
		// Заголовок — полужирный, записи — обычное начертание, все тоном риска.
		expect(lines[0]?.className).toContain("font-semibold");
		expect(lines[1]?.className).not.toContain("font-semibold");
		expect(lines[2]?.className).not.toContain("font-semibold");
		for (const line of lines) {
			expect(line?.className).toContain("text-[color:var(--risk)]");
		}
	});

	it("скрывает секцию предупреждений без предупреждений", () => {
		// Пустой перечень диагностики — секции нет: карточка без
		// предупреждений не показывает ни заголовок, ни записи.
		// Traceability: openspec:ui/screens#requirement-construction-warnings-display
		// Act: карточка конструкции без предупреждений.
		renderPreview();

		// Assert: секции предупреждений нет.
		expect(document.querySelector('[data-slot="construction-preview-warnings"]')).toBeNull();
		expect(screen.queryByText("Предупреждения")).not.toBeInTheDocument();
	});

	it("содержит единственное действие «Открыть карточку»", () => {
		// Краткая карточка не несёт команд работы с журналом: единственное
		// действие — переход на маршрут карточки конструкции; открытие в
		// новом окне удалено (покрыто двойным кликом по строке списка).
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		// Traceability: change:rework-spa-shell-and-screens/design#D4
		// Act: карточка открытой конструкции.
		renderPreview();

		// Assert: одна ссылка на карточку, кнопок в новом окне нет.
		expect(screen.getByRole("link", { name: /открыть карточку/i })).toHaveAttribute("href", "/constructions/7");
		expect(screen.queryByText(/в новом окне/i)).not.toBeInTheDocument();
	});
});
