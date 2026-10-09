import * as React from "react";
import { cn } from "@/lib/utils";
import {
	computeFinResultGeometry,
	type FinResultGeometry,
	type FinResultInput,
	type FinResultSpan,
} from "@/lib/finresult/geometry";

// Индикатор финансового результата конструкции: полный (карточка/превью),
// средний (сводные блоки) и компактный (строки таблиц). Все три вида рендерят
// одну геометрию калькулятора в долях шкалы и отличаются только пресетом
// размеров: полоса с зонами планового риска/профита/сверхприбыли, заливки
// результата, насечка границы realized и маркер итога с подписями двумя
// единицами у полного и среднего видов.
// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

/** Пропсы всех видов индикатора. */
export interface FinResultIndicatorProps {
	/** Показатели конструкции и текущий результат. */
	readonly input: FinResultInput;
	readonly className?: string;
}

// Пресет размеров представления: высоты, шрифты и радиусы из макета
// (полный референс onchD, средний rMxFZ, компактный FMghY).
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
	scaleLabelClass: "text-[11.5px]",
	markerLabelClass: "text-[10.5px]",
};

const MEDIUM_PRESET: SizePreset = {
	barHeight: 14,
	barRadius: 7,
	markerSize: 10,
	borderWidth: 2,
	borderOverhang: 4,
	scaleLabelClass: "text-[10.5px]",
	markerLabelClass: "text-[9.5px]",
};

const COMPACT_PRESET: SizePreset = {
	barHeight: 10,
	barRadius: 5,
	markerSize: 8,
	borderWidth: 2,
	borderOverhang: 2,
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

// Полный и средний виды: деления шкалы над полосой, полоса, подписи
// границы и маркера под полосой.
function DetailedFinResultIndicator({
	input,
	preset,
	className,
	dataSlot,
}: FinResultIndicatorProps & { preset: SizePreset; dataSlot: string }) {
	const geometry = computeFinResultGeometry(input);

	return (
		<div
			data-slot={dataSlot}
			data-incomplete={geometry.incomplete ? "true" : "false"}
			className={cn("flex flex-col", preset.markerLabelClass.includes("9.5") ? "gap-2" : "gap-2.5", className)}
			role="img"
			aria-label={describeForScreenReader(geometry)}
		>
			<ScaleLabels geometry={geometry} preset={preset} />
			<FinResultBar geometry={geometry} preset={preset} />
			<MarkerLabels geometry={geometry} preset={preset} />
		</div>
	);
}

// Деления шкалы: −Риск / 0 / +Профит и край золотой зоны при сверхприбыли.
function ScaleLabels({ geometry, preset }: { geometry: FinResultGeometry; preset: SizePreset }) {
	return (
		<div className={cn("relative h-3.5", preset.scaleLabelClass)}>
			{geometry.labels.risk !== null && (
				<span
					data-part="risk-scale"
					className="absolute left-0 -translate-x-1/2 text-[color:var(--fin-negative)]"
				>
					{geometry.labels.risk}
				</span>
			)}
			{!geometry.neutral && (
				<span
					data-part="zero-scale"
					className="absolute -translate-x-1/2 text-muted-foreground"
					style={{ left: percent(geometry.zeroAt) }}
				>
					{geometry.labels.zero}
				</span>
			)}
			{geometry.labels.profit !== null && (
				<span
					data-part="profit-scale"
					className="absolute -translate-x-1/2 font-medium text-[color:var(--fin-positive-strong)]"
					style={{ left: percent(geometry.profitZone ? geometry.profitZone.to : 1) }}
				>
					{geometry.labels.profit}
				</span>
			)}
			{geometry.labels.superEnd !== null && (
				// Подпись сверхприбыли сохраняет точный цвет макета: собственного
				// токена пока нет; его добавление запланировано в дизайн-backlog #73.
				// Traceability: issue:#73
				// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
				<span
					data-part="super-scale"
					className="absolute right-0 translate-x-1/2 text-[#8a6a1f]"
				>
					{geometry.labels.superEnd}
				</span>
			)}
		</div>
	);
}

// Полоса: тонируемые зоны в скруглённой маске, заливки результата,
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
				{geometry.fillUnreal && (
					<span
						data-part="fill-unreal"
						className={cn(
							"absolute inset-y-0",
							geometry.tone === "positive"
								? "bg-[color:var(--fin-unreal-positive)]"
								: "bg-[color:var(--fin-unreal-negative)]",
						)}
						style={spanStyle(geometry.fillUnreal)}
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

// Подписи под полосой: у границы — состояние риска двумя единицами,
// у маркера — нереализованная или вынесенный итог.
function MarkerLabels({ geometry, preset }: { geometry: FinResultGeometry; preset: SizePreset }) {
	const borderToneClass =
		geometry.tone === "positive" || (geometry.labels.borderTitle === "риска нет")
			? "text-[color:var(--fin-positive-strong)]"
			: "text-[color:var(--fin-negative)]";
	const markerToneClass =
		geometry.tone === "positive"
			? "text-[color:var(--fin-positive-strong)]"
			: "text-[color:var(--fin-negative)]";

	return (
		<div className={cn("relative h-9", preset.markerLabelClass)}>
			{geometry.labels.borderTitle !== null && geometry.borderAt !== null && (
				<span
					data-part="border-label"
					className="absolute top-0 flex -translate-x-1/2 flex-col items-center gap-px whitespace-nowrap"
					style={{ left: percent(geometry.borderAt) }}
				>
					<span className={cn("font-medium", borderToneClass)}>{geometry.labels.borderTitle}</span>
					<span className={cn("font-semibold", borderToneClass)}>{geometry.labels.borderValue}</span>
				</span>
			)}

			{geometry.labels.marker !== null && geometry.markerAt !== null && (
				<span
					data-part="marker-label"
					className={cn(
						"absolute top-4 -translate-x-1/2 font-semibold whitespace-nowrap tabular-nums",
						markerToneClass,
					)}
					style={{ left: percent(geometry.markerAt) }}
				>
					{geometry.labels.marker}
				</span>
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

function percent(fraction: number): string {
	return `${fraction * 100}%`;
}

function spanStyle(span: FinResultSpan): React.CSSProperties {
	return {
		left: percent(span.from),
		width: percent(span.to - span.from),
	};
}
