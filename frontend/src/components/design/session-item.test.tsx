import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SessionItem } from "./session-item";

// Проверяется примитив «Сессия/пункт» (мастер-нода s9J3h, reusable-компонент
// design.pen) на рендер-шве Testing Library: геометрия мастера, опциональное
// превью (ChatDto чат-слоя превью пока не несёт) и клик выбора сессии,
// который потребует интеграция задачи 5.2.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("SessionItem: пункт списка сессий по дизайн-ноде s9J3h", () => {
	it("несёт геометрию мастера: радиус 10, [8,10], зазор 3, 12.5/600 + 11 + 11.5", () => {
		// Act: пункт сессии с данными дизайн-инстанса S1.
		render(
			<SessionItem title="Стреддл BTC — оценка риска" time="13:47">
				Оцени риск стреддла BTC на конец недели…
			</SessionItem>,
		);

		// Assert: кнопка выбора сессии с классами мастера s9J3h.
		const item = screen.getByRole("button", { name: /Стреддл BTC — оценка риска/ });
		expect(item.className).toContain("rounded-md");
		expect(item.className).toContain("px-2.5");
		expect(item.className).toContain("py-2");
		expect(item.className).toContain("gap-[3px]");

		// Заголовок — textPrimary 12.5/600 (нода mPoUU).
		const title = screen.getByText("Стреддл BTC — оценка риска");
		expect(title.className).toContain("text-[12.5px]");
		expect(title.className).toContain("font-semibold");
		expect(title.className).toContain("text-text-primary");

		// Время — textMuted 11/normal (нода SqhBW).
		const time = screen.getByText("13:47");
		expect(time.className).toContain("text-[11px]");
		expect(time.className).toContain("text-text-muted");

		// Превью — textMuted 11.5/normal, одна строка (нода r6Idf).
		const preview = screen.getByText("Оцени риск стреддла BTC на конец недели…");
		expect(preview.getAttribute("data-slot")).toBe("session-item-preview");
		expect(preview.className).toContain("text-[11.5px]");
		expect(preview.className).toContain("text-text-muted");
		expect(preview.className).toContain("truncate");
	});

	it("рендерит пункт без превью: ChatDto чат-слоя превью не несёт", () => {
		// Act: пункт только с заголовком и временем (текущие данные agent-page).
		render(<SessionItem title="Контртренд ETH: прикрытие" time="11:20" />);

		// Assert: структура пункта целая, превью-слота нет.
		const item = screen.getByRole("button", { name: /Контртренд ETH/ });
		expect(item.querySelector('[data-slot="session-item-preview"]')).toBeNull();
		expect(screen.getByText("11:20")).toBeInTheDocument();
	});

	it("передаёт клик выбора сессии (интеграция задачи 5.2)", async () => {
		// Arrange: пункт с обработчиком выбора.
		const onSelect = vi.fn();
		render(
			<SessionItem title="Пост-мортем: календарь BTC" time="вчера" onClick={onSelect} />,
		);

		// Act: клик по пункту.
		await userEvent.click(screen.getByRole("button", { name: /Пост-мортем/ }));

		// Assert: обработчик вызван один раз.
		expect(onSelect).toHaveBeenCalledTimes(1);
	});

	it("активное состояние инстанса Cur: surface2, бордер, aria-current", () => {
		// Act: текущая сессия (инстанс WizmS «Cur» экрана «Агент · Чат активный»).
		render(
			<SessionItem active title="Стреддл BTC — оценка риска" time="13:47">
				Оцени риск стреддла на конец недели…
			</SessionItem>,
		);

		// Assert: заливка surface2 и бордер border из переопределений инстанса.
		const item = screen.getByRole("button", { name: /Стреддл BTC/ });
		expect(item.className).toContain("bg-surface-2");
		expect(item.className).toContain("border");
		// Текущая сессия доступно помечена для скринридера.
		expect(item.getAttribute("aria-current")).toBe("true");
	});
});
