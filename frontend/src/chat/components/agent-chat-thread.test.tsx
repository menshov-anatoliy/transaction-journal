import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AgentChatThread } from "./agent-chat-thread";
import type { AgentChatThreadContext } from "./agent-chat-thread";
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

function Harness({ chatId, model, context }: { chatId: string; model?: string; context?: AgentChatThreadContext }) {
	const chat = useAgentChatRuntime(chatId, model ?? "GLM-5.3");
	return <AgentChatThread chat={chat} context={context} />;
}

function renderChat(chatId = "chat-1", model?: string, context?: AgentChatThreadContext) {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<Harness chatId={chatId} model={model} context={context} />
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
	/** Стрим, который доставляет кадры и остаётся открытым до отмены. */
	openStream?: boolean;
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
				if (options.openStream === true)
					return openSseResponse(options.streamFrames ?? [], init?.signal);
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

/** Канал без терминального кадра: кадры доставлены, ход идёт до отмены владельцем. */
function openSseResponse(frames: readonly string[], signal: AbortSignal | null | undefined): Response {
	const encoder = new TextEncoder();
	const stream = new ReadableStream<Uint8Array>({
		start(controller) {
			for (const frame of frames)
				controller.enqueue(encoder.encode(frame));
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

function toolFrame(name: string, phase: "start" | "end", detail?: string): string {
	const payload: Record<string, string> = { name, phase };
	if (detail !== undefined)
		payload["detail"] = detail;
	return `event: tool\ndata: ${JSON.stringify(payload)}\n\n`;
}

/** Подписи видимых пилюль статуса инструментов — в порядке DOM. */
function pillLabels(): string[] {
	return Array.from(document.querySelectorAll('[data-slot="tool-status"]')).map(
		(pill) => pill.querySelector('[data-slot="tool-status-label"]')?.textContent ?? "",
	);
}

/** Высота строки композера в px для подменённых layout-метрик jsdom. */
const LINE_HEIGHT = 16;

/**
 * jsdom не считает layout: scrollHeight всегда 0, а lineHeight в computed
 * style — «normal», поэтому react-textarea-autosize не может измерить строку.
 * Для проверки автороста подменяются только эти две метрики (строка = 16px,
 * scrollHeight = число строк текста × высота строки); расчёт высоты по
 * minRows/maxRows библиотека выполняет по ним честно.
 */
function stubTextareaLayout(): () => void {
	const overrides: Record<string, string> = {
		boxSizing: "content-box",
		lineHeight: `${LINE_HEIGHT}px`,
		paddingTop: "0px",
		paddingRight: "0px",
		paddingBottom: "0px",
		paddingLeft: "0px",
		borderTopWidth: "0px",
		borderRightWidth: "0px",
		borderBottomWidth: "0px",
		borderLeftWidth: "0px",
	};
	const realGetComputedStyle = window.getComputedStyle.bind(window);
	const computedStyleSpy = vi.spyOn(window, "getComputedStyle").mockImplementation((node) => {
		const real = realGetComputedStyle(node);
		return new Proxy(real, {
			get(target, prop) {
				if (typeof prop === "string" && prop in overrides)
					return overrides[prop];
				return Reflect.get(target, prop, target);
			},
		});
	});
	const proto = HTMLTextAreaElement.prototype;
	const original = Object.getOwnPropertyDescriptor(proto, "scrollHeight");
	Object.defineProperty(proto, "scrollHeight", {
		configurable: true,
		get(this: HTMLTextAreaElement) {
			return this.value.split("\n").length * LINE_HEIGHT;
		},
	});
	return () => {
		computedStyleSpy.mockRestore();
		if (original !== undefined)
			Object.defineProperty(proto, "scrollHeight", original);
	};
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

// Живые пилюли активности инструментов: тред рендерит ToolStatus из
// состояния activeTools runtime — кадр tool start зажигает пилюлю «имя —
// деталь…», парный end гасит её, терминальные кадры и отмена чистят список;
// статичная надпись «источники — собираю данные…» и сами tool-call
// фрагменты в ленте не появляются.
// Traceability: openspec:ui/screens#scenario-ui-streaming-with-tool-status
describe("живые пилюли активности инструментов в треде", () => {
	it("рендерит пилюли инструментов из кадров tool start с именем и деталью", async () => {
		// Arrange: ход по открытому каналу; агент вызвал два инструмента,
		// оба start-кадра пришли до токенов ответа.
		mockChatApi({
			historyByCall: [[]],
			openStream: true,
			streamFrames: [
				"event: start\ndata: {}\n\n",
				toolFrame("get_market_snapshot", "start", "BTC"),
				toolFrame("read_rule_card", "start"),
			],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет вопрос — стартует ход со стримом.
		const input = await screen.findByRole("textbox");
		await user.type(input, "новый вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: две пилюли в порядке вызовов — формат ноды PfMRJ
		// «источник — что делаю…»; примитив мастера ix8ma (pill на surface2).
		await waitFor(() => {
			expect(pillLabels()).toEqual(["get_market_snapshot — BTC…", "read_rule_card…"]);
		});
		const pills = Array.from(document.querySelectorAll('[data-slot="tool-status"]'));
		expect(pills[0]?.className).toContain("rounded-full");
		expect(pills[0]?.className).toContain("bg-surface-2");
	});

	it("гасит пилюлю инструмента по парному кадру end", async () => {
		// Arrange: из двух вызванных инструментов первый уже завершился —
		// его end-кадр пришёл до токенов ответа.
		mockChatApi({
			historyByCall: [[]],
			openStream: true,
			streamFrames: [
				"event: start\ndata: {}\n\n",
				toolFrame("get_market_snapshot", "start"),
				toolFrame("get_option_board", "start"),
				toolFrame("get_market_snapshot", "end"),
			],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет вопрос.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: пилюля завершённого вызова погасла, осталась только
		// пилюля незавершённого.
		await waitFor(() => {
			expect(pillLabels()).toEqual(["get_option_board…"]);
		});
	});

	it("убирает пилюли по терминальному кадру done", async () => {
		// Arrange: ход с вызовом инструмента завершается кадром done;
		// сервер отдаёт перечитанную историю с финальным ответом.
		mockChatApi({
			historyByCall: [[], [userMessage("вопрос", "m1"), assistantMessage("Ответ готов", "m2")]],
			streamFrames: [
				"event: start\ndata: {}\n\n",
				toolFrame("get_market_snapshot", "start"),
				tokenFrame("Ответ "),
				toolFrame("get_market_snapshot", "end"),
				tokenFrame("готов"),
				"event: done\ndata: {}\n\n",
			],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: ход доходит до завершения сам.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: ответ собран, история перечитана, пилюль не осталось.
		expect(await screen.findByText("Ответ готов")).toBeInTheDocument();
		await waitFor(() => {
			expect(pillLabels()).toEqual([]);
		});
	});

	it("убирает пилюли по кадру ошибки генерации", async () => {
		// Arrange: вызов инструмента оборвался error-кадром того же
		// соединения при активном вызове.
		mockChatApi({
			historyByCall: [[]],
			streamFrames: [
				"event: start\ndata: {}\n\n",
				toolFrame("get_market_snapshot", "start"),
				'event: error\ndata: {"message":"генерация не удалась"}\n\n',
			],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: ход падает ошибкой.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: баннер деградации виден, активных пилюль не осталось.
		expect(await screen.findByRole("alert")).toHaveTextContent(/генерация не удалась/);
		await waitFor(() => {
			expect(pillLabels()).toEqual([]);
		});
	});

	it("не показывает статичную надпись источников на время хода", async () => {
		// Arrange: ход идёт по открытому каналу без tool-кадров — агент
		// ещё не вызывал инструменты.
		mockChatApi({
			historyByCall: [[]],
			openStream: true,
			streamFrames: ["event: start\ndata: {}\n\n"],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет вопрос — генерация идёт.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));
		await screen.findByRole("button", { name: "Остановить" });

		// Assert: статичная надпись «источники — собираю данные…» исчезла
		// из треда, пилюль без вызовов инструментов тоже нет.
		expect(screen.queryByText(/источники — собираю данные/)).toBeNull();
		expect(pillLabels()).toEqual([]);
	});

	it("не рендерит tool-call фрагменты в ленте сообщений", async () => {
		// Arrange: ход с активным вызовом инструмента и первым токеном.
		mockChatApi({
			historyByCall: [[]],
			openStream: true,
			streamFrames: [
				"event: start\ndata: {}\n\n",
				toolFrame("read_rule_card", "start", "R-1"),
				tokenFrame("Отвечаю "),
			],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет вопрос.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));

		// Assert: имя инструмента встречается в DOM только внутри пилюли
		// статуса — как фрагмент вызова в ленте сообщений оно не рендерится.
		await waitFor(() => {
			expect(pillLabels()).toEqual(["read_rule_card — R-1…"]);
		});
		const mentions = screen.getAllByText(/read_rule_card/);
		expect(mentions.length).toBeGreaterThan(0);
		for (const mention of mentions) {
			expect(mention.closest('[data-slot="tool-status"]')).not.toBeNull();
		}
	});
});

// Практики ввода композера (решение D5, мастер Z14sH): поле ввода
// авторастёт от одной до восьми строк, Enter отправляет, Shift+Enter
// переносит строку, отправка заблокирована на пустом поле.
// Traceability: openspec:ui/screens#scenario-ui-composer-input-practices
describe("практики ввода композера", () => {
	it("авторастёт с текстом от одной строки до восьми", async () => {
		// Arrange: композер на пустом чате; layout-метрики jsdom подменены
		// детерминированными (строка = 16px).
		const restoreLayout = stubTextareaLayout();
		try {
			mockChatApi({ historyByCall: [[]] });
			const user = userEvent.setup();
			renderChat();
			const input = (await screen.findByRole("textbox")) as HTMLTextAreaElement;

			// Assert: пустое поле — одна строка (minRows 1 → 16px).
			expect(input.style.height).toBe(`${LINE_HEIGHT}px`);

			// Act: владелец вводит четыре строки через Shift+Enter.
			await user.type(input, "раз{Shift>}{Enter}{/Shift}два{Shift>}{Enter}{/Shift}три{Shift>}{Enter}{/Shift}четыре");

			// Assert: поле выросло пропорционально числу строк.
			expect(input.style.height).toBe(`${LINE_HEIGHT * 4}px`);

			// Act: владелец доводит текст до девяти строк.
			for (let line = 5; line <= 9; line += 1)
				await user.type(input, `{Shift>}{Enter}{/Shift}строка ${line}`);

			// Assert: рост остановлен на восьми строках (maxRows 8 → 128px),
			// дальше поле не раздувается.
			expect(input.style.height).toBe(`${LINE_HEIGHT * 8}px`);
			expect(input.value.split("\n")).toHaveLength(9);
		}
		finally {
			restoreLayout();
		}
	});

	it("Enter отправляет многострочный текст, а Shift+Enter переносит строку", async () => {
		// Arrange: пустой чат, после отправки сервер отдаёт обновлённую историю.
		const calls = mockChatApi({
			historyByCall: [[], [userMessage("первая строка\nвторая строка", "m1")]],
			streamFrames: ["event: start\ndata: {}\n\n", tokenFrame("Отвечаю"), "event: done\ndata: {}\n\n"],
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец набирает две строки переносом Shift+Enter.
		const input = await screen.findByRole("textbox");
		await user.type(input, "первая строка{Shift>}{Enter}{/Shift}вторая строка");

		// Assert: перенос попал в значение, отправки по Shift+Enter не было.
		expect(input).toHaveValue("первая строка\nвторая строка");
		expect(calls.some((call) => call.url.includes("/messages/stream"))).toBe(false);

		// Act: Enter отправляет набранный многострочный вопрос.
		await user.type(input, "{Enter}");

		// Assert: стрим стартовал с многострочным текстом владельца.
		const streamCall = await waitFor(() => {
			const found = calls.find((call) => call.url.includes("/messages/stream"));
			expect(found).toBeDefined();
			return found!;
		});
		expect(JSON.parse(streamCall.init?.body as string).text).toBe("первая строка\nвторая строка");
	});

	it("блокирует отправку при пустом поле", async () => {
		// Arrange: пустой чат без истории.
		mockChatApi({ historyByCall: [[]] });
		const user = userEvent.setup();
		renderChat();
		await screen.findByRole("textbox");

		// Assert: на пустом поле кнопка отправки недоступна.
		expect(screen.getByRole("button", { name: "Отправить" })).toBeDisabled();

		// Act: владелец начинает печатать вопрос.
		await user.type(screen.getByRole("textbox"), "вопрос");

		// Assert: с непустым вводом отправка разблокировалась.
		expect(screen.getByRole("button", { name: "Отправить" })).toBeEnabled();
	});
});

// Stop во время генерации (решение D5): на время хода кнопка отправки
// исчезает — доступна только остановка; Stop обрывает SSE-стрим по
// AbortSignal и возвращает панель в состояние ввода.
// Traceability: openspec:ui/screens#scenario-ui-cancel-generation
describe("остановка генерации во время стрима", () => {
	it("во время генерации остаётся только остановка, Stop обрывает стрим и возвращает состояние ввода", async () => {
		// Arrange: канал не завершается сам; сервер после отмены отдаёт
		// историю с частичным ответом.
		const calls = mockChatApi({
			historyByCall: [[], [userMessage("вопрос", "m1"), assistantMessage("Токен", "m2")]],
			neverEndingStream: true,
		});
		const user = userEvent.setup();
		renderChat();

		// Act: владелец отправляет вопрос.
		const input = await screen.findByRole("textbox");
		await user.type(input, "вопрос");
		await user.click(screen.getByRole("button", { name: "Отправить" }));
		expect(await screen.findByText("Токен")).toBeInTheDocument();

		// Assert: на время хода отправки нет — только остановка; поле ввода
		// остаётся доступным.
		expect(screen.queryByRole("button", { name: "Отправить" })).toBeNull();
		expect(screen.getByRole("button", { name: "Остановить" })).toBeEnabled();
		expect(screen.getByRole("textbox")).toBeEnabled();

		// Act: владелец останавливает генерацию.
		await user.click(screen.getByRole("button", { name: "Остановить" }));

		// Assert: SSE-стрим реально оборван по AbortSignal.
		await waitFor(() => {
			const streamCall = calls.find((call) => call.url.includes("/messages/stream"));
			expect(streamCall?.init?.signal?.aborted).toBe(true);
		});

		// Assert: панель вернулась в состояние ввода — остановка исчезла,
		// отправка вернулась, поле снова принимает текст.
		await screen.findByRole("button", { name: "Отправить" });
		expect(screen.queryByRole("button", { name: "Остановить" })).toBeNull();
		const inputAfterStop = screen.getByRole("textbox");
		expect(inputAfterStop).toBeEnabled();
		await user.type(inputAfterStop, "дополнение");
		expect(inputAfterStop).toHaveValue("дополнение");
	});
});

// Чипы контекста в тулбаре композера (мастер Z14sH, тулбар ZCrdN): под
// полем — чип привязки (если чат привязан к конструкции), чип модели и
// чипы источников; параметры чата фиксируются при создании и не меняются.
// Traceability: openspec:ui/screens#scenario-ui-composer-input-practices
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
describe("чипы контекста в тулбаре композера", () => {
	it("показывает под полем чипы привязки, модели и источников", async () => {
		// Arrange: чат привязан к конструкции и читает все три источника.
		mockChatApi({ historyByCall: [[]] });
		renderChat("chat-1", "GLM-5.3", {
			model: "GLM-5.3",
			constructionLabel: "конструкция 7",
			sources: ["journal", "rules-corpus", "market"],
		});
		const input = await screen.findByRole("textbox");

		// Assert: тулбар следует за полем ввода внутри композера.
		const toolbar = document.querySelector('[data-slot="composer-context-chips"]');
		expect(toolbar).not.toBeNull();
		expect(toolbar?.closest("form")).toBe(input.closest("form"));
		expect(input.compareDocumentPosition(toolbar!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

		// Assert: чип привязки — мастер bCLlQ: пилюля на $infoSoft, иконка
		// link, подпись конструкции.
		const bindChip = within(toolbar as HTMLElement).getByText("конструкция 7").closest('[data-slot="composer-bind-chip"]');
		expect(bindChip?.className).toContain("rounded-full");
		expect(bindChip?.className).toContain("bg-info-soft");
		expect(bindChip).toHaveAttribute("title", "Чат привязан к конструкции");

		// Assert: чип модели — мастер aF5vM: $surface2 с радиусом 8 и
		// подписью модели.
		const modelChip = within(toolbar as HTMLElement).getByText("GLM-5.3").closest('[data-slot="composer-model-chip"]');
		expect(modelChip?.className).toContain("rounded-sm");
		expect(modelChip?.className).toContain("bg-secondary");
		expect(modelChip).toHaveAttribute("title", "Модель чата");

		// Assert: источники — инстансы примитива fYadZ в порядке справочника:
		// «журнал», «корпус правил» с check, «рынок Bybit» с globe.
		const sourceChips = Array.from((toolbar as HTMLElement).querySelectorAll('[data-slot="source-chip"]'));
		expect(sourceChips.map((chip) => chip.textContent)).toEqual(["журнал", "корпус правил", "рынок Bybit"]);
		expect(sourceChips[2]?.querySelector("svg.lucide-globe")).not.toBeNull();
	});

	it("у портфельного чата не рисует чип привязки", async () => {
		// Arrange: чат без привязки к конструкции — модель и один источник.
		mockChatApi({ historyByCall: [[]] });
		renderChat("chat-1", "GLM-5.3", { model: "GLM-5.3", sources: ["journal"] });

		// Assert: чипа привязки нет, модель и источник показаны.
		await screen.findByRole("textbox");
		expect(document.querySelector('[data-slot="composer-bind-chip"]')).toBeNull();
		expect(screen.getByText("GLM-5.3")).toBeInTheDocument();
		expect(screen.getByText("журнал")).toBeInTheDocument();
	});

	it("без контекста тулбар не рисует чипы вовсе", async () => {
		// Arrange: базовая обёртка без context (формы без параметров чата).
		mockChatApi({ historyByCall: [[]] });
		renderChat();

		// Assert: контейнера чипов нет — композер остаётся только с полем
		// и отправкой.
		await screen.findByRole("textbox");
		expect(document.querySelector('[data-slot="composer-context-chips"]')).toBeNull();
		expect(screen.getByRole("button", { name: "Отправить" })).toBeInTheDocument();
	});
});
