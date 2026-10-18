import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useCompleteChatMutation, useDeleteChatMutation, useResumeChatMutation } from "./chat-mutations";
import { chatKeys } from "./chat-queries";
import { listChats } from "@/chat/api/chat-api";

// Мутации жизненного цикла чата: завершение, продолжение и удаление —
// ручные действия владельца; после мутации списки активных и завершённых
// чатов перечитываются, чтобы чат переехал между ними или исчез без
// ручного обновления.
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/history#requirement-chat-manual-completion-and-deletion
// Traceability: change:rework-contextual-topbar/tasks#7.0

function LifecycleHarness({ chatId }: { chatId: string }) {
	const complete = useCompleteChatMutation();
	const resume = useResumeChatMutation();
	const remove = useDeleteChatMutation();
	const active = useQuery({
		queryKey: chatKeys.list("active"),
		queryFn: () => listChats("active"),
	});

	return (
		<div>
			<span data-testid="active-count">{active.data?.length ?? 0}</span>
			<button type="button" onClick={() => complete.mutate(chatId)}>
				Завершить
			</button>
			<button type="button" onClick={() => resume.mutate(chatId)}>
				Продолжить
			</button>
			<button type="button" onClick={() => remove.mutate(chatId)}>
				Удалить
			</button>
		</div>
	);
}

function renderLifecycle(chatId: string) {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<LifecycleHarness chatId={chatId} />
		</QueryClientProvider>,
	);
}

afterEach(() => {
	vi.unstubAllGlobals();
});

describe("мутации завершения, продолжения и удаления чата", () => {
	it("завершение уходит в эндпоинт метки и обновляет списки чатов", async () => {
		// Arrange: активный список читается; мутация завершает чат и после
		// неё список активных перечитывается (уже без чата).
		const calls: { url: string; init?: RequestInit }[] = [];
		let activeListReads = 0;
		vi.stubGlobal(
			"fetch",
			vi.fn(async (url: string | URL, init?: RequestInit) => {
				const href = typeof url === "string" ? url : url.href;
				calls.push({ url: href, init });
				if (href.includes("/completion"))
					return jsonResponse({ id: "chat-1", status: "completed" });
				if (href.includes("status=active")) {
					activeListReads += 1;
					return jsonResponse(activeListReads === 1 ? [{ id: "chat-1" }] : []);
				}
				return jsonResponse([]);
			}),
		);
		const user = userEvent.setup();
		renderLifecycle("chat-1");

		// Act: владелец завершает чат.
		await user.click(await screen.findByRole("button", { name: "Завершить" }));

		// Assert: мутация ушла POST-меткой завершения, список активных
		// перечитан — счётчик обнулился.
		await waitFor(() => {
			expect(screen.getByTestId("active-count")).toHaveTextContent("0");
		});
		const completion = calls.find((call) => call.url.includes("/completion"));
		expect(completion?.url).toBe("/api/v1/chats/chat-1/completion");
		expect(completion?.init?.method).toBe("POST");
	});

	it("продолжение снимает метку завершения и обновляет списки чатов", async () => {
		// Arrange: завершённый чат возвращается в активные.
		const calls: { url: string; init?: RequestInit }[] = [];
		let activeListReads = 0;
		vi.stubGlobal(
			"fetch",
			vi.fn(async (url: string | URL, init?: RequestInit) => {
				const href = typeof url === "string" ? url : url.href;
				calls.push({ url: href, init });
				if (href.includes("/completion"))
					return jsonResponse({ id: "chat-2", status: "active" });
				if (href.includes("status=active")) {
					activeListReads += 1;
					return jsonResponse(activeListReads === 1 ? [] : [{ id: "chat-2" }]);
				}
				return jsonResponse([]);
			}),
		);
		const user = userEvent.setup();
		renderLifecycle("chat-2");

		// Act: владелец продолжает завершённый чат.
		await user.click(await screen.findByRole("button", { name: "Продолжить" }));

		// Assert: метка завершения снята DELETE-запросом, чат вернулся
		// в активные — список перечитан.
		await waitFor(() => {
			expect(screen.getByTestId("active-count")).toHaveTextContent("1");
		});
		const completion = calls.find((call) => call.url.includes("/completion"));
		expect(completion?.init?.method).toBe("DELETE");
	});

	/*
		Удаление — явное действие владельца (раунд-сверка 7.0): чат стирается
		целиком DELETE-запросом /api/v1/chats/{id} без тела ответа, списки
		активных и завершённых перечитываются — выбор следующего чата или
		пустого состояния остаётся за страницей.
	*/
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	// Traceability: change:rework-contextual-topbar/tasks#7.0
	it("удаление стирает чат целиком и обновляет списки чатов", async () => {
		// Arrange: активный список читается; DELETE /chats/{id} отвечает 204.
		const calls: { url: string; init?: RequestInit }[] = [];
		let activeListReads = 0;
		vi.stubGlobal(
			"fetch",
			vi.fn(async (url: string | URL, init?: RequestInit) => {
				const href = typeof url === "string" ? url : url.href;
				calls.push({ url: href, init });
				if (href.includes("/chats/chat-3"))
					return new Response(null, { status: 204 });
				if (href.includes("status=active")) {
					activeListReads += 1;
					return jsonResponse(activeListReads === 1 ? [{ id: "chat-3" }] : []);
				}
				return jsonResponse([]);
			}),
		);
		const user = userEvent.setup();
		renderLifecycle("chat-3");

		// Act: владелец удаляет чат кнопкой шапки (команда deleteChat).
		await user.click(await screen.findByRole("button", { name: "Удалить" }));

		// Assert: чат стёрт DELETE-запросом, список активных перечитан —
		// счётчик обнулился.
		await waitFor(() => {
			expect(screen.getByTestId("active-count")).toHaveTextContent("0");
		});
		const removed = calls.find((call) => call.url.includes("/chats/chat-3"));
		expect(removed?.init?.method).toBe("DELETE");
	});
});

function jsonResponse(body: unknown): Response {
	return new Response(JSON.stringify(body), {
		status: 200,
		headers: { "content-type": "application/json" },
	});
}
