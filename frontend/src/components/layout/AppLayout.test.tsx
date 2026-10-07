import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { appSections } from "@/config/sections";
import { AppLayout } from "./AppLayout";

// Проверяется каркас приложения по концепции §2: тонкий топбар с брендом
// «Журнал Bybit» (итог журнала здесь не размещается), левая панель с пятью
// разделами и сворачивание панели до иконок.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения

function renderLayout() {
	const router = createMemoryRouter(
		[{ element: <AppLayout />, children: [{ index: true, element: <div /> }] }],
		{ initialEntries: ["/"] },
	);
	return render(<RouterProvider router={router} />);
}

describe("раскладка каркаса приложения", () => {
	it("показывает бренд «Журнал Bybit» в тонком топбаре", () => {
		// Act: раскладка без маршрута.
		renderLayout();

		// Assert: бренд топбара из концепции, без итога журнала рядом.
		expect(screen.getByText("Журнал Bybit")).toBeInTheDocument();
	});

	it.each(appSections)("выводит пункт панели «$label»", (section) => {
		// Act: раскладка без маршрута.
		renderLayout();

		// Assert: каждый раздел конфига доступен ссылкой в панели.
		expect(
			screen.getByRole("link", { name: section.label }),
		).toBeInTheDocument();
	});

	it("сворачивает панель навигации до иконок", async () => {
		// Arrange: раскладка с пользователем для нажатия кнопки.
		const user = userEvent.setup();
		renderLayout();

		// Act: сворачивание панели.
		await user.click(screen.getByRole("button", { name: /панель навигации/i }));

		// Assert: подписи скрыты, ссылки-иконки всех разделов остаются.
		for (const section of appSections) {
			expect(screen.queryByText(section.label)).not.toBeInTheDocument();
		}
		expect(screen.getAllByRole("link")).toHaveLength(appSections.length);
	});
});
