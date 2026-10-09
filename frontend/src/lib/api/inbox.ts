import { apiCommand, apiFetch } from "./http";

// Контракт раздела «Входящие» единого API: чтение непривязанных сделок,
// счётчик бейджа и команды разбора (привязка, создание, инкрементальная
// сборка) идут через /api/v1.
// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

/** Непривязанная сделка таблицы «Входящих». */
export interface InboxTrade {
	readonly execId: string;
	readonly symbol: string;
	readonly executedAt: string;
	readonly isBuy: boolean;
	readonly quantity: number;
	readonly price: number;
	readonly amountUsdt: number;
	readonly fee: number;
	readonly feeCurrency: string | null;
}

/** Компактная конструкция-цель для привязки выбранных сделок. */
export interface InboxTargetConstruction {
	readonly constructionId: number;
	readonly name: string;
	readonly status: "open" | "closed" | "archived";
	readonly totalPnL: number | null;
}

/** Снимок раздела «Входящие»: список сделок и целей привязки. */
export interface InboxOverview {
	readonly items: readonly InboxTrade[];
	readonly targets: readonly InboxTargetConstruction[];
}

/** Команда создания конструкции из выбранных сделок. */
export interface CreateInboxConstructionRequest {
	readonly name: string;
	readonly allocatedCapitalUsdt: number | null;
	readonly execIds: readonly string[];
}

/** Итог инкрементальной сборки из «Входящих». */
export interface AssembleInboxResult {
	readonly constructionsCount: number;
	readonly boundCount: number;
	readonly tradesInInbox: number;
}

/** Читает непривязанные сделки и список конструкций-целей. */
export function fetchInboxOverview(): Promise<InboxOverview> {
	return apiFetch<InboxOverview>("/inbox");
}

/** Читает счётчик непривязанных сделок для бейджа пункта «Входящие». */
export async function fetchInboxCount(): Promise<number> {
	const payload = await apiFetch<{ count: number }>("/inbox/count");
	return payload.count;
}

/** Массово привязывает выбранные сделки к целевой конструкции. */
export function bindInboxTrades(constructionId: number, execIds: readonly string[]): Promise<void> {
	return apiCommand("/inbox/bind", {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify({ constructionId, execIds }),
	});
}

/** Создаёт конструкцию из выбранных и привязывает к ней сделки. */
export function createConstructionFromInbox(request: CreateInboxConstructionRequest): Promise<{ constructionId: number }> {
	return apiFetch<{ constructionId: number }>("/inbox/create-construction", {
		method: "POST",
		headers: { "content-type": "application/json" },
		body: JSON.stringify(request),
	});
}

/** Запускает инкрементальную сборку «Собрать из Входящих». */
export function assembleInbox(): Promise<AssembleInboxResult> {
	return apiFetch<AssembleInboxResult>("/inbox/assemble", { method: "POST" });
}
