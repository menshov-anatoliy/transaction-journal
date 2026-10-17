import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as React from "react";
import { describe, expect, it, vi } from "vitest";
import { Switch } from "./switch";

// Проверяется дизайн-примитив тумблера по решению раунд-сверки 9.0 (мастер
// uxrZO «Экран/Синхронизация»): дорожка 36×20 r999, включённое состояние —
// $accent, выключенное — нейтральный $border, ручка 16×16 #FFFFFF;
// доступность: role="switch", aria-checked, клавиатура (Space).
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:rework-contextual-topbar/tasks#9.0

describe("тумблер: тема дизайн-примитива", () => {
	it("несёт геометрию мастера: дорожка 36×20 r999, ручка 16 на поверхности", () => {
		// Act: базовый рендер выключенного тумблера.
		render(<Switch checked={false} />);

		// Assert: обёртка 36×20 (w-9 h-5), input с радиусом 999 и токенами
		// нейтральной дорожки $border, ручка 16×16 залита $surface (#FFFFFF).
		const track = screen.getByRole("switch");
		expect(track).toHaveAttribute("type", "checkbox");
		expect(track.closest("[data-slot='switch']")?.className).toContain("h-5 w-9");
		expect(track.className).toContain("rounded-full");
		expect(track.className).toContain("bg-border");
		expect(track.className).toContain("border-border");
		const knob = track.closest("[data-slot='switch']")?.querySelector("span[aria-hidden='true']");
		expect(knob?.className).toContain("size-4");
		expect(knob?.className).toContain("bg-surface");
		expect(knob?.className).toContain("rounded-full");
	});

	it("перекрашивает дорожку в accent и сдвигает ручку во включённом состоянии", () => {
		// Act: рендер включённого тумблера.
		render(<Switch checked readOnly />);

		// Assert: заливка/кайма $accent включаются состоянием checked,
		// ручка сдвигается вправо через peer-checked.
		const track = screen.getByRole("switch");
		expect(track.className).toContain("checked:bg-primary");
		expect(track.className).toContain("checked:border-primary");
		const knob = track.closest("[data-slot='switch']")?.querySelector("span[aria-hidden='true']");
		expect(knob?.className).toContain("peer-checked:translate-x-4");
	});

	it("доступен как switch: aria-checked следует состоянию", () => {
		// Arrange: выключенный тумблер.
		const { rerender } = render(<Switch checked={false} />);

		// Assert: роль switch с aria-checked="false".
		expect(screen.getByRole("switch")).toHaveAttribute("role", "switch");
		expect(screen.getByRole("switch")).toHaveAttribute("aria-checked", "false");

		// Act/Assert: включённое состояние отражается в aria-checked="true".
		rerender(<Switch checked readOnly />);
		expect(screen.getByRole("switch")).toHaveAttribute("aria-checked", "true");
	});

	it("переключается кликом и клавиатурой (Space)", async () => {
		// Arrange: контролируемый тумблер с обработчиком — состояние гонится
		// через onCheckedChange, как на странице синхронизации.
		const onCheckedChange = vi.fn();
		function Harness() {
			const [checked, setChecked] = React.useState(false);
			return (
				<Switch
					checked={checked}
					onCheckedChange={(next) => {
						onCheckedChange(next);
						setChecked(next);
					}}
				/>
			);
		}
		const user = userEvent.setup();
		render(<Harness />);

		// Act/Assert: клик переводит тумблер во включённое состояние.
		await user.click(screen.getByRole("switch"));
		expect(onCheckedChange).toHaveBeenLastCalledWith(true);

		// Act/Assert: Space на сфокусированном тумблере (фокус остался после
		// клика) возвращает выключенное состояние — нативная клавиатурная
		// семантика input сохранена.
		await user.keyboard(" ");
		expect(onCheckedChange).toHaveBeenLastCalledWith(false);
	});
});
