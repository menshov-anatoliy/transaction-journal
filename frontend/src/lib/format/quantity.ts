// Знаковые и абсолютные форматы величин журнала: суммы USDT, проценты от
// капитала, количества. Разделитель — точка; дробная часть без хвостовых
// нулей; рост — с явным «плюсом», падение — с «минусом», ноль — без знака.
// Слой один на все экраны, чтобы подписи не разъезжались между сводкой,
// таблицами и индикатором (перенос №25).
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

/** Максимальное число дробных знаков суммы по умолчанию — как в сводке списка конструкций. */
const AMOUNT_FRACTION_DIGITS = 2;

/** Максимальное число дробных знаков процента от капитала. */
const PERCENT_FRACTION_DIGITS = 1;

/** Убирает хвостовые нули дробной части и точку без остатка: «0.50» → «0.5», «3.00» → «3». */
function trimFraction(digits: string): string {
	if (!digits.includes(".")) {
		return digits;
	}

	return digits.replace(/0+$/, "").replace(/\.$/, "");
}

/** Округляет до заданного числа дробных знаков без хвостовых нулей. */
function formatNumber(value: number, fractionDigits: number): string {
	return trimFraction(value.toFixed(fractionDigits));
}

/** Формат суммы без обязательного знака: «3000» / «214.32» / «0.5»; отрицательное сохраняет минус. */
export function formatAmount(value: number, fractionDigits: number = AMOUNT_FRACTION_DIGITS): string {
	return formatNumber(value, fractionDigits);
}

/** Формат знаковой суммы USDT: «+214.32» / «-58.2» / «0». */
export function formatSignedAmount(value: number, fractionDigits: number = AMOUNT_FRACTION_DIGITS): string {
	return formatSign(value, formatNumber(value, fractionDigits));
}

/** Формат знакового процента: «+8.6%» / «-3.1%» / «0%». */
export function formatSignedPercent(value: number): string {
	return `${formatSign(value, formatNumber(value, PERCENT_FRACTION_DIGITS))}%`;
}

/** Ставит «плюс» перед ненулевым положительным числом; ноль остаётся без знака. */
function formatSign(value: number, magnitude: string): string {
	if (value > 0) {
		return `+${magnitude}`;
	}

	return magnitude;
}
