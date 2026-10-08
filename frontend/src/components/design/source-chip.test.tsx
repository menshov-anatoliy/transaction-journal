import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Globe } from "lucide-react";
import { SourceChip } from "./source-chip";

// Проверяется примитив «Чип/Источник» (нода fYadZ, фрейм «Примитивы» s0YfZ)
// макета design.pen: pill радиус 999, заливка surface, бордер border,
// паддинги [5,10], иконка Lucide 12×12 accent (мастер — check), подпись
// Inter 12/normal textSecondary. Инстансы дизайн-нод: «журнал», «корпус
// правил» (check), «рынок Bybit» (globe), «GLM-5.3» (cpu, textSecondary).
// Пиксельная сверка — парой скриншотов в
// .wf-research/design-audit/screenshots/02-primitives/design-components/.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("SourceChip: пилюля источника по дизайн-нодам", () => {
	it("несёт геометрию мастера fYadZ: pill 999, surface, бордер, [5,10], 12", () => {
		// Act: базовый рендер с подписью источника.
		render(<SourceChip>журнал</SourceChip>);

		// Assert: классы ноды fYadZ.
		const chip = screen.getByText("журнал").closest('[data-slot="source-chip"]');
		expect(chip).not.toBeNull();
		expect(chip?.className).toContain("rounded-full");
		expect(chip?.className).toContain("border");
		expect(chip?.className).toContain("bg-card");
		expect(chip?.className).toContain("px-2.5");
		expect(chip?.className).toContain("py-[5px]");
		expect(chip?.className).toContain("text-[12px]");
		expect(chip?.className).toContain("text-text-secondary");
	});

	it("рисует иконку check цветом accent по умолчанию", () => {
		// Act: рендер без кастомной иконки.
		render(<SourceChip>журнал</SourceChip>);

		// Assert: внутри пилюли одна иконка 12×12 (size-3) в цвете accent.
		const chip = screen.getByText("журнал").closest('[data-slot="source-chip"]');
		const icon = chip?.querySelector("svg");
		expect(icon).toBeDefined();
		expect(icon?.getAttribute("aria-hidden")).toBe("true");
		expect(icon?.getAttribute("class")).toContain("size-3");
		expect(icon?.getAttribute("class")).toContain("text-primary");
	});

	it("принимает кастомную иконку инстанса «рынок Bybit» (globe)", () => {
		// Act: инстанс xB2ZS подменяет check на globe.
		render(<SourceChip icon={Globe}>рынок Bybit</SourceChip>);

		// Assert: иконка по-прежнему в пилюле, размер дизайн-системы.
		const chip = screen.getByText("рынок Bybit").closest('[data-slot="source-chip"]');
		expect(chip?.querySelectorAll("svg")).toHaveLength(1);
	});

	it("позволяет перекрасить иконку под инстанс «GLM-5.3» (cpu, textSecondary)", () => {
		// Act: инстанс Y10aLX красит иконку в textSecondary.
		render(
			<SourceChip iconClassName="text-text-secondary">GLM-5.3</SourceChip>,
		);

		// Assert: цвет иконки переопределён, акцентный класс снят merge-ом.
		const icon = screen
			.getByText("GLM-5.3")
			.closest('[data-slot="source-chip"]')
			?.querySelector("svg");
		expect(icon?.getAttribute("class")).toContain("text-text-secondary");
		expect(icon?.getAttribute("class")).not.toContain("text-primary");
	});
});
