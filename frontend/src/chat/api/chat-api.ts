import type { ChatDto, ChatMessageDto, ChatParams, ChatStatus } from "@/chat/types";
import { createSseFrameParser } from "@/chat/sse/parse-sse";
import { decodeChatStreamFrame, type ChatStreamEvent } from "@/chat/sse/chat-stream-events";

/**
 * Тонкий типизированный клиент API чата агента поверх /api/v1.
 *
 * Бэкенд-контракт реализован AgentEndpoints; полноценный ИИ-конвейер
 * подключается отдельно в change add-agent-chat:
 *
 *   GET    /api/v1/chats?status=active|completed
 *          → ChatDto[] — списки активных и завершённых чатов
 *   POST   /api/v1/chats            body { text, params }
 *          → ChatDto — чат создаётся первым сообщением владельца с параметрами
 *   GET    /api/v1/chats/{chatId}/messages
 *          → ChatMessageDto[] — плоская полная история чата
 *   POST   /api/v1/chats/{chatId}/messages/stream   body { text, model }
 *          → SSE text/event-stream — события start/token/done/error
 *            транспорта 2.1; нагрузка done опционально несёт финальное
 *            ChatMessageDto со следом источников; отправка сообщения
 *            в завершённый чат возвращает его в активные
 *   POST   /api/v1/chats/{chatId}/completion
 *          → ChatDto — ручное завершение чата владельцем
 *   DELETE /api/v1/chats/{chatId}/completion
 *          → ChatDto — продолжение: снятие метки завершения
 *   DELETE /api/v1/chats/{chatId}
 *          → 204 — удаление чата целиком вместе с сообщениями
 */

/** Базовый префикс API из задачи 2.1: версия контракта в маршруте. */
const API_PREFIX = "/api/v1";

/**
 * Сбой SSE-канала чата: ошибка генерации, доставленная тем же соединением,
 * либо обрыв соединения/отказ HTTP. Частичный текст ответа сохраняется —
 * деградация без потери уже показанной истории.
 */
export class ChatStreamFailure extends Error {
	constructor(
		message: string,
		/** Текст, собранный из токенов до сбоя канала. */
		readonly partialText: string,
	) {
		super(message);
		this.name = "ChatStreamFailure";
	}
}

export interface StreamChatReplyOptions {
	chatId: string;
	/** Текст сообщения владельца. */
	text: string;
	/** Текущая модель чата: новые сообщения уходят выбранной модели. */
	model?: string;
	/** Сигнал отмены генерации владельцем. */
	signal?: AbortSignal;
	/** Приёмник событий канала по мере их прихода. */
	onEvent: (event: ChatStreamEvent) => void;
}

export interface StreamChatReplyResult {
	/** Полный текст ответа, собранный из токенов. */
	text: string;
	/** Финальное сообщение из нагрузки done, если сервер его прислал. */
	message?: ChatMessageDto;
}

/**
 * Стриминговый запрос ответа ИИ-помощника: POST + ReadableStream, кадры SSE
 * разбираются инкрементально, текст ответа собирается по токенам. Ошибка
 * генерации приходит событием error того же соединения и выбрасывается
 * как ChatStreamFailure с сохранённым частичным текстом.
 */
// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
export async function streamChatReply(
	options: StreamChatReplyOptions,
): Promise<StreamChatReplyResult> {
	const response = await fetch(`${API_PREFIX}/chats/${options.chatId}/messages/stream`, {
		method: "POST",
		headers: {
			"content-type": "application/json",
			accept: "text/event-stream",
		},
		body: JSON.stringify({ text: options.text, model: options.model }),
		signal: options.signal,
	});

	if (response.ok === false || response.body === null)
		throw new ChatStreamFailure(`канал ответа недоступен: HTTP ${response.status}`, "");

	return readChatStream(response.body, options);
}

async function readChatStream(
	body: ReadableStream<Uint8Array>,
	options: StreamChatReplyOptions,
): Promise<StreamChatReplyResult> {
	const parser = createSseFrameParser();
	const decoder = new TextDecoder();
	let text = "";

	// Ошибка чтения и декодирования переносится в ChatStreamFailure —
	// вместе с уже собранным текстом, чтобы история не терялась.
	const reader = body.getReader();
	try {
		// Чтение идёт чанками без async-iterator: контроль границ кадров
		// остаётся у парсера, отмена — у сигнала fetch.
		for (;;) {
			const { done, value } = await reader.read();
			if (done)
				break;
			for (const frame of parser.push(decoder.decode(value, { stream: true }))) {
				const event = decodeChatStreamFrame(frame);
				if (event === null)
					continue;
				options.onEvent(event);
				if (event.type === "token")
					text += event.text;
				else if (event.type === "done")
					return { text, message: event.message };
				else if (event.type === "error") {
					// Сбой генерации доставлен событием ошибки того же соединения:
					// SPA деградирует, не теряя показанной истории чата.
					// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
					throw new ChatStreamFailure(event.message, text);
				}
			}
		}
	} catch (error) {
		if (error instanceof ChatStreamFailure)
			throw error;
		// Отмена владельцем проходит наружу как есть — это не сбой генерации.
		if (options.signal?.aborted === true || (error as Error).name === "AbortError")
			throw error;
		throw new ChatStreamFailure((error as Error).message, text);
	} finally {
		reader.releaseLock();
	}

	// Соединение закрылось без терминального события — транспортное нарушение.
	throw new ChatStreamFailure("канал закрылся без события завершения", text);
}

/** Список чатов по статусу жизненного цикла: активные или завершённые. */
export async function listChats(status: ChatStatus, signal?: AbortSignal): Promise<ChatDto[]> {
	const response = await fetch(`${API_PREFIX}/chats?status=${status}`, {
		headers: { accept: "application/json" },
		signal,
	});
	await ensureOk(response, "список чатов");
	return (await response.json()) as ChatDto[];
}

/**
 * Создание чата первым сообщением: чат рождается вместе с параметрами —
 * моделью, опциональной привязкой к конструкции и набором источников.
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
export async function createChat(text: string, params: ChatParams): Promise<ChatDto> {
	const response = await fetch(`${API_PREFIX}/chats`, {
		method: "POST",
		headers: { "content-type": "application/json", accept: "application/json" },
		body: JSON.stringify({ text, params }),
	});
	await ensureOk(response, "создание чата");
	return (await response.json()) as ChatDto;
}

/** Плоская полная история сообщений чата. */
export async function listChatMessages(chatId: string, signal?: AbortSignal): Promise<ChatMessageDto[]> {
	const response = await fetch(`${API_PREFIX}/chats/${chatId}/messages`, {
		headers: { accept: "application/json" },
		signal,
	});
	await ensureOk(response, "история чата");
	return (await response.json()) as ChatMessageDto[];
}

/**
 * Ручное завершение чата владельцем: активный чат уходит в завершённые,
 * история сохраняется.
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
export async function completeChat(chatId: string): Promise<ChatDto> {
	const response = await fetch(`${API_PREFIX}/chats/${chatId}/completion`, {
		method: "POST",
		headers: { accept: "application/json" },
	});
	await ensureOk(response, "завершение чата");
	return (await response.json()) as ChatDto;
}

/**
 * Продолжение завершённого чата: снятие ручной метки завершения и возврат
 * чата в активные (также происходит отправкой сообщения в завершённый чат).
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
export async function resumeChat(chatId: string): Promise<ChatDto> {
	const response = await fetch(`${API_PREFIX}/chats/${chatId}/completion`, {
		method: "DELETE",
		headers: { accept: "application/json" },
	});
	await ensureOk(response, "продолжение чата");
	return (await response.json()) as ChatDto;
}

/** Удаление чата целиком после подтверждения владельцем. */
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
export async function deleteChat(chatId: string): Promise<void> {
	const response = await fetch(`${API_PREFIX}/chats/${encodeURIComponent(chatId)}`, {
		method: "DELETE",
		headers: { accept: "application/json" },
	});
	await ensureOk(response, "удаление чата");
}

async function ensureOk(response: Response, what: string): Promise<void> {
	if (response.ok === false)
		throw new Error(`${what}: HTTP ${response.status}`);
}
