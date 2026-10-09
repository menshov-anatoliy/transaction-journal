import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ToolStatus } from "./tool-status";

// Проверяется примитив «Tool-статус» (мастер-нода ix8ma, reusable-компонент
// design.pen) на рендер-шве Testing Library: pill 999 на surface2, паддинги
// [4,10], зазор 7, иконка Lucide loader 12×12 textMuted и курсивная подпись
// 11.5/normal textSecondary. В макете существует единственное состояние
// «выполняется» (инстансы dK1P3 и tMmi0 — только тексты меняются); состояний
// «готово»/«ошибка» дизайн-ноды не содержат, поэтому в примитив не включены.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("ToolStatus: индикация выполнения tool-вызова по ноде ix8ma", () => {
	it("несёт геометрию мастера: pill 999, surface2, [4,10], зазор 7, иконка 12×12", () => {
		// Act: статус с подписью мастер-ноды.
		render(<ToolStatus>рынок Bybit — запрашиваю котировки опционов…</ToolStatus>);

		// Assert: классы мастера ix8ma на корне.
		const status = screen.getByText(
			"рынок Bybit — запрашиваю котировки опционов…",
		).parentElement;
		expect(status?.getAttribute("data-slot")).toBe("tool-status");
		expect(status?.className).toContain("rounded-full");
		expect(status?.className).toContain("bg-surface-2");
		expect(status?.className).toContain("px-2.5");
		expect(status?.className).toContain("py-1");
		expect(status?.className).toContain("gap-[7px]");

		// Иконка loader 12×12 цветом textMuted, вращение — индикация хода вызова.
		const icon = status?.querySelector("svg");
		expect(icon?.getAttribute("class")).toContain("size-3");
		expect(icon?.getAttribute("class")).toContain("text-text-muted");
		expect(icon?.getAttribute("class")).toContain("animate-spin");
	});

	it("подпись — курсив textSecondary 11.5, текст статуса виден", () => {
		// Act: инстанс tMmi0 с другим текстом источника.
		render(<ToolStatus>рынок Bybit — собираю стакан октябрьских страйков…</ToolStatus>);

		// Assert: курсивная подпись дизайн-ноды PfMRJ.
		const label = screen.getByText("рынок Bybit — собираю стакан октябрьских страйков…");
		expect(label.className).toContain("italic");
		expect(label.className).toContain("text-[11.5px]");
		expect(label.className).toContain("text-text-secondary");
	});
});
