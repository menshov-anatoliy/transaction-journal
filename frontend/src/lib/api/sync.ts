import { apiFetch } from "./http";

// Контракт команды синхронизации единого API: компактная кнопка шапки
// «Конструкций» запускает проход и получает итог закрытого запуска;
// подробности остаются разделу «Синхронизация».
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Режим запуска синхронизации. */
export type SyncRunMode = "backfill" | "incremental";

/** Статус закрытого запуска синхронизации. */
export type SyncRunStatus = "succeeded" | "failed";

/** Итог запуска синхронизации для компактной кнопки шапки. */
export interface SyncRunSummary {
	readonly mode: SyncRunMode;
	readonly status: SyncRunStatus;
	readonly startedAt: string;
	readonly finishedAt: string | null;
	readonly newExecutions: number;
	readonly newDeliveries: number;
	readonly newInstruments: number;
}

/** Запускает синхронизацию и возвращает итог закрытого запуска. */
export function runSync(): Promise<SyncRunSummary> {
	return apiFetch<SyncRunSummary>("/sync", { method: "POST" });
}
