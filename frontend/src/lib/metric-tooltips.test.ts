import { describe, expect, it } from "vitest";
import { METRIC_TOOLTIPS, type MetricTooltipKey } from "./metric-tooltips";

// Проверяется справочник текстов подсказок метрик: каждая запись имеет
// заголовок и назначение, даты и времена в справочник не входят, метод
// расчёта присутствует только у метрик с зафиксированным в источниках
// расчётом, а многопоказательные метрики дают раздел на каждый показатель.
const KEYS = Object.keys(METRIC_TOOLTIPS) as MetricTooltipKey[];

describe("справочник подсказок метрик", () => {
	it("каждая запись имеет непустые заголовок, назначение и заполненные разделы", () => {
		// Подсказка всегда называет назначение метрики; записи и их разделы
		// не бывают пустыми — тексты выводятся из зафиксированных источников.
		// Traceability: openspec:ui/screens#requirement-metric-tooltip-text-sourced

		// Act/Assert: у каждой записи непустые title и purpose,
		// у каждого раздела — непустые label и text.
		for (const key of KEYS) {
			const tooltip = METRIC_TOOLTIPS[key];
			expect(tooltip.title.trim().length).toBeGreaterThan(0);
			expect(tooltip.purpose.trim().length).toBeGreaterThan(0);
			for (const section of tooltip.sections ?? []) {
				expect(section.label.trim().length).toBeGreaterThan(0);
				expect(section.text.trim().length).toBeGreaterThan(0);
			}
		}
	});

	it("справочник не содержит подсказок для дат и времён", () => {
		// Даты и времена (период конструкции, «котировки на», открытие
		// и закрытие) подсказками метрик не снабжаются.
		// Traceability: openspec:ui/screens#scenario-dates-skip-tooltip

		// Assert: ключи дат и времён отсутствуют в справочнике.
		expect(KEYS).not.toContain("period");
		expect(KEYS).not.toContain("quotesAt");
		expect(KEYS).not.toContain("openedAt");
		expect(KEYS).not.toContain("closedAt");
	});

	it("метод расчёта присутствует только у метрик с зафиксированным расчётом", () => {
		// «Изм., %» и «занято капитала, %» имеют метод, зафиксированный
		// в спецификации; сырая колонка сделок метода не получает —
		// формула без источника не утверждается.
		// Traceability: openspec:ui/screens#scenario-tooltip-method-from-spec
		// Traceability: openspec:ui/screens#scenario-tooltip-no-unfixed-method

		// Assert: метод есть там, где источник его фиксирует, и отсутствует
		// у сырых колонок сделок.
		expect(METRIC_TOOLTIPS.priceChangePercent.method).toBeTruthy();
		expect(METRIC_TOOLTIPS.capitalUsage.method).toBeTruthy();
		expect(METRIC_TOOLTIPS.tradeQuantity.method).toBeUndefined();
		expect(METRIC_TOOLTIPS.tradePrice.method).toBeUndefined();
	});

	it("многопоказательные метрики раскрывают раздел на каждый показатель", () => {
		// Ячейка «% капитала (N USDT)» даёт разделы о проценте и о капитале,
		// «Итог журнала» — о реализованной и нереализованной частях,
		// заголовок «Реализ. P&L» — о значении и проценте колонки.
		// Traceability: openspec:ui/screens#scenario-multi-indicator-sections
		// Traceability: openspec:ui/screens#scenario-column-tooltip-sections-per-indicator

		// Assert: по разделу на каждый показатель ячейки или колонки.
		expect(METRIC_TOOLTIPS.capitalPercent.sections).toHaveLength(2);
		expect(METRIC_TOOLTIPS.journalTotal.sections).toHaveLength(2);
		expect(METRIC_TOOLTIPS.positionRealizedPnl.sections).toHaveLength(2);
		expect(METRIC_TOOLTIPS.listTotal.sections).toHaveLength(2);
	});
});
