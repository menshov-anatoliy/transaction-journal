import { render, screen } from "@testing-library/react";
import { RouterProvider, createMemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { appSections } from "@/config/sections";
import { buildAppRoutes } from "@/router";

// Проверяется роутер каркаса: маршрут каждого раздела из единого конфига
// открывает собственную страницу-заглушку. Концепция §2 закрепляет разделы
// как отдельные маршруты без вложенности.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения

function renderAt(path: string) {
	const router = createMemoryRouter(buildAppRoutes(), { initialEntries: [path] });
	return render(<RouterProvider router={router} />);
}

describe("роутер каркаса SPA", () => {
	it.each(appSections)("маршрут $path открывает раздел «$label»", (section) => {
		// Act: переход по маршруту раздела.
		renderAt(section.path);

		// Assert: страница раздела показывает его имя заголовком.
		expect(
			screen.getByRole("heading", { level: 1, name: section.label }),
		).toBeInTheDocument();
	});

	it("строит маршруты всех разделов конфига", () => {
		// Act: сборка дерева маршрутов.
		const routes = buildAppRoutes();

		// Assert: корень один, вложенных маршрутов столько же, сколько разделов.
		expect(routes).toHaveLength(1);
		expect(routes[0]?.children).toHaveLength(appSections.length);
		expect(routes[0]?.element).toBeDefined();
	});
});
