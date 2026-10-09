import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { SourceTrace } from "@/chat/types";
import { SourceTraceView } from "./source-trace-view";

// След источников в ответе ИИ-помощника: использованные инструменты
// и ссылки на карточки правил и данные журнала с их отметками as-of —
// рендер-примитив под ответом, поверх общего MD-рендера текста.
// Traceability: change:add-agent-chat/proposal#what-changes
// Слой сверки с design.pen (задача 5.2 change reconcile-frontend-with-design):
// панель следа — мастер bUrOy «След источников» (контейнер/заголовок/строки),
// инструменты — пилюли-источники fYadZ; классы проверяются как шов.
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives

describe("след источников в ответе ИИ-помощника", () => {
	// Длинный рыночный источник переносится внутри пилюли без потери признака кэша.
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	it("разрешает перенос длинных аргументов и as-of в чипе источника", () => {
		// Arrange / Act: длинный аргумент без пробелов и деградированные данные.
		render(<SourceTraceView trace={{
			toolCalls: [{ tool: "get_market_snapshot", argument: "ETH-20261030-3200-C".repeat(8), asOf: "2026-10-09T09:00:00Z", degraded: true }],
			references: [],
		}} />);

		// Assert: ни пилюля, ни текст не вынуждают ленту расти по ширине.
		const cache = screen.getByText("кэш");
		const chip = cache.closest('[data-slot="source-chip"]');
		expect(chip).toHaveClass("max-w-full", "whitespace-normal");
		expect(chip).not.toHaveClass("whitespace-nowrap");
		expect(cache.parentElement).toHaveClass("min-w-0", "[overflow-wrap:anywhere]");
		expect(chip?.parentElement).toHaveClass("min-w-0", "max-w-full");
	});

	it("показывает вызванные инструменты пилюлями-источниками с аргументами и as-of", () => {
		// Arrange: ответ использовал рыночный инструмент и чтение карточки.
		const trace: SourceTrace = {
			toolCalls: [
				{
					tool: "get_market_snapshot",
					argument: "BTC",
					asOf: "2026-10-08T05:00:00Z",
				},
				{ tool: "read_rule_card", argument: "R-12" },
			],
			references: [],
		};

		// Act: след рендерится примитивом.
		render(<SourceTraceView trace={trace} />);

		// Assert: инструменты видны с аргументами; as-of показан локальным
		// временем зоны браузера, а не сырой ISO-строкой.
		expect(screen.getByText(/get_market_snapshot/)).toBeInTheDocument();
		expect(screen.getByText(/BTC/)).toBeInTheDocument();
		expect(screen.getByText(/read_rule_card/)).toBeInTheDocument();
		expect(screen.getByText(/R-12/)).toBeInTheDocument();
		expect(screen.getByText(/2026-10-08/)).toBeInTheDocument();

		// Assert: вызов инструмента — пилюля-источник (мастер fYadZ,
		// pill 999), а не прямоугольный чип rounded-md аудита §3.5:5.
		const chip = screen.getByText(/get_market_snapshot/).closest('[data-slot="source-chip"]');
		expect(chip).not.toBeNull();
		expect(chip?.className).toContain("rounded-full");
		expect(chip?.className).not.toContain("rounded-md");
	});

	it("показывает ссылки на карточки правил и данные журнала с as-of", () => {
		// Arrange: ответ ссылается на карточку правила и запись журнала.
		const trace: SourceTrace = {
			toolCalls: [],
			references: [
				{
					kind: "rule-card",
					id: "R-3",
					title: "Роллирование коротких коллов",
					asOf: "2026-10-07T12:00:00Z",
				},
				{
					kind: "journal",
					id: "constr-42",
					title: "Конструкция №42",
					asOf: "2026-10-07T09:30:00Z",
				},
			],
		};

		// Act: след рендерится примитивом.
		render(<SourceTraceView trace={trace} />);

		// Assert: обе ссылки видны с названиями и as-of; карточка правила
		// отличима от записи журнала подписью вида ссылки мастера bUrOy.
		expect(screen.getByText(/Роллирование коротких коллов/)).toBeInTheDocument();
		expect(screen.getByText(/Конструкция №42/)).toBeInTheDocument();
		expect(screen.getAllByText(/Правило/).length).toBeGreaterThan(0);
		expect(screen.getByText(/Журнал/)).toBeInTheDocument();
		expect(screen.getAllByText(/2026-10-07/).length).toBe(2);
	});

	// Деградация рынка видна как предупреждение на паре risk/riskSoft.
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	it("помечает рыночный след из кэша при недоступности биржи", () => {
		// Arrange: рыночный инструмент ответил кэшем с явным as-of —
		// владелец должен видеть, что рынок был недоступен.
		const trace: SourceTrace = {
			toolCalls: [
				{
					tool: "get_option_board",
					argument: "ETH",
					asOf: "2026-10-06T10:00:00Z",
					degraded: true,
				},
			],
			references: [],
		};

		// Act: след рендерится примитивом.
		render(<SourceTraceView trace={trace} />);

		// Assert: признак деградации рынка виден рядом с инструментом.
		expect(screen.getByText(/кэш/i)).toBeInTheDocument();
		expect(screen.getByText(/кэш/i)).toHaveClass("bg-risk-soft", "text-risk");
	});

	it("пустой след не рендерит ничего", () => {
		// Arrange: ответ не использовал источники данных.
		const trace: SourceTrace = { toolCalls: [], references: [] };

		// Act: след рендерится примитивом.
		const { container } = render(<SourceTraceView trace={trace} />);

		// Assert: примитив не оставляет пустой разметки в ответе.
		expect(container).toBeEmptyDOMElement();
	});

	it("несёт панель мастера bUrOy: мягкая зелёная заливка, радиус 10, капс-заголовок", () => {
		// Arrange: след с одной ссылкой на карточку правила.
		const trace: SourceTrace = {
			toolCalls: [],
			references: [
				{ kind: "rule-card", id: "R-14", title: "Не удерживать голый стреддл до экспирации", asOf: "2026-10-07T13:40:00Z" },
			],
		};

		// Act: след рендерится примитивом.
		render(<SourceTraceView trace={trace} />);

		// Assert: контейнер — панель $accentSofter со stroke $accentSoft,
		// радиус 10 и паддинги [10,12] (замер мастера bUrOy через MCP pen);
		// заголовок «ИСТОЧНИКИ ОТВЕТА» — капс 10/0.5 $textMuted (нода nFpGY).
		const panel = screen.getByLabelText("След источников ответа");
		expect(panel.className).toContain("bg-accent-softer");
		expect(panel.className).toContain("border-accent-soft");
		expect(panel.className).toContain("rounded-[10px]");
		expect(panel.className).toContain("px-3");
		expect(panel.className).toContain("py-2.5");
		const title = screen.getByText("Источники ответа");
		expect(title.className).toContain("text-[10px]");
		expect(title.className).toContain("tracking-[0.5px]");
		expect(title.className).toContain("uppercase");

		// Assert: строка ссылки — цвет $accentStrong мастера (текст и иконка).
		const ruleRow = screen.getByRole("button", { name: /Не удерживать голый стреддл/ });
		expect(ruleRow.className).toContain("text-accent-strong");
		expect(ruleRow.className).toContain("text-[12px]");
	});
});
