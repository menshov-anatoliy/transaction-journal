import type { SseFrame } from "./parse-sse";
import type { ChatMessageDto } from "@/chat/types";

/** Фаза активности инструмента из кадра tool: вызов стартовал или завершён. */
export type ChatToolPhase = "start" | "end";

/** Событие SSE-канала чата в типах SPA: начало, токен, активность инструмента, завершение, ошибка. */
export type ChatStreamEvent =
	| { readonly type: "start" }
	| { readonly type: "token"; readonly text: string }
	| {
			readonly type: "tool";
			/** Имя инструмента реестра из кадра активности. */
			readonly name: string;
			/** Фаза вызова: start — вызов пошёл, end — вызов завершён. */
			readonly phase: ChatToolPhase;
			/** Опциональная деталь вызова; транспорт не сериализует её при отсутствии. */
			readonly detail?: string;
			/** Опциональный идентификатор вызова; кадры без него сводятся по имени (совместимость). */
			readonly callId?: string;
	  }
	| { readonly type: "done"; readonly message?: ChatMessageDto }
	| { readonly type: "error"; readonly message: string };

/**
 * Декодирует кадр SSE в событие протокола чата: имена событий и нагрузки
 * соответствуют транспорту задачи 2.1 (start {}, token {text},
 * tool {name, phase, detail?, callId?}, done {message?}, error {message}). Нагрузка
 * done опционально несёт финальное DTO ответа с идентификатором, as-of и
 * следом источников — этим эндпоинт чата из add-agent-chat дополнит транспорт.
 * Незнакомые имена событий игнорируются ради прямой совместимости.
 */
export function decodeChatStreamFrame(frame: SseFrame): ChatStreamEvent | null {
	const payload = readPayload(frame.data);
	switch (frame.event) {
		case "start":
			return { type: "start" };
		case "token":
			return { type: "token", text: asText(payload.text) };
		case "tool":
			return decodeToolEvent(payload);
		case "done":
			return { type: "done", message: payload.message as ChatMessageDto | undefined };
		case "error":
			return { type: "error", message: asText(payload.message) };
		default:
			return null;
	}
}

/**
 * Кадр активности инструмента: { name, phase: "start"|"end", detail?, callId? } —
 * деталь и идентификатор вызова при отсутствии не сериализуются
 * (WhenWritingNull транспорта). Идентификатор вызова сопоставляет пары
 * «начало — конец»: одновременные вызовы одного инструмента различимы.
 * Кадр без имени или с незнакомой фазой не является событием протокола
 * и игнорируется, как и незнакомое имя события.
 */
// Активность инструментов доставляется кадрами стрима до токенов результата,
// не ломая единственности терминального кадра.
// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
function decodeToolEvent(payload: Record<string, unknown>): ChatStreamEvent | null {
	const name = asText(payload.name);
	if (name === "")
		return null;
	if (payload.phase !== "start" && payload.phase !== "end")
		return null;
	// Отсутствующие деталь и callId не попадают в событие ключом — так же,
	// как транспорт не сериализует их при отсутствии; не-строковый callId
	// не делает кадр невалидным, он лишь сводится по имени.
	const detail = typeof payload.detail === "string" ? payload.detail : undefined;
	const callId = typeof payload.callId === "string" ? payload.callId : undefined;
	const event: Extract<ChatStreamEvent, { type: "tool" }> = {
		type: "tool",
		name,
		phase: payload.phase,
		...(detail !== undefined && { detail }),
		...(callId !== undefined && { callId }),
	};
	return event;
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
