import { createBrowserRouter } from "react-router";
import type { RouteObject } from "react-router";
import { AppLayout } from "@/components/layout/AppLayout";
import { appSections } from "@/config/sections";
import { ConstructionCardPage } from "@/pages/construction-card-page";
import { ErrorPage } from "@/pages/error-page";

/** Префикс монтажа SPA на .NET-хосте: /spa до переключения журнала (задача 7.2). */
export const SPA_BASE_PATH = "/spa";

/**
 * Дерево маршрутов каркаса: раскладка приложения в корне и по одному
 * маршруту на раздел из единого конфига. «Конструкции» — индексный маршрут;
 * карточка конструкции адресуется динамическим маршрутом выделенной строки
 * (переход из превью и «в новом окне»).
 */
export function buildAppRoutes(): RouteObject[] {
	return [
		{
			element: <AppLayout />,
			children: [
				...appSections.map((section) =>
					section.path === "/"
						? { index: true, element: <section.component /> }
						: { path: section.path.slice(1), element: <section.component /> },
				),
				{ path: "constructions/:constructionId", element: <ConstructionCardPage /> },
				// Русифицированный паритет старого маршрута /Error; отдельная страница
				// удерживает UX при прямом переходе и сбоях до включения глобального
				// errorElement.
				// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
				{ path: "Error", element: <ErrorPage /> },
			],
		},
	];
}

/** Браузерный роутер SPA: базовый путь учитывает монтаж под /spa. */
export function createAppRouter() {
	return createBrowserRouter(buildAppRoutes(), { basename: SPA_BASE_PATH });
}
