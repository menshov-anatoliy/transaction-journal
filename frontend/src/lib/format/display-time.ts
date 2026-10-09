import { DASH } from "./degradation";

// Локальное время журнала: моменты показываются в часовой зоне браузера.
// Форматы унаследованы от таблиц журнала — «2026-06-20 14:30» и календарный
// день «2026-06-20»; отсутствующее значение — прочерк (перенос №17).
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

/** Вход момента: ISO-строка из API или готовый Date. */
export type MomentInput = string | Date;

/** Дополняет число нулями слева: «7» → «07». */
function pad(value: number, length: number): string {
	return String(value).padStart(length, "0");
}

/** Разбирает вход в Date; неразборный или отсутствующий вход даёт null. */
function parseMoment(value: MomentInput | null | undefined): Date | null {
	if (value === null || value === undefined) {
		return null;
	}

	const parsed = value instanceof Date ? value : new Date(value);
	return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** Дата-время записи: «2026-06-20 14:30» в локальной зоне браузера; отсутствие — прочерк. */
export function formatMoment(value: MomentInput | null | undefined): string {
	const moment = parseMoment(value);
	if (moment === null) {
		return DASH;
	}

	const local = [
		pad(moment.getFullYear(), 4),
		pad(moment.getMonth() + 1, 2),
		pad(moment.getDate(), 2),
	].join("-");
	const clock = [pad(moment.getHours(), 2), pad(moment.getMinutes(), 2)].join(":");

	return `${local} ${clock}`;
}

/** Календарный день записи: «2026-06-20» по локальному дню мгновения; отсутствие — прочерк. */
export function formatDay(value: MomentInput | null | undefined): string {
	const moment = parseMoment(value);
	if (moment === null) {
		return DASH;
	}

	return [
		pad(moment.getFullYear(), 4),
		pad(moment.getMonth() + 1, 2),
		pad(moment.getDate(), 2),
	].join("-");
}
