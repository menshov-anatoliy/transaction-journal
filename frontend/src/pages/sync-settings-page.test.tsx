import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SyncRunJournalEntry, SyncRunSummary } from "@/lib/api/sync";
import { SyncSettingsPage } from "./sync-settings-page";

// Раздел «Синхронизация» проверяется как пользовательский сценарий: настройки,
// журнал запусков, предупреждения/заметки, блок подключения Bybit и опасная зона.
// Дизайн-проверки сверяются с мастером Body #8 «Синхронизация» (qA7CW)
// макета design.pen: опасная зона (UOt6s) — negSoft/neg, предупреждения
// (tv63s) — riskSoft/risk, плотный Danger — заливка neg/белый текст (инстансы
// jmklC/EK3Ob), журнал запусков (D91td) — плотность 6/12·11.5.
// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
// Traceability: change:reconcile-frontend-with-design/design#D2

vi.mock("@/lib/api/sync", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/sync")>()),
	fetchSyncSettings: vi.fn(),
	fetchSyncRuns: vi.fn(),
	setSyncBackupBeforeSync: vi.fn(),
	runSync: vi.fn(),
	runFullRebuild: vi.fn(),
	resetSyncCategory: vi.fn(),
}));

const api = vi.mocked(await import("@/lib/api/sync"));

function renderPage() {
	const queryClient = new QueryClient({
		defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
	});
	return render(
		<QueryClientProvider client={queryClient}>
			<SyncSettingsPage />
		</QueryClientProvider>,
	);
}

function makeRunJournalEntry(overrides: Partial<SyncRunJournalEntry> = {}): SyncRunJournalEntry {
	return {
		id: 11,
		startedAt: "2026-10-08T09:00:00Z",
		finishedAt: "2026-10-08T09:01:30Z",
		mode: "incremental",
		status: "succeeded",
		error: null,
		newExecutions: 5,
		newDeliveries: 1,
		newInstruments: 2,
		skippedAreas: [],
		unresolvedInstruments: [],
		uncoveredBaseCoins: [],
		...overrides,
	};
}

beforeEach(() => {
	vi.clearAllMocks();
	api.fetchSyncSettings.mockResolvedValue({
		bybit: {
			isConfigured: true,
			maskedApiKey: "abcd······wxyz",
			accountDescription: "Unified (UTA), subaccount disabled",
			secretStorage: "файл appsettings.Local.json, секция Bybit",
			setupHint: null,
		},
		backupBeforeSyncEnabled: true,
		backupPolicyError: null,
	});
	api.fetchSyncRuns.mockResolvedValue([
		{
			id: 11,
			startedAt: "2026-10-08T09:00:00Z",
			finishedAt: "2026-10-08T09:01:30Z",
			mode: "incremental",
			status: "succeeded",
			error: null,
			newExecutions: 5,
			newDeliveries: 1,
			newInstruments: 2,
			skippedAreas: ["option:BTC"],
			unresolvedInstruments: ["ETH-30OCT26-4500-C"],
			uncoveredBaseCoins: ["ETH"],
		},
		{
			id: 10,
			startedAt: "2026-10-05T18:12:00Z",
			finishedAt: "2026-10-05T18:13:04Z",
			mode: "backfill",
			status: "failed",
			error: "сбой котировок (timeout Bybit)",
			newExecutions: 204,
			newDeliveries: 0,
			newInstruments: 3,
			skippedAreas: [],
			unresolvedInstruments: [],
			uncoveredBaseCoins: [],
		},
	]);
	api.setSyncBackupBeforeSync.mockResolvedValue(undefined);
	api.runSync.mockResolvedValue({
		mode: "incremental",
		status: "succeeded",
		startedAt: "2026-10-08T09:05:00Z",
		finishedAt: "2026-10-08T09:06:00Z",
		newExecutions: 2,
		newDeliveries: 0,
		newInstruments: 1,
		projectionError: null,
		reconciliationWarnings: [{ symbol: "BTC-27DEC26-65000-C", deliveryTime: "2026-10-08T09:06:00Z", difference: 3.5 }],
		skippedAreas: [],
		unresolvedInstruments: [],
		uncoveredBaseCoins: [],
	});
	api.runFullRebuild.mockResolvedValue({ constructionsCount: 3, boundCount: 17, tradesInInbox: 2 });
	api.resetSyncCategory.mockResolvedValue(undefined);
});

describe("раздел «Синхронизация»", () => {
	it("показывает подключение Bybit, переключатель бэкапа и заметки последнего запуска", async () => {
		renderPage();

		expect(await screen.findByRole("heading", { name: "Синхронизация", level: 1 })).toBeInTheDocument();
		// Титул раздела — Inter 21/600 дизайн-системы, не дефолтный text-2xl.
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		expect(screen.getByRole("heading", { name: "Синхронизация", level: 1 }).className).toContain("page-title");
		expect(await screen.findByText(/abcd······wxyz/i)).toBeInTheDocument();
		expect(screen.getByLabelText(/резервная копия перед синхронизацией/i)).toBeChecked();
		expect(await screen.findByText(/исполнений 5/i)).toBeInTheDocument();
		expect(screen.getByText(/Пропущенные области: option:BTC/i)).toBeInTheDocument();
		expect(screen.getByText(/Неразрешённые инструменты:/i)).toBeInTheDocument();
		expect(screen.getByText(/Непокрытые базовые активы: ETH/i)).toBeInTheDocument();
		// Блок «Результат запуска» постоянный: собирается по последней записи
		// журнала без ручного запуска (решение раунд-сверки 9.0).
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		expect(screen.getByText(/Результат запуска /)).toBeInTheDocument();
	});

	it("показывает строку последнего запуска в шапке карточки", async () => {
		// Решения раунд-сверки 9.0: справа шапки карточки — постоянная строка
		// «последний запуск: <статус> · <dd.MM HH:mm> · <N,N с>» по последней
		// записи журнала; статус словами, длительность «90,0 с» (запятая).
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		renderPage();

		// Assert: успешный запуск 09:00:00→09:01:30 даёт «успешно · 90,0 с».
		expect(await screen.findByTestId("last-run-label")).toHaveTextContent(
			/^последний запуск: успешно · \d{2}\.\d{2} \d{2}:\d{2} · 90,0 с$/,
		);
	});

	it("скрывает строку последнего запуска и результат, когда журнал пуст", async () => {
		// Решения раунд-сверки 9.0: без записей журнала строка «последний
		// запуск» в шапке карточки и блок «Результат запуска» скрыты.
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		api.fetchSyncRuns.mockResolvedValue([]);
		renderPage();

		expect(await screen.findByText("Запусков синхронизации пока нет.")).toBeInTheDocument();
		expect(screen.queryByTestId("last-run-label")).not.toBeInTheDocument();
		expect(screen.queryByText(/Результат запуска /)).not.toBeInTheDocument();
	});

	it("показывает выполняющийся запуск без длительности и результата", async () => {
		// Решения раунд-сверки 9.0: выполняющийся запуск — «выполняется» в
		// строке шапки и «выполняется…» в колонке «Итог»; длительности нет,
		// блок «Результат запуска» для незакрытого запуска скрыт.
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		api.fetchSyncRuns.mockResolvedValue([
			{
				id: 12,
				startedAt: "2026-10-08T09:00:00Z",
				finishedAt: null,
				mode: "incremental",
				status: "running",
				error: null,
				newExecutions: 0,
				newDeliveries: 0,
				newInstruments: 0,
				skippedAreas: [],
				unresolvedInstruments: [],
				uncoveredBaseCoins: [],
			},
		]);
		renderPage();

		expect(await screen.findByTestId("last-run-label")).toHaveTextContent(
			/^последний запуск: выполняется · \d{2}\.\d{2} \d{2}:\d{2}$/,
		);
		expect(await screen.findByText("выполняется…")).toBeInTheDocument();
		expect(screen.queryByText(/Результат запуска /)).not.toBeInTheDocument();
	});

	it("скрывает старый итог во время ручной мутации", async () => {
		// Старый итог не должен выглядеть актуальным, пока выполняется новый запуск.
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		let finishSync: ((summary: SyncRunSummary) => void) | undefined;
		api.runSync.mockImplementation(
			() =>
				new Promise((resolve) => {
					finishSync = resolve;
				}),
		);
		const user = userEvent.setup();
		renderPage();
		await screen.findByText(/Результат запуска /);

		await user.click(screen.getByRole("button", { name: /синхронизировать сейчас/i }));

		await waitFor(() => expect(api.runSync).toHaveBeenCalled());
		expect(screen.queryByText(/Результат запуска /)).not.toBeInTheDocument();
		finishSync?.({
			mode: "incremental",
			status: "succeeded",
			startedAt: "2026-10-08T09:05:00Z",
			finishedAt: "2026-10-08T09:06:00Z",
			newExecutions: 2,
			newDeliveries: 0,
			newInstruments: 1,
			projectionError: null,
			reconciliationWarnings: [],
			skippedAreas: [],
			unresolvedInstruments: [],
			uncoveredBaseCoins: [],
		});
		expect(await screen.findByText(/исполнений 2/i)).toBeInTheDocument();
	});

	it("переключает бэкап тумблером с ролью switch и клавиатурой Space", async () => {
		// Решения раунд-сверки 9.0: «резервная копия перед синхронизацией» —
		// тумблер Switch (role="switch", aria-checked, клавиатура Space),
		// а не чекбокс.
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		// Настройки гонятся последовательно: старт — включено, после клика —
		// выключено, после Space — снова включено.
		const settingsState = (enabled: boolean) => ({
			bybit: {
				isConfigured: true,
				maskedApiKey: "abcd······wxyz",
				accountDescription: "Unified (UTA), subaccount disabled",
				secretStorage: "файл appsettings.Local.json, секция Bybit",
				setupHint: null,
			},
			backupBeforeSyncEnabled: enabled,
			backupPolicyError: null,
		});
		api.fetchSyncSettings
			.mockResolvedValueOnce(settingsState(true))
			.mockResolvedValueOnce(settingsState(false))
			.mockResolvedValueOnce(settingsState(true));
		const user = userEvent.setup();
		renderPage();

		// Act/Assert: клик по тумблеру выключает опцию (проверяется первый
		// аргумент вызова — react-query передаёт мутации служебный контекст).
		const toggle = await screen.findByRole("switch", { name: /резервная копия перед синхронизацией/i });
		expect(toggle).toBeChecked();
		await user.click(toggle);
		await waitFor(() => expect(api.setSyncBackupBeforeSync.mock.calls[0][0]).toBe(false));

		// Act/Assert: Space на сфокусированном тумблере (фокус остался после
		// клика) возвращает опцию.
		toggle.focus();
		await user.keyboard(" ");
		await waitFor(() => expect(api.setSyncBackupBeforeSync.mock.calls.at(-1)?.[0]).toBe(true));
	});

	it("показывает инструкцию appsettings при незастроенном ключе", async () => {
		api.fetchSyncSettings.mockResolvedValueOnce({
			bybit: {
				isConfigured: false,
				maskedApiKey: null,
				accountDescription: null,
				secretStorage: null,
				setupHint: "Ключ не настроен: заполните Bybit:ApiKey в appsettings.Local.json.",
			},
			backupBeforeSyncEnabled: true,
			backupPolicyError: null,
		});
		renderPage();

		expect(await screen.findByText(/appsettings\.Local\.json/i)).toBeInTheDocument();
		// Пилюля «подключено» (oi3fT) показывается только настроенному ключу.
		expect(screen.queryByText("подключено")).not.toBeInTheDocument();
	});

	it("выполняет ручную синхронизацию, пересбор и сброс категории", async () => {
		// Журнал может ещё содержать running-строку того же запуска; расширенный
		// ручной итог сохраняет приоритет, пока эта строка не обновилась.
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		api.fetchSyncRuns.mockResolvedValue([
			makeRunJournalEntry({
				id: 12,
				startedAt: "2026-10-08T09:05:00Z",
				finishedAt: null,
				status: "running",
				newExecutions: 0,
				newDeliveries: 0,
				newInstruments: 0,
			}),
		]);
		const user = userEvent.setup();
		renderPage();
		await screen.findByRole("heading", { name: "Синхронизация", level: 1 });

		await user.click(screen.getByRole("button", { name: /синхронизировать сейчас/i }));
		await waitFor(() => expect(api.runSync).toHaveBeenCalled());
		expect(await screen.findByText(/расхождение 3\.5/i)).toBeInTheDocument();

		await user.click(screen.getByRole("button", { name: /собрать конструкции/i }));
		await user.click(await screen.findByRole("button", { name: /подтвердить пересбор/i }));
		await waitFor(() => expect(api.runFullRebuild).toHaveBeenCalled());
		expect(await screen.findByText(/пересбор завершён: конструкций 3/i)).toBeInTheDocument();

		await user.click(screen.getByRole("button", { name: /сбросить option/i }));
		await waitFor(() => expect(api.resetSyncCategory).toHaveBeenCalled());
		expect(api.resetSyncCategory.mock.calls[0][0]).toBe("option");
	});

	it("заменяет ручной итог, если журнал уже содержит более новый запуск", async () => {
		// Более новая запись журнала вытесняет прежний расширенный ручной итог.
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		api.fetchSyncRuns
			.mockResolvedValueOnce([makeRunJournalEntry()])
			.mockResolvedValueOnce([
				makeRunJournalEntry({
					id: 12,
					startedAt: "2026-10-08T09:07:00Z",
					finishedAt: "2026-10-08T09:08:00Z",
					newExecutions: 99,
				}),
			]);
		const user = userEvent.setup();
		renderPage();
		await screen.findByText(/Результат запуска /);

		await user.click(screen.getByRole("button", { name: /синхронизировать сейчас/i }));

		await waitFor(() => expect(api.runSync).toHaveBeenCalled());
		const result = await screen.findByRole("status");
		expect(within(result).getByText(/исполнений 99/i)).toBeInTheDocument();
		expect(within(result).queryByText(/исполнений 2/i)).not.toBeInTheDocument();
		expect(screen.queryByText(/расхождение 3\.5/i)).not.toBeInTheDocument();
	});

	it("оформляет опасную зону парой neg/negSoft с плотными Danger-кнопками мастера", async () => {
		// Arrange: зона UOt6s мастера Body #8 — заливка $negSoft, кайма $neg,
		// радиус 12, паддинги [16,18]; шапка — OctagonAlert 16 + Inter 13.5/600
		// $neg; действия — инстансы «Кнопка/Danger» с заливкой $neg и белым
		// текстом (jmklC/EK3Ob), не мягкая negSoft-база примитива.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		// Traceability: change:reconcile-frontend-with-design/design#D2
		renderPage();
		await screen.findByRole("heading", { name: "Опасная зона", level: 2 });

		// Assert: контейнер зоны — токены neg/negSoft и геометрия секции.
		const zone = screen.getByRole("heading", { name: "Опасная зона", level: 2 }).closest("section");
		expect(zone?.className).toContain("border-neg");
		expect(zone?.className).toContain("bg-neg-soft");
		expect(zone?.className).toContain("rounded-lg");
		expect(zone?.className).toContain("px-[18px]");
		expect(zone?.className).toContain("py-4");

		// Assert: заголовок 13.5/600 на neg и пиктограмма предупреждения.
		const heading = screen.getByRole("heading", { name: "Опасная зона", level: 2 });
		expect(heading.className).toContain("text-neg");
		expect(heading.className).toContain("text-[13.5px]");
		expect(heading.className).toContain("font-semibold");
		expect(heading.parentElement?.querySelector("svg")).not.toBeNull();

		// Assert: кнопки действий зоны — плотный Danger (neg-заливка, светлый текст).
		const rebuildButton = screen.getByRole("button", { name: "Собрать конструкции" });
		expect(rebuildButton.className).toContain("bg-neg");
		expect(rebuildButton.className).toContain("text-destructive-foreground");
		expect(rebuildButton.className).not.toContain("bg-neg-soft");
		for (const name of [/сбросить linear/i, /сбросить option/i]) {
			const button = screen.getByRole("button", { name });
			expect(button.className).toContain("bg-neg");
			expect(button.className).toContain("text-destructive-foreground");
		}

		// Assert: сноска LLM-провайдера мастера (fzke1) — 11 $textMuted.
		const llmNote = screen.getByText(/Настройки LLM-провайдера/i);
		expect(llmNote.className).toContain("text-[11px]");
		expect(llmNote.className).toContain("text-text-muted");
	});

	it("показывает предупреждения запусков на паре risk/riskSoft мастера", async () => {
		// Arrange: предупреждения Body #8 — блок Warn (tv63s): заливка
		// $riskSoft, радиус 9, паддинги [8,12], иконка TriangleAlert 14 и
		// текст Inter 11.5 $risk (сверка экспираций/заметки запуска).
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		// Traceability: change:reconcile-frontend-with-design/design#D2
		renderPage();
		await screen.findByText(/Пропущенные области: option:BTC/i);

		// Assert: заметки последнего запуска собраны в riskSoft-блок.
		const warn = screen.getByText(/Пропущенные области/).closest(".bg-risk-soft");
		expect(warn).not.toBeNull();
		expect(warn?.className).toContain("rounded-[9px]");
		expect(warn?.className).toContain("text-risk");
		expect(warn?.className).toContain("text-[11.5px]");
		expect(within(warn as HTMLElement).getByText(/Неразрешённые инструменты:/i)).toBeInTheDocument();
		expect(warn?.querySelector("svg")).not.toBeNull();

		// Act: ручной запуск с расхождением сверки добавляет предупреждение.
		const user = userEvent.setup();
		await user.click(screen.getByRole("button", { name: /синхронизировать сейчас/i }));

		// Assert: предупреждение сверки — тот же riskSoft-блок мастера.
		const reconciliation = await screen.findByText(/расхождение 3\.5/i);
		expect(reconciliation.closest(".bg-risk-soft")).not.toBeNull();
	});

	it("держит журнал запусков в плотности dense мастера с шапкой surface2", async () => {
		// Arrange: журнал D91td мастера — ячейки [6,12], кегль 11.5, текст
		// $textSecondary, шапка на $surface2, прерванный запуск — $neg.
		// Колонки «Время | Режим | Исполнений | Итог» со словесным итогом —
		// решение раунд-сверки 9.0 (мастер uxrZO).
		// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
		// Traceability: change:reconcile-frontend-with-design/design#D6
		// Traceability: change:rework-contextual-topbar/tasks#9.0
		renderPage();
		await screen.findByRole("table");

		// Assert: плотность dense общего слоя таблиц ([6,12]·11.5).
		expect(screen.getByRole("table")).toHaveAttribute("data-density", "dense");

		// Assert: шапка журнала залита $surface2 (нода HR мастера) и несёт
		// четыре колонки решения 9.0.
		const headerRow = screen.getByText("Время").closest("tr");
		expect(headerRow?.className).toContain("bg-surface-2");
		expect(screen.getByText("Режим")).toBeInTheDocument();
		expect(screen.getByText("Исполнений")).toBeInTheDocument();
		expect(screen.getByText("Итог")).toBeInTheDocument();

		// Assert: ячейки успешного запуска — 11.5/normal $textSecondary,
		// итог словом с длительностью «90,0 с».
		const cell = screen.getByText("догрузка").closest("td");
		expect(cell?.className).toContain("text-[11.5px]");
		expect(cell?.className).toContain("text-text-secondary");
		expect(screen.getByText("успешно · 90,0 с").closest("td")).not.toBeNull();

		// Assert: прерванный запуск подсвечен токеном neg (Row:4 мастера),
		// итог — «прервано: <error>».
		const failedCell = screen.getByText(/прервано: сбой котировок/i).closest("td");
		expect(failedCell?.className).toContain("text-neg");
	});

	it("оформляет секции подключения и синхронизации геометрией мастера", async () => {
		// Arrange: секции Uf04R/YmlIB мастера — $surface/$border, радиус 12,
		// паддинги [16,18]; шапка подключения несёт пилюлю «подключено»
		// (oi3fT: accentSoft r999 [3,8] + accentStrong 10.5); ряды ключ/значение
		// — Inter 12: ключ $textMuted, значение $textPrimary.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		renderPage();
		const bybitHeading = await screen.findByRole("heading", { name: "Подключение Bybit", level: 2 });
		await screen.findByText(/abcd······wxyz/i);

		// Assert: карточки секций — surface/border r12 [16,18].
		const bybitCard = bybitHeading.closest("section");
		expect(bybitCard?.className).toContain("rounded-lg");
		expect(bybitCard?.className).toContain("bg-surface");
		expect(bybitCard?.className).toContain("px-[18px]");
		expect(bybitCard?.className).toContain("py-4");
		const syncHeading = screen.getByRole("heading", { name: "Синхронизация", level: 2 });
		expect(syncHeading.className).toContain("text-[13.5px]");
		expect(syncHeading.closest("section")?.className).toContain("bg-surface");

		// Assert: пилюля «подключено» — пара accentSoft/accentStrong мастера.
		const pill = screen.getByText("подключено");
		expect(pill.className).toContain("rounded-full");
		expect(pill.className).toContain("bg-accent-soft");
		expect(pill.className).toContain("text-accent-strong");
		expect(pill.className).toContain("text-[10.5px]");

		// Assert: ряды подключения — подписи мастера 12 с ключами $textMuted.
		expect(screen.getByText("Ключ API").className).toContain("text-text-muted");
		expect(screen.getByText("Хранение секрета").className).toContain("text-text-muted");
		expect(screen.getByText(/abcd······wxyz/i).className).toContain("text-text-primary");
	});
});
