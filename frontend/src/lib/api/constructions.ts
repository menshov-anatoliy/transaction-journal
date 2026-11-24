import { apiFetch } from "./http";

// Контракты раздела «Конструкции» единого API: обзор списка со сводкой,
// превью выделенной конструкции. Величины недоступные из-за сбоя котировок
// приходят null — слой формата покажет прочерк с признаком сбоя, не ноль.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Ручной статус конструкции в контракте API. */
export type ConstructionStatus = "open" | "closed" | "archived";

/** Состояние реального риска: число величины выдаётся только конечному риску. */
// Перечень состояний вырожден в finite/unavailable по ADR-0025: неограниченный
// хвост по текущим маркам невозможен, строка «unbounded» удалена из контракта.
// Traceability: adr:docs/adr/0025-real-risk-negative-part-of-current-result.md#consequences
export type RealRiskStatus = "finite" | "unavailable";

/** Сводка журнала для шапки раздела. */
export interface ConstructionsSummary {
	readonly totalPnL: number | null;
	readonly realizedPnL: number;
	readonly unrealizedPnL: number | null;
	readonly quotesAsOf: string | null;
	readonly hasQuoteFailure: boolean;
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
	readonly quoteValue: number | null;
	readonly capitalUsagePercent: number | null;
	// Реальный риск идёт парой «величина + статус»: число выдаётся только
	// конечному риску — минусовой части текущего результата по текущим
	// маркам; null при недоступном риске (сбой котировок или неоценённый
	// остаток), частичный расчёт запрещён, угадывать finite по плановому
	// риску нельзя.
	// Traceability: issue:#98
	// Traceability: adr:docs/adr/0025-real-risk-negative-part-of-current-result.md#consequences
	readonly realRiskUsdt: number | null;
	readonly realRiskStatus: RealRiskStatus;
	readonly openedAt: string | null;
	readonly closedAt: string | null;
	readonly liveHintCount: number;
	// Счётчик предупреждений диагностики незакрытых рисков — данные бейджа
	// «⚠ N» строки списка: ноль означает отсутствие предупреждений и бейджа.
	// Traceability: openspec:ui/screens#requirement-construction-warnings-display
	readonly riskWarningsCount: number;
	// Величины времени жизни открытых опционных ног строки: остаток суток до
	// ближайшей экспирации и нетто-дневной распад; null — опционных ног нет,
	// распад null и без тикеров. Клиент рендерит прочерк, красные сутки
	// при ≤ 14 выводит сам.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-list-lifetime-columns
	readonly daysToNearestExpiry: number | null;
	readonly netDailyDecayUsdt: number | null;
	// Суммарная внутренняя стоимость открытых опционных остатков — третье
	// слагаемое саммари времени жизни; null без опционных ног или без тикеров.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	readonly totalIntrinsicValueUsdt: number | null;
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

/** Запись предупреждения диагностики в перечне превью. */
export interface ConstructionPreviewWarning {
	readonly text: string;
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
	readonly quoteValue: number | null;
	readonly capitalUsagePercent: number | null;
	// Реальный риск идёт парой «величина + статус»: число только при
	// конечном риске — минусовой части текущего результата по текущим
	// маркам; null при недоступном риске без подстановки планового риска.
	// Traceability: issue:#98
	// Traceability: adr:docs/adr/0025-real-risk-negative-part-of-current-result.md#consequences
	readonly realRiskUsdt: number | null;
	readonly realRiskStatus: RealRiskStatus;
	readonly openedAt: string | null;
	readonly closedAt: string | null;
	readonly quotesAsOf: string | null;
	readonly hasQuoteFailure: boolean;
	readonly counts: ConstructionPreviewCounts;
	// Счётчик предупреждений превью — тот же источник, что у бейджа строки:
	// число и перечень идут одним вызовом вычислителя, расхождение исключено.
	// Traceability: openspec:ui/screens#requirement-construction-warnings-display
	readonly riskWarningsCount: number;
	// Полный перечень текстов предупреждений краткой карточки: карточка
	// перечисляет все записи, а не счётчик; пустой список — секции нет.
	// Traceability: openspec:ui/screens#scenario-preview-lists-all-warnings
	readonly riskWarnings: readonly ConstructionPreviewWarning[];
}

/** Читает обзор раздела: сводку шапки и строки таблицы конструкций. */
export function fetchConstructionsOverview(): Promise<ConstructionsOverview> {
	return apiFetch<ConstructionsOverview>("/constructions");
}

/** Читает read-only превью выделенной конструкции. */
export function fetchConstructionPreview(constructionId: number): Promise<ConstructionPreview> {
	return apiFetch<ConstructionPreview>(`/constructions/${constructionId}/preview`);
}
