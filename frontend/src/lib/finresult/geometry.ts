// Калькулятор геометрии индикатора финансового результата конструкции.
// Чистая функция без React: переводит плановые границы, реальный риск и
// текущий результат в доли шкалы 0..1, подписи и размещения меток. Рендер
// (полный, средний и компактный индикаторы) строится поверх этих долей и
// не считает ничего сам — одна геометрия на все представления.
// Шкала: зона риска от −Риск до 0, зона планового профита от 0 до +Профит;
// при выходе маркера итога за +Профит справа добавляется золотая зона до
// итога, граница реального риска в расчёте шкалы не участвует и клипуется
// её краем. Граница — «реализованный результат минус реальный риск»,
// маркер — позиция итога (реализованный + нереализованный); итог рисуется
// одной заливкой без разложения на реализованную и нереализованную части.
// Traceability: openspec:ui/screens#requirement-risk-profit-hint

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
	/** Сбой котировок: нереализованная оценка недоступна, индикатор неполный. */
	readonly quotesDegraded: boolean;
	// Неполнота индикатора считается только при открытых остатках: сбой
	// марок скрывает нереализованную часть, лишь когда она существует.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	readonly hasOpenResidual: boolean;
	/**
	 * Реальный риск открытых остатков в USDT (наихудший результат на
	 * экспирации); null или отсутствие поля — метрика недоступна, граница
	 * считается по заглушке плановым риском. Поле опционально до проводки
	 * метрики из API в вызывающие компоненты.
	 */
	readonly realRisk?: number | null;
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

/**
 * Метрики разметки меток: ширина полосы и ширина одного символа подписи
 * пресета. Без метрик разнос не считается (компактный вид меток не имеет).
 */
export interface FinResultLabelLayout {
	/** Ширина полосы индикатора в px — для перевода ширин меток в доли. */
	readonly scaleWidthPx: number;
	/** Ширина символа подписей делений шкалы (над полосой), px. */
	readonly aboveCharWidthPx: number;
	/** Ширина символа подписей границы и маркера (под полосой), px. */
	readonly belowCharWidthPx: number;
}

/** Размещения всех меток индикатора по ключам. */
export type FinResultLabelPlacements = {
	[K in LabelKey]: FinResultLabelPlacement | null;
};

/** Ключ метки в геометрии. */
type LabelKey = "risk" | "zero" | "profit" | "border" | "marker";

/** Итоговая геометрия индикатора в долях шкалы и готовые подписи. */
export interface FinResultGeometry {
	/** Нейтральная полоса: плановые границы не заданы, зон нет. */
	readonly neutral: boolean;
	/** «Неполный» индикатор: сбой котировок скрыл нереализованную часть открытых остатков. */
	readonly incomplete: boolean;
	/** Минимум шкалы в USDT (с учётом золотой надбавки). */
	readonly scaleMin: number;
	/** Максимум шкалы в USDT (с учётом золотой надбавки). */
	readonly scaleMax: number;
	/** Позиция нуля шкалы в долях. */
	readonly zeroAt: number;
	/** Зона планового риска; null — риск не задан или полоса нейтральная. */
	readonly riskZone: FinResultSpan | null;
	/** Зона планового профита; null — профит не задан или полоса нейтральная. */
	readonly profitZone: FinResultSpan | null;
	/** Золотая зона сверхприбыли за плановым профитом; null — превышения нет. */
	readonly superZone: FinResultSpan | null;
	/** Позиция границы реального риска (с клипом слева); null — данных нет. */
	readonly borderAt: number | null;
	/** Позиция маркера итога; null — данных нет. */
	readonly markerAt: number | null;
	/** Маркер клипован по краю шкалы: итог за её пределами. */
	readonly markerClipped: boolean;
	/** Единая заливка итога — от нуля до итога либо до планового профита. */
	readonly fillMain: FinResultSpan | null;
	/** Тон маркера и подписей: положительный итог — зелёный, иначе красный. */
	readonly tone: "positive" | "negative";
	/** Готовые подписи индикатора. */
	readonly labels: {
		readonly risk: string | null;
		readonly zero: string;
		readonly profit: string | null;
		readonly borderTitle: string | null;
		readonly borderValue: string | null;
		readonly marker: string | null;
	};
	/**
	 * Разнос меток: сторона, вертикальный уровень и выравнивание каждой;
	 * null — разнос не считался (метрики разметки не переданы).
	 */
	readonly placements: FinResultLabelPlacements | null;
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
	assertFinite("realRisk", input.realRisk);

	// Нереализованная часть существует только при открытых остатках и
	// доступных котировках: сбой скрывает её, не подменяя нулём.
	const unrealized = input.unrealized !== null && !input.quotesDegraded ? input.unrealized : null;
	const realized = input.realized;
	const total = realized === null ? null : realized + (unrealized ?? 0);
	// Неполный индикатор — сбой марок именно при открытых остатках: без
	// остатков нереализованной части не существует, скрывать нечего.
	// Комбинация приходит из read-моделей: сбой марок бэкенд отдаёт вместе
	// с null нереализованной части только при открытых остатках.
	// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
	const incomplete = input.quotesDegraded && input.hasOpenResidual;

	// Реальный риск с каскадом заглушек: нет метрики — граница считается
	// по плановому риску, затем по плановому профиту, затем по нулю.
	// Traceability: openspec:ui/screens#scenario-finresult-real-risk-fallback-planned
	const realRiskEff = input.realRisk ?? input.plannedRisk ?? input.plannedProfit ?? 0;

	// Нейтральная полоса: плановых границ нет — зоны и деления отсутствуют,
	// заполнение идёт от нуля до края в сторону знака итога.
	if (input.plannedRisk === null && input.plannedProfit === null) {
		return buildNeutralGeometry(realized, total, incomplete, layout);
	}

	// Граница реального риска: «реализованный результат минус реальный
	// риск» — где конструкция оказалась бы при худшем исходе открытых
	// остатков с учётом уже реализованной прибыли. Насечка скрыта только
	// без данных о позиции (realized === null); свежая позиция попадает на
	// −реальныйРиск естественно, по формуле: спец-случай из #62 упразднён.
	// Traceability: openspec:ui/screens#scenario-finresult-border-real-risk
	// Traceability: openspec:ui/screens#scenario-finresult-fresh-position-real-risk
	const borderValue = realized === null ? null : realized - realRiskEff;

	// Шкала: базовые зоны из плановых показателей, при сверхприбыли —
	// золотая надбавка справа.
	const scale = resolveScale(input.plannedRisk, input.plannedProfit, total);
	const toFraction = (value: number): number =>
		clamp01((value - scale.min) / (scale.max - scale.min));

	const zeroAt = toFraction(0);
	const riskZone = input.plannedRisk !== null ? { from: 0, to: zeroAt } : null;
	const profitZone =
		input.plannedProfit !== null ? spanOf(zeroAt, toFraction(input.plannedProfit)) : null;
	// Золотая зона сверхприбыли возникает только когда итог превысил
	// плановый профит, и занимает непересекающийся с зелёной заливкой
	// участок от профита до итога; граница реального риска зону не создаёт.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-border
	const superZone =
		input.plannedProfit !== null && total !== null && total > input.plannedProfit
			? spanOf(toFraction(input.plannedProfit), toFraction(total))
			: null;

	// Клип слева: насечка прижимается к краю шкалы, истинное число остаётся
	// в подписи.
	// Traceability: openspec:ui/screens#scenario-finresult-border-left-clip
	const borderAt = borderValue === null ? null : toFraction(borderValue);
	const markerAt = total === null ? null : toFraction(total);
	const markerClipped = total !== null && (total > scale.max || total < scale.min);

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

	const borderLabels = resolveBorderLabels(realized, input.plannedRisk, realRiskEff);
	const markerLabel = resolveMarkerLabel(realized, unrealized, total, input.quotesDegraded);

	const labels = {
		risk: input.plannedRisk !== null ? formatFinAmount(-input.plannedRisk) : null,
		zero: "0",
		profit: input.plannedProfit !== null ? formatFinAmount(input.plannedProfit) : null,
		borderTitle: borderLabels === null ? null : borderLabels.title,
		borderValue: borderLabels === null ? null : borderLabels.value,
		marker: markerLabel,
	};

	// Дескрипторы меток для разноса в порядке обработки: маркер → граница →
	// профит → ноль → риск — главное число выигрывает место. Метка без
	// текста в разнос не попадает; ширинообразующий текст границы — самая
	// длинная из двух её строк.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	const describeLabel = (
		key: LabelKey,
		x: number,
		widthText: string | null,
		preferred: FinResultLabelSide,
		rows = 1,
	): LabelItem | null => (widthText === null ? null : { key, x, widthText, preferred, rows });
	const borderWidthText =
		borderLabels === null
			? null
			: borderLabels.title.length > borderLabels.value.length
				? borderLabels.title
				: borderLabels.value;
	const labelItems = [
		describeLabel("marker", markerAt ?? 0, markerLabel, "below"),
		describeLabel("border", borderAt ?? 0, borderWidthText, "below", 2),
		describeLabel("profit", profitZone === null ? zeroAt : profitZone.to, labels.profit, "above"),
		describeLabel("zero", zeroAt, labels.zero, "above"),
		describeLabel("risk", 0, labels.risk, "above"),
	].filter((item): item is LabelItem => item !== null);

	const placements = layout === null || layout === undefined ? null : placeLabels(labelItems, layout);

	return {
		neutral: false,
		incomplete,
		scaleMin: scale.min,
		scaleMax: scale.max,
		zeroAt,
		riskZone,
		profitZone,
		superZone,
		borderAt,
		markerAt,
		markerClipped,
		fillMain,
		tone: total !== null && total > 0 ? "positive" : "negative",
		labels,
		placements,
	};
}

// Нейтральная полоса без плановых границ: ноль в центре, позиционных
// отметок нет (пропорция не определена), заполнение — до края по знаку
// итога, значение итога выносится в подпись маркера.
// Traceability: openspec:ui/screens#scenario-finresult-neutral-without-params
function buildNeutralGeometry(
	realized: number | null,
	total: number | null,
	incomplete: boolean,
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
		borderAt: null,
		markerAt: null,
		markerClipped: false,
		fillMain,
		tone: total !== null && total > 0 ? "positive" : "negative",
		labels: {
			risk: null,
			zero: "0",
			profit: null,
			borderTitle: null,
			borderValue: null,
			marker: resolveMarkerLabel(realized, null, total, false),
		},
		placements:
			layout === null || layout === undefined
				? null
				: { risk: null, zero: null, profit: null, border: null, marker: null },
	};
}

// Границы шкалы: базовая часть −Риск…+Профит (незаданная сторона
// симметрична заданной). Правая граница расширяется только до итога,
// когда тот превышает плановый профит; граница реального риска описывает
// худший будущий исход открытых остатков, а не полученную прибыль, и в
// расчёте шкалы не участвует — надбавка без капа длины.
// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
// Traceability: openspec:ui/screens#scenario-finresult-super-zone-border
// Traceability: openspec:ui/screens#scenario-finresult-single-bound
function resolveScale(
	plannedRisk: number | null,
	plannedProfit: number | null,
	total: number | null,
): { min: number; max: number } {
	const risk = plannedRisk ?? plannedProfit ?? 0;
	const profit = plannedProfit ?? plannedRisk ?? 0;
	const min = -Math.abs(risk);

	let max = profit;
	if (plannedProfit !== null && total !== null && total > plannedProfit) {
		max = total;
	}

	return { min, max };
}

// Подпись границы: заголовок «риск есть», пока реальный риск положителен,
// иначе «риска нет»; значение — величина реального риска «150 USDT · 50%»,
// проценты — только при заданном плановом риске.
// Traceability: openspec:ui/screens#scenario-finresult-closed-no-risk
// Traceability: openspec:ui/screens#requirement-risk-profit-hint
function resolveBorderLabels(
	realized: number | null,
	plannedRisk: number | null,
	realRiskEff: number,
): { title: string; value: string } | null {
	if (realized === null) {
		return null;
	}

	const title = realRiskEff > 0 ? "риск есть" : "риска нет";
	const percent =
		plannedRisk !== null && plannedRisk > 0 ? Math.round((realRiskEff / plannedRisk) * 100) : null;
	const value = `${formatFinMagnitude(realRiskEff)} USDT${percent === null ? "" : ` · ${percent}%`}`;

	return { title, value };
}

// Подпись маркера — итог (реализованный + нереализованный); у свежей
// позиции — «≈ 0» (ликвидация вернёт около нуля за вычетом комиссий и
// спреда); при сбое котировок числа нет.
// Traceability: openspec:ui/screens#scenario-finresult-fresh-position-real-risk
// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
function resolveMarkerLabel(
	realized: number | null,
	unrealized: number | null,
	total: number | null,
	quotesDegraded: boolean,
): string | null {
	if (realized === null || total === null) {
		return null;
	}

	if (quotesDegraded) {
		return null;
	}

	if (realized === 0 && unrealized === 0) {
		return "≈ 0";
	}

	return formatFinAmount(total);
}

// ===== Разнос меток =====

// Метка для разноса: позиция в долях, ширинообразующий текст, высота в
// строках (метка границы — в две строки) и предпочитаемая сторона.
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

// Верхний вертикальный уровень: пять меток его не исчерпают, а при
// исчерпании метка остаётся на предпочитаемой стороне.
const MAX_LEVEL = 3;

// Разносит метки по сторонам, уровням и выравниванию. Порядок перебора мест:
// вертикальный уровень внешний, стороны внутри уровня — при коллизии метка
// сначала уходит на противоположную сторону и лишь затем на следующий
// уровень; крайние метки прижимаются к краям изнутри.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function placeLabels(items: readonly LabelItem[], layout: FinResultLabelLayout): FinResultLabelPlacements {
	const placed: OccupiedArea[] = [];
	const result: FinResultLabelPlacements = { risk: null, zero: null, profit: null, border: null, marker: null };

	for (const item of items) {
		const choice = choosePlacement(item, layout, placed);
		placed.push(choice.area);
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
	placed: readonly OccupiedArea[],
): { area: OccupiedArea; align: FinResultLabelAlign } {
	const oppositeSide: FinResultLabelSide = item.preferred === "above" ? "below" : "above";

	for (let level = 0; level <= MAX_LEVEL; level++) {
		for (const side of [item.preferred, oppositeSide] as const) {
			const widthFraction = labelWidthFraction(item.widthText, side, layout);
			const align = chooseAlign(item.x, widthFraction);
			const [from, to] = labelRange(item.x, widthFraction, align);

			if (!overlapsAny(placed, side, level, item.rows, from, to)) {
				return { area: { side, level, rows: item.rows, from, to }, align };
			}
		}
	}

	// Все места заняты (недостижимо при пяти метках): метка остаётся на
	// предпочитаемой стороне нижнего уровня, перекрытие допустимо.
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
	placed: readonly OccupiedArea[],
	side: FinResultLabelSide,
	level: number,
	rows: number,
	from: number,
	to: number,
): boolean {
	return placed.some(
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

function clamp01(value: number): number {
	return Math.min(1, Math.max(0, value));
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
 * для подписей границ («250 USDT · 83%»), где знак не нужен.
 */
export function formatFinMagnitude(value: number): string {
	const magnitude = Math.abs(value);
	const [integer, fraction = ""] = magnitude.toFixed(2).split(".");
	const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, "\u00A0");
	const trimmedFraction = fraction.replace(/0+$/, "");

	return grouped + (trimmedFraction.length > 0 ? `.${trimmedFraction}` : "");
}
