import type { SseFrame } from "./parse-sse";
import type { ChatMessageDto } from "@/chat/types";

/** Событие SSE-канала чата в типах SPA: начало, токен, завершение, ошибка. */
export type ChatStreamEvent =
	| { readonly type: "start" }
	| { readonly type: "token"; readonly text: string }
	| { readonly type: "done"; readonly message?: ChatMessageDto }
	| { readonly type: "error"; readonly message: string };

/**
 * Декодирует кадр SSE в событие протокола чата: имена событий и нагрузки
 * соответствуют транспорту задачи 2.1 (start {}, token {text},
 * done {message?}, error {message}). Нагрузка done опционально несёт
 * финальное DTO ответа с идентификатором, as-of и следом источников —
 * этим эндпоинт чата из add-agent-chat дополнит транспорт.
 * Незнакомые имена событий игнорируются ради прямой совместимости.
 */
export function decodeChatStreamFrame(frame: SseFrame): ChatStreamEvent | null {
	const payload = readPayload(frame.data);
	switch (frame.event) {
		case "start":
			return { type: "start" };
		case "token":
			return { type: "token", text: asText(payload.text) };
		case "done":
			return { type: "done", message: payload.message as ChatMessageDto | undefined };
		case "error":
			return { type: "error", message: asText(payload.message) };
		default:
			return null;
	}
}

// Нагрузка кадра — JSON-объект; пустая нагрузка разбирается как отсутствие полей.
function readPayload(data: string): Record<string, unknown> {
	if (data === "")
		return {};
	const parsed: unknown = JSON.parse(data);
	if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed))
		throw new SyntaxError("нагрузка кадра SSE не является JSON-объектом");
	return parsed as Record<string, unknown>;
}

function asText(value: unknown): string {
	return typeof value === "string" ? value : "";
}
