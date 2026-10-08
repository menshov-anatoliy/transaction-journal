// Калькулятор геометрии индикатора финансового результата конструкции.
// Чистая функция без React: переводит плановые границы и текущий результат
// в доли шкалы 0..1, подписи и признаки состояний. Рендер (полный, средний
// и компактный индикаторы) строится поверх этих долей и не считает ничего
// сам — одна геометрия на все представления.
// Шкала: зона риска от −Риск до 0, зона планового профита от 0 до +Профит;
// при сверхприбыли справа добавляется золотая зона. Граница — позиция
// реализованного результата, маркер — позиция итога (реализованный +
// нереализованный); между границей и итогем — бледная заливка
// нереализованной части.
// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

/** Вход калькулятора: изначальные показатели конструкции и текущий результат. */
export interface FinResultInput {
	/** Плановый риск конструкции в USDT; null — не задан. */
	readonly plannedRisk: number | null;
	/** Плановый профит конструкции в USDT; null — не задан. */
	readonly plannedProfit: number | null;
	/** Реализованный результат закрытых частей позиций в USDT; null — данных нет. */
	readonly realized: number | null;
	/**
	 * Нереализованный результат открытых остатков в USDT;
	 * null — открытых остатков нет (нереализованной части не существует).
	 */
	readonly unrealized: number | null;
	/** Сбой котировок: нереализованная оценка недоступна, индикатор неполный. */
	readonly quotesDegraded: boolean;
}

/** Диапазон шкалы в долях 0..1; from не больше to. */
export interface FinResultSpan {
	readonly from: number;
	readonly to: number;
}

/** Итоговая геометрия индикатора в долях шкалы и готовые подписи. */
export interface FinResultGeometry {
	/** Нейтральная полоса: плановые границы не заданы, зон нет. */
	readonly neutral: boolean;
	/** «Неполный» индикатор: сбой котировок скрыл нереализованную часть. */
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
	/** Позиция границы реализованного результата; null — данных нет. */
	readonly borderAt: number | null;
	/** Позиция маркера итога; null — данных нет. */
	readonly markerAt: number | null;
	/** Маркер клипован по краю шкалы: итог за её пределами. */
	readonly markerClipped: boolean;
	/** Заливка основным тоном — от нуля до итога. */
	readonly fillMain: FinResultSpan | null;
	/** Бледная заливка нереализованной части — от границы до итога. */
	readonly fillUnreal: FinResultSpan | null;
	/** Тон маркера и подписей: положительный итог — зелёный, иначе красный. */
	readonly tone: "positive" | "negative";
	/** Готовые подписи индикатора. */
	readonly labels: {
		readonly risk: string | null;
		readonly zero: string;
		readonly profit: string | null;
		readonly superEnd: string | null;
		readonly borderTitle: string | null;
		readonly borderValue: string | null;
		readonly marker: string | null;
	};
}

/**
 * Считает геометрию индикатора финрезультата в долях шкалы.
 * Бросает ошибку на NaN и бесконечностях во входных числах.
 */
export function computeFinResultGeometry(input: FinResultInput): FinResultGeometry {
	assertFinite("plannedRisk", input.plannedRisk);
	assertFinite("plannedProfit", input.plannedProfit);
	assertFinite("realized", input.realized);
	assertFinite("unrealized", input.unrealized);

	// Нереализованная часть существует только при открытых остатках и
	// доступных котировках: сбой скрывает её, не подменяя нулём.
	const unrealized = input.unrealized !== null && !input.quotesDegraded ? input.unrealized : null;
	const realized = input.realized;
	const total = realized === null ? null : realized + (unrealized ?? 0);
	const incomplete = input.quotesDegraded && input.unrealized !== null;

	// Нейтральная полоса: плановых границ нет — зоны и деления отсутствуют,
	// заполнение идёт от нуля до края в сторону знака итога.
	if (input.plannedRisk === null && input.plannedProfit === null) {
		return buildNeutralGeometry(realized, total, incomplete);
	}

	// Значение границы: позиция реализованного результата. Для свежей
	// позиции (реализованного нет) граница уходит на −плановойРиск:
	// «весь реальный риск впереди», конструкция ещё не окупила затраты на
	// себя. Решение владельца: маркер свежей позиции остаётся «≈ 0»
	// (ликвидация вернёт около нуля за вычетом комиссий и спреда), а не
	// «итог − реальныйРиск».
	// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции
	// Traceability: issue:#62
	const borderValue = resolveBorderValue(realized, input.plannedRisk);

	// Шкала: базовые зоны из плановых показателей, при сверхприбыли —
	// золотая надбавка справа.
	const scale = resolveScale(input.plannedRisk, input.plannedProfit, borderValue, total);
	const toFraction = (value: number): number =>
		clamp01((value - scale.min) / (scale.max - scale.min));

	const zeroAt = toFraction(0);
	const riskZone = input.plannedRisk !== null ? { from: 0, to: zeroAt } : null;
	const profitZone =
		input.plannedProfit !== null ? spanOf(zeroAt, toFraction(input.plannedProfit)) : null;
	const superZone =
		input.plannedProfit !== null && scale.max > input.plannedProfit
			? spanOf(toFraction(input.plannedProfit), 1)
			: null;

	const borderAt = borderValue === null ? null : toFraction(borderValue);
	const markerAt = total === null ? null : toFraction(total);
	const markerClipped = total !== null && (total > scale.max || total < scale.min);

	const fillMain = total === null ? null : spanOf(zeroAt, toFraction(total));
	const fillUnreal =
		unrealized === null || realized === null || total === null || unrealized === 0
			? null
			: spanOf(toFraction(realized), toFraction(total));

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
		fillUnreal,
		tone: total !== null && total > 0 ? "positive" : "negative",
		labels: {
			risk: input.plannedRisk !== null ? formatFinAmount(-input.plannedRisk) : null,
			zero: "0",
			profit: input.plannedProfit !== null ? formatFinAmount(input.plannedProfit) : null,
			superEnd: superZone !== null ? formatFinAmount(scale.max) : null,
			borderTitle: resolveBorderTitle(realized, input.plannedRisk),
			borderValue: resolveBorderValueLabel(realized, input.plannedRisk),
			marker: resolveMarkerLabel(realized, unrealized, total, markerClipped, input.quotesDegraded),
		},
	};
}

// Нейтральная полоса без плановых границ: ноль в центре, позиционных
// отметок нет (пропорция не определена), заполнение — до края по знаку
// итога, значение итога выносится в подпись маркера.
function buildNeutralGeometry(
	realized: number | null,
	total: number | null,
	incomplete: boolean,
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
		fillUnreal: null,
		tone: total !== null && total > 0 ? "positive" : "negative",
		labels: {
			risk: null,
			zero: "0",
			profit: null,
			superEnd: null,
			borderTitle: null,
			borderValue: null,
			marker: resolveMarkerLabel(realized, null, total, false, false),
		},
	};
}

// Значение границы реализованного результата: у свежей позиции (нет
// реализованного) граница стоит на −плановомРиске — весь риск впереди.
function resolveBorderValue(realized: number | null, plannedRisk: number | null): number | null {
	if (realized === null) {
		return null;
	}

	if (realized === 0 && plannedRisk !== null) {
		return -plannedRisk;
	}

	return realized;
}

// Границы шкалы: базовая часть −Риск…+Профит (незаданная сторона
// симметрична заданной), при сверхприбыли — золотая надбавка справа.
function resolveScale(
	plannedRisk: number | null,
	plannedProfit: number | null,
	borderValue: number | null,
	total: number | null,
): { min: number; max: number } {
	const risk = plannedRisk ?? plannedProfit ?? 0;
	const profit = plannedProfit ?? plannedRisk ?? 0;
	const min = -Math.abs(risk);

	// Золото тянется до источника сверхприбыли: вышедшая за профит граница
	// задаёт полную длину зоны, нереализованный итог — половину превышения
	// (шкала растягивается умеренно, маркер клипуется по краю, значение
	// выносится числом).
	let gold = 0;
	if (borderValue !== null && borderValue > profit) {
		gold = borderValue - profit;
	} else if (total !== null && total > profit) {
		gold = (total - profit) / 2;
	}

	// Порог: золотая надбавка не длиннее базовой части шкалы.
	const baseWidth = Math.abs(risk) + Math.abs(profit);
	gold = Math.min(gold, baseWidth);

	return { min, max: profit + gold };
}

// Заголовок подписи границы: слева от нуля конструкция ещё не окупила
// затраты на себя — «риск есть»; справа — безусловно в плюсе, «риска нет».
function resolveBorderTitle(realized: number | null, plannedRisk: number | null): string | null {
	if (realized === null || plannedRisk === null) {
		return null;
	}

	return realized <= 0 ? "риск есть" : "риска нет";
}

// Значение подписи границы двумя единицами: сумма в USDT и доля планового
// риска в процентах («250 USDT · 83%»); у свежей позиции — весь риск.
function resolveBorderValueLabel(realized: number | null, plannedRisk: number | null): string | null {
	if (realized === null || plannedRisk === null) {
		return null;
	}

	// Сколько осталось отыграть до нуля: реализованный убыток по модулю;
	// свежая позиция рискует всеми планами.
	const magnitude = realized < 0 ? -realized : realized === 0 ? plannedRisk : realized;
	const percent = Math.round((magnitude / plannedRisk) * 100);

	return `${formatFinMagnitude(magnitude)} USDT · ${percent}%`;
}

// Подпись маркера итога: без клипа у маркера стоит нереализованная (живая
// часть), при клипе значение итога выносится числом; свежая позиция —
// «≈ 0»; при сбое котировок числа нет.
function resolveMarkerLabel(
	realized: number | null,
	unrealized: number | null,
	total: number | null,
	markerClipped: boolean,
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

	if (markerClipped || unrealized === null) {
		return formatFinAmount(total);
	}

	return formatFinAmount(unrealized);
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

function assertFinite(name: string, value: number | null): void {
	if (value !== null && !Number.isFinite(value)) {
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
