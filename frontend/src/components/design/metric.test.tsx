import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Metric } from "./metric";

// Проверяется примитив «Метрика» (нода jtDmV, фрейм «Примитивы» s0YfZ)
// макета design.pen: вертикальный блок с зазором 3, подпись Inter 11/normal
// textMuted с трекингом 0.3, значение Inter 15/600 textPrimary. Инстансы из
// «Карточек» (sUDDX) переопределяют размер (14) и цвет (accentStrong) значения,
// поэтому стиль значения настраивается valueClassName. Пиксельная сверка —
// парой скриншотов в
// .wf-research/design-audit/screenshots/02-primitives/design-components/.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("Metric: подпись/значение по дизайн-нодам", () => {
	it("несёт типографику мастера jtDmV: подпись 11 textMuted, значение 15/600", () => {
		// Act: базовый рендер пары подпись/значение.
		render(<Metric label="Реализов.">+940,20</Metric>);

		// Assert: подпись — Caption (bGGN9), значение — Value (Q4NLmR).
		const caption = screen.getByText("Реализов.");
		const value = screen.getByText("+940,20");
		expect(caption.className).toContain("text-[11px]");
		expect(caption.className).toContain("text-text-muted");
		expect(caption.className).toContain("tracking-[0.3px]");
		expect(value.className).toContain("text-[15px]");
		expect(value.className).toContain("font-semibold");
		expect(value.className).toContain("text-text-primary");
		expect(value.className).not.toContain("text-text-muted");
	});

	it("фиксирует шрифтовой интерлиньяж мастера вместо унаследованного контекста", () => {
		// Мастер jtDmV не задаёт lineHeight — шрифтовой normal Inter ≈1.21;
		// без пина строка наследует 1.5 страницы и метрика растёт выше мастера.
		render(<Metric label="Период">12 авг — 07 окт</Metric>);

		// Assert: оба текста несут шрифтовой интерлиньяж.
		expect(screen.getByText("Период").className).toContain("leading-[normal]");
		expect(screen.getByText("12 авг — 07 окт").className).toContain("leading-[normal]");
	});

	it("выкладывает подпись над значением с зазором 3", () => {
		// Act: рендер метрики.
		render(
			<Metric label="Капитал">
				<span>6 000</span>
			</Metric>,
		);

		// Assert: колонка gap-3px, подпись раньше значения в порядке DOM.
		const wrapper = screen.getByText("Капитал").parentElement;
		expect(wrapper?.className).toContain("flex-col");
		expect(wrapper?.className).toContain("gap-[3px]");
		const position = screen.getByText("6 000").compareDocumentPosition(
			screen.getByText("Капитал"),
		) as number;
		expect(position & Node.DOCUMENT_POSITION_PRECEDING).toBe(
			Node.DOCUMENT_POSITION_PRECEDING,
		);
	});

	it("valueClassName перекрашивает значение под инстанс M1 «Общий P&L»", () => {
		// Act: инстанс ZzdbC — значение accentStrong 16.
		render(
			<Metric label="Общий P&L" valueClassName="text-[16px] text-accent-strong">
				+385,30
			</Metric>,
		);

		// Assert: базовые textPrimary/15 сняты merge-ом, инстансовые применены.
		const classes = screen.getByText("+385,30").className;
		expect(classes).toContain("text-accent-strong");
		expect(classes).toContain("text-[16px]");
		expect(classes).not.toContain("text-text-primary");
		expect(classes).not.toContain("text-[15px]");
	});
});
