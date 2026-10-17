import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { RouterProvider, createMemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppLayout } from "./AppLayout";
import { useContextPanel, type ContextPanelRegistration } from "./context-panel";

vi.mock("@/lib/api/inbox", () => ({
	fetchInboxCount: vi.fn(),
}));

const inboxApi = vi.mocked(await import("@/lib/api/inbox"));

// Слот контекстной правой панели: без регистрации контекста панель и
// сегмент контекста топбара скрыты и центр занимает всю ширину;
// регистрация содержимого страницей через useContextPanel показывает
// панель на сохранённой ширине и чип короткого названия в сегменте
// контекста. Футеров у панелей нет: кнопка управления — в сегменте
// топбара, свёрнутая панель — рейка на всю высоту.
// Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
// Traceability: change:rework-spa-shell-and-screens/design#D3
// Traceability: change:rework-contextual-topbar/design#D4

// Стабильные регистрации: объект контракта обязан быть memo-стабильным
// между рендерами, иначе эффект перезапускается на каждом рендере.
const briefCardRegistration: ContextPanelRegistration = {
	chipLabel: "Конструкция",
	content: <div>краткая карточка конструкции</div>,
	iconActions: <button type="button" title="Действие контекста">Действие контекста</button>,
};

/**
 * Экран-проверка: регистрирует контекст и позволяет снять регистрацию
 * изнутри страницы, как это происходит при снятии выделения на списке.
 */
function ToggleContextScreen({ initialRegistered = true }: { initialRegistered?: boolean }) {
	const [registered, setRegistered] = useState(initialRegistered);
	const registration = registered ? briefCardRegistration : null;
	useContextPanel(registration);
	return (
		<div>
			<div>центр экрана</div>
			{registered && (
				<button type="button" onClick={() => setRegistered(false)}>
					Снять контекст
				</button>
			)}
		</div>
	);
}

function renderLayout(initialRegistered = true) {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false } },
	});
	const router = createMemoryRouter(
		[{
			element: <AppLayout />,
			children: [{ index: true, element: <ToggleContextScreen initialRegistered={initialRegistered} /> }],
		}],
		{ initialEntries: ["/"] },
	);
	return render(
		<QueryClientProvider client={queryClient}>
			<RouterProvider router={router} />
		</QueryClientProvider>,
	);
}

// matchMedia-заглушка как в тестах каркаса: рабочий стол 1280px.
function setViewport(width: number) {
	Object.defineProperty(window, "innerWidth", { value: width, writable: true, configurable: true });
	window.matchMedia = vi.fn().mockImplementation((query: string) => ({
		matches: query.includes("max-width") ? width <= 1023 : false,
		media: query,
		onchange: null,
		addEventListener: vi.fn(),
		removeEventListener: vi.fn(),
		addListener: vi.fn(),
		removeListener: vi.fn(),
		dispatchEvent: vi.fn(),
	})) as typeof window.matchMedia;
}

beforeEach(() => {
	localStorage.clear();
	inboxApi.fetchInboxCount.mockResolvedValue(0);
	setViewport(1280);
});

describe("слот контекстной панели", () => {
	it("без регистрации контекста панель и сегмент контекста скрыты", () => {
		// Arrange/Act: экран ещё не зарегистрировал контекст — правой панели
		// и её сегмента топбара быть не должно.
		// Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
		renderLayout(false);

		// Assert: панель и сегмент контекста не отрисованы, центральный
		// Outlet работает.
		expect(screen.queryByTestId("context-panel")).not.toBeInTheDocument();
		expect(screen.queryByTestId("topbar-segment-context")).not.toBeInTheDocument();
		expect(screen.getByText("центр экрана")).toBeInTheDocument();
		expect(screen.getByRole("main")).toBeInTheDocument();
	});

	it("регистрация контекста показывает содержимое, чип и кнопку в сегменте топбара", async () => {
		// Act: экран регистрирует краткую карточку, чип и действие.
		renderLayout();

		// Assert: развёрнутая панель показывает контент; действия доступны
		// только в рейке, кнопка сворачивания и чип — в сегменте контекста
		// топбара, а не в самой панели.
		// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
		expect(await screen.findByTestId("context-panel")).toBeInTheDocument();
		expect(screen.getByText("краткая карточка конструкции")).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Действие контекста" })).not.toBeInTheDocument();
		const segment = screen.getByTestId("topbar-segment-context");
		const chip = within(segment).getByTestId("context-chip");
		expect(chip).toHaveTextContent("Конструкция");
		expect(chip.className).toContain("bg-info-soft");
		expect(
			within(segment).getByRole("button", { name: "Свернуть контекстную панель" }),
		).toBeInTheDocument();
		expect(screen.getByTestId("context-panel").querySelector('[data-slot="panel-footer"]')).toBeNull();
	});

	it("регистрация разворачивает панель на сохранённую ширину", async () => {
		// Arrange: пользователь ранее задал ширину правой панели 28 rem.
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 28, collapsed: false }));

		// Act: экран регистрирует контекст.
		renderLayout();

		// Assert: панель развёрнута на сохранённые 28 rem, сегмент контекста
		// выровнен по ней.
		expect(await screen.findByTestId("context-panel")).toHaveStyle({ width: "28rem" });
		expect(screen.getByTestId("topbar-segment-context")).toHaveStyle({ width: "28rem" });
	});

	it("сброс регистрации скрывает панель и сегмент контекста", async () => {
		// Arrange: панель показана зарегистрированным контекстом.
		const user = userEvent.setup();
		renderLayout();
		expect(await screen.findByTestId("context-panel")).toBeInTheDocument();

		// Act: страница сняла регистрацию (например, снятие выделения).
		await user.click(screen.getByRole("button", { name: "Снять контекст" }));

		// Assert: панель и сегмент контекста исчезли, центр продолжает работать.
		expect(screen.queryByTestId("context-panel")).not.toBeInTheDocument();
		expect(screen.queryByTestId("topbar-segment-context")).not.toBeInTheDocument();
		expect(screen.getByText("центр экрана")).toBeInTheDocument();
	});

	it("сохранённая свёрнутость показывает рейку на всю высоту, разворачивание — в топбаре", async () => {
		// Arrange: пользователь свернул правую панель, геометрия сохранена.
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 24, collapsed: true }));

		// Act: экран регистрирует контекст.
		renderLayout();

		// Assert: содержимое и сплиттер скрыты; рейка — единственное
		// содержимое панели (футеров нет), действие остаётся в ней, кнопка
		// разворачивания — в сегменте топбара.
		// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
		const panel = await screen.findByTestId("context-panel");
		expect(panel).toHaveStyle({ width: "2.75rem" });
		expect(screen.queryByText("краткая карточка конструкции")).not.toBeInTheDocument();
		expect(screen.queryByRole("separator", { name: "Изменить ширину контекстной панели" })).not.toBeInTheDocument();
		const rail = panel.querySelector('[data-slot="panel-miniature-rail"]');
		expect(rail).not.toBeNull();
		expect(rail?.className).toContain("w-11");
		expect(rail?.className).toContain("flex-1");
		expect(panel.querySelector('[data-slot="panel-footer"]')).toBeNull();
		const action = screen.getByRole("button", { name: "Действие контекста" });
		expect(action).toHaveAttribute("title", "Действие контекста");
		expect(rail?.contains(action)).toBe(true);
		const segment = screen.getByTestId("topbar-segment-context");
		expect(segment).toHaveStyle({ width: "2.75rem" });
		expect(within(segment).queryByTestId("context-chip")).not.toBeInTheDocument();
		const expand = within(segment).getByRole("button", { name: "Развернуть контекстную панель" });
		expect(expand).toHaveAttribute("title", "Развернуть контекстную панель");
		expect(panel.contains(expand)).toBe(false);
	});
});
