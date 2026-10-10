import { apiFetch } from "./http";

// Контракты раздела «Конструкции» единого API: обзор списка со сводкой,
// превью выделенной конструкции. Величины недоступные из-за сбоя котировок
// приходят null — слой формата покажет прочерк с признаком сбоя, не ноль.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Ручной статус конструкции в контракте API. */
export type ConstructionStatus = "open" | "closed" | "archived";

/** Сводка журнала для шапки раздела. */
export interface ConstructionsSummary {
	readonly totalPnL: number | null;
	readonly realizedPnL: number;
	readonly unrealizedPnL: number | null;
	readonly marksAsOf: string | null;
	readonly hasMarkFailure: boolean;
	readonly constructionCount: number;
	readonly openCount: number;
}

/** Строка конструкции таблицы раздела. */
export interface ConstructionRow {
	readonly constructionId: number;
	readonly name: string;
	readonly status: ConstructionStatus;
	readonly allocatedCapitalUsdt: number | null;
	readonly riskPercent: number | null;
	readonly riskUsdt: number | null;
	readonly profitPercent: number | null;
	readonly profitUsdt: number | null;
	readonly realizedPnL: number;
	readonly unrealizedPnL: number | null;
	readonly adjustmentsPnL: number;
	readonly totalPnL: number | null;
	readonly totalPnLPercent: number | null;
	readonly markValue: number | null;
	readonly capitalUsagePercent: number | null;
	// Реальный риск открытых остатков: бэкенд отдаёт null при неограниченном
	// худшем случае или неразобранном символе — индикатор подставит заглушку
	// плановым риском, граница не исчезает.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	readonly realRiskUsdt: number | null;
	readonly openedAt: string | null;
	readonly closedAt: string | null;
	readonly liveHintCount: number;
}

/** Обзор раздела: сводка шапки и строки таблицы одним ответом. */
export interface ConstructionsOverview {
	readonly summary: ConstructionsSummary;
	readonly items: readonly ConstructionRow[];
}

/** Счётчики записей конструкции в превью правой области. */
export interface ConstructionPreviewCounts {
	readonly positions: number;
	readonly openPositions: number;
	readonly trades: number;
	readonly adjustments: number;
}

/** Read-only превью выделенной конструкции правой области. */
export interface ConstructionPreview {
	readonly constructionId: number;
	readonly name: string;
	readonly status: ConstructionStatus;
	readonly allocatedCapitalUsdt: number | null;
	readonly riskPercent: number | null;
	readonly riskUsdt: number | null;
	readonly profitPercent: number | null;
	readonly profitUsdt: number | null;
	readonly realizedPnL: number;
	readonly unrealizedPnL: number | null;
	readonly adjustmentsPnL: number;
	readonly totalPnL: number | null;
	readonly totalPnLPercent: number | null;
	readonly markValue: number | null;
	readonly capitalUsagePercent: number | null;
	// Реальный риск открытых остатков: null при неограниченном худшем случае
	// или неразобранном символе остатка — слой индикатора применит каскад
	// заглушек вместо скрытия границы.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	readonly realRiskUsdt: number | null;
	readonly openedAt: string | null;
	readonly closedAt: string | null;
	readonly marksAsOf: string | null;
	readonly hasMarkFailure: boolean;
	readonly counts: ConstructionPreviewCounts;
}

/** Читает обзор раздела: сводку шапки и строки таблицы конструкций. */
export function fetchConstructionsOverview(): Promise<ConstructionsOverview> {
	return apiFetch<ConstructionsOverview>("/constructions");
}

/** Читает read-only превью выделенной конструкции. */
export function fetchConstructionPreview(constructionId: number): Promise<ConstructionPreview> {
	return apiFetch<ConstructionPreview>(`/constructions/${constructionId}/preview`);
}
