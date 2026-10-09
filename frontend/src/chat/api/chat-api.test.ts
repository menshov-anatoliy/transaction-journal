import { afterEach, describe, expect, it, vi } from "vitest";
import {
	ChatStreamFailure,
	completeChat,
	createChat,
	deleteChat,
	listChatMessages,
	listChats,
	resumeChat,
	streamChatReply,
} from "./chat-api";
import type { ChatParams } from "@/chat/types";

// Стриминговый запрос ответа ИИ-помощника — единственное место, где SPA
// читает SSE-канал транспорта 2.1: fetch + ReadableStream, кадры
// start/token/done/error разбираются в события, текст ответа собирается
// инкрементально.
// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse

function sseResponse(frames: readonly string[], init?: ResponseInit): Response {
	const encoder = new TextEncoder();
	const stream = new ReadableStream<Uint8Array>({
		start(controller) {
			for (const frame of frames)
				controller.enqueue(encoder.encode(frame));
			controller.close();
		},
	});
	return new Response(stream, { status: 200, ...init });
}

function tokenFrame(text: string): string {
	return `event: token\ndata: ${JSON.stringify({ text })}\n\n`;
}

afterEach(() => {
	vi.unstubAllGlobals();
});

// Удаление целиком подтверждено в UI и передаётся отдельным DELETE без completion.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
describe("удаление чата", () => {
	it("TryIfDeletionReturnsNoContent", async () => {
		// Arrange: успешный DELETE не содержит JSON.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
		vi.stubGlobal("fetch", fetchMock);
		// Act: удаляем чат.
		await deleteChat("chat-1");
		// Assert: отдельный маршрут целого чата.
		expect(fetchMock).toHaveBeenCalledWith("/api/v1/chats/chat-1", expect.objectContaining({ method: "DELETE" }));
	});

	it("ThrowOnDeletingUnknownChat", async () => {
		// Arrange: неизвестный идентификатор.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));
		// Act / Assert: ошибка остаётся видимой вызывающему коду.
		await expect(deleteChat("unknown")).rejects.toThrow("удаление чата: HTTP 404");
	});
});

describe("стриминг ответа ИИ-помощника по SSE", () => {
	it("собирает текст ответа из токенов и отдаёт события по мере прихода", async () => {
		// Arrange: сервер отвечает SSE-каналом с началом, токенами и завершением.
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue(
				sseResponse([
					"event: start\ndata: {}\n\n",
					tokenFrame("Привет"),
					tokenFrame(", владелец"),
					"event: done\ndata: {}\n\n",
				]),
			),
		);
		const events: string[] = [];

		// Act: сообщение владельца уходит в стриминговый эндпоинт.
		const result = await streamChatReply({
			chatId: "chat-1",
			text: "Как дела?",
			onEvent: (event) => events.push(event.type),
		});

		// Assert: текст собран из токенов, события приходили в порядке протокола.
		expect(result.text).toBe("Привет, владелец");
		expect(events).toEqual(["start", "token", "token", "done"]);
	});

	it("отправляет сообщение и модель чата в теле POST-запроса", async () => {
		// Arrange: перехватываем аргументы fetch.
		const fetchMock = vi.fn().mockResolvedValue(
			sseResponse(["event: start\ndata: {}\n\n", "event: done\ndata: {}\n\n"]),
		);
		vi.stubGlobal("fetch", fetchMock);
		const controller = new AbortController();

		// Act: запрос идёт с текстом, моделью и сигналом отмены.
		await streamChatReply({
			chatId: "chat-7",
			text: "Вопрос",
			model: "GLM-5.3",
			signal: controller.signal,
			onEvent: () => {},
		});

		// Assert: путь, метод, заголовки и тело соответствуют контракту API.
		const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
		expect(url).toBe("/api/v1/chats/chat-7/messages/stream");
		expect(init.method).toBe("POST");
		expect(init.signal).toBe(controller.signal);
		expect(new Headers(init.headers).get("accept")).toBe("text/event-stream");
		expect(JSON.parse(init.body as string)).toEqual({
			text: "Вопрос",
			model: "GLM-5.3",
		});
	});

	it("принимает финальное сообщение со следом источников из нагрузки done", async () => {
		// Arrange: завершение канала несёт финальное DTO ответа — с идентификатором,
		// as-of и следом источников.
		const finalMessage = {
			id: "msg-9",
			role: "assistant",
			text: "Ответ",
			asOf: "2026-10-08T05:00:00Z",
			sourceTrace: {
				toolCalls: [{ tool: "get_market_snapshot", argument: "BTC", asOf: "2026-10-08T05:00:00Z" }],
				references: [{ kind: "rule-card", id: "R-1", title: "Роллирование", asOf: "2026-10-08T04:55:00Z" }],
			},
		};
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue(
				sseResponse([
					tokenFrame("Ответ"),
					`event: done\ndata: ${JSON.stringify({ message: finalMessage })}\n\n`,
				]),
			),
		);

		// Act: стрим доходит до завершения.
		const result = await streamChatReply({ chatId: "c", text: "в", onEvent: () => {} });

		// Assert: финальное сообщение доезжает до вызывающего целиком.
		expect(result.message).toEqual(finalMessage);
	});

	it("ошибка в том же соединении деградирует с сохранением частичного текста", async () => {
		// Arrange: канал падает по ходу генерации — событие ошибки приходит после
		// нескольких токенов того же соединения.
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue(
				sseResponse([
					tokenFrame("Част"),
					tokenFrame("ичный ответ"),
					'event: error\ndata: {"message":"генерация не удалась"}\n\n',
				]),
			),
		);

		// Act: стрим завершается событием ошибки.
		const failure = await streamChatReply({ chatId: "c", text: "в", onEvent: () => {} }).catch(
			(error: unknown) => error,
		);

		// Assert: сбой типизирован, уже показанные токены не теряются —
		// деградация без потери истории чата.
		expect(failure).toBeInstanceOf(Error);
		expect((failure as Error & { partialText: string }).partialText).toBe("Частичный ответ");
		expect((failure as Error).message).toBe("генерация не удалась");
	});

	it("сетевой обрыв соединения сохраняет полученные токены", async () => {
		// Arrange: соединение умирает после токенов без терминального кадра —
		// токены успевают дойти до клиента раньше сбоя.
		const encoder = new TextEncoder();
		const stream = new ReadableStream<Uint8Array>({
			async start(controller) {
				controller.enqueue(encoder.encode(tokenFrame("Начало")));
				await new Promise((resolve) => setTimeout(resolve, 20));
				controller.error(new Error("network reset"));
			},
		});
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue(new Response(stream, { status: 200 })),
		);

		// Act: чтение потока обрывается на середине.
		const failure = await streamChatReply({ chatId: "c", text: "в", onEvent: () => {} }).catch(
			(error: unknown) => error,
		);

		// Assert: обрыв типизирован так же, как ошибка генерации, с частичным текстом.
		expect(failure).toBeInstanceOf(Error);
		expect((failure as Error & { partialText: string }).partialText).toBe("Начало");
	});

	it("отказ HTTP до начала канала даёт пустой частичный текст", async () => {
		// Arrange: сервер отвечает ошибкой без SSE-канала.
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue(new Response("not found", { status: 404 })),
		);

		// Act: запрос выполняется.
		const failure = await streamChatReply({ chatId: "c", text: "в", onEvent: () => {} }).catch(
			(error: unknown) => error,
		);

		// Assert: отказ канала типизирован, стриминга не было — текст пуст.
		expect(failure).toBeInstanceOf(Error);
		expect((failure as Error & { partialText: string }).partialText).toBe("");
	});

	it("отмена генерации прерывает чтение канала", async () => {
		// Arrange: канал не завершается сам — только отменой владельцем.
		// Мок повторяет поведение undici: прерывание сигнала fetch роняет
		// тело ответа с AbortError.
		const controller = new AbortController();
		const encoder = new TextEncoder();
		const stream = new ReadableStream<Uint8Array>({
			start(c) {
				c.enqueue(encoder.encode(tokenFrame("Токен")));
				controller.signal.addEventListener("abort", () => {
					c.error(new DOMException("Aborted", "AbortError"));
				});
			},
		});
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue(new Response(stream, { status: 200 })),
		);

		// Act: чтение начинается и отменяется посреди генерации.
		const promise = streamChatReply({
			chatId: "c",
			text: "в",
			signal: controller.signal,
			onEvent: () => {},
		});
		await new Promise((resolve) => setTimeout(resolve, 20));
		controller.abort();
		const outcome = await promise.catch((error: unknown) => error);

		// Assert: отмена доезжает прерыванием (имя AbortError у DOMException
		// отмены fetch), а не сбоем генерации канала.
		expect(outcome).toBeInstanceOf(DOMException);
		expect((outcome as Error).name).toBe("AbortError");
		expect(outcome).not.toBeInstanceOf(ChatStreamFailure);
	});
});

describe("crud-эндпоинты чата агента", () => {
	function jsonResponse(body: unknown): Response {
		return new Response(JSON.stringify(body), {
			status: 200,
			headers: { "content-type": "application/json" },
		});
	}

	it("запрашивает список чатов по статусу жизненного цикла", async () => {
		// Arrange: сервер отдаёт список активных чатов.
		const chats = [{ id: "chat-1", status: "active" }];
		const fetchMock = vi.fn().mockResolvedValue(jsonResponse(chats));
		vi.stubGlobal("fetch", fetchMock);

		// Act: клиент читает активные чаты.
		const result = await listChats("active");

		// Assert: путь несёт статус, ответ разбирается в DTO чатов.
		expect(result).toEqual(chats);
		const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
		expect(url).toBe("/api/v1/chats?status=active");
		expect(init.method).toBeUndefined();
	});

	it("создаёт чат первым сообщением с параметрами", async () => {
		// Arrange: первый вопрос владельца создаёт чат с выбранными параметрами.
		const chat = { id: "chat-2", status: "active" };
		const fetchMock = vi.fn().mockResolvedValue(jsonResponse(chat));
		vi.stubGlobal("fetch", fetchMock);
		const params: ChatParams = {
			model: "GLM-5.3",
			constructionId: null,
			sources: ["journal", "rules-corpus", "market"],
		};

		// Act: клиент создаёт чат текстом первого сообщения и параметрами.
		const result = await createChat("Первый вопрос", params);

		// Assert: POST уходит с текстом и параметрами, ответ — DTO нового чата.
		expect(result).toEqual(chat);
		const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
		expect(url).toBe("/api/v1/chats");
		expect(init.method).toBe("POST");
		expect(JSON.parse(init.body as string)).toEqual({
			text: "Первый вопрос",
			params,
		});
	});

	it("читает плоскую историю сообщений чата", async () => {
		// Arrange: история чата — плоский список сообщений обеих ролей.
		const messages = [
			{ id: "m1", role: "user", text: "в", asOf: "2026-10-08T05:00:00Z" },
			{ id: "m2", role: "assistant", text: "о", asOf: "2026-10-08T05:00:05Z" },
		];
		const fetchMock = vi.fn().mockResolvedValue(jsonResponse(messages));
		vi.stubGlobal("fetch", fetchMock);

		// Act: клиент читает историю чата.
		const result = await listChatMessages("chat-9");

		// Assert: путь указывает на сообщения чата, порядок сохранён.
		expect(result).toEqual(messages);
		expect((fetchMock.mock.calls[0] as [string])[0]).toBe("/api/v1/chats/chat-9/messages");
	});

	it("завершает чат ручной меткой владельца", async () => {
		// Arrange: завершение — отдельное действие над активным чатом.
		const chat = { id: "chat-3", status: "completed" };
		const fetchMock = vi.fn().mockResolvedValue(jsonResponse(chat));
		vi.stubGlobal("fetch", fetchMock);

		// Act: клиент завершает чат.
		const result = await completeChat("chat-3");

		// Assert: ставится ручная метка завершения, ответ — обновлённый чат.
		expect(result).toEqual(chat);
		const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
		expect(url).toBe("/api/v1/chats/chat-3/completion");
		expect(init.method).toBe("POST");
	});

	it("продолжает завершённый чат снятием метки завершения", async () => {
		// Arrange: продолжение возвращает завершённый чат в активные.
		const chat = { id: "chat-3", status: "active" };
		const fetchMock = vi.fn().mockResolvedValue(jsonResponse(chat));
		vi.stubGlobal("fetch", fetchMock);

		// Act: клиент продолжает чат.
		const result = await resumeChat("chat-3");

		// Assert: метка завершения снимается, чат снова активен.
		expect(result).toEqual(chat);
		const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
		expect(url).toBe("/api/v1/chats/chat-3/completion");
		expect(init.method).toBe("DELETE");
	});

	it("отказ сервера превращается в ошибку клиента", async () => {
		// Arrange: сервер отвечает отказом на чтение истории.
		vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("err", { status: 500 })));

		// Act: клиент запрашивает историю.
		const outcome = await listChatMessages("chat-x").catch((error: unknown) => error);

		// Assert: отказ типизирован ошибкой с кодом статуса.
		expect(outcome).toBeInstanceOf(Error);
		expect((outcome as Error).message).toContain("500");
	});
});
