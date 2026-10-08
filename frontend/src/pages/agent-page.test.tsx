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
		// Источники данных — группа чипов вместо чекбокс-филдсета (§3.4:2).
		expect(screen.getByRole("group", { name: /источники данных/i })).toBeInTheDocument();
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

	/*
		Форма нового чата оформлена по мастеру Body #3 — композеру Z14sH
		(§3.4:2 аудита): модель — чип aF5vM (surface2, радиус 8, [5,10],
		12/500, шеврон), привязка конструкции — чип bCLlQ (infoSoft, пилюля
		999, иконка link $info), сообщение — 13.5/normal c плейсхолдером
		мастера; источники — чипы «Чип/Источник» с переключением выбора.
	*/
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	// Traceability: change:reconcile-frontend-with-design/design#D2
	it("оформляет поля формы нового чата по дизайн-нодам композера Body #3", async () => {
		// Act: рендер вкладки «Чаты» с формой по умолчанию.
		renderPage();

		// Assert: модель — чип aF5vM: surface2, радиус 8, паддинги [5,10], 12/500.
		const modelSelect = await screen.findByLabelText(/модель/i);
		expect(modelSelect.className).toContain("bg-secondary");
		expect(modelSelect.className).toContain("rounded-sm");
		expect(modelSelect.className).toContain("px-2.5");
		expect(modelSelect.className).toContain("py-[5px]");
		expect(modelSelect.className).toContain("text-[12px]");
		expect(modelSelect.className).toContain("font-medium");
		expect(modelSelect.closest("span")?.querySelector("svg")?.getAttribute("class")).toContain("text-text-muted");

		// Assert: конструкция — чип bCLlQ: infoSoft, пилюля 999, link $info.
		const constructionInput = screen.getByLabelText(/опциональная конструкция/i);
		expect(constructionInput.className).toContain("bg-info-soft");
		expect(constructionInput.className).toContain("rounded-full");
		expect(constructionInput.className).toContain("text-[12px]");
		expect(constructionInput.closest("span")?.querySelector("svg")?.getAttribute("class")).toContain("text-info");

		// Assert: первое сообщение — плейсхолдер dkwDP мастера: 13.5/normal.
		const messageField = screen.getByLabelText(/первое сообщение/i);
		expect(messageField.className).toContain("text-[13.5px]");
		expect(messageField.getAttribute("placeholder")).toBe("Спросите агента о конструкции, правилах или рынке…");
	});

	it("переключает выбор источников чипами вместо чекбоксов (§3.4:2)", async () => {
		// Arrange: чат-источники по умолчанию выбраны (инвариант справочника).
		const user = userEvent.setup();
		renderPage();

		// Assert: все три чипа — toggle-кнопки в выбранном состоянии мастера.
		const journalChip = await screen.findByRole("button", { name: "журнал" });
		const rulesChip = screen.getByRole("button", { name: "корпус правил" });
		const marketChip = screen.getByRole("button", { name: "рынок Bybit" });
		for (const chip of [journalChip, rulesChip, marketChip]) {
			expect(chip.getAttribute("aria-pressed")).toBe("true");
			expect(chip.querySelector('[data-slot="source-chip"]')?.getAttribute("data-selected")).toBe("true");
		}
		expect(screen.queryByRole("checkbox")).toBeNull();

		// Act: снятие выбора рынка кликом по чипу.
		await user.click(marketChip);

		// Assert: чип погашен до textMuted, выбор снят.
		expect(marketChip.getAttribute("aria-pressed")).toBe("false");
		expect(marketChip.querySelector('[data-slot="source-chip"]')?.getAttribute("data-selected")).toBe("false");

		// Act: повторный клик возвращает выбор.
		await user.click(marketChip);
		expect(marketChip.getAttribute("aria-pressed")).toBe("true");
	});

	it("создаёт чат первым сообщением с выбранными источниками", async () => {
		const user = userEvent.setup();
		renderPage();

		// Act: снимаем рынок Bybit, вводим сообщение и создаём чат.
		await user.click(await screen.findByRole("button", { name: "рынок Bybit" }));
		await user.type(screen.getByLabelText(/первое сообщение/i), "Собери сводку по лимитам");
		await user.click(screen.getByRole("button", { name: /создать чат/i }));

		// Assert: в создании чата остались только выбранные источники.
		await waitFor(() =>
			expect(chatsApi.createChat).toHaveBeenCalledWith("Собери сводку по лимитам", {
				model: "GLM-5.3",
				constructionId: null,
				sources: ["journal", "rules-corpus"],
			}),
		);
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
