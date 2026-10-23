import { apiCommand, apiFetch } from "./http";
import type { TradeSource } from "./manual-trades";

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
	readonly quoteValue: number | null;
	readonly capitalUsagePercent: number | null;
	// Реальный риск открытых остатков идёт парой «величина + статус»:
	// число только при конечном риске, статус различает неограниченный
	// хвост и неполные данные без заглушки плановым риском.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	readonly realRiskUsdt: number | null;
	readonly realRiskStatus: RealRiskStatus;
	// Худший исход конструкции — знаковая величина рядом с реальным риском:
	// число при том же статусе finite, null при unbounded/unavailable.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	readonly worstCaseUsdt: number | null;
	// Сводная величина «с учётом зафиксированного синтетикой» —
	// «Реализованный результат» + Σ локов действующих (неоспоренных)
	// связок; null без действующих связок. Величина не меняет
	// инструментальные метрики и индикатор финрезультата.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/ui/screens/spec#requirement-card-synthetic-adjusted-total-row
	readonly syntheticAdjustedTotalUsdt: number | null;
	readonly openedAt: string | null;
	readonly closedAt: string | null;
	readonly durationSeconds: number | null;
	// Саммари времени жизни открытых опционных ног конструкции: остаток суток
	// до ближайшей экспирации, нетто-дневной распад и суммарная временная
	// стоимость; null без опционных ног или без тикеров — клиент рендерит
	// прочерком, красные сутки при ≤ 14 выводит сам.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	readonly daysToNearestExpiry: number | null;
	readonly netDailyDecayUsdt: number | null;
	readonly totalTimeValueUsdt: number | null;
	// Суммарная внутренняя стоимость открытых опционных остатков — третье
	// слагаемое саммари рядом с временной: null без опционных ног или без
	// тикеров, нога без тикеров исключается прочерком.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	readonly totalIntrinsicValueUsdt: number | null;
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
	readonly quoteValue: number | null;
	readonly priceChangePercent: number | null;
	// Распад остатка линейной ноги: пара не-null отличает синтетическое
	// содержимое ноги от нулевого инструментального остатка; null — у ноги
	// нет неоспоренного синтетического содержимого.
	// Traceability: openspec:ui/screens#scenario-detail-linear-leg-splits-synthetic-and-scalps
	readonly syntheticResidual: number | null;
	readonly otherResidual: number | null;
	// Величины времени жизни открытой опционной ноги: остаток суток до
	// экспирации, дневной распад и состав тултипа (внутренняя/временная
	// стоимость, вчерашний распад); null у закрытой позиции, фьючерсной
	// ноги и без тикеров — клиент рендерит прочерком.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
	readonly daysToExpiry: number | null;
	readonly dailyDecayUsdt: number | null;
	readonly intrinsicValueUsdt: number | null;
	readonly timeValueUsdt: number | null;
	readonly decayDayAgoUsdt: number | null;
	// Признак закрытия синтетикой опционной ноги: сумма количеств действующих
	// связок по символу ноги и остаток после вычета (может быть отрицательным);
	// null-пара — действующих связок по ноге нет: отсутствие, а не ноль.
	readonly syntheticClosedQuantity: number | null;
	readonly syntheticOpenQuantity: number | null;
}

/** Строка распознанного синтетического закрытия карточки. */
export interface ConstructionCardSyntheticClose {
	/** Символ закрываемой опционной ноги связки. */
	readonly closedLegSymbol: string;
	/** Знаковое количество линейной части связки. */
	readonly quantity: number;
	/** Величина паритетного лока к экспирации, USDT; от котировок не зависит. */
	readonly lockedValueUsdt: number;
	/** Признак ручного оспаривания: спорная связка в логический слой не входит. */
	readonly disputed: boolean;
	// Пара execId замыкает запись зеркально бэкенду: UI адресует связку
	// в готовых эндпоинтах спора и возврата, старшие потребители ответа
	// не ломаются.
	// Traceability: change:add-synthetic-close-dispute-ui/design#d1
	// Traceability: change:add-synthetic-close-dispute-ui/specs/ui/screens/spec#requirement-detail-synthetic-close-dispute-list
	/** Идентификатор линейной сделки связки — единый ключ «execId» либо «manual:{id}». */
	readonly linearExecId: string;
	/** Идентификатор встречной опционной сделки связки. */
	readonly counterOptionExecId: string;
	// Идентификатор ручной связки идёт рядом с парой: удаление адресуется
	// строкой снимка напрямую, клиентский сессионный реестр связок не нужен.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	/** Идентификатор строки ручной связки — ключ удаления; null у распознанных связок. */
	readonly manualLinkId: number | null;
}

/** Строка таблицы сделок карточки; источники различаются полем source. */
export interface ConstructionCardTrade {
	/** Ключ исполнения биржевой сделки; у ручной записи отсутствует. */
	readonly execId: string | null;
	readonly symbol: string;
	readonly executedAt: string;
	readonly isBuy: boolean;
	readonly quantity: number;
	readonly price: number;
	readonly amountUsdt: number;
	readonly fee: number;
	readonly comment: string | null;
	/** Источник записи: «биржа» либо «ручной ввод» — признак бейджа источника. */
	// Traceability: openspec:ui/screens#scenario-source-badge-shown
	readonly source: TradeSource;
	/** Идентификатор строки сырья ручной сделки для правки и удаления; null у биржевых. */
	readonly manualTradeId: number | null;
}

/** Строка таблицы закрывающих записей карточки. */
export interface ConstructionCardClosingEntry {
	readonly closedAt: string;
	readonly kind: ClosingEntryKind;
	readonly symbol: string;
	readonly quantity: number;
	readonly price: number | null;
	readonly amountUsdt: number | null;
	readonly manualAnnotationId: number | null;
}

/** Предупреждение об избыточной закрывающей записи. */
export interface ConstructionCardClosingWarning {
	readonly kind: ClosingEntryKind;
	readonly symbol: string;
	readonly closedAt: string;
}

/** Запись предупреждения диагностики незакрытых рисков карточки. */
export interface ConstructionCardRiskWarning {
	readonly text: string;
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
	readonly hasQuoteFailure: boolean;
	readonly quotesAsOf: string | null;
	readonly metrics: ConstructionCardMetrics;
	readonly positions: readonly ConstructionCardPosition[];
	readonly trades: readonly ConstructionCardTrade[];
	readonly closingEntries: readonly ConstructionCardClosingEntry[];
	readonly closingWarnings: readonly ConstructionCardClosingWarning[];
	readonly adjustments: readonly ConstructionCardAdjustment[];
	// Связки синтетического закрытия; пустой список — обычное состояние
	// конструкции без связок, строка «зафиксировано синтетикой» не выводится.
	// Traceability: openspec:ui/screens#requirement-detail-synthetic-close-line
	readonly syntheticCloses: readonly ConstructionCardSyntheticClose[];
	// Полный перечень предупреждений диагностики незакрытых рисков: секция
	// «Предупреждения» карточки перечисляет все записи, а пустой список
	// скрывает секцию целиком. Источник тот же вызов вычислителя, что у
	// счётчика бейджа списка и перечня превью, — расхождение наборов исключено.
	// Traceability: openspec:ui/screens#requirement-construction-warnings-display
	readonly riskWarnings: readonly ConstructionCardRiskWarning[];
}

/** Ручной статус конструкции (реэкспорт контракта раздела). */
export type ConstructionStatus = import("./constructions").ConstructionStatus;

/** Состояние реального риска (реэкспорт контракта раздела). */
export type RealRiskStatus = import("./constructions").RealRiskStatus;

/** Цель переноса сделки: активная конструкция без текущей. */
export interface ConstructionMoveTarget {
	readonly constructionId: number;
	readonly name: string;
}

/** Атрибуты ручной пометки закрытия: инструмент, время, цена и опциональное количество. */
export interface ManualCloseAnnotationInput {
	readonly symbol: string;
	readonly annotatedAt: string;
	readonly price: number | null;
	// Количество пометки: незаданное (null) закрывает весь остаток на момент
	// применения, заданное клэмпится остатком производным слоем.
	// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
	readonly quantity: number | null;
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

/** Читает последнюю известную котировку инструмента — дефолт формы пометки. */
export function fetchLastInstrumentQuote(constructionId: number, symbol: string): Promise<number | null> {
	return apiFetch<{ quote: number | null }>(
		`/constructions/${constructionId}/positions/${encodeURIComponent(symbol)}/last-mark`,
	).then((payload) => payload.quote);
}

/** Ставит ручную пометку закрытия позиции. */
export function addManualCloseAnnotation(constructionId: number, annotation: ManualCloseAnnotationInput): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/close-marks`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(annotation),
	});
}

/** Правит ручную пометку закрытия. */
export function editManualCloseAnnotation(annotationId: number, annotation: ManualCloseAnnotationInput): Promise<void> {
	return apiCommand(`/close-marks/${annotationId}`, {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(annotation),
	});
}

/** Удаляет ручную пометку закрытия. */
export function deleteManualCloseAnnotation(annotationId: number): Promise<void> {
	return apiCommand(`/close-marks/${annotationId}`, { method: "DELETE" });
}

/** Ключ связки для команд спора: пара execId линейного и встречного опционного исполнений. */
export interface SyntheticCloseDisputeInput {
	readonly linearExecId: string;
	readonly counterOptionExecId: string;
}

// Оспаривание распознанной связки: спор скрывает связку из логического слоя
// до явного возврата, карточка перечитывается ближайшим чтением и показывает
// связку с признаком спора.
// Traceability: change:add-synthetic-close-dispute-ui/specs/ui/screens/spec#requirement-detail-synthetic-close-dispute-list
/** Оспаривает распознанную синтетическую связку карточки. */
export function disputeSyntheticClose(constructionId: number, key: SyntheticCloseDisputeInput): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/synthetic-closes/dispute`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(key),
	});
}

// Возврат оспоренной связки: спор стирается, и связка снова входит
// в логический слой ближайшим чтением карточки.
// Traceability: change:add-synthetic-close-dispute-ui/specs/ui/screens/spec#requirement-detail-synthetic-close-dispute-list
/** Возвращает оспоренную синтетическую связку в логический слой. */
export function revertSyntheticClose(constructionId: number, key: SyntheticCloseDisputeInput): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/synthetic-closes/revert`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(key),
	});
}

/** Атрибуты ручной синтетической связки из формы деталей конструкции. */
export interface SyntheticCloseManualLinkInput {
	/** Идентификатор линейной сделки: execId биржевой либо ключ ручной «manual:{id}». */
	readonly linearTradeId: string;
	/** Идентификатор встречной опционной сделки — с теми же правилами. */
	readonly optionTradeId: string;
	/** Количество связки: не превышает количество ни одной из сделок пары. */
	readonly quantity: number;
}

/** Сохранённая ручная синтетическая связка в контракте API. */
export interface SyntheticCloseManualLinkRecord {
	/** Идентификатор ручной связки — ключ удаления. */
	readonly id: number;
	/** Идентификатор линейной сделки связки. */
	readonly linearTradeId: string;
	/** Идентификатор встречной опционной сделки связки. */
	readonly optionTradeId: string;
	/** Количество связки. */
	readonly quantity: number;
}

// Ручная синтетическая связка создаётся из двух сделок конструкции без
// 60-минутного окна; правила пары (одна конструкция, совпадение страйка
// и экспирации, согласованное количество) проверяет сервер, форма показывает
// человекочитаемую причину отказа. Тот же идентификатор приходит в строке
// связки снимка карточки — удаление адресуется списком напрямую.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
// Traceability: change:manual-journal-management/design#d5
/** Создаёт ручную синтетическую связку из линейной сделки и встречного опциона. */
export function createManualSyntheticLink(
	constructionId: number,
	request: SyntheticCloseManualLinkInput,
): Promise<SyntheticCloseManualLinkRecord> {
	return apiFetch<SyntheticCloseManualLinkRecord>(`/constructions/${constructionId}/synthetic-closes/manual-links`, {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(request),
	});
}

// Удаление свободно: связка исчезает из логического слоя ближайшим
// чтением карточки; несуществующий идентификатор сервер отвечает 404
// с человекочитаемой причиной.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
/** Удаляет ручную синтетическую связку по идентификатору из ответа создания. */
export function deleteManualSyntheticLink(constructionId: number, linkId: number): Promise<void> {
	return apiCommand(`/constructions/${constructionId}/synthetic-closes/manual-links/${linkId}`, {
		method: "DELETE",
	});
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
