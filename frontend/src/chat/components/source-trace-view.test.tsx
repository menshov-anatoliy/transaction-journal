import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { SourceTrace } from "@/chat/types";
import { SourceTraceView } from "./source-trace-view";

// След источников в ответе ИИ-помощника: использованные инструменты
// и ссылки на карточки правил и данные журнала с их отметками as-of —
// рендер-примитив под ответом, поверх общего MD-рендера текста.
// Traceability: change:add-agent-chat/proposal#what-changes

describe("след источников в ответе ИИ-помощника", () => {
	it("показывает вызванные инструменты с аргументами и as-of", () => {
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

		// Assert: обе ссылки видны с названиями; карточка правила отличима
		// от записи журнала подписью вида ссылки.
		expect(screen.getByText(/Роллирование коротких коллов/)).toBeInTheDocument();
		expect(screen.getByText(/Конструкция №42/)).toBeInTheDocument();
		expect(screen.getAllByText(/Карточка правила/).length).toBeGreaterThan(0);
		expect(screen.getByText(/Данные журнала/)).toBeInTheDocument();
	});

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
	});

	it("пустой след не рендерит ничего", () => {
		// Arrange: ответ не использовал источники данных.
		const trace: SourceTrace = { toolCalls: [], references: [] };

		// Act: след рендерится примитивом.
		const { container } = render(<SourceTraceView trace={trace} />);

		// Assert: примитив не оставляет пустой разметки в ответе.
		expect(container).toBeEmptyDOMElement();
	});
});
