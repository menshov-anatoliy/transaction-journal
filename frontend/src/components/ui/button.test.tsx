import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Button, buttonVariants } from "./button";

// Проверяется тема кнопки по дизайн-примитивам «Кнопка/*» фрейма «Примитивы»
// (s0YfZ) макета design.pen: радиус 8, шрифт 13/500 и маппинг варианта на
// токен-классы (Primary — accent, Secondary — surface/border, Ghost —
// textSecondary, Danger — negSoft/neg). Пиксельная сверка — скриншот-парой
// в .wf-research/design-audit/screenshots/02-primitives/button/.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("кнопка: тема дизайн-примитива", () => {
	it("несёт общую геометрию и типографику дизайн-системы", () => {
		// Act: базовый рендер без переопределений.
		render(<Button>Действие</Button>);

		// Assert: радиус 8 (шаг --radius-sm), шрифт 13/500, высота 36.
		const classes = screen.getByRole("button", { name: "Действие" }).className;
		expect(classes).toContain("rounded-sm");
		expect(classes).toContain("text-[13px]");
		expect(classes).toContain("font-medium");
		expect(classes).toContain("h-9");
	});

	it("даёт Ghost компактный паддинг 12 из compound-варианта", () => {
		// Act: ghost базового размера.
		render(<Button variant="ghost">Действие</Button>);

		// Assert: tailwind-merge оставляет px-3 (дизайн [9,12]), px-4 удалён.
		const classes = screen.getByRole("button", { name: "Действие" }).className;
		expect(classes).toContain("px-3");
		expect(classes).not.toContain("px-4");
	});

	it.each([
		// Вариант → ключевые классы заливки/текста по дизайн-нодам Кнопка/*.
		["default", "bg-primary text-primary-foreground"],
		["outline", "border bg-card text-foreground"],
		["secondary", "border bg-card text-foreground"],
		["ghost", "text-text-secondary"],
		["destructive", "bg-neg-soft text-neg"],
		["destructive-solid", "bg-neg text-destructive-foreground"],
	] as const)("вариант %s разрешается в токены дизайн-системы", (variant, expected) => {
		// Act + Assert: cva-маппинг вариант→классы без рендера.
		expect(buttonVariants({ variant })).toContain(expected);
	});

	it("держит мягкий и плотный Danger раздельно по инстансам мастера", () => {
		// Arrange: в design.pen базовая «Кнопка/Danger» (JRitT) мягкая —
		// negSoft-заливка/neg-текст (инстанс Delete YMfP7 в попапе удаления),
		// а инстансы опасной зоны Body #8 (jmklC/EK3Ob) переопределяют заливку
		// на $neg с белым текстом — это отдельный вариант destructive-solid.
		// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
		// Traceability: change:reconcile-frontend-with-design/design#D2

		// Act + Assert: мягкий вариант остаётся negSoft/neg без светлого текста.
		const soft = buttonVariants({ variant: "destructive" });
		expect(soft).toContain("bg-neg-soft text-neg");
		expect(soft).not.toContain("text-destructive-foreground");

		// Act + Assert: плотный вариант — заливка neg и светлый текст.
		const solid = buttonVariants({ variant: "destructive-solid" });
		expect(solid).toContain("bg-neg text-destructive-foreground");
		expect(solid).not.toContain("bg-neg-soft");
	});

	it("остаётся рабочей кнопкой: клик и disabled работают", async () => {
		// Arrange: обработчик клика.
		const onClick = vi.fn();
		const user = userEvent.setup();
		const { rerender } = render(
			<Button onClick={onClick} variant="destructive">
				Опасно
			</Button>,
		);

		// Act: клик по активной кнопке.
		await user.click(screen.getByRole("button", { name: "Опасно" }));

		// Assert: обработчик вызван.
		expect(onClick).toHaveBeenCalledTimes(1);

		// Act: блокировка кнопки.
		rerender(
			<Button onClick={onClick} variant="destructive" disabled>
				Опасно
			</Button>,
		);

		// Assert: неактивная кнопка не реагирует.
		await user.click(screen.getByRole("button", { name: "Опасно" }));
		expect(onClick).toHaveBeenCalledTimes(1);
	});
});
