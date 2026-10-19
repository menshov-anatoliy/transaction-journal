import { apiCommand, apiFetch } from "./http";

// Контракт ручных сделок единого API: внесение во «Входящие» либо сразу в
// конструкцию, правка атрибутов и удаление. Валидация значений (дата не в
// будущем, количество и цена больше нуля, комиссия не отрицательная) живёт
// на сервере — клиент показывает человекочитаемую причину отказа формы.
// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui

/** Источник сделки журнала: биржевая запись или ручной ввод. */
export type TradeSource = "exchange" | "manual";

/** Сторона ручной сделки биржевым значением контракта API. */
export type ManualTradeSide = "Buy" | "Sell";

/** Атрибуты ручной сделки из формы ручного ввода. */
export interface ManualTradeInput {
	/** Момент исполнения в ISO-8601; дата в будущем отклоняется сервером. */
	readonly executedAt: string;
	/** Инструмент сделки; символ разрешается справочником на сервере. */
	readonly symbol: string;
	/** Сторона сделки: Buy или Sell. */
	readonly side: ManualTradeSide;
	/** Количество сделки: положительное число. */
	readonly quantity: number;
	/** Цена сделки: положительное число. */
	readonly price: number;
	/** Комиссия в USDT; null — комиссии нет. */
	readonly feeUsdt: number | null;
}

/** Сохранённая ручная сделка в контракте API. */
export interface ManualTradeRecord {
	/** Идентификатор строки сырья ручной сделки. */
	readonly id: number;
	/** Привязка к конструкции; null — сделка во «Входящие». */
	readonly constructionId: number | null;
}

/** Вносит ручную сделку; constructionId null — в общий пул «Входящих». */
export function createManualTrade(
	request: ManualTradeInput & { constructionId: number | null },
): Promise<ManualTradeRecord> {
	return apiFetch<ManualTradeRecord>("/manual-trades", {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(request),
	});
}

/** Правит атрибуты ручной сделки; привязка правкой не меняется. */
export function editManualTrade(id: number, request: ManualTradeInput): Promise<ManualTradeRecord> {
	return apiFetch<ManualTradeRecord>(`/manual-trades/${id}`, {
		method: "PUT",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(request),
	});
}

/** Удаляет ручную сделку из любого места — из «Входящих» и из конструкции. */
export function deleteManualTrade(id: number): Promise<void> {
	return apiCommand(`/manual-trades/${id}`, { method: "DELETE" });
}
