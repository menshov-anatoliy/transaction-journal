import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
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
	readRule: vi.fn(),
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
	readRule: rulesApi.readRule,
}));

function renderPage(path = "/agent") {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<MemoryRouter initialEntries={[path]}>
				<AgentPage />
			</MemoryRouter>
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

	// Полная карточка правила (GET /rules/{id}) для тела карточек каталога,
	// правой панели и краткого попапа из чата.
	rulesApi.readRule.mockImplementation(async (ruleId: string) => ({
		id: ruleId,
		title: "Лимиты риска на период",
		character: "risk-mode",
		clarity: "crisp",
		subject: "construction",
		status: "active",
		scope: "open-constructions",
		technique: null,
		triggerDescription: "Убыток недели приближается к лимиту периода 1% капитала.",
		actionDescription: "Остановить наращивание риска до конца периода.",
		thresholds: [{ name: "weeklyRiskLimit", value: "1", unit: "percent" }],
		sources: [{ tag: "ПИ", file: "vanna.md", quotes: ["лимит недели"] }],
	}));
});

describe("страница агента", () => {
	// Переход из источника ответа в новом окне раскрывает именно выбранное правило.
	// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
	it("TryIfRuleLinkOpensCatalogWithFullCard", async () => {
		renderPage("/agent?rule=ac-01");
		expect(await screen.findByText("Триггер:")).toBeInTheDocument();
		expect(screen.getByText("лимит недели")).toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Правила" })).toHaveAttribute("aria-pressed", "true");
		expect(rulesApi.readRule).toHaveBeenCalledWith("ac-01", expect.any(AbortSignal));
	});

	// Недоступность источника по прямой ссылке не маскируется приглашением выбрать правило.
	// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
	it("ThrowOnRuleLinkReadFailure", async () => {
		rulesApi.readRule.mockRejectedValue(new Error("HTTP 404"));
		renderPage("/agent?rule=missing");
		expect(await screen.findByRole("alert")).toHaveTextContent("Правило недоступно: HTTP 404");
		expect(screen.queryByText("Выберите карточку правила в списке.")).not.toBeInTheDocument();
	});
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

		// Act: выбор характера чипом-пилюлей мастера (фильтры Body #7 —
		// группы чипов viuZI вместо нативных select).
		await user.click(await screen.findByRole("button", { name: "лимиты и режим риска" }));
		await user.type(screen.getByLabelText(/поиск правил/i), "лимит");

		await waitFor(() => {
			expect(rulesApi.listRules).toHaveBeenCalledWith(
				expect.objectContaining({ character: "risk-mode", search: "лимит" }),
				expect.anything(),
			);
		});
	});

	/*
		Вкладка «Правила» оформлена по мастеру Body #7 (R3kdzS) design.pen
		(§3.8 аудита): табы — сегмент-контейнер z5Fqgz ($surface, r9, [3],
		gap 4) с активным табом accentSoft/accentStrong r7 [6,14] 12.5/600
		и счётчиком «корпус · N … · только чтение» (Cnt zYZRw); поиск —
		Q7Ur0 ($surface, r10, [10,14], иконка search 15 $textMuted, плейс-
		холдер 13); фильтры — группы чипов viuZI (метка 11.5 $textMuted +
		пилюли 999 [4,10] 11.5: выбранный accentSoft/accentStrong 600,
		невыбранный $surface/$border $textSecondary); каталог — сетка cRRlh
		рядами по 2 с гэпом 12; карточка — «Карточка правила/Полная» Zes7z.
	*/
	// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	// Traceability: change:reconcile-frontend-with-design/design#D2
	it("оформляет вкладку правил по мастеру Body #7: табы, счётчик, поиск, фильтры-чипы", async () => {
		// Arrange: в каталоге одно правило (total 1) — счётчик в единственном
		// числе «1 правило».
		const user = userEvent.setup();
		renderPage();
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		// Assert: активный таб «Правила» — заливка accentSoft секции мастера.
		const rulesTab = screen.getByRole("button", { name: "Правила" });
		expect(rulesTab.className).toContain("bg-accent-soft");
		expect(rulesTab.className).toContain("text-accent-strong");
		expect(rulesTab.className).toContain("font-semibold");
		expect(rulesTab.className).toContain("rounded-[7px]");

		// Assert: неактивный таб погашен до $textSecondary.
		const chatsTab = screen.getByRole("button", { name: "Чаты" });
		expect(chatsTab.className).toContain("text-text-secondary");
		expect(chatsTab.className).not.toContain("bg-accent-soft");

		// Assert: счётчик корпуса (нода Cnt zYZRw): 12/normal $textMuted.
		const counter = await screen.findByText(/корпус · 1 правило · только чтение/i);
		expect(counter.className).toContain("text-text-muted");
		expect(counter.className).toContain("text-xs");

		// Assert: поиск — поле мастера Q7Ur0: r10, [10,14], иконка search
		// 15×15 $textMuted, плейсхолдер 13 $textMuted.
		const searchField = screen.getByLabelText(/поиск правил/i);
		expect(searchField.className).toContain("text-[13px]");
		expect(searchField.getAttribute("placeholder")).toBe("Поиск по корпусу правил…");
		const searchIcon = searchField.parentElement?.querySelector("svg");
		expect(searchIcon?.getAttribute("class")).toContain("text-text-muted");

		// Assert: фильтры — пилюли 999 [4,10] 11.5; выбранные «Все» (по
		// одному в каждой группе) несут accentSoft/accentStrong — секции
		// accentSoft мастера.
		const allChips = screen.getAllByRole("button", { name: /^Все$/ });
		expect(allChips.length).toBe(3);
		for (const chip of allChips) {
			expect(chip.getAttribute("aria-pressed")).toBe("true");
			expect(chip.className).toContain("rounded-full");
			expect(chip.className).toContain("bg-accent-soft");
			expect(chip.className).toContain("text-accent-strong");
		}

		// Assert: невыбранный чип характера — $surface/$border $textSecondary.
		const characterChip = screen.getByRole("button", { name: "лимиты и режим риска" });
		expect(characterChip.getAttribute("aria-pressed")).toBe("false");
		expect(characterChip.className).toContain("bg-card");
		expect(characterChip.className).toContain("text-text-secondary");
	});

	it("рендерит карточки каталога правил по ноде Zes7z: пилюли-атрибуты 999, тело, футер", async () => {
		const user = userEvent.setup();
		renderPage();
		await user.click(await screen.findByRole("button", { name: /правила/i }));

		// Assert: карточка «Карточка правила/Полная» (Zes7z): $surface,
		// кайма $border, r12, паддинги 18, гэп 12.
		const card = await screen.findByText(/Лимиты риска на период/i, { selector: "button" });
		const article = card.closest("article");
		expect(article).not.toBeNull();
		expect(article?.className).toContain("rounded-lg");
		expect(article?.className).toContain("border");
		expect(article?.className).toContain("bg-card");
		expect(article?.className).toContain("p-[18px]");
		expect(article?.className).toContain("gap-3");

		// Assert: идентификатор правила — 11/normal $textMuted (нода Id NLBwE).
		expect(within(article!).getByText("ac-01").className).toContain("text-text-muted");

		// Assert: пилюли-атрибуты — примитив «Чип/Статус» (aa6cK, r999
		// [4,10] 11.5/500): характер — пара $riskSoft/$risk (инстанс
		// nrPzY «Мягкое» Body #7), чёткость — $infoSoft/$info (U6mVZ),
		// субъект — surface2/$textSecondary (u9Vm0h).
		const characterPill = within(article!).getByText("лимиты и режим риска");
		expect(characterPill.className).toContain("rounded-full");
		expect(characterPill.className).toContain("bg-risk-soft");
		expect(characterPill.className).toContain("text-risk");
		const clarityPill = within(article!).getByText("Однозначное");
		expect(clarityPill.className).toContain("bg-info-soft");
		expect(clarityPill.className).toContain("text-info");
		const subjectPill = within(article!).getByText("Субъект: конструкция");
		expect(subjectPill.className).toContain("bg-surface-2");
		expect(subjectPill.className).toContain("text-text-secondary");

		// Assert: тело карточки (нода Body GjcwT: 13/normal $textSecondary,
		// межстрочный 1.55) приходит полной карточкой GET /rules/{id}.
		const body = await within(article!).findByText(/Остановить наращивание риска до конца периода/i);
		expect(body.closest("div")?.className).toContain("text-[13px]");
		expect(body.closest("div")?.className).toContain("leading-[1.55]");
		expect(body.closest("div")?.className).toContain("text-text-secondary");

		// Assert: футер (p4Ot2): иконка external-link 12 $accentStrong и
		// ссылка «Открыть в каталоге в новом окне» 12 $accentStrong.
		const footer = article!.querySelector("footer");
		const openLink = within(footer as HTMLElement).getByRole("link", { name: /открыть в каталоге в новом окне/i });
		expect(openLink.className).toContain("text-accent-strong");
		expect(openLink.className).toContain("text-xs");
		expect(footer?.querySelector("svg")?.getAttribute("class")).toContain("text-accent-strong");

		// Act: клик по заголовку открывает правило в правой панели
		// (поведение сохранено).
		await user.click(card);

		// Assert: правая панель несёт полную карточку с порогами.
		await waitFor(() => {
			expect(rulesApi.readRule).toHaveBeenCalledWith("ac-01", expect.anything());
		});
	});

	it("показывает краткий попап правила по ноде Lpcap при наведении в следе источников (§3.5:7)", async () => {
		// Arrange: активный чат с ответом ассистента, ссылающимся на
		// карточку правила в следе источников.
		chatsApi.listChatMessages.mockResolvedValue([
			{
				id: "m2",
				role: "assistant",
				text: "Ответ по лимитам риска",
				asOf: "2026-10-08T09:01:00Z",
				sourceTrace: {
					toolCalls: [],
					references: [{ kind: "rule-card", id: "ac-01", title: "Лимиты риска на период", asOf: "2026-10-08T09:00:00Z" }],
				},
			},
		]);
		const user = userEvent.setup();
		renderPage();

		// Act: наведение на ссылку карточки правила в следе источников.
		const ruleRef = await screen.findByRole("button", { name: /Лимиты риска на период/ });
		await user.hover(ruleRef);

		// Assert: попап «Попап правила/Краткий» (Lpcap): ширина 280, r10,
		// тень мастера 0 8 24 #17171E20, паддинги 12, гэп 7.
		const popup = await screen.findByLabelText(/карточка правила \(hover из чата\)/i);
		expect(popup.className).toContain("w-[280px]");
		expect(popup.className).toContain("rounded-[10px]");
		expect(popup.className).toContain("shadow-[0_8px_24px]");
		expect(popup.className).toContain("shadow-text-primary/12");
		expect(popup.className).toContain("p-3");
		expect(popup.className).toContain("gap-[7px]");

		// Assert: заголовок 12.5/600 $textPrimary, мета 11 $textMuted,
		// суть 12/1.45 $textSecondary (ноды oYr9W/rlwtL/lidNU).
		const popupTitle = screen.getByText("Лимиты риска на период", { selector: "p" });
		expect(popupTitle.className).toContain("text-[12.5px]");
		expect(popupTitle.className).toContain("font-semibold");
		const metaLine = screen.getByText(/лимиты и режим риска · Однозначное · конструкция · ac-01/i);
		expect(metaLine.className).toContain("text-[11px]");
		expect(metaLine.className).toContain("text-text-muted");
		const gist = screen.getByText(/Остановить наращивание риска/i);
		expect(gist.closest("div")?.className).toContain("text-xs");
		expect(gist.closest("div")?.className).toContain("leading-[1.45]");
	});
});
