import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { HintsPage } from "./hints-page";

// Экран «Подсказки» проверяется как read-only журнал всех субъектов: фильтры
// статус/группа/характер, ссылки на конструкции и трасса источников.
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
		],
		total: 2,
		limit: 200,
		offset: 0,
	});
});

describe("раздел «Подсказки»", () => {
	it("показывает журнал всех субъектов, ссылку на конструкцию и источники", async () => {
		renderPage();

		expect(await screen.findByRole("heading", { level: 1, name: "Подсказки" })).toBeInTheDocument();
		expect(await screen.findByText("Лимит риска недели достигнут")).toBeInTheDocument();
		expect(screen.getByText("Снизить плечо фьючерсной ноги")).toBeInTheDocument();
		expect(screen.getByRole("link", { name: "конструкция 7" })).toHaveAttribute("href", "/constructions/7");
		expect(screen.getByText("risk/limits.md")).toBeInTheDocument();
		expect(screen.getByText("Лимит риска недели")).toBeInTheDocument();
		expect(screen.getByText("Показано 2 из 2")).toBeInTheDocument();
	});

	it("передаёт фильтры в API и сужает характеры выбранной группой", async () => {
		const user = userEvent.setup();
		renderPage();
		await screen.findByText("Лимит риска недели достигнут");

		await user.selectOptions(screen.getByLabelText("Фильтр по статусу"), "new");
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", limit: 200 }));

		await user.selectOptions(screen.getByLabelText("Фильтр по группе"), "futures-leg");
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", group: "futures-leg", limit: 200 }));

		const characterSelect = screen.getByLabelText("Фильтр по характеру");
		expect(characterSelect).toHaveTextContent("характер: фьючерсная нога");
		expect(characterSelect).not.toHaveTextContent("характер: лимиты и режим риска");

		await user.selectOptions(characterSelect, "futures-leg");
		await waitFor(() =>
			expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", group: "futures-leg", character: "futures-leg", limit: 200 }),
		);
	});

	it("отличает пустой журнал от пустого результата фильтра", async () => {
		api.fetchHintsLog
			.mockResolvedValueOnce({ items: [], total: 0, limit: 200, offset: 0 })
			.mockResolvedValueOnce({ items: [], total: 0, limit: 200, offset: 0 });
		renderPage();
		expect(await screen.findByText("записей нет")).toBeInTheDocument();

		const user = userEvent.setup();
		await user.selectOptions(screen.getByLabelText("Фильтр по статусу"), "new");
		await waitFor(() => expect(api.fetchHintsLog).toHaveBeenLastCalledWith({ status: "new", limit: 200 }));
		expect(await screen.findByText("по фильтру записей нет")).toBeInTheDocument();
	});
});
