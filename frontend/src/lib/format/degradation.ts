// Сквозной паттерн деградации при сбое котировок: единый способ показать
// «котировка недоступна» во всех разделах. Семантика величин не меняется —
// недоступное значение показывается прочерком с признаком сбоя, а ноль
// остаётся нулём (перенос №2).
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

/** Прочерк недоступной величины: «нет данных», а не ноль и не пустота. */
export const DASH = "—";

/** Единая метка сбоя котировок для прочерков, пометок строк и тултипов. */
export const QUOTES_UNAVAILABLE_LABEL = "сбой котировок";

/** Величина к показу: доступная несёт готовый текст, недоступная — прочерк и причину. */
export interface DegradedText {
	/** Готовая к показу строка: значение либо прочерк. */
	readonly text: string;
	/** Доступна ли величина: ложь означает сбой котировок, а не ноль. */
	readonly available: boolean;
	/** Человекочитаемая причина недоступности — для тултипа или подписи. */
	readonly reason?: string;
}

// Оборачивает значение форматтером с единым контрактом деградации: null и
// undefined никогда не доходят до числового форматтера и не превращаются
// в «0»/«+0» — отсутствие данных показывается прочерком с причиной.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
export function degrade<T>(
	value: T | null | undefined,
	format: (value: T) => string,
	reason: string = QUOTES_UNAVAILABLE_LABEL,
): DegradedText {
	if (value === null || value === undefined) {
		return { text: DASH, available: false, reason };
	}

	return { text: format(value), available: true };
}
