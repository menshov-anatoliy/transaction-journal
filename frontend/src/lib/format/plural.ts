/** Три грамматические формы русского числительного для согласования со счётчиком. */
export interface RussianPluralForms {
	/** Форма для 1, 21, 101…: «конструкция», «открыта». */
	one: string;
	/** Форма для 2–4, 22–24…: «конструкции», «открыты». */
	few: string;
	/** Форма для 0, 5–20, 11–14…: «конструкций», «открыто». */
	many: string;
}

// Выбор формы числительного — единая точка правил русской грамматики для всех
// экранов: окончания зависят от двух младших разрядов числа, исключения —
// 11–14 всегда дают родительный падеж множественного числа.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
export function pluralForm(count: number, forms: RussianPluralForms): string {
	const value = Math.abs(count);
	const lastDigit = value % 10;
	const lastTwoDigits = value % 100;

	if (lastDigit === 1 && lastTwoDigits !== 11) {
		return forms.one;
	}

	if (lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14)) {
		return forms.few;
	}

	return forms.many;
}

// Подпись «число + слово в верной форме» одной строкой: счётчики шапки и
// бейджи навигации собираются без ручной сборки строк в компонентах.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
export function formatCount(count: number, forms: RussianPluralForms): string {
	return `${count} ${pluralForm(count, forms)}`;
}
