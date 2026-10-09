import { describe, expect, it } from "vitest";
import { createAppQueryClient } from "./query-client";

// Проверяется слой запросов TanStack Query каркаса. По концепции §1 живость
// данных обеспечит серверный канал событий, а не перезапросы по фокусу окна:
// клиент не должен дёргать API при каждом возврате на вкладку.
// Traceability: doc:.wf-research/ui-concept/concept.md#1-рамка-и-принципы

describe("клиент запросов журнала", () => {
	it("не перезапрашивает данные по фокусу окна", () => {
		// Act: создаётся клиент слоя запросов.
		const client = createAppQueryClient();

		// Assert: фокус окна не источник обновлений.
		expect(client.getDefaultOptions().queries?.refetchOnWindowFocus).toBe(false);
	});

	it("считает кэш свежим 30 секунд", () => {
		// Act: создаётся клиент слоя запросов.
		const client = createAppQueryClient();

		// Assert: умеренная свежесть кэша без постоянных фоновых перезапросов.
		expect(client.getDefaultOptions().queries?.staleTime).toBe(30_000);
	});

	it("повторяет неудачный запрос один раз", () => {
		// Act: создаётся клиент слоя запросов.
		const client = createAppQueryClient();

		// Assert: локальный журнал не заслуживает долгих повторов при сбоях.
		expect(client.getDefaultOptions().queries?.retry).toBe(1);
	});
});
