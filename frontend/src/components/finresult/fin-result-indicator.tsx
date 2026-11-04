import * as React from "react";
import { cn } from "@/lib/utils";
import { MetricHint } from "@/components/ui/metric-tooltip";
import { METRIC_TOOLTIPS, resultMarkerBreakdownTooltip } from "@/lib/metric-tooltips";
import {
	computeFinResultGeometry,
	formatFinAmount,
	type FinResultBadgePlacement,
	type FinResultGeometry,
	type FinResultHistoryMarker,
	type FinResultInput,
	type FinResultLabelAlign,
	type FinResultLabelLayout,
	type FinResultLabelPlacement,
	type FinResultMarkerPoint,
	type FinResultSpan,
} from "@/lib/finresult/geometry";

// Индикатор финансового результата конструкции: полный (карточка), средний
// (сводные блоки и превью) и компактный (строки таблиц). Все три вида рендерят
// одну геометрию калькулятора в долях шкалы и отличаются только пресетом
// размеров: полоса с зонами планового риска/профита/сверхприбыли, заливка
// результата, маркер текущего результата и маркеры изменения результата.
// Полный вид показывает маркеры изменения результата номерными бейджами
// с суммами над полосой (тёмный штрих на полосе под каждым бейджем); у
// каждого бейджа — подсказка-расшифровка метки: перечень операций изменения
// с предыдущей выведенной метки; средний и компактный виды рисуют только
// последний маркер — тёмный вертикальный штрих на полосе без номера и
// суммы, с той же подсказкой-расшифровкой на области последнего маркера.
// У полного и среднего видов подписи (деления шкалы над полосой, маркер под
// ней) размещает калькулятор: разнос по сторонам полосы, вертикальным
// уровням и выравниванию считается по измеренной ширине полосы. Состояние
// не конечного риска (unbounded/unavailable) выводится отдельно от шкалы:
// текстовый статус у полного и среднего видов, бейдж ∞/? у компактного —
// обозначения зафиксированы в мастерах design.pen.
// Traceability: openspec:ui/screens#requirement-risk-profit-hint

/** Пропсы всех видов индикатора. */
export interface FinResultIndicatorProps {
	/** Показатели конструкции, маркеры истории и текущий результат. */
	readonly input: FinResultInput;
	readonly className?: string;
}

// Пресет размеров представления: высоты, шрифты и радиусы из макета
// (полный референс onchD, средний rMxFZ, компактный FMghY), плюс метрики
// подписей и бейджей маркеров для разноса: ширина символа над/под полосой,
// высота строки метки, отступ между секциями и номинальная ширина полосы
// до измерения.
interface SizePreset {
	/** Высота полосы, px. */
	readonly barHeight: number;
	/** Радиус скругления полосы, px. */
	readonly barRadius: number;
	/** Диаметр маркера итога, px. */
	readonly markerSize: number;
	/** Вертикальный внутренний отступ бокса полосы (задан мастерами видов), px. */
	readonly barPaddingY: number;
	/** Высота строки подписи, px. */
	readonly labelLineHeightPx: number;
	/** Вертикальный отступ между секциями (метки/полоса), px: flex-gap
	 * прибавляется к боксу полосы, который уже несёт вертикальный отступ,
	 * поэтому видимый зазор «метка → полоса» равен sectionGapPx + barPaddingY. */
	readonly sectionGapPx: number;
	/** Ширина символа подписей над полосой (деления шкалы), px. */
	readonly aboveCharWidthPx: number;
	/** Ширина символа подписей под полосой (маркер), px. */
	readonly belowCharWidthPx: number;
	/** Несумма бейджа маркера без текста суммы: кружок, зазор, паддинги, рамка, px. */
	readonly badgeChromePx: number;
	/** Высота пилюли бейджа маркера, px. */
	readonly badgePillHeightPx: number;
	/** Высота бейджа в строках модели разноса. */
	readonly badgeRows: number;
	/** Кегль суммы бейджа маркера, px. */
	readonly badgeFontPx: number;
	/** Диаметр кружка номера бейджа, px. */
	readonly badgeCirclePx: number;
	/** Номинальная ширина полосы до измерения ResizeObserver, px. */
	readonly nominalWidthPx: number;
	/** Класс подписей делений шкалы. */
	readonly scaleLabelClass: string;
	/** Класс подписей маркера. */
	readonly markerLabelClass: string;
	/** Класс тона подписи планового риска на шкале. */
	readonly riskScaleToneClass: string;
	/** Класс тона подписи планового профита на шкале. */
	readonly profitScaleToneClass: string;
	/** Режим маркеров истории: полный вид — бейджи над полосой, остальные — последний штрих. */
	readonly markerDisplay: "badges" | "last-tick";
}

const FULL_PRESET: SizePreset = {
	barHeight: 14,
	barRadius: 7,
	markerSize: 12,
	barPaddingY: 4,
	labelLineHeightPx: 14,
	// Видимый зазор «строка меток шкалы → видимая полоса» задан мастером
	// onchD (и средним rMxFZ) как 9px; бокс полосы содержит 4px вертикального
	// отступа, поэтому flex-gap = 9 − 4 = 5px. Больший gap уводил числовые
	// метки от полосы вверх относительно макета.
	// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
	sectionGapPx: 5,
	aboveCharWidthPx: 7,
	belowCharWidthPx: 6.5,
	// Бейдж маркера (мастер onchD): кружок номера 12px, зазор 4px, паддинги
	// пилюли по 6px и рамка 1px с двух сторон — несумма 30px; пилюля 18px
	// занимает две строки модели разноса.
	// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
	badgeChromePx: 30,
	badgePillHeightPx: 18,
	badgeRows: 2,
	badgeFontPx: 10,
	badgeCirclePx: 12,
	nominalWidthPx: 480,
	scaleLabelClass: "text-[11.5px]",
	markerLabelClass: "text-[10.5px]",
	// Полный вид (мастер onchD): плановые границы подписаны цветами
	// риска/профита.
	// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
	riskScaleToneClass: "text-[color:var(--fin-negative)]",
	profitScaleToneClass: "font-medium text-[color:var(--fin-positive-strong)]",
	markerDisplay: "badges",
};

const MEDIUM_PRESET: SizePreset = {
	barHeight: 14,
	barRadius: 7,
	markerSize: 10,
	barPaddingY: 4,
	labelLineHeightPx: 13,
	// Тот же расчёт, что у полного вида: 9px видимого зазора мастера rMxFZ
	// минус 4px вертикального отступа в боксе полосы.
	// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
	sectionGapPx: 5,
	aboveCharWidthPx: 6.5,
	belowCharWidthPx: 6,
	// Средний вид показывает только последний маркер штрихом — бейджей нет.
	badgeChromePx: 0,
	badgePillHeightPx: 0,
	badgeRows: 0,
	badgeFontPx: 0,
	badgeCirclePx: 0,
	nominalWidthPx: 360,
	scaleLabelClass: "text-[10.5px]",
	markerLabelClass: "text-[9.5px]",
	// Средний вид (мастер rMxFZ): плановые границы подписаны приглушённо —
	// цветным остаётся только маркер итога.
	// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
	riskScaleToneClass: "text-text-muted",
	profitScaleToneClass: "font-medium text-text-muted",
	markerDisplay: "last-tick",
};

const COMPACT_PRESET: SizePreset = {
	barHeight: 10,
	barRadius: 5,
	markerSize: 8,
	barPaddingY: 2,
	labelLineHeightPx: 12,
	sectionGapPx: 6,
	aboveCharWidthPx: 6,
	belowCharWidthPx: 6,
	// Компактный вид показывает только последний маркер штрихом — бейджей нет.
	badgeChromePx: 0,
	badgePillHeightPx: 0,
	badgeRows: 0,
	badgeFontPx: 0,
	badgeCirclePx: 0,
	nominalWidthPx: 132,
	scaleLabelClass: "",
	markerLabelClass: "",
	// Компактному виду подписи не положены — тона шкалы не применяются.
	riskScaleToneClass: "",
	profitScaleToneClass: "",
	markerDisplay: "last-tick",
};

/** Полный индикатор для карточки конструкции: бейджи маркеров с подсказками-расшифровками. */
export function FullFinResultIndicator({ input, className }: FinResultIndicatorProps) {
	return <DetailedFinResultIndicator input={input} preset={FULL_PRESET} className={className} dataSlot="fin-result-full" />;
}

/** Средний индикатор для сводных блоков и превью: последний маркер штрихом. */
export function MediumFinResultIndicator({ input, className }: FinResultIndicatorProps) {
	return <DetailedFinResultIndicator input={input} preset={MEDIUM_PRESET} className={className} dataSlot="fin-result-medium" />;
}

/** Компактный индикатор для строк таблицы конструкций: только полоса. */
// Ширина компактной шкалы — 132px по мастеру FMghY, независимо от ширины ячейки.
// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
export function CompactFinResultIndicator({ input, className }: FinResultIndicatorProps) {
	// Компактному виду подписи не положены — layout разметки не передаётся.
	const geometry = computeFinResultGeometry(input);
	// Короткий символ состояния: ∞ — неограниченный хвост, ? — расчёт не
	// удался. Бейдж выводится отдельным элементом поверх полосы, поэтому
	// координатная геометрия и ширина 132px не меняются; в конечном
	// состоянии бейджа нет.
	// Traceability: openspec:ui/screens#scenario-finresult-compact-risk-state
	// Traceability: change:show-unbounded-finresult-risk/design#d3
	const riskGlyph =
		geometry.realRiskStatus === "unbounded"
			? "∞"
			: geometry.realRiskStatus === "unavailable"
				? "?"
				: null;
	const title =
		[geometry.labels.riskState, geometry.incomplete ? "неполный: сбой котировок" : null]
			.filter(Boolean)
			.join(", ") || undefined;

	return (
		<div
			data-slot="fin-result-compact"
			data-incomplete={geometry.incomplete ? "true" : "false"}
			className={cn("relative w-[132px]", className)}
			style={{ height: COMPACT_PRESET.barHeight + COMPACT_PRESET.markerSize / 2 }}
			role="img"
			aria-label={describeForScreenReader(geometry)}
			title={title}
		>
			<FinResultBar geometry={geometry} preset={COMPACT_PRESET} lastMarker={input.resultMarkers[input.resultMarkers.length - 1]} />
			{riskGlyph !== null && (
				<span
					data-part="risk-state"
					className="absolute top-1/2 left-1/2 flex -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-text-primary text-surface"
					style={{ width: 16, height: 12, fontSize: 9, lineHeight: "12px", fontWeight: 700 }}
				>
					{riskGlyph}
				</span>
			)}
		</div>
	);
}

// Полный и средний виды: метки над полосой, полоса, метки под полосой.
// Ширина полосы измеряется ResizeObserver для разноса меток; до измерения
// калькулятор работает с номинальной шириной пресета.
function DetailedFinResultIndicator({
	input,
	preset,
	className,
	dataSlot,
}: FinResultIndicatorProps & { preset: SizePreset; dataSlot: string }) {
	const { ref, width } = useElementWidth();
	const layout: FinResultLabelLayout = {
		scaleWidthPx: width ?? preset.nominalWidthPx,
		aboveCharWidthPx: preset.aboveCharWidthPx,
		belowCharWidthPx: preset.belowCharWidthPx,
		badgeChromePx: preset.badgeChromePx,
		badgeRows: preset.badgeRows,
	};
	const geometry = computeFinResultGeometry(input, layout);

	return (
		<div
			ref={ref}
			data-slot={dataSlot}
			data-incomplete={geometry.incomplete ? "true" : "false"}
			className={cn("flex flex-col", className)}
			style={{ gap: preset.sectionGapPx }}
		>
			<LabelStack geometry={geometry} preset={preset} side="above" markers={input.resultMarkers} />
			{/* Текстовое описание состояния несёт сама полоса: контейнер не
			    объявлен role="img", иначе триггеры подсказок числовых подписей
			    внутри были бы скрыты от скринридеров вместе с содержимым img.
			    Traceability: openspec:ui/screens#requirement-indicator-numbers-tooltips-alongside-reading-popover */}
			<div role="img" aria-label={describeForScreenReader(geometry)}>
				<FinResultBar geometry={geometry} preset={preset} lastMarker={input.resultMarkers[input.resultMarkers.length - 1]} />
			</div>
			<LabelStack geometry={geometry} preset={preset} side="below" markers={input.resultMarkers} />
			{geometry.labels.riskState !== null && (
				// Статус не конечного риска — отдельная строка вне координат
				// шкалы: собственной строкой колонки он гарантированно не
				// пересекается с метками делений и маркера.
				// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
				// Traceability: openspec:ui/screens#scenario-finresult-unavailable-risk
				// Traceability: change:show-unbounded-finresult-risk/design#d3
				<span
					data-part="risk-state"
					className={cn(
						"whitespace-nowrap font-semibold text-[color:var(--fin-negative)]",
						preset.markerLabelClass,
					)}
				>
					{geometry.labels.riskState}
				</span>
			)}
		</div>
	);
}

// Стек меток одной стороны полосы: контейнер высотой в занятые уровни,
// каждая метка абсолютом на своём уровне; сторона, уровень и выравнивание
// назначены калькулятором так, чтобы метки не пересекались, а крайние
// прижимались внутрь шкалы. Высота стека считается по нижней границе самой
// далёкой метки (уровень + строки), включая бейджи маркеров над полосой.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function LabelStack({
	geometry,
	preset,
	side,
	markers,
}: {
	geometry: FinResultGeometry;
	preset: SizePreset;
	side: "above" | "below";
	markers: readonly FinResultHistoryMarker[];
}) {
	const placements = geometry.placements;
	if (placements === null) {
		return null;
	}

	const items = collectLabelItems(geometry, preset, side, placements);
	const badges = side === "above" && preset.markerDisplay === "badges" ? geometry.markerPoints : [];
	const badgePlacements = geometry.badgePlacements;
	const stackRows = Math.max(
		0,
		...items.map((item) => item.placement.level + item.rows),
		...badges.map((_, index) => (badgePlacements?.[index]?.level ?? 0) + preset.badgeRows),
	);

	if (items.length === 0 && badges.length === 0) {
		return null;
	}

	return (
		<div className="relative w-full" style={{ height: stackRows * preset.labelLineHeightPx }}>
			{items.map((item) => (
				<span
					key={item.part}
					data-part={item.part}
					className={cn("absolute whitespace-nowrap", item.className)}
					style={labelAnchorStyle(item, preset.labelLineHeightPx, side, stackRows)}
				>
					{item.content}
				</span>
			))}
			{badges.map((point, index) => {
				const placement = badgePlacements?.[index];
				if (placement === undefined) {
					return null;
				}
				const marker = markers[index];

				return (
					<MarkerBadge
						key={index}
						marker={marker ?? { totalUsdt: point.total, number: index + 1 }}
						point={point}
						placement={placement}
						preset={preset}
					/>
				);
			})}
		</div>
	);
}

// Бейдж маркера изменения результата: номер в кружке и сумма накопленного
// зафиксированного результата в пилюле с рамкой по знаку итога; уровень
// разноса отсчитывается от полосы, крайние бейджи прижимаются к краям
// изнутри. Содержимое бейджа (номер и сумма) — триггер подсказки-расшифровки
// метки: перечень операций изменения с предыдущей выведенной метки, включая
// подавленные порогом, при переполнении перечень прокручивается; подсказки
// чисел сосуществуют с попапом «Как читать индикатор», не заменяя его.
// Traceability: openspec:ui/screens#scenario-result-markers-badges-above-bar
// Traceability: openspec:ui/screens#scenario-marker-badge-breakdown-tooltip
// Traceability: openspec:ui/screens#scenario-indicator-number-tooltip-coexists
function MarkerBadge({
	marker,
	point,
	placement,
	preset,
}: {
	marker: FinResultHistoryMarker;
	point: FinResultMarkerPoint;
	placement: FinResultBadgePlacement;
	preset: SizePreset;
}) {
	const toneClass =
		point.tone === "negative"
			? "border-[color:var(--fin-negative)] text-[color:var(--fin-negative)]"
			: point.tone === "positive"
				? "border-[color:var(--fin-positive-strong)] text-[color:var(--fin-positive-strong)]"
				: "border-text-muted text-text-secondary";
	const circleClass =
		point.tone === "negative"
			? "bg-[color:var(--fin-negative)]"
			: point.tone === "positive"
				? "bg-[color:var(--fin-positive-strong)]"
				: "bg-text-muted";

	return (
		<span
			data-part="marker-badge"
			data-tone={point.tone}
			className={cn("absolute flex items-center gap-1 rounded-full border bg-surface px-1.5", toneClass)}
			style={{
				bottom: placement.level * preset.labelLineHeightPx,
				height: preset.badgePillHeightPx,
				fontSize: preset.badgeFontPx,
				...badgeAnchorStyle(point.x, placement.align),
			}}
		>
			{/* Расшифровка метки строится по операциям контракта; скролл
			    перечня при переполнении несёт попап подсказки. */}
			<MetricHint
				tooltip={resultMarkerBreakdownTooltip(marker)}
				contentClassName="max-h-[240px] overflow-y-auto"
				triggerAsChild
			>
				<span tabIndex={0} className="flex items-center gap-1">
					<span
						aria-hidden="true"
						className={cn("flex shrink-0 items-center justify-center rounded-full font-semibold text-surface", circleClass)}
						style={{ width: preset.badgeCirclePx, height: preset.badgeCirclePx, lineHeight: 1 }}
					>
						{marker.number ?? 0}
					</span>
					<span className="font-semibold tabular-nums">
						{formatFinAmount(point.total)}
					</span>
				</span>
			</MetricHint>
		</span>
	);
}

// Горизонтальное позиционирование бейджа по выравниванию из разноса:
// центр — по ширине позиции, края — прижаты изнутри, габариты не растут.
function badgeAnchorStyle(x: number, align: FinResultLabelAlign): React.CSSProperties {
	if (align === "start") {
		return { left: `${x * 100}%` };
	}

	if (align === "end") {
		return { right: `${(1 - x) * 100}%` };
	}

	return { left: `${x * 100}%`, transform: "translateX(-50%)" };
}

// Метки стороны: деления шкалы (риск, ноль, профит) и маркер; у каждой —
// высота в строках и позиция по x.
interface LabelItemCandidate {
	readonly part: string;
	/** Метка существует в текущей геометрии. */
	readonly visible: boolean;
	/** Число строк в модели разноса. */
	readonly rows: number;
	/** Нормированная позиция якоря метки. */
	readonly x: number;
	/** Место, назначенное калькулятором; null — метки нет. */
	readonly placement: FinResultLabelPlacement | null;
	readonly content: React.ReactNode;
	readonly className?: string;
}

interface LabelItemView {
	readonly part: string;
	readonly rows: number;
	readonly x: number;
	readonly placement: FinResultLabelPlacement;
	readonly content: React.ReactNode;
	readonly className?: string;
}

// Метки индикатора: деления шкалы (риск, ноль, профит) и маркер итога.
// Сторону каждой метке назначает калькулятор — при коллизии он переносит
// метку на противоположную сторону полосы, поэтому стек собирается по
// фактической стороне из разноса, а не по канонической; перенесённая метка
// рендерится в стеке другой стороны.
// Числовые подписи (кроме нулевого деления) снабжаются подсказками метрик
// рядом с попапом «Как читать индикатор»: попап поясняет чтение шкалы
// целиком и остаётся без изменений, подсказка — только отдельное число.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
// Traceability: openspec:ui/screens#requirement-indicator-numbers-tooltips-alongside-reading-popover
function collectLabelItems(
	geometry: FinResultGeometry,
	preset: SizePreset,
	side: "above" | "below",
	placements: NonNullable<FinResultGeometry["placements"]>,
): LabelItemView[] {
	const items: LabelItemView[] = [];
	const add = (candidate: LabelItemCandidate) => {
		const { visible, placement } = candidate;
		if (visible && placement !== null && placement.side === side) {
			items.push({
				part: candidate.part,
				rows: candidate.rows,
				x: candidate.x,
				placement,
				content: candidate.content,
				className: candidate.className,
			});
		}
	};

	add({
		part: "risk-scale",
		visible: geometry.labels.risk !== null,
		rows: 1,
		x: 0,
		placement: placements.risk,
		// Деление планового риска поясняется назначением плановой оценки
		// потерь к экспирации.
		// Traceability: openspec:ui/screens#requirement-indicator-numbers-tooltips-alongside-reading-popover
		content: <MetricHint tooltip={METRIC_TOOLTIPS.plannedRisk}>{geometry.labels.risk}</MetricHint>,
		// Тон плановой границы зависит от представления: полный — красный,
		// средний — приглушённый (мастера onchD и rMxFZ).
		// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
		className: cn(preset.scaleLabelClass, preset.riskScaleToneClass),
	});
	add({
		part: "zero-scale",
		visible: true,
		rows: 1,
		x: geometry.zeroAt,
		placement: placements.zero,
		content: geometry.labels.zero,
		className: cn(preset.scaleLabelClass, "text-muted-foreground"),
	});
	add({
		part: "profit-scale",
		visible: geometry.labels.profit !== null,
		rows: 1,
		x: geometry.profitZone === null ? geometry.zeroAt : geometry.profitZone.to,
		placement: placements.profit,
		// Деление планового профита поясняется назначением плановой прибыли.
		// Traceability: openspec:ui/screens#requirement-indicator-numbers-tooltips-alongside-reading-popover
		content: <MetricHint tooltip={METRIC_TOOLTIPS.plannedProfit}>{geometry.labels.profit}</MetricHint>,
		className: cn(preset.scaleLabelClass, preset.profitScaleToneClass),
	});
	add({
		part: "marker-label",
		visible: geometry.labels.marker !== null,
		rows: 1,
		x: geometry.markerAt ?? geometry.zeroAt,
		placement: placements.marker,
		// Подпись маркера поясняет смысл текущего результата на шкале.
		// Traceability: openspec:ui/screens#requirement-indicator-numbers-tooltips-alongside-reading-popover
		content: <MetricHint tooltip={METRIC_TOOLTIPS.totalMarker}>{geometry.labels.marker}</MetricHint>,
		className: cn(
			preset.markerLabelClass,
			"font-semibold tabular-nums",
			geometry.tone === "positive"
				? "text-[color:var(--fin-positive-strong)]"
				: "text-[color:var(--fin-negative)]",
		),
	});

	return items;
}

// Позиционирование метки по выравниванию из разноса: центр — по ширине
// позиции; края — прижаты изнутри («start» — левым краем от позиции,
// «end» — правым краем до позиции), габариты индикатора не растут.
// Вертикаль: нулевой уровень прижат к полосе одинаково с обеих сторон —
// строка метки примыкает к стороне стека, обращённой к полосе (снизу стека
// над полосой, сверху стека под ней); уровень L смещает метку на L строк от
// полосы наружу, поэтому верхние числовые метки держат тот же отступ до
// линейки, что и нижние.
// Traceability: openspec:ui/screens#scenario-finresult-labels-edge-flush
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function labelAnchorStyle(
	item: { rows: number; x: number; placement: FinResultLabelPlacement },
	lineHeightPx: number,
	side: "above" | "below",
	stackRows: number,
): React.CSSProperties {
	const levelOffsetRows = side === "below" ? item.placement.level : stackRows - item.placement.level - item.rows;
	const base: React.CSSProperties = {
		top: levelOffsetRows * lineHeightPx,
		lineHeight: `${lineHeightPx}px`,
	};
	const left = `${item.x * 100}%`;

	if (item.placement.align === "start") {
		return { ...base, left };
	}

	if (item.placement.align === "end") {
		return { ...base, right: `${(1 - item.x) * 100}%` };
	}

	return { ...base, left, transform: "translateX(-50%)" };
}

// Полоса: тонируемые зоны в скруглённой маске, единая заливка результата,
// тёмные штрихи маркеров изменения результата и маркер итога поверх.
// Полный вид ставит штрих под каждым бейджем, средний и компактный — только
// под последним маркером (без номера и суммы), а область последнего маркера
// снабжается той же подсказкой-расшифровкой, что и бейджи полного вида.
// Traceability: openspec:ui/screens#requirement-risk-profit-hint
// Traceability: openspec:ui/screens#scenario-medium-compact-last-marker-only
function FinResultBar({
	geometry,
	preset,
	lastMarker,
}: {
	geometry: FinResultGeometry;
	preset: SizePreset;
	/** Последний маркер истории — источник расшифровки для среднего и компактного видов. */
	readonly lastMarker?: FinResultHistoryMarker;
}) {
	// Штрих на полосе стоит в истинной позиции маркера; полный вид штрихует
	// все точки истории, остальные виды — только последнюю.
	// Traceability: openspec:ui/screens#scenario-result-markers-badges-above-bar
	const tickPoints =
		preset.markerDisplay === "badges" ? geometry.markerPoints : geometry.markerPoints.slice(-1);

	return (
		<div className="relative" style={{ height: preset.barHeight + preset.barPaddingY * 2 }}>
			<div
				data-part="bar"
				className="absolute inset-x-0 overflow-hidden bg-secondary"
				style={{
					top: preset.barPaddingY,
					height: preset.barHeight,
					borderRadius: preset.barRadius,
				}}
			>
				{geometry.riskZone && (
					<span
						data-part="risk-zone"
						className="absolute inset-y-0 left-0 bg-[color:var(--fin-risk-zone)]"
						style={spanStyle(geometry.riskZone)}
					/>
				)}
				{geometry.profitZone && (
					<span
						data-part="profit-zone"
						className="absolute inset-y-0 bg-[color:var(--fin-profit-zone)]"
						style={spanStyle(geometry.profitZone)}
					/>
				)}
				{geometry.superZone && (
					<span
						data-part="super-zone"
						className="absolute inset-y-0 bg-[color:var(--fin-super-zone)]"
						style={spanStyle(geometry.superZone)}
					/>
				)}
				{geometry.fillMain && (
					<span
						data-part="fill-main"
						className={cn(
							"absolute inset-y-0",
							geometry.tone === "positive"
								? "bg-[color:var(--fin-positive)]"
								: "bg-[color:var(--fin-negative)]",
						)}
						style={spanStyle(geometry.fillMain)}
					/>
				)}
				{tickPoints.map((point, index) =>
					preset.markerDisplay === "last-tick" && lastMarker !== undefined ? (
						// Последний маркер среднего/компактного вида несёт ту же
						// подсказку-расшифровку, что и бейджи полного вида:
						// триггер — хит-зона вокруг штриха, доступная с клавиатуры.
						// Traceability: openspec:ui/screens#scenario-medium-compact-last-marker-only
						<MetricHint
							key={index}
							tooltip={resultMarkerBreakdownTooltip(lastMarker)}
							contentClassName="max-h-[240px] overflow-y-auto"
							triggerAsChild
						>
							<span
								tabIndex={0}
								role="button"
								data-part="marker-tick-trigger"
								aria-label="Расшифровка последней метки изменения результата"
								className="absolute inset-y-0 -translate-x-1/2 flex items-center justify-center"
								style={{ left: percent(point.x), width: 12 }}
							>
								<span
									data-part="marker-tick"
									className="h-full w-[2px] rounded-[1px] bg-text-primary"
								/>
							</span>
						</MetricHint>
					) : (
						// Тёмный штрих 2px в позиции маркера: стоит в истинной точке
						// истории, бейдж с суммой при коллизии уходит вверх от него.
						// Traceability: openspec:ui/screens#scenario-result-markers-badges-above-bar
						<span
							key={index}
							data-part="marker-tick"
							className="absolute top-0 bottom-0 -translate-x-1/2 rounded-[1px] bg-text-primary"
							style={{ left: percent(point.x), width: 2 }}
						/>
					),
				)}
			</div>

			{geometry.markerAt !== null && (
				<span
					data-part="marker"
					data-tone={geometry.tone}
					className={cn(
						"absolute top-1/2 -translate-y-1/2 -translate-x-1/2 rounded-full border-2 border-surface",
						geometry.tone === "positive"
							? "bg-[color:var(--fin-positive)]"
							: "bg-[color:var(--fin-negative)]",
					)}
					style={{
						left: percent(geometry.markerAt),
						width: preset.markerSize,
						height: preset.markerSize,
					}}
				/>
			)}
		</div>
	);
}

// Текстовое описание для скринридеров: состояние риска, плановая граница
// в состоянии не конечного риска, число маркеров истории и результат.
// В состояниях unbounded/unavailable описание называет полное состояние и
// отдельно плановый риск, чтобы тонированную плановую зону не принимали за
// реальный риск; в конечном состоянии описание прежнее.
// Traceability: openspec:ui/screens#scenario-finresult-compact-risk-state
// Traceability: openspec:ui/screens#scenario-finresult-unbounded-risk
function describeForScreenReader(geometry: FinResultGeometry): string {
	const state = geometry.labels.riskState ?? "";
	const planned = state !== "" ? (geometry.labels.risk ?? "") : "";
	// Метрика называется «результат» по канону «текущего результата»:
	// подпись несёт полную сумму — реализованную, нереализованную и
	// корректировки, а при сбое котировок — доступную часть.
	// Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-finresult-marker-incomplete-with-adjustments
	const marker = geometry.labels.marker !== null ? `результат ${geometry.labels.marker}` : "";
	const markers =
		geometry.markerPoints.length > 0 ? `маркеров изменения результата ${geometry.markerPoints.length}` : "";
	const incomplete = geometry.incomplete ? "неполный, сбой котировок" : "";

	return [state, planned, markers, marker, incomplete].filter(Boolean).join(", ") || "нет данных о результате";
}

// Ширина контейнера через ResizeObserver; без наблюдения (тестовая среда
// без ResizeObserver) — null, калькулятор берёт номинальную ширину пресета.
function useElementWidth(): { ref: React.RefObject<HTMLDivElement | null>; width: number | null } {
	const ref = React.useRef<HTMLDivElement | null>(null);
	const [width, setWidth] = React.useState<number | null>(null);

	React.useEffect(() => {
		const element = ref.current;
		if (element === null || typeof ResizeObserver === "undefined") {
			return;
		}

		const observer = new ResizeObserver((entries) => {
			for (const entry of entries) {
				setWidth(entry.contentRect.width);
			}
		});
		observer.observe(element);
		setWidth(element.getBoundingClientRect().width);

		return () => observer.disconnect();
	}, []);

	return { ref, width };
}

function percent(fraction: number): string {
	return `${fraction * 100}%`;
}

function spanStyle(span: FinResultSpan): React.CSSProperties {
	return {
		left: percent(span.from),
		width: percent(span.to - span.from),
	};
}
