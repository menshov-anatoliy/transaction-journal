import { describe, expect, it } from "vitest";
import { appSections } from "./sections";

// Проверяется каркас навигации по концепции интерфейса §2: состав и порядок
// разделов левой панели — Конструкции, Входящие, Подсказки, Агент,
// Синхронизация — с маршрутами каждого раздела.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения

describe("конфигурация разделов журнала", () => {
	it("содержит ровно пять разделов каркаса в порядке концепции", () => {
		// Arrange/Act: состав и порядок читаются из единого конфига разделов.
		const labels = appSections.map((section) => section.label);

		// Assert: эталонный порядок пунктов левой панели из концепции.
		expect(labels).toEqual([
			"Конструкции",
			"Входящие",
			"Подсказки",
			"Агент",
			"Синхронизация",
		]);
	});

	it("закрепляет маршрут каждого раздела по концепции", () => {
		// Arrange/Act: маршруты разделов в порядке конфига.
		const paths = appSections.map((section) => section.path);

		// Assert: маршруты разделов; «Конструкции» — корень SPA.
		expect(paths).toEqual(["/", "/inbox", "/hints", "/agent", "/sync-settings"]);
	});

	it("даёт каждому разделу иконку и компонент-страницу", () => {
		// Assert: у каждого пункта панели есть иконка и заглушка страницы,
		// чтобы навигация и роутер собирались из одного конфига.
		for (const section of appSections) {
			expect(section.icon).toBeDefined();
			expect(section.component).toBeDefined();
		}
	});
});
