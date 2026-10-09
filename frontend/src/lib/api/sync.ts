import { apiCommand, apiFetch } from "./http";

// Контракт команды синхронизации единого API: компактная кнопка шапки
// «Конструкций» запускает проход и получает итог закрытого запуска;
// подробности остаются разделу «Синхронизация».
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Режим запуска синхронизации. */
export type SyncRunMode = "backfill" | "incremental";

/** Статус закрытого запуска синхронизации. */
export type SyncRunStatus = "succeeded" | "failed";

/** Предупреждение сверки экспирации с биржей в результате запуска. */
export interface SyncReconciliationWarning {
	readonly symbol: string;
	readonly deliveryTime: string;
	readonly difference: number;
}

/** Итог запуска синхронизации для компактной кнопки шапки. */
export interface SyncRunSummary {
	readonly mode: SyncRunMode;
	readonly status: SyncRunStatus;
	readonly startedAt: string;
	readonly finishedAt: string | null;
	readonly newExecutions: number;
	readonly newDeliveries: number;
	readonly newInstruments: number;
	readonly projectionError: string | null;
	readonly reconciliationWarnings: readonly SyncReconciliationWarning[];
	readonly skippedAreas: readonly string[];
	readonly unresolvedInstruments: readonly string[];
	readonly uncoveredBaseCoins: readonly string[];
}

/** Снимок подключения Bybit для блока раздела «Синхронизация». */
export interface BybitConnectionState {
	readonly isConfigured: boolean;
	readonly maskedApiKey: string | null;
	readonly accountDescription: string | null;
	readonly secretStorage: string | null;
	readonly setupHint: string | null;
}

/** Настройки раздела синхронизации. */
export interface SyncSettingsState {
	readonly bybit: BybitConnectionState;
	readonly backupBeforeSyncEnabled: boolean;
	readonly backupPolicyError: string | null;
}

/** Строка журнала синхронизаций для таблицы раздела. */
export interface SyncRunJournalEntry {
	readonly id: number;
	readonly startedAt: string;
	readonly finishedAt: string | null;
	readonly mode: SyncRunMode;
	readonly status: SyncRunStatus | "running";
	readonly error: string | null;
	readonly newExecutions: number;
	readonly newDeliveries: number;
	readonly newInstruments: number;
	readonly skippedAreas: readonly string[];
	readonly unresolvedInstruments: readonly string[];
	readonly uncoveredBaseCoins: readonly string[];
}

/** Итог полного пересбора конструкций из блока «Опасная зона». */
export interface SyncRebuildResult {
	readonly constructionsCount: number;
	readonly boundCount: number;
	readonly tradesInInbox: number;
}

/** Запускает синхронизацию и возвращает итог закрытого запуска. */
export function runSync(): Promise<SyncRunSummary> {
	return apiFetch<SyncRunSummary>("/sync", { method: "POST" });
}

/** Читает настройки раздела синхронизации и состояние подключения Bybit. */
export function fetchSyncSettings(): Promise<SyncSettingsState> {
	return apiFetch<SyncSettingsState>("/sync/settings");
}

/** Переключает опциональную резервную копию перед синхронизацией. */
export function setSyncBackupBeforeSync(enabled: boolean): Promise<void> {
	return apiCommand("/sync/settings/backup-before-sync", {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ enabled }),
	});
}

/** Читает журнал запусков синхронизации новыми сверху. */
export function fetchSyncRuns(limit = 20): Promise<readonly SyncRunJournalEntry[]> {
	return apiFetch<{ runs: readonly SyncRunJournalEntry[] }>(`/sync/runs?limit=${limit}`).then((payload) => payload.runs);
}

/** Запускает полный пересбор конструкций из опасной зоны. */
export function runFullRebuild(): Promise<SyncRebuildResult> {
	return apiFetch<SyncRebuildResult>("/sync/rebuild", { method: "POST" });
}

/** Сбрасывает состояние категории синхронизации (linear/option). */
export function resetSyncCategory(category: "linear" | "option"): Promise<void> {
	return apiCommand("/sync/reset-category", {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ category }),
	});
}
