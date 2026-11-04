import type { RealRiskStatus } from "../api/constructions";
import type { ResultMarkerOperation } from "../api/construction-card";

// Калькулятор геометрии индикатора финансового результата конструкции.
// Чистая функция без React: переводит плановые границы, маркеры изменения
// результата и текущий результат в доли шкалы 0..1, подписи и размещения
// меток. Рендер (полный, средний и компактный индикаторы) строится поверх
// этих долей и не считает ничего сам — одна геометрия на все представления.
// Шкала: зона риска от −Риск до 0, зона планового профита от 0 до +Профит;
// при выходе точки за плановые границы шкала расширяется до истинного
// положения — охват строится по плановым границам, маркеру текущего
// результата и всем меткам истории, клипа крайних значений нет.
// Маркер — позиция текущего результата (реализованный + нереализованный +
// внешние корректировки PnL); маркеры изменения результата — накопленный
// зафиксированный результат на момент события журнала; результат рисуется
// одной заливкой без разложения на реализованную и нереализованную части.
// Бейджи маркеров стоят над полосой и при коллизии разносятся вертикально.
// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#requirement-risk-profit-hint

/** Состояние риска без числовой границы. */
type RiskState = Exclude<RealRiskStatus, "finite">;

/** Точка истории результата из контракта API: позиция на шкале считается по накопленному итогу. */
// Карточка публикует маркеры полями eventTime/kind/changeUsdt, список и
// превью — последний маркер полями totalUsdt/eventAt; общая часть — итог.
// Рядом с каждым маркером идёт расшифровка его изменения перечнем операций
// (включая события, подавленные порогом) — подсказка бейджа сверяется
// сложением; фронтенд перечни не реконструирует.
// Traceability: change:fix-finresult-marker-contributions/design#d4
export interface FinResultHistoryMarker {
	/** Накопленный зафиксированный результат на момент события, USDT. */
	readonly totalUsdt: number;
	/** Порядковый номер среди выведенных; нумерацию даёт контракт API. */
	readonly number?: number;
	/** Время события журнала, ISO — поле контракта карточки. */
	readonly eventTime?: string;
	/** Время события журнала, ISO — поле контракта списка и превью. */
	readonly eventAt?: string;
	/** Тип события журнала — строка из контракта API. */
	readonly kind?: string;
	/** Изменение от предыдущего выведенного маркера, USDT. */
	readonly changeUsdt?: number;
	/** Операции расшифровки изменения: вид, дата, вклад. */
	readonly operations?: readonly ResultMarkerOperation[];
}

/** Вход калькулятора: изначальные показатели конструкции и текущий результат. */
export interface FinResultInput {
	/** Плановый риск конструкции в USDT; null — не задан. */
	readonly plannedRisk: number | null;
	/** Плановый профит конструкции в USDT; null — не задан. */
	readonly plannedProfit: number | null;
	/** Реализованный результат закрытых частей позиций в USDT; null — данных нет. */
	readonly realized: number | null;
	/**
	 * Нереализованный результат открытых остатков в USDT; null — открытых
	 * остатков нет либо их оценка недоступна из-за сбоя котировок
	 * (различает пара hasOpenResidual + quotesDegraded).
	 */
	readonly unrealized: number | null;
	/**
	 * Сумма внешних корректировок PnL конструкции в USDT (правки вне
	 * реализованных сделок и открытых остатков); 0 — корректировок нет.
	 * Входит в текущий результат наравне с реализованной и нереализованной
	 * частями и доступна независимо от сбоя котировок.
	 */
	readonly adjustments: number;
	/** Сбой котировок: нереализованная оценка недоступна, индикатор неполный. */
	readonly quotesDegraded: boolean;
	// Неполнота индикатора считается только при открытых остатках: сбой
	// котировок скрывает нереализованную часть, лишь когда она существует.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	readonly hasOpenResidual: boolean;
	/**
	 * Состояние реального риска из контракта API: finite — конечный риск,
	 * unbounded — неограниченный хвост, unavailable — расчёт не удался.
	 * Координаты на шкале не задаёт: состояние выводится вне полосы.
	 * Traceability: change:show-unbounded-finresult-risk/design#d1
	 */
	readonly realRiskStatus: RealRiskStatus;
	/**
	 * Выведенные маркеры изменения результата в хронологическом порядке —
	 * уже отфильтрованные порогом и перенумерованные сервером; значение —
	 * накопленный зафиксированный результат на момент события, от котировок
	 * не зависит. Пустой список — истории ещё нет.
	 * Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-contract
	 */
	readonly resultMarkers: readonly FinResultHistoryMarker[];
}

/** Диапазон шкалы в долях 0..1; from не больше to. */
export interface FinResultSpan {
	readonly from: number;
	readonly to: number;
}

/** Сторона полосы, на которой стоит метка. */
export type FinResultLabelSide = "above" | "below";

/** Выравнивание метки относительно её позиции на шкале. */
export type FinResultLabelAlign = "center" | "start" | "end";

/** Размещение метки: сторона полосы, вертикальный уровень и выравнивание. */
export interface FinResultLabelPlacement {
	readonly side: FinResultLabelSide;
	readonly level: number;
	readonly align: FinResultLabelAlign;
}

/** Размещение бейджа маркера изменения результата; бейдж всегда над полосой. */
export interface FinResultBadgePlacement {
	readonly level: number;
	readonly align: FinResultLabelAlign;
}

/**
 * Метрики разметки меток: ширина полосы, ширина одного символа подписи
 * пресета и габариты бейджа маркера. Без метрик разнос не считается
 * (компактный вид меток не имеет).
 */
export interface FinResultLabelLayout {
	/** Ширина полосы индикатора в px — для перевода ширин меток в доли. */
	readonly scaleWidthPx: number;
	/** Ширина символа подписей делений шкалы (над полосой), px. */
	readonly aboveCharWidthPx: number;
	/** Ширина символа подписей маркера (под полосой), px. */
	readonly belowCharWidthPx: number;
	/** Несумма бейджа без текста суммы: кружок номера, зазор и паддинги пилюли, px. */
	readonly badgeChromePx: number;
	/** Высота бейджа в строках модели разноса: ceil(высоты пилюли / строки). */
	readonly badgeRows: number;
}

/** Размещения всех меток индикатора по ключам. */
export type FinResultLabelPlacements = {
	[K in LabelKey]: FinResultLabelPlacement | null;
};

/** Ключ метки в геометрии. */
type LabelKey = "risk" | "zero" | "profit" | "marker";

/** Точка маркера изменения результата на шкале: позиция, итог и тон по знаку. */
export interface FinResultMarkerPoint {
	/** Истинная позиция накопленного итога в долях шкалы — без клипа. */
	readonly x: number;
	/** Накопленный зафиксированный итог маркера, USDT. */
	readonly total: number;
	/** Тон бейджа по знаку итога: акцент — плюс, риск — минус, ноль — нейтральный. */
	readonly tone: "positive" | "negative" | "zero";
}

/** Итоговая геометрия индикатора в долях шкалы и готовые подписи. */
export interface FinResultGeometry {
	/** Нейтральная полоса: плановые границы не заданы, зон нет. */
	readonly neutral: boolean;
	/** «Неполный» индикатор: сбой котировок скрыл нереализованную часть открытых остатков. */
	readonly incomplete: boolean;
	/** Минимум шкалы в USDT (с учётом расширения под точки). */
	readonly scaleMin: number;
	/** Максимум шкалы в USDT (с учётом расширения под точки). */
	readonly scaleMax: number;
	/** Позиция нуля шкалы в долях. */
	readonly zeroAt: number;
	/** Зона планового риска; null — риск не задан или полоса нейтральная. */
	readonly riskZone: FinResultSpan | null;
	/** Зона планового профита; null — профит не задан или полоса нейтральная. */
	readonly profitZone: FinResultSpan | null;
	/** Золотая зона сверхприбыли за плановым профитом; null — превышения нет. */
	readonly superZone: FinResultSpan | null;
	/**
	 * Состояние реального риска: различает конечный риск, неограниченный
	 * хвост и нехватку данных; пробрасывается из входа как есть.
	 * Traceability: change:show-unbounded-finresult-risk/design#d1
	 */
	readonly realRiskStatus: RealRiskStatus;
	/** Позиция маркера итога; null — данных нет. */
	readonly markerAt: number | null;
	/** Единая заливка итога — от нуля до итога либо до планового профита. */
	readonly fillMain: FinResultSpan | null;
	/** Тон маркера и подписей: положительный итог — зелёный, иначе красный. */
	readonly tone: "positive" | "negative";
	/** Точки маркеров изменения результата в хронологическом порядке; позиция истинная, без клипа. */
	readonly markerPoints: readonly FinResultMarkerPoint[];
	/** Готовые подписи индикатора. */
	readonly labels: {
		readonly risk: string | null;
		readonly zero: string;
		readonly profit: string | null;
		/** Текст состояния риска вне шкалы; null — риск конечный. */
		readonly riskState: string | null;
		readonly marker: string | null;
	};
	/**
	 * Разнос меток: сторона, вертикальный уровень и выравнивание каждой;
	 * null — разнос не считался (метрики разметки не переданы).
	 */
	readonly placements: FinResultLabelPlacements | null;
	/**
	 * Разнос бейджей маркеров по индексам markerPoints: вертикальный
	 * уровень над полосой и выравнивание; null — разнос не считался.
	 */
	readonly badgePlacements: readonly FinResultBadgePlacement[] | null;
}

/**
 * Считает геометрию индикатора финрезультата в долях шкалы. Бросает ошибку
 * на NaN и бесконечностях во входных числах. Без layout метки получают
 * только текст (разнос не считается).
 */
export function computeFinResultGeometry(
	input: FinResultInput,
	layout?: FinResultLabelLayout | null,
): FinResultGeometry {
	assertFinite("plannedRisk", input.plannedRisk);
	assertFinite("plannedProfit", input.plannedProfit);
	assertFinite("realized", input.realized);
	assertFinite("unrealized", input.unrealized);
	assertFinite("adjustments", input.adjustments);
	for (const marker of input.resultMarkers) {
		assertFinite("resultMarkers[].totalUsdt", marker.totalUsdt);
	}

	// Нереализованная часть существует только при открытых остатках и
	// доступных котировках: сбой скрывает её, не подменяя нулём.
	const unrealized = input.unrealized !== null && !input.quotesDegraded ? input.unrealized : null;
	const realized = input.realized;
	// Текущий результат конструкции — реализованная часть плюс доступная
	// нереализованная и внешние корректировки PnL; без реализованной части
	// результата нет вовсе. Все производные величины (шкала, золотая зона,
	// заливка, позиция маркера) строятся только от этого total.
	// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-includes-adjustments
	const total = realized === null ? null : realized + (unrealized ?? 0) + input.adjustments;
	// Неполный индикатор — сбой котировок именно при открытых остатках: без
	// остатков нереализованной части не существует, скрывать нечего.
	// Комбинация приходит из read-моделей: сбой котировок бэкенд отдаёт вместе
	// с null нереализованной части только при открытых остатках.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	const incomplete = input.quotesDegraded && input.hasOpenResidual;

	// Состояние риска без координаты на шкале: unbounded/unavailable
	// выводятся отдельно от полосы, плановая граница подписывается
	// плановым риском, а маркеры истории и маркер итога сохраняются —
	// от состояния риска и котировок они не зависят.
	// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
	// Traceability: openspec:ui/screens#scenario-finresult-unavailable-risk
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	const riskState: RiskState | null =
		input.realRiskStatus === "unbounded" || input.realRiskStatus === "unavailable"
			? input.realRiskStatus
			: null;

	// Нейтральная полоса: плановых границ нет — зоны, деления, маркер
	// текущего результата и маркеры истории отсутствуют, заполнение идёт
	// от нуля до края в сторону знака итога; состояние риска остаётся
	// в геометрии отдельно от полосы.
	if (input.plannedRisk === null && input.plannedProfit === null) {
		return buildNeutralGeometry(
			realized,
			total,
			input.adjustments,
			input.quotesDegraded,
			incomplete,
			input.realRiskStatus,
			riskState,
			layout,
		);
	}

	// Шкала: базовые зоны из плановых показателей, охват расширяется до
	// истинного положения маркера итога и всех меток истории — клипа
	// крайних значений нет. Сверхприбыль маркеров истории золотую зону
	// не создаёт: участки истории сверх +Профит расширяют шкалу, но не
	// красятся золотом.
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-scale-expands-to-markers-no-clip
	const markerTotals = input.resultMarkers.map((marker) => marker.totalUsdt);
	const scale = resolveScale(input.plannedRisk, input.plannedProfit, total, markerTotals);
	const toFraction = (value: number): number => (value - scale.min) / (scale.max - scale.min);

	const zeroAt = toFraction(0);
	const riskZone = input.plannedRisk !== null ? { from: 0, to: zeroAt } : null;
	const profitZone =
		input.plannedProfit !== null ? spanOf(zeroAt, toFraction(input.plannedProfit)) : null;
	// Золотая зона сверхприбыли возникает только когда итог превысил
	// плановый профит, и занимает непересекающийся с зелёной заливкой
	// участок от профита до итога.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	const superZone =
		input.plannedProfit !== null && total !== null && total > input.plannedProfit
			? spanOf(toFraction(input.plannedProfit), toFraction(total))
			: null;

	// Маркер итога стоит в истинном положении оценки ликвидации: шкала
	// всегда охватывает итог, поэтому клип и прижатие не нужны.
	const markerAt = total === null ? null : toFraction(total);

	// Точки маркеров изменения результата: истинные позиции накопленных
	// итогов на расширенной шкале, тон бейджа — по знаку итога.
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-result-markers-badges-above-bar
	const markerPoints: readonly FinResultMarkerPoint[] = input.resultMarkers.map((marker) => ({
		x: toFraction(marker.totalUsdt),
		total: marker.totalUsdt,
		tone: marker.totalUsdt > 0 ? "positive" : marker.totalUsdt < 0 ? "negative" : "zero",
	}));

	// Единая заливка итога: неположительный итог — один красный тон от нуля
	// до маркера, без разложения на реализованную и нереализованную части;
	// положительный итог — зелёный участок до планового профита, а золотой
	// участок за ним рисует superZone, слои не перекрываются.
	// Traceability: openspec:ui/screens#scenario-finresult-negative-total-single-fill
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	const fillMain =
		total === null
			? null
			: spanOf(
					zeroAt,
					toFraction(
						input.plannedProfit === null ? total : Math.min(total, input.plannedProfit),
					),
				);

	const markerLabel = resolveMarkerLabel(
		realized,
		unrealized,
		total,
		input.adjustments,
		input.quotesDegraded,
	);

	const labels = {
		risk: resolveRiskTickLabel(input.plannedRisk, riskState),
		zero: "0",
		profit: input.plannedProfit !== null ? formatFinAmount(input.plannedProfit) : null,
		riskState: resolveRiskStateLabel(riskState),
		marker: markerLabel,
	};

	// Разнос: сначала бейджи маркеров занимают уровни над полосой — их
	// вертикальное положение задаёт штрихи на полосе, — затем текстовые
	// подписи обходят занятое: переносом на противоположную сторону полосы,
	// затем на следующий вертикальный уровень. Наплыва нет ни между собой,
	// ни с бейджами.
	// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-finresult-labels-no-overlap
	if (layout === null || layout === undefined) {
		return {
			neutral: false,
			incomplete,
			scaleMin: scale.min,
			scaleMax: scale.max,
			zeroAt,
			riskZone,
			profitZone,
			superZone,
			realRiskStatus: input.realRiskStatus,
			markerAt,
			fillMain,
			tone: total !== null && total > 0 ? "positive" : "negative",
			markerPoints,
			labels,
			placements: null,
			badgePlacements: null,
		};
	}
	const occupied: OccupiedArea[] = [];
	const badgePlacements = placeBadges(markerPoints, layout, occupied);
	const placements = placeLabels(
		labelItemsOf(labels, markerAt, zeroAt, profitZone),
		layout,
		occupied,
	);

	return {
		neutral: false,
		incomplete,
		scaleMin: scale.min,
		scaleMax: scale.max,
		zeroAt,
		riskZone,
		profitZone,
		superZone,
		realRiskStatus: input.realRiskStatus,
		markerAt,
		fillMain,
		tone: total !== null && total > 0 ? "positive" : "negative",
		markerPoints,
		labels,
		placements,
		badgePlacements,
	};
}

// Нейтральная полоса без плановых границ: ноль в центре, позиционных
// отметок нет (пропорция не определена), заполнение — до края по знаку
// итога, значение итога выносится в подпись маркера. Состояние риска
// остаётся в геометрии отдельно от нейтральной полосы; маркеров истории
// на нейтральной полосе нет — как и маркера текущего результата.
// Traceability: openspec:ui/screens#scenario-finresult-neutral-without-params
// Traceability: openspec:ui/screens#scenario-finresult-risk-state-without-plan
function buildNeutralGeometry(
	realized: number | null,
	total: number | null,
	adjustments: number,
	quotesDegraded: boolean,
	incomplete: boolean,
	riskStatus: RealRiskStatus,
	riskState: RiskState | null,
	layout: FinResultLabelLayout | null | undefined,
): FinResultGeometry {
	const zeroAt = 0.5;
	const fillMain =
		total === null || total === 0
			? null
			: total > 0
				? { from: zeroAt, to: 1 }
				: { from: 0, to: zeroAt };

	return {
		neutral: true,
		incomplete,
		scaleMin: 0,
		scaleMax: 0,
		zeroAt,
		riskZone: null,
		profitZone: null,
		superZone: null,
		realRiskStatus: riskStatus,
		markerAt: null,
		fillMain,
		tone: total !== null && total > 0 ? "positive" : "negative",
		markerPoints: [],
		labels: {
			risk: null,
			zero: "0",
			profit: null,
			riskState: resolveRiskStateLabel(riskState),
			marker: resolveMarkerLabel(realized, null, total, adjustments, quotesDegraded),
		},
		placements:
			layout === null || layout === undefined
				? null
				: { risk: null, zero: null, profit: null, marker: null },
		badgePlacements: null,
	};
}

// Границы шкалы: базовая часть −Риск…+Профит (незаданная сторона
// симметрична заданной). Охват расширяется до истинного положения маркера
// текущего результата и всех меток истории в обе стороны — клипа крайних
// значений нет; золотая надбавка справа возникает только от итога.
// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-scale-expands-to-markers-no-clip
// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
// Traceability: openspec:ui/screens#scenario-finresult-single-bound
function resolveScale(
	plannedRisk: number | null,
	plannedProfit: number | null,
	total: number | null,
	markerTotals: readonly number[],
): { min: number; max: number } {
	const risk = plannedRisk ?? plannedProfit ?? 0;
	const profit = plannedProfit ?? plannedRisk ?? 0;
	let min = -Math.abs(risk);
	let max = profit;

	// Расширение охвата: каждая точка истории и итог попадают в шкалу
	// целиком, позиции остаются истинными.
	for (const value of markerTotals) {
		if (value < min) {
			min = value;
		}
		if (value > max) {
			max = value;
		}
	}
	if (total !== null) {
		if (total < min) {
			min = total;
		}
		if (total > max) {
			max = total;
		}
	}

	return { min, max };
}

// Подпись состояния риска вне шкалы: тексты совпадают со сценариями
// спецификации, null — состояния нет, риск конечный.
// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
// Traceability: openspec:ui/screens#scenario-finresult-unavailable-risk
function resolveRiskStateLabel(riskState: RiskState | null): string | null {
	if (riskState === "unbounded") {
		return "риск не ограничен";
	}

	return riskState === "unavailable" ? "не удалось рассчитать" : null;
}

// Подпись планового деления риска: без состояния — число «−300»; при
// unbounded/unavailable деление явно называется плановым риском, чтобы
// тонированную плановую зону не принимали за реальный риск.
// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
// Traceability: change:show-unbounded-finresult-risk/design#d2
function resolveRiskTickLabel(
	plannedRisk: number | null,
	riskState: RiskState | null,
): string | null {
	if (plannedRisk === null) {
		return null;
	}

	const amount = formatFinAmount(-plannedRisk);

	return riskState === null ? amount : `плановый риск ${amount}`;
}

// Подпись маркера — текущий результат (реализованный + нереализованный +
// корректировки); у свежей позиции с нулевым полным результатом — «≈ 0»
// (ликвидация вернёт около нуля за вычетом комиссий и спреда); при сбое
// котировок маркер подписывается доступной частью результата —
// реализованным и корректировками, скрытая нереализованная часть в подпись
// не попадает; без реализованной части подписи нет.
// Traceability: openspec:ui/screens#scenario-finresult-fresh-position-real-risk
// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
function resolveMarkerLabel(
	realized: number | null,
	unrealized: number | null,
	total: number | null,
	adjustments: number,
	quotesDegraded: boolean,
): string | null {
	if (realized === null || total === null) {
		return null;
	}

	// Сбой котировок: маркер остаётся подписанным доступной частью
	// результата, индикатор при этом помечается неполным.
	// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-incomplete-with-adjustments
	if (quotesDegraded) {
		return formatFinAmount(realized + adjustments);
	}

	// «≈ 0» только у свежей позиции с нулевым полным результатом:
	// ненулевые корректировки делают подпись обычной суммой.
	if (realized === 0 && unrealized === 0 && adjustments === 0) {
		return "≈ 0";
	}

	return formatFinAmount(total);
}

// ===== Разнос меток и бейджей =====

// Метка для разноса: позиция в долях, ширинообразующий текст, высота в
// строках и предпочитаемая сторона.
interface LabelItem {
	readonly key: LabelKey;
	readonly x: number;
	readonly widthText: string;
	readonly preferred: FinResultLabelSide;
	readonly rows: number;
}

// Занятая область: сторона, строки уровня и горизонтальный диапазон в долях.
interface OccupiedArea {
	readonly side: FinResultLabelSide;
	readonly level: number;
	readonly rows: number;
	readonly from: number;
	readonly to: number;
}

// Верхний вертикальный уровень: бейджи плотной истории могут занять
// несколько уровней, текстовых меток всегда меньше.
const MAX_LEVEL = 8;

// Дескрипторы текстовых меток для разноса: маркер → профит → ноль → риск —
// главное число выигрывает место. Метка без текста в разнос не попадает.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function labelItemsOf(
	labels: FinResultGeometry["labels"],
	markerAt: number | null,
	zeroAt: number,
	profitZone: FinResultSpan | null,
): LabelItem[] {
	const describeLabel = (
		key: LabelKey,
		x: number,
		widthText: string | null,
		preferred: FinResultLabelSide,
		rows = 1,
	): LabelItem | null => (widthText === null ? null : { key, x, widthText, preferred, rows });

	return [
		describeLabel("marker", markerAt ?? 0, labels.marker, "below"),
		describeLabel("profit", profitZone === null ? zeroAt : profitZone.to, labels.profit, "above"),
		describeLabel("zero", zeroAt, labels.zero, "above"),
		describeLabel("risk", 0, labels.risk, "above"),
	].filter((item): item is LabelItem => item !== null);
}

// Разносит бейджи маркеров над полосой: первый свободный вертикальный
// уровень, прижатие крайних к краям изнутри. Коллизия решается только
// вертикальным разносом — на противоположную сторону бейдж не переносится,
// соседние бейджи на одном уровне не перекрываются.
// Traceability: change:add-finresult-result-history-markers/specs/ui/screens/spec#scenario-result-markers-badges-above-bar
function placeBadges(
	points: readonly FinResultMarkerPoint[],
	layout: FinResultLabelLayout,
	occupied: OccupiedArea[],
): FinResultBadgePlacement[] {
	return points.map((point) => {
		const widthFraction = badgeWidthFraction(point.total, layout);
		const align = chooseAlign(point.x, widthFraction);
		const [from, to] = labelRange(point.x, widthFraction, align);

		let level = 0;
		while (
			level <= MAX_LEVEL &&
			overlapsAny(occupied, "above", level, layout.badgeRows, from, to)
		) {
			level++;
		}
		// Все уровни заняты (плотность выше модели): бейдж остаётся на
		// верхнем уровне, перекрытие допустимо.
		if (level > MAX_LEVEL) {
			level = MAX_LEVEL;
		}
		occupied.push({ side: "above", level, rows: layout.badgeRows, from, to });

		return { level, align };
	});
}

// Ширина бейджа в долях: несумма пилюли (кружок номера, зазор, паддинги)
// плюс текст суммы по ширине символа пресета — детерминированная функция
// без DOM (табличные цифры, суммы короткие).
function badgeWidthFraction(total: number, layout: FinResultLabelLayout): number {
	const text = formatFinAmount(total);

	return (layout.badgeChromePx + text.length * layout.aboveCharWidthPx) / layout.scaleWidthPx;
}

// Разносит текстовые метки по сторонам, уровням и выравниванию с учётом
// уже занятых бейджами областей. Порядок перебора мест: вертикальный
// уровень внешний, стороны внутри уровня — при коллизии метка сначала
// уходит на противоположную сторону и лишь затем на следующий уровень;
// крайние метки прижимаются к краям изнутри.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function placeLabels(
	items: readonly LabelItem[],
	layout: FinResultLabelLayout,
	occupied: OccupiedArea[],
): FinResultLabelPlacements {
	const result: FinResultLabelPlacements = { risk: null, zero: null, profit: null, marker: null };

	for (const item of items) {
		const choice = choosePlacement(item, layout, occupied);
		occupied.push(choice.area);
		result[item.key] = { side: choice.area.side, level: choice.area.level, align: choice.align };
	}

	return result;
}

// Подбирает место одной метке: уровни 0..MAX_LEVEL внешним циклом, внутри
// уровня — предпочитаемая сторона, затем противоположная; первое свободное
// место выигрывает.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function choosePlacement(
	item: LabelItem,
	layout: FinResultLabelLayout,
	occupied: readonly OccupiedArea[],
): { area: OccupiedArea; align: FinResultLabelAlign } {
	const oppositeSide: FinResultLabelSide = item.preferred === "above" ? "below" : "above";

	for (let level = 0; level <= MAX_LEVEL; level++) {
		for (const side of [item.preferred, oppositeSide] as const) {
			const widthFraction = labelWidthFraction(item.widthText, side, layout);
			const align = chooseAlign(item.x, widthFraction);
			const [from, to] = labelRange(item.x, widthFraction, align);

			if (!overlapsAny(occupied, side, level, item.rows, from, to)) {
				return { area: { side, level, rows: item.rows, from, to }, align };
			}
		}
	}

	// Все места заняты: метка остаётся на предпочитаемой стороне нижнего
	// уровня, перекрытие допустимо.
	const widthFraction = labelWidthFraction(item.widthText, item.preferred, layout);
	const align = chooseAlign(item.x, widthFraction);
	const [from, to] = labelRange(item.x, widthFraction, align);

	return { area: { side: item.preferred, level: 0, rows: item.rows, from, to }, align };
}

function labelWidthFraction(
	text: string,
	side: FinResultLabelSide,
	layout: FinResultLabelLayout,
): number {
	const charWidth = side === "above" ? layout.aboveCharWidthPx : layout.belowCharWidthPx;

	// Оценка ширины: символы × ширина символа пресета — детерминированная
	// функция без DOM (табличные цифры, метки короткие).
	return (text.length * charWidth) / layout.scaleWidthPx;
}

// Прижатие крайних: выход центра с половиной ширины за край шкалы —
// выравнивание по этому краю изнутри, габариты индикатора не растут.
// Traceability: openspec:ui/screens#scenario-finresult-labels-edge-flush
function chooseAlign(x: number, widthFraction: number): FinResultLabelAlign {
	if (x - widthFraction / 2 < 0) {
		return "start";
	}

	if (x + widthFraction / 2 > 1) {
		return "end";
	}

	return "center";
}

// Горизонтальный диапазон метки в долях после прижатия.
function labelRange(x: number, widthFraction: number, align: FinResultLabelAlign): [number, number] {
	switch (align) {
		case "start":
			return [x, x + widthFraction];
		case "end":
			return [x - widthFraction, x];
		default:
			return [x - widthFraction / 2, x + widthFraction / 2];
	}
}

// Коллизия: одна сторона полосы, пересекающиеся строки уровня и диапазоны.
function overlapsAny(
	occupied: readonly OccupiedArea[],
	side: FinResultLabelSide,
	level: number,
	rows: number,
	from: number,
	to: number,
): boolean {
	return occupied.some(
		(area) =>
			area.side === side &&
			level < area.level + area.rows &&
			area.level < level + rows &&
			from < area.to &&
			area.from < to,
	);
}

// Диапазон с нормированным порядком концов; вырожденный диапазон — null.
function spanOf(from: number, to: number): FinResultSpan | null {
	if (from === to) {
		return null;
	}

	return { from: Math.min(from, to), to: Math.max(from, to) };
}

function assertFinite(name: string, value: number | null | undefined): void {
	if (value !== null && value !== undefined && !Number.isFinite(value)) {
		throw new Error(`Недопустимое значение «${name}»: ${String(value)}`);
	}
}

/**
 * Формат величин индикатора в стиле макета: типографский минус «−300»,
 * плюс у положительных «+900», тысячи с неразрывным пробелом «+1 150»,
 * до двух дробных знаков без хвостовых нулей, ноль — без знака.
 */
export function formatFinAmount(value: number): string {
	const sign = value < 0 ? "−" : value > 0 ? "+" : "";

	return sign + formatFinMagnitude(value);
}

/**
 * Формат модуля величины индикатора без знака: «300» / «1 150» / «945.5» —
 * для подписей плановых границ, где знак не нужен.
 */
export function formatFinMagnitude(value: number): string {
	const magnitude = Math.abs(value);
	const [integer, fraction = ""] = magnitude.toFixed(2).split(".");
	const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, "\u00A0");
	const trimmedFraction = fraction.replace(/0+$/, "");

	return grouped + (trimmedFraction.length > 0 ? `.${trimmedFraction}` : "");
}
