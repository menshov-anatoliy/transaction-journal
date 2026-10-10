import type { ServerResponse } from "node:http";
import type { Connect, Plugin } from "vite";

// Вход в SPA по корню дев-сервера: запрос «/» отвечает 302-редиректом на /spa/,
// чтобы браузер открывал приложение без ошибки base URL.
// Traceability: change:rework-spa-shell-and-screens/design#D8
// Traceability: doc:frontend/README.md#запуск-в-rider
export function spaRootRedirectMiddleware(
	req: Connect.IncomingMessage,
	res: ServerResponse,
	next: Connect.NextFunction,
): void {
	if (req.url === "/") {
		res.statusCode = 302;
		res.setHeader("Location", "/spa/");
		res.end();
		return;
	}
	next();
}

// Плагин действует только на дев-сервере: в собранном приложении корень
// занят Blazor-UI, и редирект там раздаёт .NET-хост.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
export function spaRootRedirectPlugin(): Plugin {
	return {
		name: "transaction-journal:spa-root-redirect",
		apply: "serve",
		configureServer(server) {
			// Middleware добавляется до внутренних middleware Vite, чтобы корень
			// перехватывался раньше проверки public base URL.
			server.middlewares.use(spaRootRedirectMiddleware);
		},
	};
}
