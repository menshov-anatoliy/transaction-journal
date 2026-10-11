import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ConstructionChatsPanel } from "./construction-chats-panel";
import type { ChatDto } from "@/chat/types";
import { chatKeys } from "@/chat/chat-queries";

const api = vi.hoisted(() => ({
	listChats: vi.fn(), createChat: vi.fn(), completeChat: vi.fn(),
	resumeChat: vi.fn(), deleteChat: vi.fn(), listChatMessages: vi.fn(), readRule: vi.fn(),
}));
vi.mock("@/chat/api/chat-api", async (original) => ({
	...await original<typeof import("@/chat/api/chat-api")>(),
	listChats: api.listChats, createChat: api.createChat, completeChat: api.completeChat,
	resumeChat: api.resumeChat, deleteChat: api.deleteChat, listChatMessages: api.listChatMessages,
}));
vi.mock("@/lib/api/agent-rules", () => ({ readRule: api.readRule }));
vi.mock("@/chat/runtime/use-agent-chat-runtime", () => ({
	useAgentChatRuntime: () => ({ runtime: {}, isRunning: false, error: null, cancel: vi.fn() }),
}));
// Проверяем связь панели с реальным следом источников, изолируя только runtime assistant-ui.
vi.mock("@/chat/components/agent-chat-thread", async () => {
	const { SourceTraceView } = await import("@/chat/components/source-trace-view");
	return {
		AgentChatThread: (props: import("@/chat/components/agent-chat-thread").AgentChatThreadProps) => (
			<SourceTraceView
				trace={{ toolCalls: [], references: [{ kind: "rule-card", id: "ac-01", title: "Лимит риска", asOf: "2026-10-08T09:00:00Z" }] }}
				onRuleHover={props.onRuleHover} onRuleLeave={props.onRuleLeave} onRuleOpen={props.onRuleOpen}
			/>
		),
	};
});

const activeChat: ChatDto = {
	id: "active-1", status: "active",
	params: { constructionId: "7", model: "GLM-5.3", sources: ["journal", "rules-corpus"] },
	createdAt: "2026-10-08T09:00:00Z", lastMessageAt: "2026-10-08T09:00:00Z",
};
const completedChat: ChatDto = { ...activeChat, id: "completed-1", status: "completed", params: { ...activeChat.params, model: "completed-model" } };
const rule = {
	id: "ac-01", title: "Лимит риска", character: "risk-mode", clarity: "crisp", subject: "construction",
	status: "active", scope: "open-constructions", technique: null,
	triggerDescription: "Приближение к лимиту", actionDescription: "Не наращивать **риск**",
	thresholds: [{ name: "limit", value: "1", unit: "%" }], sources: [{ tag: "ПИ", file: "vanna.md", quotes: ["Цитата **источника**"] }],
};

function LocationProbe() {
	return <span aria-label="URL">{useLocation().search}</span>;
}
function renderPanel(search = "?chat=active-1&other=keep") {
	const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
	const view = render(
		<QueryClientProvider client={client}>
			<MemoryRouter initialEntries={[`/constructions/7${search}`]}>
				<ConstructionChatsPanel constructionId={7} isClosed={false} />
				<LocationProbe />
			</MemoryRouter>
		</QueryClientProvider>,
	);
	return { client, unmount: () => view.unmount() };
}

beforeEach(() => {
	vi.resetAllMocks();
	api.listChats.mockImplementation(async (status) => status === "active" ? [activeChat] : [completedChat]);
	api.listChatMessages.mockResolvedValue([]);
	api.readRule.mockResolvedValue(rule);
	api.deleteChat.mockResolvedValue(undefined);
});

// Панель конструкции: жизненный цикл и источники ответа сохраняются в текущем API.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
describe("чаты конструкции", () => {
	it("TryIfCompletedChatsHiddenAndUrlSelectionRestored", async () => {
		// Arrange: открытие и выбор восстановлены из URL.
		// Traceability: doc:.wf-research/ui-concept/concept.md#1-рамка-и-принципы
		const user = userEvent.setup();
		renderPanel();
		// Act: показываем завершённые и выбираем чат.
		expect(await screen.findByLabelText("След источников ответа")).toBeInTheDocument();
		expect(screen.queryByText("completed-model")).not.toBeInTheDocument();
		await user.click(screen.getByRole("button", { name: "Показать завершённые" }));
		await user.click(await screen.findByRole("button", { name: /completed-model/ }));
		// Assert: query сохраняет соседние параметры и выбор.
		expect(screen.getByLabelText("URL")).toHaveTextContent("completedChats=true");
		expect(screen.getByLabelText("URL")).toHaveTextContent("chat=completed-1");
		expect(screen.getByLabelText("URL")).toHaveTextContent("other=keep");
		// Параметр свёрнутости панели ушёл: геометрию задаёт каркас, не панель.
		// Traceability: openspec:ui/screens#scenario-ui-chats-panel-in-context-panel
		expect(screen.getByLabelText("URL")).not.toHaveTextContent("chats=");
		expect(screen.queryByRole("button", { name: "Чаты конструкции" })).not.toBeInTheDocument();
	});

	// На карточке панель чатов развёрнута всегда: контекст есть, каркас панель
	// не сворачивает, URL-параметр ?chats=* больше не нужен.
	// Traceability: openspec:ui/screens#scenario-ui-chats-panel-in-context-panel
	it("TryIfPanelExpandedByDefaultWithoutChatsUrlParam", async () => {
		renderPanel("");
		// Assert: содержимое доступно сразу, без toggle-кнопки и параметров URL.
		expect(await screen.findByRole("button", { name: "Создать чат" })).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Чаты конструкции" })).not.toBeInTheDocument();
		expect(screen.getByLabelText("URL")).toHaveTextContent("");
	});

	// Выбор чата живёт в ?chat= и восстанавливается после повторного монтирования
	// (перезагрузка страницы), так же как остальные query-параметры.
	// Traceability: openspec:ui/screens#scenario-ui-chats-panel-in-context-panel
	it("TryIfChatSelectionSurvivesReloadViaUrl", async () => {
		const { unmount } = renderPanel("?chat=active-1");
		expect(await screen.findByLabelText("След источников ответа")).toBeInTheDocument();
		unmount();
		renderPanel("?chat=active-1");
		// Assert: повторный заход восстанавливает тот же выбранный чат.
		expect(await screen.findByLabelText("След источников ответа")).toBeInTheDocument();
		expect(screen.getByLabelText("URL")).toHaveTextContent("chat=active-1");
		expect(screen.getByLabelText("URL")).not.toHaveTextContent("chats=");
	});

	it("TryIfDeletionRequiresConfirmationAndClearsSelectionAndHistory", async () => {
		// Arrange: удаление обновляет авторитетный список.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		const user = userEvent.setup();
		const { client } = renderPanel();
		await screen.findByLabelText("След источников ответа");
		api.deleteChat.mockImplementation(async () => { api.listChats.mockResolvedValue([]); });
		// Act: сначала отменяем, затем подтверждаем.
		await user.click(screen.getByRole("button", { name: "удалить чат active-1" }));
		expect(api.deleteChat).not.toHaveBeenCalled();
		await user.click(screen.getByRole("button", { name: "Отмена" }));
		await user.click(screen.getByRole("button", { name: "удалить чат active-1" }));
		await user.click(screen.getByRole("button", { name: "Подтвердить удаление" }));
		// Assert: чат исчезает только после успешного DELETE.
		await waitFor(() => expect(screen.queryByRole("button", { name: "удалить чат active-1" })).not.toBeInTheDocument());
		expect(api.deleteChat).toHaveBeenCalledWith("active-1", expect.anything());
		expect(screen.getByLabelText("URL")).not.toHaveTextContent("chat=active-1");
		expect(client.getQueryData(chatKeys.messages("active-1"))).toBeUndefined();
	});

	it("ThrowOnDeleteFailureKeepsChatAndShowsReason", async () => {
		// Arrange: сервер отказал в удалении.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		const user = userEvent.setup();
		api.deleteChat.mockRejectedValue(new Error("HTTP 500"));
		renderPanel();
		await screen.findByRole("button", { name: "удалить чат active-1" });
		// Act: подтверждаем.
		await user.click(screen.getByRole("button", { name: "удалить чат active-1" }));
		await user.click(screen.getByRole("button", { name: "Подтвердить удаление" }));
		// Assert: причина видима, выбор не сброшен.
		expect(await screen.findByRole("alert")).toHaveTextContent("Чат не удалён: HTTP 500");
		expect(screen.getByLabelText("URL")).toHaveTextContent("chat=active-1");
	});

	it.each(["complete", "resume"])("ThrowOnLifecycleFailure_%s", async (action) => {
		// Arrange: оба перехода должны показывать отказ API.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		const user = userEvent.setup();
		api.completeChat.mockRejectedValue(new Error("HTTP 409"));
		api.resumeChat.mockRejectedValue(new Error("HTTP 409"));
		renderPanel("?completedChats=true");
		// Act: завершаем или продолжаем чат.
		const label = action === "complete" ? "завершить чат active-1" : "продолжить чат completed-1";
		await user.click(await screen.findByRole("button", { name: label }));
		// Assert: не выдаём отказ за успешный переход.
		expect(await screen.findByRole("alert")).toHaveTextContent("Статус чата не изменён: HTTP 409");
	});

	it("TryIfCreationSelectsChatWhileListRefetchIsPending", async () => {
		// Arrange: сервер создал чат, но последующая перечитка ещё не закончилась.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		const user = userEvent.setup();
		const created = { ...activeChat, id: "new-chat" };
		api.createChat.mockResolvedValue(created);
		renderPanel();
		await screen.findByRole("button", { name: "Создать чат" });
		api.listChats.mockImplementation(() => new Promise(() => {}));
		// Act: отправка первого сообщения.
		await user.type(screen.getByPlaceholderText("Спросите ИИ-помощника о конструкции…"), "Проверь риск");
		await user.click(screen.getByRole("button", { name: "Создать чат" }));
		// Assert: кеш уже содержит DTO нового чата, выбор не ждёт refetch.
		expect(await screen.findByRole("button", { name: "удалить чат new-chat" })).toBeInTheDocument();
		expect(screen.getByLabelText("URL")).toHaveTextContent("chat=new-chat");
		expect(api.createChat).toHaveBeenCalledWith("Проверь риск", expect.objectContaining({ constructionId: "7" }));
	});

	it("TryIfRuleSourceHoverAndOpenReadFullCard", async () => {
		// Arrange: настоящий чип источника вызывает callbacks панели.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		const user = userEvent.setup();
		renderPanel();
		const source = await screen.findByRole("button", { name: /Правило · Лимит риска/ });
		// Act: hover, leave и открытие полной карточки.
		await user.hover(source);
		expect(await screen.findByLabelText("Карточка правила (hover из чата)")).toHaveTextContent("Не наращивать риск");
		await user.unhover(source);
		expect(screen.queryByLabelText("Карточка правила (hover из чата)")).not.toBeInTheDocument();
		await user.click(source);
		// Assert: полные данные читаются из readRule, а не из подписи чипа.
		const card = await screen.findByLabelText("Полная карточка правила");
		expect(await within(card).findByText("Триггер:")).toBeInTheDocument();
		expect(card).toHaveTextContent("limit: 1 %");
		expect(card).toHaveTextContent("vanna.md");
		// Описания и цитаты проходят безопасный Markdown-рендер.
		// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
		expect(within(card).getByText("риск", { selector: "strong" })).toBeInTheDocument();
		expect(within(card).getByText("источника", { selector: "strong" })).toBeInTheDocument();
		expect(within(card).getByText("Статус:")).toBeInTheDocument();
		// Ссылка корне-относительная: SPA живёт в корне хоста без префикса /spa.
		// Traceability: adr:docs/adr/0013-spa-served-from-host-root.md#fixed-mechanics
		expect(within(card).getByRole("link", { name: "Открыть в каталоге в новом окне" }))
			.toHaveAttribute("href", "/agent?rule=ac-01");
		expect(api.readRule).toHaveBeenCalledWith("ac-01", expect.any(AbortSignal));
	});

	it("ThrowOnRuleReadFailureShowsLoadingThenError", async () => {
		// Arrange: чтение полной карточки задержано и затем отклонено.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		let reject!: (error: Error) => void;
		api.readRule.mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
		const user = userEvent.setup();
		renderPanel();
		// Act: источник открывается до ответа API.
		await user.click(await screen.findByRole("button", { name: /Правило · Лимит риска/ }));
		expect(screen.getByRole("status")).toHaveTextContent("Чтение полной карточки правила");
		reject(new Error("HTTP 404"));
		// Assert: неизвестное правило не превращается в пустую карточку.
		expect(await screen.findByRole("alert")).toHaveTextContent("Правило недоступно: HTTP 404");
	});

	it("TryIfLoadingChatsDoesNotLookEmpty", async () => {
		// Arrange: список ещё не получен.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		api.listChats.mockImplementation(() => new Promise(() => {}));
		renderPanel();
		// Assert: явная загрузка вместо ложной пустой истории.
		expect(screen.getByRole("status")).toHaveTextContent("Загрузка чатов конструкции");
		expect(screen.queryByText(/Чатов конструкции ещё нет/)).not.toBeInTheDocument();
	});

	it("ThrowOnHistoryFailureDoesNotShowEmptyThread", async () => {
		// Arrange: список есть, история недоступна.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		api.listChatMessages.mockRejectedValue(new Error("HTTP 503"));
		renderPanel();
		// Assert: отдельная ошибка истории, а не приглашение задать первый вопрос.
		expect(await screen.findByRole("alert")).toHaveTextContent("История чата недоступна: HTTP 503");
		expect(screen.queryByLabelText("След источников ответа")).not.toBeInTheDocument();
	});
});
