import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
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

// Слот контекстной правой панели: без регистрации контекста панель
// свёрнута и центр занимает всю ширину; регистрация содержимого страницей
// через useContextPanel показывает панель на сохранённой ширине.
// Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
// Traceability: change:rework-spa-shell-and-screens/design#D3

// Стабильные регистрации: объект контракта обязан быть memo-стабильным
// между рендерами, иначе эффект перезапускается на каждом рендере.
const briefCardRegistration: ContextPanelRegistration = {
	content: <div>краткая карточка конструкции</div>,
	iconActions: <button type="button">Действие контекста</button>,
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
	it("без регистрации контекста панель свёрнута, центр на всю ширину", () => {
		// Arrange/Act: экран ещё не зарегистрировал контекст — правой панели
		// быть не должно (сценарий свёрнутости без контекста).
		renderLayout(false);

		// Assert: панель не отрисована, центральный Outlet работает.
		expect(screen.queryByTestId("context-panel")).not.toBeInTheDocument();
		expect(screen.getByText("центр экрана")).toBeInTheDocument();
		expect(screen.getByRole("main")).toBeInTheDocument();
	});

	it("регистрация контекста показывает панель с содержимым и иконками действий", async () => {
		// Act: экран регистрирует краткую карточку и действие.
		renderLayout();

		// Assert: панель появилась, содержимое и иконочные действия на месте.
		expect(await screen.findByTestId("context-panel")).toBeInTheDocument();
		expect(screen.getByText("краткая карточка конструкции")).toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Действие контекста" })).toBeInTheDocument();
	});

	it("регистрация разворачивает панель на сохранённую ширину", async () => {
		// Arrange: пользователь ранее задал ширину правой панели 28 rem.
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 28, collapsed: false }));

		// Act: экран регистрирует контекст.
		renderLayout();

		// Assert: панель развёрнута на сохранённые 28 rem.
		const panel = await screen.findByTestId("context-panel");
		expect(panel.style.width).toBe("28rem");
	});

	it("сброс регистрации сворачивает панель, центр остаётся", async () => {
		// Arrange: панель показана зарегистрированным контекстом.
		const user = userEvent.setup();
		renderLayout();
		expect(await screen.findByTestId("context-panel")).toBeInTheDocument();

		// Act: страница сняла регистрацию (например, снятие выделения).
		await user.click(screen.getByRole("button", { name: "Снять контекст" }));

		// Assert: панель исчезла, центр продолжает работать.
		expect(screen.queryByTestId("context-panel")).not.toBeInTheDocument();
		expect(screen.getByText("центр экрана")).toBeInTheDocument();
	});

	it("сохранённая свёрнутость показывает панель только иконочной колонкой", async () => {
		// Arrange: пользователь свернул правую панель, геометрия сохранена.
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 24, collapsed: true }));

		// Act: экран регистрирует контекст.
		renderLayout();

		// Assert: панель схлопнута до иконочной колонки — содержимого и
		// сплиттера нет, доступна кнопка разворачивания.
		const panel = await screen.findByTestId("context-panel");
		expect(panel.style.width).toBe("");
		expect(screen.queryByText("краткая карточка конструкции")).not.toBeInTheDocument();
		expect(screen.queryByRole("separator", { name: "Изменить ширину контекстной панели" })).not.toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Развернуть контекстную панель" })).toBeInTheDocument();
	});
});
