import type { ConstructionCard } from "@/lib/api/construction-card";
import { FullFinResultIndicator } from "@/components/finresult/fin-result-indicator";
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

	return (
		<section data-slot="construction-metric-strip" className="flex flex-col gap-4">
			<dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-3 xl:grid-cols-5">
				<div className="contents">
					<dt className="text-muted-foreground">общий P&L</dt>
					<dd>
						{metrics.totalPnL === null ? (
							// Итог без нереализованной части неполный — признак сбоя марок.
							<span className="text-muted-foreground" title="Общий P&L неполный: нереализованная часть не оценена из-за сбоя котировок">
								неполный (сбой котировок)
							</span>
						) : (
							<b className={`tabular-nums ${toneClass(metrics.totalPnL)}`}>
								{formatSignedAmount(metrics.totalPnL)} USDT
							</b>
						)}
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">
						% капитала{card.allocatedCapitalUsdt !== null ? ` (${formatAmount(card.allocatedCapitalUsdt)})` : ""}
					</dt>
					<dd>
						{metrics.totalPnLPercent === null ? (
							<span className="text-muted-foreground">{DASH}</span>
						) : (
							<span className={`tabular-nums font-semibold ${toneClass(metrics.totalPnLPercent)}`}>
								{formatSignedPercent(metrics.totalPnLPercent)}
							</span>
						)}
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">стоимость</dt>
					<dd>
						{card.hasOpenResidual === false ? (
							<span className="text-muted-foreground" title="Открытых остатков нет — стоимости нет">
								{DASH}
							</span>
						) : metrics.markValue === null ? (
							<span className="text-muted-foreground" title="Провайдер котировок недоступен — стоимость не оценена">
								сбой котировок
							</span>
						) : (
							<span className={`tabular-nums ${toneClass(metrics.markValue)}`}>{formatSignedAmount(metrics.markValue)}</span>
						)}
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">занято капитала, %</dt>
					<dd>
						{metrics.capitalUsagePercent === null ? (
							<span className="text-muted-foreground">{DASH}</span>
						) : (
							<span className={`tabular-nums ${toneClass(metrics.capitalUsagePercent)}`}>
								{formatSignedPercent(metrics.capitalUsagePercent)}
							</span>
						)}
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">реализ. P&L</dt>
					<dd>
						<span className={`tabular-nums ${toneClass(metrics.realizedPnL)}`}>{formatSignedAmount(metrics.realizedPnL)}</span>
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">нереализ. P&L</dt>
					<dd>
						{card.hasMarkFailure ? (
							<span className="text-muted-foreground" title="Провайдер котировок недоступен — нереализованный PnL не оценён">
								сбой котировок
							</span>
						) : (
							<span className={`tabular-nums ${toneClass(metrics.unrealizedPnL ?? 0)}`}>
								{formatSignedAmount(metrics.unrealizedPnL ?? 0)}
							</span>
						)}
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">корректировки</dt>
					<dd>
						<span className={`tabular-nums ${toneClass(metrics.adjustmentsPnL)}`}>
							{metrics.adjustmentsPnL === 0 ? DASH : formatSignedAmount(metrics.adjustmentsPnL)}
						</span>
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">котировки на</dt>
					<dd>
						{card.hasMarkFailure ? (
							<span className="text-muted-foreground" title="Провайдер котировок недоступен — время получения котировок неизвестно">
								сбой котировок
							</span>
						) : card.hasOpenResidual === false ? (
							<span className="text-muted-foreground" title="Открытых остатков нет — котировки оценке не нужны">
								не нужны
							</span>
						) : (
							<span>{card.marksAsOf === null ? DASH : formatMoment(card.marksAsOf)}</span>
						)}
					</dd>
				</div>
				<div className="contents">
					<dt className="text-muted-foreground">период</dt>
					<dd>
						{metrics.openedAt === null ? (
							<span className="text-muted-foreground">{DASH}</span>
						) : (
							<span title="Период конструкции с длительностью">
								{formatDay(metrics.openedAt)}
								{metrics.closedAt !== null ? ` — ${formatDay(metrics.closedAt)}` : " — …"}{" "}
								<span className="text-muted-foreground">
									({metrics.durationSeconds === null ? "" : formatDuration(metrics.durationSeconds)})
								</span>
							</span>
						)}
					</dd>
				</div>
			</dl>

			{/* Полный индикатор финансового результата: зоны планового риска и
			    профита, границы реализованной прибыли и итога (§9). */}
			<FullFinResultIndicator
				input={{
					plannedRisk: card.riskUsdt,
					plannedProfit: card.profitUsdt,
					realized: metrics.realizedPnL,
					unrealized: metrics.unrealizedPnL,
					quotesDegraded: card.hasMarkFailure,
				}}
			/>
		</section>
	);
}
