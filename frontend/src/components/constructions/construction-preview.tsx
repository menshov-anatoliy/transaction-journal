import { Link } from "react-router";
import type { ConstructionPreview } from "@/lib/api/constructions";
import { Metric } from "@/components/design";
import { ConstructionStatusChip } from "@/components/constructions/construction-status-chip";
import { MediumFinResultIndicator } from "@/components/finresult/fin-result-indicator";
import { Button } from "@/components/ui/button";
import { DASH } from "@/lib/format/degradation";
import { formatDay } from "@/lib/format/display-time";
import { pluralForm } from "@/lib/format/plural";
import { formatAmount, formatSignedAmount } from "@/lib/format/quantity";
import { cn } from "@/lib/utils";

// Краткая карточка конструкции в контекстной правой панели раздела по
// мастеру «Карточка конструкции/Превью» (N6abN): шапка с именем и
// статусной пилюлей, средний индикатор финрезультата (rMxFZ) с
// приглушёнными плановыми границами, метрики с цветовой индикацией по
// знаку, строка периода и единственное действие «Открыть карточку».
// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
// Traceability: change:rework-spa-shell-and-screens/design#D4

/** Пропсы краткой карточки конструкции. */
export interface ConstructionPreviewCardProps {
	/** Read-only снимок выделенной конструкции из API. */
	readonly preview: ConstructionPreview;
	/** Число живых подсказок из строки обзора; без значения сегмент скрывается. */
	readonly liveHintCount?: number;
}

export function ConstructionPreviewCard({ preview, liveHintCount }: ConstructionPreviewCardProps) {
	const constructionPath = `/constructions/${preview.constructionId}`;

	// Длительность периода в календарных днях: закрытая конструкция — до
	// даты закрытия, открытая — до текущего дня.
	const periodDays =
		preview.openedAt === null
			? null
			: Math.max(0, Math.floor((Date.parse(preview.closedAt ?? new Date().toISOString()) - Date.parse(preview.openedAt)) / 86_400_000));

	return (
		<section data-slot="construction-preview" className="flex flex-col gap-3">
			{/* Шапка карточки: имя Inter 14/600 на textPrimary и статусная
			    пилюля справа (фрейм Header мастера N6abN). */}
			<header className="flex items-center justify-between gap-2">
				<h2 className="text-sm leading-tight font-semibold">{preview.name}</h2>
				{/* Статус — тот же примитив пилюли, что в таблице и карточке.
				    Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
				<ConstructionStatusChip status={preview.status} />
			</header>

			{/* Средний индикатор финрезультата: плановые границы риска/профита
			    подписаны приглушённо (мастер rMxFZ), граница реального риска —
			    красным, маркер итога — по знаку.
			    Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card */}
			<MediumFinResultIndicator
				input={{
					plannedRisk: preview.riskUsdt,
					plannedProfit: preview.profitUsdt,
					realized: preview.realizedPnL,
					unrealized: preview.unrealizedPnL,
					quotesDegraded: preview.hasMarkFailure,
					// Открытые остатки есть, пока есть позиции с ненулевым
					// остатком: без них сбой марок не делает индикатор неполным.
					// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
					hasOpenResidual: preview.counts.openPositions > 0,
					// Пара «число + статус» из контракта API: конечный риск
					// даёт насечку, unbounded/unavailable — состояние
					// без насечки. Худший исход ставит позицию границы.
					// Traceability: openspec:ui/screens#requirement-risk-profit-hint
					// Traceability: change:show-unbounded-finresult-risk/design#d1
					realRisk: preview.realRiskUsdt,
					worstCase: preview.worstCaseUsdt,
					realRiskStatus: preview.realRiskStatus,
				}}
			/>

			{/* Метрики краткой карточки — две строки примитива Метрика
			    (MRow1/MRow2 мастера N6abN): капитал и стоимость нейтральные,
			    реализованная и нереализованная части — цветом по знаку. */}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			<div className="grid grid-cols-2 gap-x-3 gap-y-2.5">
				<Metric label="капитал">
					<NullableValue value={preview.allocatedCapitalUsdt} format={formatAmount} />
				</Metric>
				<Metric label="стоимость">
					<NullableValue value={preview.markValue} format={formatSignedAmount} />
				</Metric>
				<Metric label="реализов.">
					<SignedToneValue value={preview.realizedPnL} format={formatSignedAmount} />
				</Metric>
				<Metric label="нереализов.">
					{preview.unrealizedPnL === null ? (
						// Сбой марок — видимое состояние карточки: признак сбоя
						// занимает место нереализованной оценки.
						<span className="text-text-muted" title="Провайдер котировок недоступен — нереализованный PnL не оценён">
							сбой котировок
						</span>
					) : (
						<SignedToneValue value={preview.unrealizedPnL} format={formatSignedAmount} />
					)}
				</Metric>
			</div>

			{/* Строка периода: открытие, длительность и счётчики записей —
			    Inter 11 на textMuted (фрейм Period мастера N6abN). */}
			<p className="text-text-muted text-[11px] leading-[normal]">
				открыта <span>{preview.openedAt === null ? DASH : formatDay(preview.openedAt)}</span>
				{periodDays !== null ? ` · ${periodDays} дн.` : ""}
				{` · ${preview.counts.trades} ${pluralForm(preview.counts.trades, { one: "сделка", few: "сделки", many: "сделок" })}`}
				{` · ${preview.counts.adjustments} ${pluralForm(preview.counts.adjustments, {
					one: "корректировка",
					few: "корректировки",
					many: "корректировок",
				})}`}
				{liveHintCount !== undefined && liveHintCount > 0 ? ` · живых подсказок: ${liveHintCount}` : ""}
			</p>

			{/* Единственное действие карточки — переход на маршрут карточки
			    конструкции; открытие в новом окне покрывает двойной клик
			    по строке списка.
			    Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card */}
			<div>
				<Button asChild size="sm">
					<Link to={constructionPath}>Открыть карточку</Link>
				</Button>
			</div>
		</section>
	);
}

/** Величина с цветом по знаку: плюс — зелёная, минус — красная, ноль — нейтральная. */
function SignedToneValue({ value, format }: { value: number; format: (value: number) => string }) {
	return (
		<span
			className={cn(
				"tabular-nums",
				value > 0 ? "text-[color:var(--fin-positive-strong)]" : value < 0 ? "text-[color:var(--fin-negative)]" : undefined,
			)}
		>
			{format(value)}
		</span>
	);
}

/** Величина без специальной семантики сбоя: null — прочерк. */
function NullableValue({ value, format }: { value: number | null; format: (value: number) => string }) {
	return value === null ? <span className="text-text-muted">{DASH}</span> : <span className="tabular-nums">{format(value)}</span>;
}
