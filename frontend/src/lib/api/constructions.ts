import { apiFetch } from "./http";

// Контракты раздела «Конструкции» единого API: обзор списка со сводкой,
// превью выделенной конструкции. Величины недоступные из-за сбоя котировок
// приходят null — слой формата покажет прочерк с признаком сбоя, не ноль.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Ручной статус конструкции в контракте API. */
export type ConstructionStatus = "open" | "closed" | "archived";

/** Состояние реального риска: число величины выдаётся только конечному риску. */
export type RealRiskStatus = "finite" | "unbounded" | "unavailable";

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
	// Реальный риск открытых остатков идёт парой «величина + статус»: число
	// выдаётся только конечному риску, null при неограниченном хвосте или
	// неполных данных, а статус различает эти причины — угадывать finite
	// по плановому риску нельзя.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	readonly realRiskUsdt: number | null;
	readonly realRiskStatus: RealRiskStatus;
	// Худший исход конструкции — знаковая величина рядом с реальным риском
	// (реализованный результат плюс неклэмпированный минимум платежа групп
	// остатков): число при том же статусе finite, null при
	// unbounded/unavailable. Ставит границу индикатора финрезультата.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	readonly worstCaseUsdt: number | null;
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
	readonly markValue: number | null;
	readonly capitalUsagePercent: number | null;
	// Реальный риск открытых остатков идёт парой «величина + статус»: число
	// только при конечном риске, null при неограниченном хвосте или неполных
	// данных, а статус различает эти причины без подстановки планового риска.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	readonly realRiskUsdt: number | null;
	readonly realRiskStatus: RealRiskStatus;
	// Худший исход конструкции — знаковая величина рядом с реальным риском:
	// число при том же статусе finite, null при unbounded/unavailable.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	readonly worstCaseUsdt: number | null;
	readonly openedAt: string | null;
	readonly closedAt: string | null;
	readonly marksAsOf: string | null;
	readonly hasMarkFailure: boolean;
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
