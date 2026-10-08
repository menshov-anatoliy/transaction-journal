import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { HintsPage } from "./hints-page";

// Экран «Подсказки» проверяется как read-only журнал всех субъектов: фильтры
// статус/группа/характер, ссылки на конструкции и трасса источников. Разметка
// сверена с мастером Body #6 (LmLr6): сетка карточек «Карточка подсказки»
// (H0y2H) вместо таблицы, фильтры — сегмент-контрол + поля-дропдауны (ccdID).
// Traceability: doc:.wf-research/ui-concept/concept.md#6-раздел-подсказки-маршрут-hints
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

vi.mock("@/lib/api/hints", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/hints")>()),
	fetchHintsLog: vi.fn(),
}));

const api = vi.mocked(await import("@/lib/api/hints"));

function renderPage() {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<MemoryRouter initialEntries={["/hints"]}>
				<Routes>
					<Route path="/hints" element={<HintsPage />} />
					<Route path="/constructions/:constructionId" element={<h2>Карточка конструкции</h2>} />
				</Routes>
			</MemoryRouter>
		</QueryClientProvider>,
	);
}

beforeEach(() => {
	vi.clearAllMocks();
	api.fetchHintsLog.mockResolvedValue({
		items: [
			{
				id: 71,
				ruleId: "risk-limit-week",
				subject: { kind: "journal", constructionId: null },
				group: { id: "risk-mode", title: "Риск-режим" },
				character: "risk-mode",
				clarity: "crisp",
				sources: [{ tag: "ПИ", file: "risk/limits.md", quotes: ["Лимит риска недели"] }],
				text: "Лимит риска недели достигнут",
				facts: { weekPnl: "2.1%" },
				asOf: "2026-10-08T09:30:00Z",
				status: "new",
				firstSeenAt: null,
			},
			{
				id: 72,
				ruleId: "futures-bias",
				subject: { kind: "construction", constructionId: 7 },
				group: { id: "futures-leg", title: "Фьючерсная нога" },
				character: "futures-leg",
				clarity: "fuzzy",
				sources: [],
				text: "Снизить плечо фьючерсной ноги",
				facts: {},
				asOf: "2026-10-08T09:00:00Z",
				status: "applied",
				firstSeenAt: "2026-10-08T09:05:00Z",
			},
			{
				id: 73,
				ruleId: "composition-whitelist",
				subject: { kind: "journal", constructionId: null },
				group: { id: "construction-management", title: "Управление конструкцией" },
				character: "other",
				clarity: "fuzzy",
				sources: [],
				text: "Инструмент вне справочника допустимых",
				facts: {},
				asOf: "2026-10-07T18:20:00Z",
				status: "dismissed",
				firstSeenAt: "2026-10-07T18:25:00Z",
			},
			{
				id: 74,
				ruleId: "roll-calendar",
				subject: { kind: "journal", constructionId: null },
				group: { id: "construction-management", title: "Управление конструкцией" },
				character: "rolling",
				clarity: "crisp",
				sources: [],
				text: "Пропущен плановый ролл",
				facts: {},
				asOf: "2026-10-06T12:00:00Z",
				status: "expired",
				firstSeenAt: "2026-10-06T12:05:00Z",
			},
		],
		total: 4,
		limit: 200,
		offset: 0,
	});
});

describe("раздел «Подсказки»", () => {
	it("показывает журнал всех субъектов, ссылку на конструкцию и источники", async () => {
		renderPage();

		expect(await screen.findByRole("heading", { level: 1, name: "Подсказки" })).toBeInTheDocument();
		// Титул раздела — Inter 21/600 дизайн-системы, не дефолтный text-2xl.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		expect(screen.getByRole("heading", { level: 1, name: "Подсказки" }).className).toContain("page-title");
		expect(await screen.findByText("Лимит риска недели достигнут")).toBeInTheDocument();
		expect(screen.getByText("Снизить плечо фьючерсной ноги")).toBeInTheDocument();
		expect(screen.getByRole("link", { name: "конструкция 7" })).toHaveAttribute("href", "/constructions/7");
		expect(screen.getByText("risk/limits.md")).toBeInTheDocument();
		expect(screen.getByText("Лимит риска недели")).toBeInTheDocument();
		// Счётчик шапки мастера (pdFeV): 12.5/normal $textSecondary — счётчик
		// выборки живёт в том же слоте рядом с титулом.
		expect(screen.getByText("журнал всех субъектов · показано 4 из 4")).toBeInTheDocument();
	});

	it("рендерит карточки по мастеру Body #6 вместо таблицы", async () => {
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		// Arrange: журнал — сетка карточек «Карточка подсказки» (H0y2H):
		// $surface, кайма $border, радиус 12, паддинги 14, гэп 8.
		// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
		const cards = screen.getAllByRole("article");
		expect(cards).toHaveLength(4);

		const card = cards[0];
		expect(card.className).toContain("rounded-lg");
		expect(card.className).toContain("border");
		expect(card.className).toContain("bg-card");
		expect(card.className).toContain("p-3.5");
		expect(card.className).toContain("gap-2");
		// Сетка списка (SVwot): ряды по две карточки, гэп 12.
		expect(document.querySelector("ul.grid")?.className).toContain("sm:grid-cols-2");
		expect(document.querySelector("ul.grid")?.className).toContain("gap-3");

		// Assert: шапка карточки — группа Inter 12.5/600 + чип статуса.
		expect(screen.getByRole("heading", { level: 3, name: "Риск-режим" }).className).toContain("text-[12.5px]");
		expect(screen.getByRole("heading", { level: 3, name: "Риск-режим" }).className).toContain("font-semibold");
		// Чип ищется внутри карточки: такой же текст несёт таб фильтра статуса.
		const liveChip = within(cards[0]).getByText("живая");
		expect(liveChip.className).toContain("bg-accent-soft");
		expect(liveChip.className).toContain("text-accent-strong");
	});

	it("тон чипа статуса следует инстансам мастера: применена pos, отклонена neg, погашена muted", async () => {
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		// Мастер Body #6: «Применено» — accentSoft/accentStrong, «Отклонено» —
		// negSoft/neg; «погашена» без мастер-ноды — приглушённый muted.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		const cards = screen.getAllByRole("article");
		const appliedChip = within(cards[1]).getByText("применена");
		const dismissedChip = within(cards[2]).getByText("отклонена");
		const expiredChip = within(cards[3]).getByText("погашена");
		expect(appliedChip.className).toContain("bg-accent-soft");
		expect(dismissedChip.className).toContain("bg-neg-soft");
		expect(dismissedChip.className).toContain("text-neg");
		expect(expiredChip.className).toContain("bg-surface-2");
		expect(expiredChip.className).toContain("text-text-muted");
	});

	it("источники карточки печатаются Inter 11 ($font), а не font-mono", async () => {
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		// Arrange: файл источника — списочная типографика мастера (11/normal
		// $textMuted); моно в дизайн-системе живёт только в MD-редакторе.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		const file = screen.getByText("risk/limits.md");

		// Assert: кегль 11 задан блоком списка, моноширинного класса нет.
		expect(file.closest("ul")?.className).toContain("text-[11px]");
		expect(file.closest("ul")?.className).toContain("text-text-muted");
		expect(file.className).not.toContain("font-mono");
		expect(document.querySelector("[data-slot='hint-card'] .font-mono")).toBeNull();
	});

	it("показывает сегмент-контрол статуса геометрией мастера", async () => {
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		// Arrange: контейнер btUq8 — $surface, r9, кайма $border, [3];
		// таб «Все» активен по умолчанию (accentSoft/accentStrong 12/600).
		// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
		const group = screen.getByRole("radiogroup", { name: "Фильтр по статусу" });
		expect(group.className).toContain("rounded-[9px]");
		expect(group.className).toContain("border");
		expect(group.className).toContain("bg-card");
		expect(group.className).toContain("p-[3px]");

		const allTab = screen.getByRole("radio", { name: "Все" });
		expect(allTab).toHaveAttribute("aria-checked", "true");
		expect(allTab.className).toContain("bg-accent-soft");
		expect(allTab.className).toContain("text-accent-strong");
		expect(allTab.className).toContain("font-semibold");

		const appliedTab = screen.getByRole("radio", { name: "применена" });
		expect(appliedTab).toHaveAttribute("aria-checked", "false");
		expect(appliedTab.className).toContain("text-text-secondary");
	});

	it("поля группы и характера несут геометрию поля мастера с шевроном", async () => {
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		// Ноды wgKkr/XyeI5: $surface, r8, [7,10], Inter 12, шеврон 13×13
		// $textMuted; нативная стрелка select скрыта (appearance-none).
		// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
		const groupSelect = screen.getByLabelText("Фильтр по группе");
		expect(groupSelect.className).toContain("appearance-none");
		expect(groupSelect.className).toContain("rounded-sm");
		expect(groupSelect.className).toContain("py-[7px]");
		expect(groupSelect.className).toContain("text-xs");
		expect(groupSelect).toHaveTextContent("Группа: все");
		expect(screen.getByLabelText("Фильтр по характеру")).toHaveTextContent("Характер: все");
	});

	it("передаёт фильтры в API и сужает характеры выбранной группой", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		await user.click(screen.getByRole("radio", { name: "живая" }));
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", limit: 200 }));
		// Активность перешла на таб «живая» сегмент-контрола.
		expect(screen.getByRole("radio", { name: "живая" })).toHaveAttribute("aria-checked", "true");
		expect(screen.getByRole("radio", { name: "Все" })).toHaveAttribute("aria-checked", "false");

		await user.selectOptions(screen.getByLabelText("Фильтр по группе"), "futures-leg");
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", group: "futures-leg", limit: 200 }));

		const characterSelect = screen.getByLabelText("Фильтр по характеру");
		expect(characterSelect).toHaveTextContent("Характер: все");
		expect(characterSelect).not.toHaveTextContent("Характер: лимиты и режим риска");

		await user.selectOptions(characterSelect, "futures-leg");
		await waitFor(() =>
			expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", group: "futures-leg", character: "futures-leg", limit: 200 }),
		);
	});

	it("сбрасывает фильтры кнопкой Ghost мастера и возвращает таб «Все»", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		// Arrange: активный фильтр открывает Ghost-кнопку «Сбросить фильтры»
		// (ciwcY: [6,10], 12) — тон textSecondary примитива Ghost.
		await user.click(screen.getByRole("radio", { name: "отклонена" }));
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "dismissed", limit: 200 }));
		const reset = screen.getByRole("button", { name: "Сбросить фильтры" });
		expect(reset.className).toContain("text-text-secondary");

		// Act: сброс возвращает исходное состояние всех трёх фильтров.
		await user.click(reset);

		// Assert: запрос уходит без фильтров, таб «Все» снова активен.
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ limit: 200 }));
		expect(screen.getByRole("radio", { name: "Все" })).toHaveAttribute("aria-checked", "true");
	});

	it("отличает пустой журнал от пустого результата фильтра", async () => {
		api.fetchHintsLog
			.mockResolvedValueOnce({ items: [], total: 0, limit: 200, offset: 0 })
			.mockResolvedValueOnce({ items: [], total: 0, limit: 200, offset: 0 });
		renderPage();
		expect(await screen.findByText("записей нет")).toBeInTheDocument();

		const user = userEvent.setup();
		await user.click(screen.getByRole("radio", { name: "живая" }));
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", limit: 200 }));
		expect(await screen.findByText("по фильтру записей нет")).toBeInTheDocument();
	});
});
