import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch } from "./http";

// Проверяется тонкий HTTP-клиент единого API: префикс версии, JSON-тела,
// ошибки с человекочитаемой причиной. Все разделы SPA ходят через один
// версионированный API одного хоста.
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

function stubFetch(body: unknown, status = 200) {
	return vi.fn(async () =>
		new Response(JSON.stringify(body), {
			status,
			headers: { "Content-Type": "application/json" },
		}),
	);
}

afterEach(() => {
	vi.unstubAllGlobals();
});

describe("http-клиент единого API", () => {
	it("запрашивает путь под префиксом версии и разбирает JSON-ответ", async () => {
		// Arrange: успешный JSON-ответ хоста.
		const fetchMock = stubFetch({ value: 42 });
		vi.stubGlobal("fetch", fetchMock);

		// Act: запрос относительного пути API.
		const payload = await apiFetch<{ value: number }>("/constructions");

		// Assert: путь ушёл под версионированный префикс, ответ разобран.
		expect(fetchMock).toHaveBeenCalledWith(
			"/api/v1/constructions",
			expect.objectContaining({ headers: expect.anything() }),
		);
		expect(payload).toEqual({ value: 42 });
	});

	it("передаёт тело POST-команды и метод", async () => {
		// Arrange: успешный ответ команды.
		const fetchMock = stubFetch({ ok: true });
		vi.stubGlobal("fetch", fetchMock);

		// Act: POST-команда без тела.
		await apiFetch("/sync", { method: "POST" });

		// Assert: метод передан в запрос.
		expect(fetchMock).toHaveBeenCalledWith(
			"/api/v1/sync",
			expect.objectContaining({ method: "POST" }),
		);
	});

	it("превращает ошибочный статус в ApiError с причиной из тела", async () => {
		// Arrange: недоступность журнала со текстом причины.
		vi.stubGlobal("fetch", stubFetch({ error: "сырьё повреждено" }, 503));

		// Act: запрос, завершившийся ошибкой.
		const failure = await apiFetch("/constructions").catch((error: unknown) => error);

		// Assert: ошибка несёт статус и человекочитаемую причину.
		expect(failure).toBeInstanceOf(ApiError);
		expect((failure as ApiError).status).toBe(503);
		expect((failure as ApiError).message).toBe("сырьё повреждено");
	});
});
