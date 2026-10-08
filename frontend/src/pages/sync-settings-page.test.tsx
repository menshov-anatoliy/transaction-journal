import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SyncSettingsPage } from "./sync-settings-page";

// Раздел «Синхронизация» проверяется как пользовательский сценарий: настройки,
// журнал запусков, предупреждения/заметки, блок подключения Bybit и опасная зона.
// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

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
		expect(screen.getByLabelText(/делать резервную копию/i)).toBeChecked();
		expect(await screen.findByText(/исполнений 5/i)).toBeInTheDocument();
		expect(screen.getByText(/Пропущенные области: option:BTC/i)).toBeInTheDocument();
		expect(screen.getByText(/Неразрешённые инструменты:/i)).toBeInTheDocument();
		expect(screen.getByText(/Непокрытые базовые активы: ETH/i)).toBeInTheDocument();
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
	});

	it("выполняет ручную синхронизацию, пересбор и сброс категории", async () => {
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

		await user.click(screen.getByRole("button", { name: /сбросить состояние option/i }));
		await waitFor(() => expect(api.resetSyncCategory).toHaveBeenCalled());
		expect(api.resetSyncCategory.mock.calls[0][0]).toBe("option");
	});
});
