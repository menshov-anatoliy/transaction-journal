import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { RouterProvider, createMemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { appSections } from "@/config/sections";
import { buildAppRoutes } from "@/router";

// Проверяется роутер каркаса: маршрут каждого раздела из единого конфига
// открывает собственную страницу. Концепция §2 закрепляет разделы как
// отдельные маршруты без вложенности; страницы с данными получают слой
// запросов провайдером клиента запросов.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения

function renderAt(path: string) {
	const router = createMemoryRouter(buildAppRoutes(), { initialEntries: [path] });
	const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
	return render(
		<QueryClientProvider client={queryClient}>
			<RouterProvider router={router} />
		</QueryClientProvider>,
	);
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

		// Assert: корень один; маршрутов столько, сколько разделов конфига,
		// плюс динамический маршрут карточки и fallback-маршрут /Error.
		expect(routes).toHaveLength(1);
		expect(routes[0]?.children).toHaveLength(appSections.length + 2);
		expect(routes[0]?.element).toBeDefined();
	});

	it("открывает маршрут карточки конструкции", () => {
		// Act: переход по динамическому маршруту карточки.
		renderAt("/constructions/7");

		// Assert: страница карточки отвечает заголовком раздела.
		expect(
			screen.getByRole("heading", { level: 1, name: "Карточка конструкции" }),
		).toBeInTheDocument();
	});

	it("открывает русифицированную страницу ошибки на маршруте /Error", () => {
		// Карта переноса фиксирует паритет старого маршрута /Error в SPA:
		// нужна явная страница ошибки вместо пустого экрана.
		// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
		renderAt("/Error");

		expect(
			screen.getByRole("heading", { level: 1, name: "Ошибка журнала" }),
		).toBeInTheDocument();
	});
});
