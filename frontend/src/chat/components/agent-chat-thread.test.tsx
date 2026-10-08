import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AgentChatThread } from "./agent-chat-thread";
import { useAgentChatRuntime } from "@/chat/runtime/use-agent-chat-runtime";
import type { ChatMessageDto } from "@/chat/types";

// Базовая обёртка чата агента: Thread/Composer assistant-ui поверх
// ExternalStoreRuntime, источником данных выступает TanStack Query;
// ответы ИИ-помощника приходят SSE-адаптером и рендерятся общим
// MD-рендером журнального текста со следом источников под ответом.
// Traceability: adr:docs/adr/0010-frontend-spa-react-stack.md
// Traceability: change:add-agent-chat/proposal#what-changes

function Harness({ chatId, model }: { chatId: string; model?: string }) {
	const chat = useAgentChatRuntime(chatId, model ?? "GLM-5.3");
	return <AgentChatThread chat={chat} />;
}

function renderChat(chatId = "chat-1", model?: string) {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<Harness chatId={chatId} model={model} />
		</QueryClientProvider>,
	);
}

interface ApiMockOptions {
	/** История чата по последовательным GET-запросам (первый — стартовый). */
	historyByCall?: readonly (readonly ChatMessageDto[])[] | (() => ChatMessageDto[]);
	/** Кадры SSE стриминга ответа. */
	streamFrames?: string[];
	/** Стрим, который завершает только отмена владельцем. */
	neverEndingStream?: boolean;
}

function mockChatApi(options: ApiMockOptions = {}) {
	const calls: { url: string; init?: RequestInit }[] = [];
	const historyByCall = options.historyByCall ?? [];
	let historyCall = 0;

	vi.stubGlobal(
		"fetch",
		vi.fn(async (url: string | URL, init?: RequestInit) => {
			const href = typeof url === "string" ? url : url.href;
			calls.push({ url: href, init });

			if (href.includes("/messages/stream")) {
				if (options.neverEndingStream === true)
					return neverEndingSseResponse(init?.signal ?? undefined);
				return sseResponse(options.streamFrames ?? []);
			}
			if (href.includes("/messages")) {
				const history =
					typeof historyByCall === "function"
						? historyByCall()
						: (historyByCall[historyCall] ?? historyByCall.at(-1) ?? []);
				historyCall += 1;
				return jsonResponse(history);
			}
			return jsonResponse([]);
		}),
	);
	return calls;
}

function jsonResponse(body: unknown): Response {
	return new Response(JSON.stringify(body), {
		status: 200,
		headers: { "content-type": "application/json" },
	});
}

function sseResponse(frames: readonly string[]): Response {
	const encoder = new TextEncoder();
	const stream = new ReadableStream<Uint8Array>({
		start(controller) {
			for (const frame of frames)
				controller.enqueue(encoder.encode(frame));
			controller.close();
		},
	});
	return new Response(stream, { status: 200 });
}

function neverEndingSseResponse(signal: AbortSignal | undefined): Response {
	const encoder = new TextEncoder();
	const stream = new ReadableStream<Uint8Array>({
		start(controller) {
			controller.enqueue(encoder.encode('event: token\ndata: {"text":"Токен"}\n\n'));
			signal?.addEventListener("abort", () => {
				controller.error(new DOMException("Aborted", "AbortError"));
			});
		},
	});
	return new Response(stream, { status: 200 });
}

function tokenFrame(text: string): string {
	return `event: token\ndata: ${JSON.stringify({ text })}\n\n`;
}

function userMessage(text: string, id: string): ChatMessageDto {
	return { id, role: "user", text, asOf: "2026-10-08T05:00:00Z" };
}

function assistantMessage(text: string, id: string, trace?: ChatMessageDto["sourceTrace"]): ChatMessageDto {
	return { id, role: "assistant", text, asOf: "2026-10-08T05:00:05Z", sourceTrace: trace };
}

afterEach(() => {
	vi.unstubAllGlobals();
});

describe("базовая обёртка чата агента", () => {
	it("рендерит историю из TanStack Query общим MD-рендером со следом источников", async () => {
		// Arrange: история чата содержит вопрос владельца и ответ ИИ-помощника
		// с markdown-текстом и следом источников.
		mockChatApi({
			historyByCall: [
				[
					userMessage("Как закрыть конструкцию?", "m1"),
					assistantMessage("Если **спрос** растёт — роллируй [карточку](/rules/1)", "m2", {
						toolCalls: [{ tool: "get_market_snapshot", argument: "BTC", asOf: "2026-10-08T05:00:00Z" }],
						references: [{ kind: "rule-card", id: "R-1", title: "Роллирование", asOf: "2026-10-07T12:00:00Z" }],
					}),
				],
			],
		});

		// Act: чат монтируется.
		renderChat();

		// Assert: текст истории виден; ответ отрендерен как Markdown
		// (жирность и ссылка), под ответом — след источников с as-of.
		expect(await screen.findByText("Как закрыть конструкцию?")).toBeInTheDocument();
		const bold = await screen.findByText("спрос");
		expect(bold.tagName).toBe("STRONG");
		expect(screen.getByRole("link", { name: /карточку/ })).toBeInTheDocument();
		expect(screen.getByText(/get_market_snapshot/)).toBeInTheDocument();
		expect(screen.getByText(/Роллирование/)).toBeInTheDocument();
		expect(screen.getByText(/Карточка правила/)).toBeInTheDocument();
	});

	it("отправляет сообщение и стримит ответ токенами до завершения", async () => {
		// Arrange: пустой чат; после завершения стрима сервер отдаёт
		// обновлённую историю с обоими сообщениями.
		const calls = mockChatApi({
			historyByCall: [
				[],
				[userMessage("вопрос", "m1"), assistantMessage("Ответ готов", "m2")],
			],
			streamFrames: [
				"event: start\ndata: {}\n\n",
				tokenFrame("Ответ "),
				tokenFrame("готов"),
				"event: done\ndata: {}\n\n",
			],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец печатает вопрос и отправляет.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: сообщение владельца видно, ответ собран из токенов; после
		// завершения история перечитана у сервера — финал без дублей.
		expect(await screen.findByText("Ответ готов")).toBeInTheDocument();
		expect(screen.getByText("вопрос")).toBeInTheDocument();
		await waitFor(() => {
			const historyReads = calls.filter((call) => call.url.includes("/messages") && !call.url.includes("stream"));
			expect(historyReads.length).toBeGreaterThanOrEqual(2);
		});
		expect(screen.getAllByText("Ответ готов")).toHaveLength(1);

		// Модель чата ушла в стриминговый запрос выбранной.
		const streamCall = calls.find((call) => call.url.includes("/messages/stream"));
		expect(JSON.parse(streamCall?.init?.body as string).model).toBe("GLM-5.3");
	});

	it("ошибка генерации деградирует с сохранением частичного текста и истории", async () => {
		// Arrange: в чате есть история; новый ответ падает событием ошибки
		// того же соединения после пары токенов.
		mockChatApi({
			historyByCall: [
				[
					userMessage("старый вопрос", "m1"),
					assistantMessage("старый ответ", "m2"),
				],
			],
			streamFrames: [
				tokenFrame("Част"),
				'event: error\ndata: {"message":"генерация не удалась"}\n\n',
			],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет новый вопрос.
		const input = await screen.findByRole("textbox");
		await user.type(input, "новый вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: баннер деградации виден, частичный текст ответа сохранён,
		// прежняя история не потеряна.
		expect(await screen.findByRole("alert")).toHaveTextContent(/генерация не удалась/);
		expect(screen.getByText("Част")).toBeInTheDocument();
		expect(screen.getByText("старый ответ")).toBeInTheDocument();
		expect(screen.getByText("новый вопрос")).toBeInTheDocument();
	});

	it("отмена останавливает генерацию и синхронизируется с историей сервера", async () => {
		// Arrange: канал не завершается сам; сервер после отмены отдаёт
		// историю с частично сгенерированным ответом.
		const calls = mockChatApi({
			historyByCall: [
				[],
				[userMessage("вопрос", "m1"), assistantMessage("Токен", "m2")],
			],
			neverEndingStream: true,
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет вопрос и останавливает генерацию.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));
		expect(await screen.findByText("Токен")).toBeInTheDocument();
		await user.click(await screen.findByRole("button", { name: "Остановить" }));

		// Assert: после отмены состояние чата приходит к истории сервера —
		// частичный ответ показан один раз, без дублей оверлея.
		await waitFor(() => {
			const historyReads = calls.filter((call) => call.url.includes("/messages") && !call.url.includes("stream"));
			expect(historyReads.length).toBeGreaterThanOrEqual(2);
		});
		await waitFor(() => {
			expect(screen.getAllByText("Токен")).toHaveLength(1);
		});
	});
});
