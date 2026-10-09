import { apiCommand, apiFetch } from "./http";

// Контракты карточки конструкции единого API: полный снимок экрана одним
// запросом (шапка, метрики с периодом, четыре таблицы записей) и команды
// действий — шапка, комментарии трёх уровней, ручные пометки закрытия,
// действия сделок и корректировки PnL. Величины, недоступные из-за сбоя
// котировок, приходят null — слой формата покажет признак сбоя, не ноль.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

/** Единица ввода плановой границы результата. */
export type TargetUnit = "percent" | "usdt";

/** Вид закрывающей записи единого потока. */
export type ClosingEntryKind = "delivery" | "otm-expiry" | "manual-mark";

/** Источник внешней корректировки PnL. */
export type AdjustmentSource = "robot" | "manual";

/** Метрики карточки: сводка kstrip с периодом и длительностью. */
export interface ConstructionCardMetrics {
	readonly realizedPnL: number;
	readonly unrealizedPnL: number | null;
	readonly adjustmentsPnL: number;
	readonly totalPnL: number | null;
	readonly totalPnLPercent: number | null;
	readonly realizedPnLPercent: number | null;
	readonly unrealizedPnLPercent: number | null;
	readonly adjustmentsPnLPercent: number | null;
	readonly markValue: number | null;
	readonly capitalUsagePercent: number | null;
	readonly openedAt: string | null;
	readonly closedAt: string | null;
	readonly durationSeconds: number | null;
}

/** Строка таблицы позиций карточки. */
export interface ConstructionCardPosition {
	readonly symbol: string;
	readonly residual: number;
	readonly averageEntryPrice: number | null;
	readonly averageClosePrice: number | null;
	readonly realizedPnL: number;
	readonly realizedPnLPercent: number | null;
	readonly unrealizedPnL: number | null;
	readonly unrealizedPnLPercent: number | null;
	readonly totalPnL: number | null;
	readonly totalPnLPercent: number | null;
	readonly accumulatedFees: number;
	readonly openedAt: string;
	readonly closedAt: string | null;
	readonly isOpen: boolean;
	readonly comment: string | null;
	readonly markValue: number | null;
	readonly priceChangePercent: number | null;
}

/** Строка таблицы сделок карточки. */
export interface ConstructionCardTrade {
	readonly execId: string;
	readonly symbol: string;
	readonly executedAt: string;
	readonly isBuy: boolean;
	readonly quantity: number;
	readonly price: number;
	readonly amountUsdt: number;
	readonly fee: number;
	readonly comment: string | null;
}

/** Строка таблицы закрывающих записей карточки. */
export interface ConstructionCardClosingEntry {
	readonly closedAt: string;
	readonly kind: ClosingEntryKind;
	readonly symbol: string;
	readonly quantity: number;
	readonly price: number | null;
	readonly amountUsdt: number | null;
	readonly manualMarkId: number | null;
}

/** Предупреждение об избыточной закрывающей записи. */
export interface ConstructionCardClosingWarning {
	readonly kind: ClosingEntryKind;
	readonly symbol: string;
	readonly closedAt: string;
}

/** Строка таблицы корректировок PnL карточки. */
export interface ConstructionCardAdjustment {
	readonly adjustmentId: number;
	readonly date: string;
	readonly description: string | null;
	readonly source: AdjustmentSource;
	readonly amountUsdt: number;
}

/** Полный снимок карточки конструкции одним запросом. */
export interface ConstructionCard {
	readonly constructionId: number;
	readonly name: string;
	readonly status: ConstructionStatus;
	readonly allocatedCapitalUsdt: number | null;
	readonly riskPercent: number | null;
	readonly riskUsdt: number | null;
	readonly riskUnit: TargetUnit | null;
	readonly profitPercent: number | null;
	readonly profitUsdt: number | null;
	readonly profitUnit: TargetUnit | null;
	readonly comment: string | null;
	readonly hasOpenResidual: boolean;
	readonly hasMarkFailure: boolean;
	readonly marksAsOf: string | null;
	readonly metrics: ConstructionCardMetrics;
	readonly positions: readonly ConstructionCardPosition[];
	readonly trades: readonly ConstructionCardTrade[];
	readonly closingEntries: readonly ConstructionCardClosingEntry[];
	readonly closingWarnings: readonly ConstructionCardClosingWarning[];
	readonly adjustments: readonly ConstructionCardAdjustment[];
}

/** Ручной статус конструкции (реэкспорт контракта раздела). */
export type ConstructionStatus = import("./constructions").ConstructionStatus;

/** Цель переноса сделки: активная конструкция без текущей. */
export interface ConstructionMoveTarget {
	readonly constructionId: number;
	readonly name: string;
}

/** Атрибуты ручной пометки закрытия: инструмент, время и цена. */
export interface ManualCloseMarkInput {
	readonly symbol: string;
	readonly markedAt: string;
	readonly price: number | null;
}

/** Атрибуты внешней корректировки PnL. */
export interface AdjustmentInput {
	readonly date: string;
	readonly source: AdjustmentSource;
	readonly amountUsdt: number;
	readonly description: string | null;
}

/** Читает полный снимок карточки конструкции. */
export function fetchConstructionCard(constructionId: number): Promise<ConstructionCard> {
	return apiFetch<ConstructionCard>(`/constructions/${constructionId}`);
}

/** Переименовывает конструкцию. */
export function renameConstruction(constructionId: number, name: string): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/rename`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ name }),
	});
}

/** Меняет ручной статус конструкции, включая архив и возврат из архива. */
export function changeConstructionStatus(
	constructionId: number,
	status: ConstructionStatus,
): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/status`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ status }),
	});
}

/** Меняет выделенный капитал; null убирает капитал у конструкции. */
export function changeAllocatedCapital(constructionId: number, capital: number | null): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/capital`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ allocatedCapitalUsdt: capital }),
	});
}

/** Задаёт или убирает плановую границу: пара «значение + единица», оба null — убрать. */
function changeBound(
	constructionId: number,
	bound: "risk" | "profit",
	value: number | null,
	unit: TargetUnit | null,
): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/${bound}`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ value, unit }),
	});
}

/** Задаёт или убирает риск конструкции. */
export function changeRisk(constructionId: number, value: number | null, unit: TargetUnit | null): Promise<void> {
	return changeBound(constructionId, "risk", value, unit);
}

/** Задаёт или убирает профит конструкции. */
export function changeProfit(constructionId: number, value: number | null, unit: TargetUnit | null): Promise<void> {
	return changeBound(constructionId, "profit", value, unit);
}

/** Удаляет пустую конструкцию с опциональной резервной копией. */
export function deleteConstruction(constructionId: number, makeBackup: boolean): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/delete`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ makeBackup }),
	});
}

/** Задаёт или снимает комментарий конструкции. */
export function setConstructionComment(constructionId: number, text: string | null): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/comment`, {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ text }),
	});
}

/** Задаёт или снимает комментарий позиции по ключу «конструкция × инструмент». */
export function setPositionComment(constructionId: number, symbol: string, text: string | null): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/positions/${encodeURIComponent(symbol)}/comment`, {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ text }),
	});
}

/** Задаёт или снимает комментарий сделки по ключу execId. */
export function setTradeComment(execId: string, text: string | null): Promise<void> {
	return apiCommand(`/trades/${encodeURIComponent(execId)}/comment`, {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ text }),
	});
}

/** Читает последнюю известную марку инструмента — дефолт формы пометки. */
export function fetchLastInstrumentMark(constructionId: number, symbol: string): Promise<number | null> {
	return apiFetch<{ mark: number | null }>(
		`/constructions/${constructionId}/positions/${encodeURIComponent(symbol)}/last-mark`,
	).then((payload) => payload.mark);
}

/** Ставит ручную пометку закрытия позиции. */
export function addManualCloseMark(constructionId: number, mark: ManualCloseMarkInput): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/close-marks`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(mark),
	});
}

/** Правит ручную пометку закрытия. */
export function editManualCloseMark(markId: number, mark: ManualCloseMarkInput): Promise<void> {
	return apiCommand(`/close-marks/${markId}`, {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(mark),
	});
}

/** Удаляет ручную пометку закрытия. */
export function deleteManualCloseMark(markId: number): Promise<void> {
	return apiCommand(`/close-marks/${markId}`, { method: "DELETE" });
}

/** Читает цели переноса сделки: активные конструкции без текущей. */
export function fetchMoveTargets(constructionId: number): Promise<readonly ConstructionMoveTarget[]> {
	return apiFetch<{ targets: readonly ConstructionMoveTarget[] }>(`/constructions/${constructionId}/move-targets`).then(
		(payload) => payload.targets,
	);
}

/** Возвращает сделку во «Входящие», сохраняя комментарий сделки. */
export function returnTradeToInbox(execId: string): Promise<void> {
	return apiCommand(`/trades/${encodeURIComponent(execId)}/return-to-inbox`, { method: "POST" });
}

/** Переносит сделку в целевую конструкцию. */
export function moveTrade(execId: string, constructionId: number): Promise<void> {
	return apiCommand(`/trades/${encodeURIComponent(execId)}/move`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ constructionId }),
	});
}

/** Добавляет внешнюю корректировку PnL. */
export function addAdjustment(constructionId: number, adjustment: AdjustmentInput): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/adjustments`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(adjustment),
	});
}

/** Правит внешнюю корректировку PnL. */
export function editAdjustment(adjustmentId: number, adjustment: AdjustmentInput): Promise<void> {
	return apiCommand(`/adjustments/${adjustmentId}`, {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(adjustment),
	});
}

/** Удаляет внешнюю корректировку PnL. */
export function deleteAdjustment(adjustmentId: number): Promise<void> {
	return apiCommand(`/adjustments/${adjustmentId}`, { method: "DELETE" });
}
