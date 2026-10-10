import type { ConstructionCard } from "@/lib/api/construction-card";
import { Metric } from "@/components/design";
import { FullFinResultIndicator, MediumFinResultIndicator } from "@/components/finresult/fin-result-indicator";
import { useIsMobile } from "@/lib/use-mobile";
import { DASH } from "@/lib/format/degradation";
import { formatDay, formatMoment } from "@/lib/format/display-time";
import { formatAmount, formatSignedAmount, formatSignedPercent } from "@/lib/format/quantity";

// Сводка метрик карточки (kstrip) по концепции §4: общий P&L, % капитала,
// стоимость, занятость, реализованная и нереализованная части, корректировки,
// «котировки на» и период с длительностью; под сводкой — полный индикатор
// финансового результата (§9). Деградация при сбое котировок — признак сбоя
// вместо нереализованных величин.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

/** Пропсы kstrip метрик карточки. */
export interface ConstructionMetricStripProps {
	/** Снимок карточки из API. */
	readonly card: ConstructionCard;
}

/** Кегль значения показателя: итог — 16, разбивка — 14 (инстансы M1–M9). */
const VALUE_TOTAL = "text-[16px]";
const VALUE_PART = "text-[14px]";

/** Тон величины: положительная — зелёная, отрицательная — красная. */
function toneClass(value: number): string {
	if (value > 0) {
		return "text-[color:var(--fin-positive-strong)]";
	}

	return value < 0 ? "text-[color:var(--fin-negative)]" : "";
}

/** Форматирует длительность периода человекочитаемыми интервалами. */
function formatDuration(seconds: number): string {
	const days = Math.floor(seconds / 86400);
	const hours = Math.floor((seconds % 86400) / 3600);
	const minutes = Math.floor((seconds % 3600) / 60);
	const parts: string[] = [];
	if (days > 0) {
		parts.push(`${days} дн`);
	}

	if (hours > 0) {
		parts.push(`${hours} ч`);
	}

	if (minutes > 0 || parts.length === 0) {
		parts.push(`${minutes} мин`);
	}

	return parts.join(" ");
}

export function ConstructionMetricStrip({ card }: ConstructionMetricStripProps) {
	const metrics = card.metrics;
	// Узкий экран сохраняет ту же геометрию и подписи в среднем размере.
	// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
	const Indicator = useIsMobile() ? MediumFinResultIndicator : FullFinResultIndicator;

	return (
		<section data-slot="construction-metric-strip" className="flex flex-col gap-4">
			{/* Сводка показателей — примитив Метрика (нода jtDmV) в карточке мастера
			    «Сводка метрик» (h69OG) Body #2: поверхность с каймой, радиус 12,
			    паддинги [14,18], одна строка из девяти метрик с зазором 20; итог (M1)
			    акцентируется кеглем 16, разбивка (M2–M9) — кегль 14. Цвет знака
			    величин — доменная семантика финрезультата, в мастере не задан. */}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<div
				data-slot="construction-metrics-card"
				className="flex flex-wrap gap-5 rounded-lg border bg-surface px-[18px] py-3.5"
			>
				<Metric
					label="общий P&L"
					valueClassName={`tabular-nums ${VALUE_TOTAL} ${toneClass(metrics.totalPnL ?? 0)}`}
				>
					{metrics.totalPnL === null ? (
						// Итог без нереализованной части неполный — признак сбоя марок.
						<span className="text-text-muted" title="Общий P&L неполный: нереализованная часть не оценена из-за сбоя котировок">
							неполный (сбой котировок)
						</span>
					) : (
						`${formatSignedAmount(metrics.totalPnL)} USDT`
					)}
				</Metric>
				<Metric
					label={`% капитала${card.allocatedCapitalUsdt !== null ? ` (${formatAmount(card.allocatedCapitalUsdt)})` : ""}`}
					valueClassName={`tabular-nums ${VALUE_PART} ${toneClass(metrics.totalPnLPercent ?? 0)}`}
				>
					{metrics.totalPnLPercent === null ? (
						<span className="text-text-muted">{DASH}</span>
					) : (
						formatSignedPercent(metrics.totalPnLPercent)
					)}
				</Metric>
				<Metric
					label="стоимость"
					valueClassName={`tabular-nums ${VALUE_PART} ${toneClass(metrics.markValue ?? 0)}`}
				>
					{card.hasOpenResidual === false ? (
						<span className="text-text-muted" title="Открытых остатков нет — стоимости нет">
							{DASH}
						</span>
					) : metrics.markValue === null ? (
						<span className="text-text-muted" title="Провайдер котировок недоступен — стоимость не оценена">
							сбой котировок
						</span>
					) : (
						formatSignedAmount(metrics.markValue)
					)}
				</Metric>
				<Metric
					label="занято капитала, %"
					valueClassName={`tabular-nums ${VALUE_PART} ${toneClass(metrics.capitalUsagePercent ?? 0)}`}
				>
					{metrics.capitalUsagePercent === null ? (
						<span className="text-text-muted">{DASH}</span>
					) : (
						formatSignedPercent(metrics.capitalUsagePercent)
					)}
				</Metric>
				<Metric
					label="реализ. P&L"
					valueClassName={`tabular-nums ${VALUE_PART} ${toneClass(metrics.realizedPnL)}`}
				>
					{formatSignedAmount(metrics.realizedPnL)}
				</Metric>
				<Metric
					label="нереализ. P&L"
					valueClassName={`tabular-nums ${VALUE_PART} ${toneClass(metrics.unrealizedPnL ?? 0)}`}
				>
					{card.hasMarkFailure ? (
						<span className="text-text-muted" title="Провайдер котировок недоступен — нереализованный PnL не оценён">
							сбой котировок
						</span>
					) : (
						formatSignedAmount(metrics.unrealizedPnL ?? 0)
					)}
				</Metric>
				<Metric
					label="корректировки"
					valueClassName={`tabular-nums ${VALUE_PART} ${toneClass(metrics.adjustmentsPnL)}`}
				>
					{metrics.adjustmentsPnL === 0 ? DASH : formatSignedAmount(metrics.adjustmentsPnL)}
				</Metric>
				<Metric label="котировки на" valueClassName={VALUE_PART}>
					{card.hasMarkFailure ? (
						<span className="text-text-muted" title="Провайдер котировок недоступен — время получения котировок неизвестно">
							сбой котировок
						</span>
					) : card.hasOpenResidual === false ? (
						<span className="text-text-muted" title="Открытых остатков нет — котировки оценке не нужны">
							не нужны
						</span>
					) : (
						(card.marksAsOf === null ? DASH : formatMoment(card.marksAsOf))
					)}
				</Metric>
				<Metric label="период" valueClassName={VALUE_PART}>
					{metrics.openedAt === null ? (
						<span className="text-text-muted">{DASH}</span>
					) : (
						<span title="Период конструкции с длительностью">
							{formatDay(metrics.openedAt)}
							{metrics.closedAt !== null ? ` — ${formatDay(metrics.closedAt)}` : " — …"}{" "}
							<span className="text-text-muted">
								({metrics.durationSeconds === null ? "" : formatDuration(metrics.durationSeconds)})
							</span>
						</span>
					)}
				</Metric>
			</div>

			{/* Полный индикатор финансового результата: зоны планового риска и
			    профита, граница реального риска, маркер итога (§9). */}
			<Indicator
				input={{
					plannedRisk: card.riskUsdt,
					plannedProfit: card.profitUsdt,
					realized: metrics.realizedPnL,
					unrealized: metrics.unrealizedPnL,
					quotesDegraded: card.hasMarkFailure,
					// Граница реального риска карточки: метрика бэкенда, при null —
					// каскад заглушек внутри геометрии.
					// Traceability: openspec:ui/screens#requirement-risk-profit-hint
					realRisk: metrics.realRiskUsdt,
				}}
			/>
		</section>
	);
}
