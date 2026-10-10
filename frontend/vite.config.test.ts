import { describe, expect, it, vi } from "vitest";
import type { ServerResponse } from "node:http";
import type { Connect } from "vite";
import { spaRootRedirectMiddleware, spaRootRedirectPlugin } from "./spa-root-redirect";

// Проверяется вход в SPA по корню дев-сервера: запрос «/» отвечает
// 302-редиректом на /spa/ вместо ошибки base URL.
// Traceability: change:rework-spa-shell-and-screens/design#D8

function createReq(url: string): Connect.IncomingMessage {
	return { url } as Connect.IncomingMessage;
}

function createRes(): {
	res: ServerResponse;
	setHeader: ReturnType<typeof vi.fn>;
	end: ReturnType<typeof vi.fn>;
} {
	const setHeader = vi.fn();
	const end = vi.fn();
	// Минимальный фейк ответа connect: плагин трогает только статус,
	// заголовок Location и завершение ответа.
	const res = { statusCode: 200, setHeader, end } as unknown as ServerResponse;
	return { res, setHeader, end };
}

describe("spaRootRedirectMiddleware", () => {
	it("редиректит корень на /spa/ кодом 302", () => {
		// Arrange: вход в приложение по адресу без префикса SPA.
		const req = createReq("/");
		const { res, setHeader, end } = createRes();
		const next = vi.fn();

		// Act: перехват запроса middleware плагина.
		spaRootRedirectMiddleware(req, res, next);

		// Assert: браузер отправлен на /spa/ без вызова следующих middleware.
		expect(res.statusCode).toBe(302);
		expect(setHeader).toHaveBeenCalledWith("Location", "/spa/");
		expect(end).toHaveBeenCalledTimes(1);
		expect(next).not.toHaveBeenCalled();
	});

	it("не перехватывает запросы вне корня", () => {
		// Arrange: запрос уже внутри префикса SPA — редирект не нужен.
		const req = createReq("/spa/constructions");
		const { res, setHeader, end } = createRes();
		const next = vi.fn();

		// Act: перехват запроса middleware плагина.
		spaRootRedirectMiddleware(req, res, next);

		// Assert: ответ не изменён, запрос передан дальше по цепочке.
		expect(res.statusCode).toBe(200);
		expect(setHeader).not.toHaveBeenCalled();
		expect(end).not.toHaveBeenCalled();
		expect(next).toHaveBeenCalledTimes(1);
	});
});

describe("spaRootRedirectPlugin", () => {
	it("регистрирует middleware на дев-сервере", () => {
		// Arrange: фейк дев-сервера с перехватом регистрации middleware.
		const use = vi.fn();
		const server = { middlewares: { use } };
		// configureServer в типах Vite — ObjectHook, фактическая реализация — функция.
		const configureServer = spaRootRedirectPlugin().configureServer as unknown as (
			s: typeof server,
		) => void;

		// Act: подключение плагина к серверу.
		configureServer(server);

		// Assert: middleware подключён к цепочке дев-сервера.
		expect(use).toHaveBeenCalledWith(spaRootRedirectMiddleware);
	});
});
