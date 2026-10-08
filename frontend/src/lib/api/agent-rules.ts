import { apiFetch } from "./http";

/** Read-only карточка корпуса правил в каталоге раздела «Агент». */
export interface AgentRuleListItem {
	readonly id: string;
	readonly title: string;
	readonly character: string;
	readonly clarity: string;
	readonly subject: string;
}

export interface AgentRulesListResponse {
	readonly items: readonly AgentRuleListItem[];
	readonly total: number;
}

export interface AgentRuleThreshold {
	readonly name: string;
	readonly value: string;
	readonly unit: string;
}

export interface AgentRuleSource {
	readonly tag: string;
	readonly file: string;
	readonly quotes: readonly string[];
}

export interface AgentRuleCard {
	readonly id: string;
	readonly title: string;
	readonly character: string;
	readonly clarity: string;
	readonly subject: string;
	readonly status: string;
	readonly scope: string;
	readonly technique: string | null;
	readonly triggerDescription: string | null;
	readonly actionDescription: string | null;
	readonly thresholds: readonly AgentRuleThreshold[];
	readonly sources: readonly AgentRuleSource[];
}

export interface AgentRulesQuery {
	readonly character?: string;
	readonly clarity?: string;
	readonly subject?: string;
	readonly search?: string;
}

// Read-only каталог корпуса правил с фильтрами и поиском в разделе «Агент».
// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
export function listRules(query: AgentRulesQuery, signal?: AbortSignal): Promise<AgentRulesListResponse> {
	const params = new URLSearchParams();
	if (query.character !== undefined && query.character !== "")
		params.set("character", query.character);
	if (query.clarity !== undefined && query.clarity !== "")
		params.set("clarity", query.clarity);
	if (query.subject !== undefined && query.subject !== "")
		params.set("subject", query.subject);
	if (query.search !== undefined && query.search.trim() !== "")
		params.set("search", query.search.trim());

	const suffix = params.size === 0 ? "" : `?${params.toString()}`;
	return apiFetch<AgentRulesListResponse>(`/rules${suffix}`, { signal });
}

/** Полная карточка правила для попапа из чата и правой панели. */
export function readRule(ruleId: string, signal?: AbortSignal): Promise<AgentRuleCard> {
	return apiFetch<AgentRuleCard>(`/rules/${encodeURIComponent(ruleId)}`, { signal });
}

