import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Inbox, Layers } from "lucide-react";
import { NavItem, navItemVariants } from "./nav-item";

// Проверяется примитив «Нав-пункт» (ноды apcZH и Vx9C2 «Нав-пункт/Активный»,
// фрейм «Примитивы» s0YfZ) макета design.pen: радиус 8, паддинги [8,10],
// зазор 10, иконка Lucide 16×16, подпись Inter 13/500 textSecondary. Активное
// состояние мастер-ноды Vx9C2: заливка surface, бордер border, иконка и
// подпись accentStrong 13/600 (не accentSoft — пара accentSoft/accentStrong
// из агрегатов аудита §3.1 принадлежит логотипу топбара и бейджу задачи 3.1,
// что подтверждено прямым чтением нод через MCP pen). Бейдж — по мастер-ноде
// q35Tj9: pill 999, accent, [2,8], 11/600. Пиксельная сверка — парой
// скриншотов в .wf-research/design-audit/screenshots/02-primitives/design-components/.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("NavItem: пункт навигации по дизайн-нодам", () => {
	it("несёт геометрию мастера apcZH: радиус 8, [8,10], зазор 10, 13/500", () => {
		// Act: неактивный пункт с иконкой раздела.
		render(<NavItem icon={Layers} href="/constructions">Конструкции</NavItem>);

		// Assert: ссылка с классами неактивного состояния.
		const item = screen.getByRole("link", { name: "Конструкции" });
		expect(item.className).toContain("rounded-sm");
		expect(item.className).toContain("px-2.5");
		expect(item.className).toContain("py-2");
		expect(item.className).toContain("gap-2.5");
		expect(item.className).toContain("text-[13px]");
		expect(item.className).toContain("font-medium");
		expect(item.className).toContain("text-text-secondary");
		expect(item.querySelector("svg")?.getAttribute("class")).toContain("size-4");
	});

	it("активное состояние Vx9C2: surface, бордер, accentStrong, 13/600", () => {
		// Act: активный пункт.
		render(
			<NavItem icon={Layers} active href="/constructions">
				Конструкции
			</NavItem>,
		);

		// Assert: классы активной мастер-ноды Vx9C2.
		const item = screen.getByRole("link", { name: "Конструкции" });
		expect(item.className).toContain("border");
		expect(item.className).toContain("bg-card");
		expect(item.className).toContain("font-semibold");
		expect(item.className).toContain("text-accent-strong");
		expect(item.className).not.toContain("text-text-secondary");
	});

	it("помечает активный пункт доступно: aria-current=\"page\"", () => {
		// Arrange: активный и обычный пункты.
		render(
			<div>
				<NavItem icon={Layers} active href="/constructions">
					Конструкции
				</NavItem>
				<NavItem icon={Inbox} href="/inbox">Входящие</NavItem>
			</div>,
		);

		// Assert: активный несёт aria-current, неактивный — нет.
		expect(
			screen.getByRole("link", { name: "Конструкции" }).getAttribute("aria-current"),
		).toBe("page");
		expect(
			screen.getByRole("link", { name: "Входящие" }).getAttribute("aria-current"),
		).toBeNull();
	});

	it("рисует числовой бейдж мастер-ноды q35Tj9 при передаче счётчика", () => {
		// Act: пункт «Входящие» со счётчиком (инстанс bKXol «Каркаса»).
		render(
			<NavItem icon={Inbox} badge={12} href="/inbox">
				Входящие
			</NavItem>,
		);

		// Assert: pill 999, accent, [2,8], 11/600, значение видно.
		const badge = screen.getByText("12");
		expect(badge.getAttribute("data-slot")).toBe("nav-item-badge");
		expect(badge.className).toContain("rounded-full");
		expect(badge.className).toContain("bg-primary");
		expect(badge.className).toContain("px-2");
		expect(badge.className).toContain("text-[11px]");
		expect(badge.className).toContain("font-semibold");
	});

	it("asChild встраивает стили в router-ссылку (интеграция задачи 3.1)", () => {
		// Act: активный пункт поверх React Router NavLink-подобного <a>.
		render(
			<NavItem asChild icon={Layers} active>
				<a href="/constructions">Конструкции</a>
			</NavItem>,
		);

		// Assert: рендерится один <a> с классами активного состояния и href.
		const link = screen.getByRole("link", { name: "Конструкции" });
		expect(document.querySelectorAll("a")).toHaveLength(1);
		expect(link.getAttribute("href")).toBe("/constructions");
		expect(link.className).toContain(navItemVariants({ active: true }));
		expect(link.getAttribute("aria-current")).toBe("page");
	});
});
