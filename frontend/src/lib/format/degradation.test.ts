import { describe, expect, it } from "vitest";
import { DASH, QUOTES_UNAVAILABLE_LABEL, degrade } from "./degradation";
import { formatSignedAmount } from "./quantity";

// Сквозной паттерн деградации при сбое котировок: недоступная величина
// показывается прочерком с признаком сбоя, но никогда не подменяется нулём —
// «нет данных ≠ ноль» (перенос №2, без изменения семантики величин).
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

describe("недоступная величина из-за сбоя котировок", () => {
	it.each([
		null,
		undefined,
	])("отсутствующее значение %p даёт прочерк, а не число", (value) => {
		// Act: величина без данных прогоняется через форматтер знаковой суммы.
		const degraded = degrade(value, formatSignedAmount);

		// Assert: контракт «нет данных ≠ ноль» — прочерк и признак сбоя,
		// но не «0», «+0» и не пустая строка.
		expect(degraded.available).toBe(false);
		expect(degraded.text).toBe(DASH);
		expect(degraded.text).not.toBe("0");
		expect(degraded.reason).toBe(QUOTES_UNAVAILABLE_LABEL);
	});

	it("причину сбоя можно уточнить для конкретного места показа", () => {
		// Act: недоступная величина получает свою человекочитаемую причину.
		const degraded = degrade(null, formatSignedAmount, "итог неполный: нереализованная часть не оценена");

		// Assert: причина доезжает до тултипа/подписи без костылей в компонентах.
		expect(degraded.reason).toBe("итог неполный: нереализованная часть не оценена");
	});
});

describe("доступная величина проходит форматтер без изменений семантики", () => {
	it("число превращается в готовую строку со знаком", () => {
		// Act: доступное значение форматируется штатным форматтером.
		const degraded = degrade(214.32, formatSignedAmount);

		// Assert: значение доступно, текст — обычный знаковый формат.
		expect(degraded.available).toBe(true);
		expect(degraded.text).toBe("+214.32");
	});

	it("ноль — это данные, а не отсутствие котировок", () => {
		// Act: нулевая величина прогоняется через слой деградации.
		const degraded = degrade(0, formatSignedAmount);

		// Assert: семантика величины сохраняется — ноль остаётся нулём.
		expect(degraded.available).toBe(true);
		expect(degraded.text).toBe("0");
	});
});
