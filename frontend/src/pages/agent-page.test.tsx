import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AgentPage } from "./agent-page";

// Раздел «Агент» по концепции §7: форма нового чата, списки active/completed,
// лента выбранного чата и read-only каталог правил с фильтрами и поиском.
// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

const chatsApi = vi.hoisted(() => ({
	listChats: vi.fn(),
	createChat: vi.fn(),
	completeChat: vi.fn(),
	resumeChat: vi.fn(),
	listChatMessages: vi.fn(),
	streamChatReply: vi.fn(),
}));

const rulesApi = vi.hoisted(() => ({
	listRules: vi.fn(),
}));

vi.mock("@/chat/api/chat-api", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/chat/api/chat-api")>()),
	listChats: chatsApi.listChats,
	createChat: chatsApi.createChat,
	completeChat: chatsApi.completeChat,
	resumeChat: chatsApi.resumeChat,
	listChatMessages: chatsApi.listChatMessages,
	streamChatReply: chatsApi.streamChatReply,
}));

vi.mock("@/lib/api/agent-rules", () => ({
	listRules: rulesApi.listRules,
}));

function renderPage() {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<AgentPage />
		</QueryClientProvider>,
	);
}

beforeEach(() => {
	vi.clearAllMocks();
	chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
		status === "active"
			? [
					{
						id: "chat-1",
						status: "active",
						params: { model: "GLM-5.3", constructionId: null, sources: ["journal", "rules-corpus", "market"] },
						createdAt: "2026-10-08T09:00:00Z",
						lastMessageAt: "2026-10-08T09:01:00Z",
					},
				]
			: [],
	);
	chatsApi.listChatMessages.mockResolvedValue([]);
	chatsApi.createChat.mockResolvedValue({
		id: "chat-new",
		status: "active",
		params: { model: "GLM-5.3", constructionId: null, sources: ["journal", "rules-corpus", "market"] },
		createdAt: "2026-10-08T09:02:00Z",
		lastMessageAt: "2026-10-08T09:02:00Z",
	});
	chatsApi.completeChat.mockResolvedValue({ id: "chat-1", status: "completed" });
	chatsApi.resumeChat.mockResolvedValue({ id: "chat-1", status: "active" });

	rulesApi.listRules.mockResolvedValue({
		items: [
			{
				id: "ac-01",
				title: "Лимиты риска на период",
				character: "risk-mode",
				clarity: "crisp",
				subject: "construction",
			},
		],
		total: 1,
	});
});

describe("страница агента", () => {
	it("показывает форму нового чата, историю и каталог правил", async () => {
		const user = userEvent.setup();
		renderPage();

		expect(await screen.findByRole("heading", { name: "Агент" })).toBeInTheDocument();
		// Титул раздела — Inter 21/600 дизайн-системы, не дефолтный text-2xl.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		expect(screen.getByRole("heading", { name: "Агент", level: 1 }).className).toContain("page-title");
		expect(screen.getByLabelText(/модель/i)).toBeInTheDocument();
		expect(screen.getByLabelText(/источники данных/i)).toBeInTheDocument();
		expect(await screen.findByRole("heading", { name: /активные/i })).toBeInTheDocument();
		expect(screen.getAllByRole("button", { name: /завершить чат/i }).length).toBeGreaterThan(0);
		await user.click(screen.getByRole("button", { name: /правила/i }));
		expect(await screen.findByText(/Лимиты риска на период/i)).toBeInTheDocument();
	});

	it("рендерит сессии примитивом SessionItem, сохраняя выбор и завершение (§3.4:1)", async () => {
		// Arrange: активный чат один; завершённый — с другой моделью, чтобы
		// пункты списка различимы по доступному имени.
		chatsApi.listChats.mockImplementation(async (status: "active" | "completed") =>
			status === "active"
				? [
						{
							id: "chat-1",
							status: "active",
							params: { model: "GLM-5.3", constructionId: null, sources: ["journal", "rules-corpus", "market"] },
							createdAt: "2026-10-08T09:00:00Z",
							lastMessageAt: "2026-10-08T09:01:00Z",
						},
					]
				: [
						{
							id: "chat-2",
							status: "completed",
							params: { model: "glm-5.3-flash", constructionId: null, sources: ["journal"] },
							createdAt: "2026-10-07T09:00:00Z",
							lastMessageAt: "2026-10-07T09:30:00Z",
						},
					],
		);
		const user = userEvent.setup();
		renderPage();

		// Assert: активная сессия — пункт мастера s9J3h (инстанс Cur):
		// заливка surface2 и доступная метка текущей сессии.
		const activeItem = await screen.findByRole("button", { name: /GLM-5\.3/ });
		expect(activeItem.getAttribute("aria-current")).toBe("true");
		expect(activeItem.className).toContain("bg-surface-2");
		expect(activeItem.className).toContain("rounded-md");

		// Assert: время последнего сообщения видно в пункте (нода SqhBW).
		expect(activeItem.textContent).toMatch(/\d{4}-\d{2}-\d{2} \d{2}:\d{2}/);

		// Assert: завершённая сессия — тем же примитивом, без активной заливки.
		const completedItem = screen.getByRole("button", { name: /glm-5\.3-flash/ });
		expect(completedItem.className).not.toContain("bg-surface-2");

		// Act: выбор завершённой сессии кликом по пункту.
		await user.click(completedItem);

		// Assert: выбранная сессия стала текущей (инстанс Cur).
		await waitFor(() => {
			expect(completedItem.getAttribute("aria-current")).toBe("true");
		});

		// Act: завершение активного чата кнопкой из списка.
		await user.click(screen.getByRole("button", { name: /завершить чат/i }));

		// Assert: команда завершения ушла выбранной сессии.
		await waitFor(() => {
			expect(chatsApi.completeChat).toHaveBeenCalledWith("chat-1");
		});
	});

	it("создаёт чат первым сообщением через форму", async () => {
		const user = userEvent.setup();
		renderPage();

		await user.type(await screen.findByLabelText(/первое сообщение/i), "Собери сводку по лимитам");
		await user.click(screen.getByRole("button", { name: /создать чат/i }));

		await waitFor(() => expect(chatsApi.createChat).toHaveBeenCalled());
	});

	it("применяет фильтры каталога правил и поиск", async () => {
		const user = userEvent.setup();
		renderPage();
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		await user.selectOptions(await screen.findByLabelText(/характер действия/i), "risk-mode");
		await user.type(screen.getByLabelText(/поиск правил/i), "лимит");

		await waitFor(() => {
			expect(rulesApi.listRules).toHaveBeenCalledWith(
				expect.objectContaining({ character: "risk-mode", search: "лимит" }),
				expect.anything(),
			);
		});
	});
});
