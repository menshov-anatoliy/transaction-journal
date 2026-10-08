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
// Слой сверки с design.pen (задача 5.1 change reconcile-frontend-with-design):
// сообщения и композер ленты чата принимаются по мастер-нодам Body #4 —
// eja01 (пузырь владельца), sENPF (плоский ответ), Z14sH/K7kSy (композер
// и кнопка отправки); классы проверяются как шов ретемизации.
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives

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
		expect(screen.getByText(/Правило/)).toBeInTheDocument();
	});

	it("стилизует сообщения и композер по мастер-нодам Body #4 (§3.5: 1–3)", async () => {
		// Arrange: история содержит вопрос владельца и ответ агента без следа,
		// чтобы классы контейнеров сообщений читались без вложенных панелей.
		mockChatApi({
			historyByCall: [
				[
					userMessage("вопрос из мастера", "m1"),
					assistantMessage("ответ из мастера", "m2"),
				],
			],
		});
		renderChat();

		// Assert: пузырь владельца — мастер eja01: заливка $surface2, радиус 12,
		// паддинги [10,14]; индиго-пузырь bg-primary исчез вместе с oklch-остатками.
		const bubble = (await screen.findByText("вопрос из мастера")).closest("div.bg-surface-2");
		expect(bubble).not.toBeNull();
		expect(bubble?.className).toContain("rounded-lg");
		expect(bubble?.className).toContain("px-3.5");
		expect(bubble?.className).toContain("py-2.5");
		expect(bubble?.className).toContain("max-w-[400px]");
		expect(bubble?.className).not.toContain("bg-primary");

		// Assert: текст сообщений — Inter 13.5 мастера: у владельца 1.5 (uuYEd).
		expect((await screen.findByText("ответ из мастера")).closest("div")?.className).toContain("text-[13.5px]");

		// Assert: ответ агента — мастер sENPF: плоский текст 13.5/1.55 без
		// карточки (нет bg-card/rounded-2xl у контейнера сообщения).
		const answerBody = screen.getByText("ответ из мастера").closest("div");
		expect(answerBody?.className).toContain("leading-[1.55]");
		expect(answerBody?.parentElement?.className).not.toContain("bg-card");
		expect(answerBody?.parentElement?.className).not.toContain("rounded-2xl");

		// Assert: композер — мастер Z14sH (радиус 14, паддинг 14, заливка
		// $surface), отправка — примитив Button Primary: квадрат 36×36
		// (size-9) на акценте $accent, радиус 10 (мастер-нода K7kSy).
		const send = screen.getByRole("button", { name: "Отправить" });
		expect(send.className).toContain("bg-primary");
		expect(send.className).toContain("size-9");
		expect(send.className).toContain("rounded-md");
		const composer = send.closest("form");
		expect(composer?.className).toContain("rounded-[14px]");
		expect(composer?.className).toContain("p-3.5");
		expect(composer?.className).toContain("bg-surface");
	});

	it("показывает пилюлю ToolStatus на время хода и убирает её по завершении (§3.5:4)", async () => {
		// Arrange: в чате есть история; новый ход идёт по каналу, который
		// не завершается сам — ход «в процессе» длится до отмены.
		mockChatApi({
			historyByCall: [[userMessage("старый вопрос", "m1")]],
			neverEndingStream: true,
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет вопрос.
		const input = await screen.findByRole("textbox");
		await user.type(input, "новый вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: пилюля «выполняется» мастера ix8ma (инстанс Tool2 Body #4)
		// видна в потоке ленты: pill 999 на surface2 с вращающейся иконкой.
		const statusLabel = await screen.findByText(/источники — собираю данные/);
		const pill = statusLabel.closest('[data-slot="tool-status"]');
		expect(pill).not.toBeNull();
		expect(pill?.className).toContain("rounded-full");
		expect(pill?.className).toContain("bg-surface-2");

		// Act: владелец останавливает генерацию.
		await user.click(await screen.findByRole("button", { name: "Остановить" }));

		// Assert: ход завершён — пилюля уходит вместе с состоянием хода.
		await waitFor(() => {
			expect(screen.queryByText(/источники — собираю данные/)).toBeNull();
		});
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
