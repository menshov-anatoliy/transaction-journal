import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Fragment, type ReactElement } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AgentChatThread } from "@/chat/components/agent-chat-thread";
import { useAgentChatRuntime } from "./use-agent-chat-runtime";
import type { ChatMessageDto } from "@/chat/types";

// Runtime чата агента хранит активные инструменты из кадров tool SSE-стрима:
// start зажигает инструмент, end гасит, терминальные кадры (done/error)
// чистят список целиком — пилюли активности рендерятся из этого состояния
// (рендер — задача 4.1, здесь проверяется только состояние).
// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed

/** Зонд состояния: активные инструменты контроллера и признак хода. */
function ActiveToolsProbe(): ReactElement {
	const chat = useAgentChatRuntime("chat-tool-1", "GLM-5.3");
	return (
		<Fragment>
			<ul data-testid="active-tools">
				{chat.activeTools.map((tool) => (
					<li key={tool.name}>
						{tool.name}
						{tool.detail === undefined ? "" : ` — ${tool.detail}`}
					</li>
				))}
			</ul>
			<span data-testid="is-running">{String(chat.isRunning)}</span>
			<AgentChatThread chat={chat} />
		</Fragment>
	);
}

function renderRuntime(): void {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	render(
		<QueryClientProvider client={queryClient}>
			<ActiveToolsProbe />
		</QueryClientProvider>,
	);
}

async function sendMessage(user: ReturnType<typeof userEvent.setup>): Promise<void> {
	const input = await screen.findByRole("textbox");
	await user.type(input, "вопрос агенту");
	await user.click(screen.getByRole("button", { name: "Отправить" }));
}

interface ApiMockOptions {
	/** Кадры SSE стриминга ответа; канал закрывается после них. */
	streamFrames?: readonly string[];
	/** Стрим, который держит канал открытым до отмены владельцем. */
	openStream?: boolean;
	/** История чата после завершённого хода: первое чтение — пустое, дальше — эта. */
	historyAfterRun?: readonly ChatMessageDto[];
}

function mockChatApi(options: ApiMockOptions = {}): void {
	let historyReads = 0;
	vi.stubGlobal(
		"fetch",
		vi.fn(async (url: string | URL, init?: RequestInit) => {
			const href = typeof url === "string" ? url : url.href;
			if (href.includes("/messages/stream")) {
				if (options.openStream === true)
					return openSseResponse(options.streamFrames ?? [], init?.signal);
				return sseResponse(options.streamFrames ?? []);
			}
			if (href.includes("/messages")) {
				historyReads += 1;
				return jsonResponse(historyReads === 1 ? [] : (options.historyAfterRun ?? []));
			}
			return jsonResponse([]);
		}),
	);
}

function jsonResponse(body: unknown): Response {
	return new Response(JSON.stringify(body), {
		status: 200,
		headers: { "content-type": "application/json" },
	});
}

// Кадры приходят одной порцией; канал закрывается — ход завершается сам.
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

/** Канал без терминального кадра: закрывается только отменой владельцем. */
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

function toolFrame(name: string, phase: "start" | "end", detail?: string): string {
	const payload: Record<string, string> = { name, phase };
	if (detail !== undefined)
		payload["detail"] = detail;
	return `event: tool\ndata: ${JSON.stringify(payload)}\n\n`;
}

function tokenFrame(text: string): string {
	return `event: token\ndata: ${JSON.stringify({ text })}\n\n`;
}

afterEach(() => {
	vi.unstubAllGlobals();
});

describe("активность инструментов в runtime чата агента", () => {
	it("копит инструменты из кадров start с деталью и порядком вызовов", async () => {
		// Arrange: ход идёт по открытому каналу; агент вызвал два тула реестра,
		// оба start-кадра дошли до токенов результата.
		// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
		vi.stubGlobal(
			"fetch",
			vi.fn(async (url: string | URL, init?: RequestInit) => {
				const href = typeof url === "string" ? url : url.href;
				if (href.includes("/messages/stream")) {
					return openSseResponse(
						[
							'event: start\ndata: {}\n\n',
							toolFrame("get_market_snapshot", "start", "BTC"),
							toolFrame("read_rule_card", "start"),
						],
						init?.signal,
					);
				}
				return jsonResponse([]);
			}),
		);
		const user = userEvent.setup();
		renderRuntime();

		// Act: владелец отправляет вопрос, стрим доставляет кадры активности.
		await sendMessage(user);

		// Assert: оба инструмента активны, порядок — по моменту старта,
		// деталь вызова видна у рыночного тула.
		await waitFor(() => {
			expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
				"get_market_snapshot — BTC",
				"read_rule_card",
			]);
		});
		expect(screen.getByTestId("is-running")).toHaveTextContent("true");
	});

	it("повторный start того же инструмента не плодит дублей", async () => {
		// Arrange: модель дважды запросила один инструмент — повторный кадр
		// старта обновляет запись, а не добавляет новую.
		// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
		vi.stubGlobal(
			"fetch",
			vi.fn(async (url: string | URL, init?: RequestInit) => {
				const href = typeof url === "string" ? url : url.href;
				if (href.includes("/messages/stream")) {
					return openSseResponse(
						[
							'event: start\ndata: {}\n\n',
							toolFrame("get_option_board", "start"),
							toolFrame("get_option_board", "start", "повторный вызов"),
						],
						init?.signal,
					);
				}
				return jsonResponse([]);
			}),
		);
		const user = userEvent.setup();
		renderRuntime();

		// Act: ход с повторным вызовом одного тула.
		await sendMessage(user);

		// Assert: инструмент в списке один, деталь обновилась последним кадром.
		await waitFor(() => {
			expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
				"get_option_board — повторный вызов",
			]);
		});
	});

	it("гасит инструмент по парному кадру end", async () => {
		// Arrange: из двух вызванных инструментов первый уже завершился —
		// его end-кадр пришёл до токенов второго.
		// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
		vi.stubGlobal(
			"fetch",
			vi.fn(async (url: string | URL, init?: RequestInit) => {
				const href = typeof url === "string" ? url : url.href;
				if (href.includes("/messages/stream")) {
					return openSseResponse(
						[
							'event: start\ndata: {}\n\n',
							toolFrame("get_market_snapshot", "start"),
							toolFrame("get_option_board", "start"),
							toolFrame("get_market_snapshot", "end"),
						],
						init?.signal,
					);
				}
				return jsonResponse([]);
			}),
		);
		const user = userEvent.setup();
		renderRuntime();

		// Act: владелец отправляет вопрос.
		await sendMessage(user);

		// Assert: остался только незавершённый вызов.
		await waitFor(() => {
			expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
				"get_option_board",
			]);
		});

		// Act: владелец останавливает генерацию — терминального кадра не будет.
		await user.click(await screen.findByRole("button", { name: "Остановить" }));

		// Assert: после отмены хода список активных инструментов пуст.
		await waitFor(() => {
			expect(screen.queryAllByRole("listitem")).toHaveLength(0);
		});
	});

	it("чистит список по терминальному кадру done", async () => {
		// Arrange: ход с вызовом инструмента завершается кадром done.
		// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
		mockChatApi({
			historyAfterRun: [
				{ id: "m1", role: "user", text: "вопрос агенту", asOf: "2026-10-08T05:00:00Z" },
				{ id: "m2", role: "assistant", text: "Ответ готов", asOf: "2026-10-08T05:00:05Z" },
			],
			streamFrames: [
				'event: start\ndata: {}\n\n',
				toolFrame("get_market_snapshot", "start"),
				tokenFrame("Ответ "),
				toolFrame("get_market_snapshot", "end"),
				tokenFrame("готов"),
				'event: done\ndata: {}\n\n',
			],
		});
		const user = userEvent.setup();
		renderRuntime();

		// Act: ход доходит до завершения сам.
		await sendMessage(user);

		// Assert: ответ собран, активных инструментов после done не осталось.
		expect(await screen.findByText("Ответ готов")).toBeInTheDocument();
		await waitFor(() => {
			expect(screen.queryAllByRole("listitem")).toHaveLength(0);
		});
		expect(screen.getByTestId("is-running")).toHaveTextContent("false");
	});

	it("чистит список по терминальному кадру error", async () => {
		// Arrange: вызов инструмента оборвался ошибкой генерации — error-кадр
		// того же соединения идёт при активном вызове.
		// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
		mockChatApi({
			streamFrames: [
				'event: start\ndata: {}\n\n',
				toolFrame("get_market_snapshot", "start"),
				'event: error\ndata: {"message":"генерация не удалась"}\n\n',
			],
		});
		const user = userEvent.setup();
		renderRuntime();

		// Act: ход падает ошибкой.
		await sendMessage(user);

		// Assert: баннер деградации виден, активных инструментов не осталось.
		expect(await screen.findByRole("alert")).toHaveTextContent(/генерация не удалась/);
		await waitFor(() => {
			expect(screen.queryAllByRole("listitem")).toHaveLength(0);
		});
	});

	it("стрим без tool-кадров оставляет список активных инструментов пустым", async () => {
		// Arrange: агент ответил без вызовов инструментов — совместимость
		// с прежним протоколом start/token/done.
		// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
		mockChatApi({
			historyAfterRun: [
				{ id: "m1", role: "user", text: "вопрос агенту", asOf: "2026-10-08T05:00:00Z" },
				{ id: "m2", role: "assistant", text: "Ответ без инструментов", asOf: "2026-10-08T05:00:05Z" },
			],
			streamFrames: [
				'event: start\ndata: {}\n\n',
				tokenFrame("Ответ без инструментов"),
				'event: done\ndata: {}\n\n',
			],
		});
		const user = userEvent.setup();
		renderRuntime();

		// Act: ход без кадров активности.
		await sendMessage(user);

		// Assert: список пуст на всём протяжении хода, текст собран из токенов.
		expect(await screen.findByText("Ответ без инструментов")).toBeInTheDocument();
		expect(screen.queryAllByRole("listitem")).toHaveLength(0);
	});
});
