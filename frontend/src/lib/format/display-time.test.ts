import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { DASH } from "./degradation";
import { formatDay, formatMoment } from "./display-time";

// Локальное время: моменты показываются в часовой зоне браузера в формате
// «2026-06-20 14:30» (перенос №17). Тест принудительно ставит зону
// Asia/Kolkata (+05:30) — получасовое смещение ловит форматирование в UTC.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

const originalTimeZone = process.env.TZ;

beforeAll(() => {
	process.env.TZ = "Asia/Kolkata";
});

afterAll(() => {
	process.env.TZ = originalTimeZone;
});

describe("формат момента в локальной зоне браузера", () => {
	it("UTC-мгновение показывается стеновым временем зоны браузера", () => {
		// Act: мгновение 09:00 UTC форматируется для показа.
		const text = formatMoment("2026-06-20T09:00:00Z");

		// Assert: в зоне +05:30 стеновое время — 14:30, а не UTC-время.
		expect(text).toBe("2026-06-20 14:30");
	});

	it("смещение во входной строке не меняет мгновение", () => {
		// Act: то же мгновение 09:00 UTC записано со смещением +02:00.
		const text = formatMoment("2026-06-20T11:00:00+02:00");

		// Assert: показ всё равно в зоне браузера.
		expect(text).toBe("2026-06-20 14:30");
	});

	it("готовый Date тоже форматируется локально", () => {
		// Act: мгновение передано объектом Date.
		const text = formatMoment(new Date("2026-06-20T09:00:00Z"));

		// Assert: источником может быть и строка, и Date — результат один.
		expect(text).toBe("2026-06-20 14:30");
	});

	it.each([
		null,
		undefined,
		"не-дата",
	])("отсутствующий или мусорный вход %p даёт прочерк", (value) => {
		// Act: вход без данных либо неразборная строка форматируются для таблицы.
		const text = formatMoment(value);

		// Assert: прочерк по контракту деградации — без «Invalid Date» на экране.
		expect(text).toBe(DASH);
	});
});

describe("календарный день в локальной зоне браузера", () => {
	it("день считается по стеновому времени, а не по UTC", () => {
		// Act: мгновение 20:00 UTC в зоне +05:30 — уже следующий день.
		const text = formatDay("2026-06-20T20:00:00Z");

		// Assert: локальный день — 21 июня.
		expect(text).toBe("2026-06-21");
	});

	it.each([
		null,
		undefined,
	])("отсутствующее значение %p даёт прочерк", (value) => {
		// Act: день без данных форматируется для группировок и форм.
		const text = formatDay(value);

		// Assert: прочерк вместо пустоты.
		expect(text).toBe(DASH);
	});
});
