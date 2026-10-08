import { apiCommand, apiFetch } from "./http";

// Контракты подсказок единого API: панель субъекта правой области, кнопка
// ручного прохода агента и команды жизненного цикла карточек.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Статус жизненного цикла подсказки. */
export type HintStatus = "new" | "applied" | "dismissed" | "expired";

/** Вид субъекта подсказки. */
export type HintSubjectKind = "journal" | "construction";

/** Субъект панели подсказок. */
export interface HintSubject {
	readonly kind: HintSubjectKind;
	readonly constructionId: number | null;
}

/** Тег источника подсказки с цитатами-доказательствами. */
export interface HintSourceTag {
	readonly tag: string;
	readonly file: string;
	readonly quotes: readonly string[];
}

/** Карточка подсказки полного состава. */
export interface HintRecord {
	readonly id: number;
	readonly ruleId: string;
	readonly character: string;
	readonly clarity: string;
	readonly sources: readonly HintSourceTag[];
	readonly text: string;
	readonly facts: Readonly<Record<string, string>>;
	readonly asOf: string;
	readonly status: HintStatus;
	readonly firstSeenAt: string | null;
}

/** Определение группы справочника v1. */
export interface HintGroupDefinition {
	readonly id: string;
	readonly title: string;
}

/** Группа живых подсказок панели. */
export interface HintGroup {
	readonly group: HintGroupDefinition;
	readonly hints: readonly HintRecord[];
}

/** Панель подсказок субъекта правой области. */
export interface HintPanel {
	readonly subject: HintSubject;
	readonly liveGroups: readonly HintGroup[];
	readonly history: readonly HintRecord[];
	readonly liveCount: number;
}

/** Исход ручного прохода агента подсказок. */
export type HintPassOutcome = "completed" | "skipped-market-unavailable" | "corpus-invalid";

/** Итог ручного прохода агента подсказок. */
export interface HintPassResult {
	readonly outcome: HintPassOutcome;
	readonly asOf: string;
	readonly createdHints: number;
	readonly expiredHints: number;
	readonly diagnostics: readonly string[] | null;
	readonly unimplementedRuleIds: readonly string[];
}

/** Читает панель подсказок журнала — правую область без выделения. */
export function fetchJournalHintsPanel(): Promise<HintPanel> {
	return apiFetch<HintPanel>("/hints/panel?subject=journal");
}

/** Читает панель подсказок конструкции — превью выделенной строки. */
export function fetchConstructionHintsPanel(constructionId: number): Promise<HintPanel> {
	return apiFetch<HintPanel>(`/hints/panel?subject=construction&constructionId=${constructionId}`);
}

/** Запускает ручной проход агента подсказок. */
export function runHintsPass(): Promise<HintPassResult> {
	return apiFetch<HintPassResult>("/hints/pass", { method: "POST" });
}

/** Результат команды перевода подсказки. */
export interface HintTransition {
	readonly hintId: number;
	readonly transitioned: boolean;
}

/** Переводит живую подсказку в applied («Применено»). */
export function applyHint(hintId: number): Promise<HintTransition> {
	return apiFetch<HintTransition>(`/hints/${hintId}/apply`, { method: "POST" });
}

/** Переводит живую подсказку в dismissed («Отклонено»). */
export function dismissHint(hintId: number): Promise<HintTransition> {
	return apiFetch<HintTransition>(`/hints/${hintId}/dismiss`, { method: "POST" });
}

/** Помечает первый показ подсказки; момент ставит сервер. */
export function markHintSeen(hintId: number): Promise<void> {
	// Команда отвечает 204 без тела: пустой ответ не разбирается как JSON.
	return apiCommand(`/hints/${hintId}/seen`, { method: "POST" });
}
