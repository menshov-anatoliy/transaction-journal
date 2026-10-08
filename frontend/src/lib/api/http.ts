// Тонкий HTTP-клиент единого API журнала: все запросы SPA идут под
// версионированным префиксом одного хоста, JSON-ответы разбираются здесь
// один раз, ошибки нормализуются в ApiError с человекочитаемой причиной.
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

/** Префикс всех запросов SPA к единому API хоста журнала. */
const API_PREFIX = "/api/v1";

/** Ошибка ответа API: статус и причина из тела ответа. */
export class ApiError extends Error {
	constructor(
		/** HTTP-статус ошибочного ответа. */
		readonly status: number,
		message: string,
	) {
		super(message);
		this.name = "ApiError";
	}
}

/** Выполняет запрос к единому API и разбирает JSON-ответ. */
export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
	const response = await fetch(`${API_PREFIX}${path}`, {
		...init,
		headers: { Accept: "application/json", ...init?.headers },
	});

	if (!response.ok) {
		throw await toApiError(response);
	}

	return (await response.json()) as T;
}

/** Строит ApiError из ошибочного ответа: причина — поле error тела, если есть. */
async function toApiError(response: Response): Promise<ApiError> {
	const fallback = `API ответил ошибкой ${response.status}`;
	try {
		const payload = (await response.json()) as { error?: unknown };
		return new ApiError(response.status, typeof payload.error === "string" ? payload.error : fallback);
	} catch {
		return new ApiError(response.status, fallback);
	}
}
