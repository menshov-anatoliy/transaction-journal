import * as React from "react";
import { cn } from "@/lib/utils";
import {
	computeFinResultGeometry,
	type FinResultGeometry,
	type FinResultInput,
	type FinResultLabelAlign,
	type FinResultLabelLayout,
	type FinResultLabelPlacement,
	type FinResultSpan,
} from "@/lib/finresult/geometry";

// Индикатор финансового результата конструкции: полный (карточка/превью),
// средний (сводные блоки) и компактный (строки таблиц). Все три вида рендерят
// одну геометрию калькулятора в долях шкалы и отличаются только пресетом
// размеров: полоса с зонами планового риска/профита/сверхприбыли, заливка
// результата, насечка границы реального риска и маркер итога. У полного и
// среднего видов подписи (деления шкалы над полосой, граница и маркер под
// ней) размещает калькулятор: разнос по сторонам полосы, вертикальным
// уровням и выравниванию считается по измеренной ширине полосы.
// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финрезультата-конструкции

/** Пропсы всех видов индикатора. */
export interface FinResultIndicatorProps {
	/** Показатели конструкции и текущий результат. */
	readonly input: FinResultInput;
	readonly className?: string;
}

// Пресет размеров представления: высоты, шрифты и радиусы из макета
// (полный референс onchD, средний rMxFZ, компактный FMghY), плюс метрики
// подписей для разноса: ширина символа над/под полосой, высота строки
// метки, отступ между секциями и номинальная ширина полосы до измерения.
interface SizePreset {
	/** Высота полосы, px. */
	readonly barHeight: number;
	/** Радиус скругления полосы, px. */
	readonly barRadius: number;
	/** Диаметр маркера итога, px. */
	readonly markerSize: number;
	/** Ширина насечки границы, px. */
	readonly borderWidth: number;
	/** Выступ насечки за полосу вверх и вниз, px. */
	readonly borderOverhang: number;
	/** Высота строки подписи, px. */
	readonly labelLineHeightPx: number;
	/** Вертикальный отступ между секциями (метки/полоса), px. */
	readonly sectionGapPx: number;
	/** Ширина символа подписей над полосой (деления шкалы), px. */
	readonly aboveCharWidthPx: number;
	/** Ширина символа подписей под полосой (граница, маркер), px. */
	readonly belowCharWidthPx: number;
	/** Номинальная ширина полосы до измерения ResizeObserver, px. */
	readonly nominalWidthPx: number;
	/** Класс подписей делений шкалы. */
	readonly scaleLabelClass: string;
	/** Класс подписей границы и маркера. */
	readonly markerLabelClass: string;
}

const FULL_PRESET: SizePreset = {
	barHeight: 14,
	barRadius: 7,
	markerSize: 12,
	borderWidth: 2,
	borderOverhang: 4,
	labelLineHeightPx: 14,
	sectionGapPx: 10,
	aboveCharWidthPx: 7,
	belowCharWidthPx: 6.5,
	nominalWidthPx: 480,
	scaleLabelClass: "text-[11.5px]",
	markerLabelClass: "text-[10.5px]",
};

const MEDIUM_PRESET: SizePreset = {
	barHeight: 14,
	barRadius: 7,
	markerSize: 10,
	borderWidth: 2,
	borderOverhang: 4,
	labelLineHeightPx: 13,
	sectionGapPx: 8,
	aboveCharWidthPx: 6.5,
	belowCharWidthPx: 6,
	nominalWidthPx: 360,
	scaleLabelClass: "text-[10.5px]",
	markerLabelClass: "text-[9.5px]",
};

const COMPACT_PRESET: SizePreset = {
	barHeight: 10,
	barRadius: 5,
	markerSize: 8,
	borderWidth: 2,
	borderOverhang: 2,
	labelLineHeightPx: 12,
	sectionGapPx: 6,
	aboveCharWidthPx: 6,
	belowCharWidthPx: 6,
	nominalWidthPx: 132,
	scaleLabelClass: "",
	markerLabelClass: "",
};

/** Полный индикатор для карточки конструкции и превью. */
export function FullFinResultIndicator({ input, className }: FinResultIndicatorProps) {
	return <DetailedFinResultIndicator input={input} preset={FULL_PRESET} className={className} dataSlot="fin-result-full" />;
}

/** Средний индикатор для сводных блоков карточки. */
export function MediumFinResultIndicator({ input, className }: FinResultIndicatorProps) {
	return <DetailedFinResultIndicator input={input} preset={MEDIUM_PRESET} className={className} dataSlot="fin-result-medium" />;
}

/** Компактный индикатор для строк таблицы конструкций: только полоса. */
// Ширина компактной шкалы — 132px по мастеру FMghY, независимо от ширины ячейки.
// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
export function CompactFinResultIndicator({ input, className }: FinResultIndicatorProps) {
	// Компактному виду подписи не положены — layout разметки не передаётся.
	const geometry = computeFinResultGeometry(input);

	return (
		<div
			data-slot="fin-result-compact"
			data-incomplete={geometry.incomplete ? "true" : "false"}
			className={cn("relative w-[132px]", className)}
			style={{ height: COMPACT_PRESET.barHeight + COMPACT_PRESET.markerSize / 2 }}
			role="img"
			aria-label={describeForScreenReader(geometry)}
			title={geometry.incomplete ? "неполный: сбой котировок" : undefined}
		>
			<FinResultBar geometry={geometry} preset={COMPACT_PRESET} />
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
	};
	const geometry = computeFinResultGeometry(input, layout);

	return (
		<div
			ref={ref}
			data-slot={dataSlot}
			data-incomplete={geometry.incomplete ? "true" : "false"}
			className={cn("flex flex-col", className)}
			style={{ gap: preset.sectionGapPx }}
			role="img"
			aria-label={describeForScreenReader(geometry)}
		>
			<LabelStack geometry={geometry} preset={preset} side="above" />
			<FinResultBar geometry={geometry} preset={preset} />
			<LabelStack geometry={geometry} preset={preset} side="below" />
		</div>
	);
}

// Стек меток одной стороны полосы: контейнер высотой в занятые уровни,
// каждая метка абсолютом на своём уровне; сторона, уровень и выравнивание
// назначены калькулятором так, чтобы метки не пересекались, а крайние
// прижимались внутрь шкалы.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function LabelStack({
	geometry,
	preset,
	side,
}: {
	geometry: FinResultGeometry;
	preset: SizePreset;
	side: "above" | "below";
}) {
	const placements = geometry.placements;
	if (placements === null) {
		return null;
	}

	const items = collectLabelItems(geometry, preset, side, placements);
	if (items.length === 0) {
		return null;
	}

	const rows = Math.max(...items.map((item) => item.rows));

	return (
		<div className="relative w-full" style={{ height: rows * preset.labelLineHeightPx }}>
			{items.map((item) => (
				<span
					key={item.part}
					data-part={item.part}
					className={cn("absolute whitespace-nowrap", item.className)}
					style={labelAnchorStyle(item, preset.labelLineHeightPx)}
				>
					{item.content}
				</span>
			))}
		</div>
	);
}

// Метки стороны: деления шкалы (риск, ноль, профит), граница и маркер; у
// каждой — высота в строках и позиция по x.
interface LabelItemCandidate {
	readonly part: string;
	/** Метка существует в текущей геометрии. */
	readonly visible: boolean;
	/** Число строк в модели разноса; у границы — две. */
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

// Метки индикатора: деления шкалы (риск, ноль, профит), граница реального
// риска и маркер итога. Сторону каждой метке назначает калькулятор — при
// коллизии он переносит метку на противоположную сторону полосы, поэтому
// стек собирается по фактической стороне из разноса, а не по канонической;
// перенесённая метка рендерится в стеке другой стороны.
// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
function collectLabelItems(
	geometry: FinResultGeometry,
	preset: SizePreset,
	side: "above" | "below",
	placements: NonNullable<FinResultGeometry["placements"]>,
): LabelItemView[] {
	// Подпись границы: «риск есть»/«риска нет» и значение реального риска
	// «150 USDT · 50%»; зелёная при выходе в плюс или отсутствии риска,
	// иначе красная.
	const borderToneClass =
		geometry.tone === "positive" || geometry.labels.borderTitle === "риска нет"
			? "text-[color:var(--fin-positive-strong)]"
			: "text-[color:var(--fin-negative)]";

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
		content: geometry.labels.risk,
		className: cn(preset.scaleLabelClass, "text-[color:var(--fin-negative)]"),
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
		content: geometry.labels.profit,
		className: cn(preset.scaleLabelClass, "font-medium text-[color:var(--fin-positive-strong)]"),
	});
	add({
		part: "border-label",
		visible: geometry.labels.borderTitle !== null && geometry.labels.borderValue !== null,
		rows: 2,
		x: geometry.borderAt ?? 0,
		placement: placements.border,
		content: (
			<>
				<span className={cn("font-medium", borderToneClass)}>{geometry.labels.borderTitle}</span>
				<span className={cn("font-semibold", borderToneClass)}>{geometry.labels.borderValue}</span>
			</>
		),
		// Строки границы рендерятся столбцом: модель разноса считает ширину
		// метки по самой длинной из двух строк (rows=2), а инлайн-рендер давал
		// одну строку заметно шире модели — на узких видах маркер наплывал на
		// подпись границы.
		// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
		className: cn(
			"flex flex-col",
			placements.border === null ? "" : itemsAlignClass(placements.border.align),
			preset.markerLabelClass,
		),
	});
	add({
		part: "marker-label",
		visible: geometry.labels.marker !== null,
		rows: 1,
		x: geometry.markerAt ?? geometry.zeroAt,
		placement: placements.marker,
		content: geometry.labels.marker,
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

// Внутреннее выравнивание строк многострочной метки: строки держатся вместе
// по тому же краю, к которому метка прижата калькулятором.
function itemsAlignClass(align: FinResultLabelAlign): string {
	if (align === "start") {
		return "items-start";
	}

	return align === "end" ? "items-end" : "items-center";
}

// Позиционирование метки по выравниванию из разноса: центр — по ширине
// позиции; края — прижаты изнутри («start» — левым краем от позиции,
// «end» — правым краем до позиции), габариты индикатора не растут.
// Traceability: openspec:ui/screens#scenario-finresult-labels-edge-flush
function labelAnchorStyle(
	item: { rows: number; x: number; placement: FinResultLabelPlacement },
	lineHeightPx: number,
): React.CSSProperties {
	const base: React.CSSProperties = { top: 0, lineHeight: `${lineHeightPx}px` };
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
// насечка границы и маркер итога поверх.
function FinResultBar({ geometry, preset }: { geometry: FinResultGeometry; preset: SizePreset }) {
	const markerHalf = preset.markerSize / 2;

	return (
		<div className="relative" style={{ height: preset.barHeight + preset.borderOverhang * 2 }}>
			<div
				data-part="bar"
				className="absolute inset-x-0 overflow-hidden bg-secondary"
				style={{
					top: preset.borderOverhang,
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
			</div>

			{geometry.borderAt !== null && (
				<span
					data-part="border"
					// Насечка использует textPrimary, а не дублирующий его hex.
					// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
					className="absolute top-1/2 -translate-y-1/2 -translate-x-1/2 rounded-[1px] bg-text-primary"
					style={{
						left: percent(geometry.borderAt),
						width: preset.borderWidth,
						height: preset.barHeight + preset.borderOverhang * 2,
					}}
				/>
			)}

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
						// У края полосы маркер отступает на половину диаметра —
						// клип не прячет его под скруглением.
						left: geometry.markerClipped
							? `clamp(${markerHalf}px, ${percent(geometry.markerAt)}, calc(100% - ${markerHalf}px))`
							: percent(geometry.markerAt),
						width: preset.markerSize,
						height: preset.markerSize,
					}}
				/>
			)}
		</div>
	);
}

// Текстовое описание для скринридеров: состояние границы и итог.
function describeForScreenReader(geometry: FinResultGeometry): string {
	const border =
		geometry.labels.borderTitle !== null && geometry.labels.borderValue !== null
			? `${geometry.labels.borderTitle} ${geometry.labels.borderValue}`
			: "";
	const marker = geometry.labels.marker !== null ? `итог ${geometry.labels.marker}` : "";
	const incomplete = geometry.incomplete ? "неполный, сбой котировок" : "";

	return [border, marker, incomplete].filter(Boolean).join(", ") || "нет данных о результате";
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
