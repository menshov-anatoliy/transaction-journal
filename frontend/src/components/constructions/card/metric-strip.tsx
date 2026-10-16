import * as React from "react";
import { ChevronDown } from "lucide-react";
import type { ConstructionCard, SyntheticCloseDisputeInput } from "@/lib/api/construction-card";
import { Metric } from "@/components/design";
import { Button } from "@/components/ui/button";
import { FullFinResultIndicator, MediumFinResultIndicator } from "@/components/finresult/fin-result-indicator";
import { useIsMobile } from "@/lib/use-mobile";
import { DASH } from "@/lib/format/degradation";
import { formatDay, formatMoment } from "@/lib/format/display-time";
import { formatAmount, formatSignedAmount, formatSignedPercent } from "@/lib/format/quantity";
import { cn } from "@/lib/utils";

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
	/** Оспаривание неоспоренной связки по паре execId — команда карточки. */
	readonly onDispute: (key: SyntheticCloseDisputeInput) => void;
	/** Возврат оспоренной связки по паре execId — команда карточки. */
	readonly onRevert: (key: SyntheticCloseDisputeInput) => void;
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

/** Сводка неоспоренных связок по закрываемой ноге: лок и логический размер ноги. */
interface SyntheticLegSummary {
	/** Символ закрываемой опционной ноги. */
	readonly symbol: string;
	/** Инструментальный остаток ноги — не меняется логическим слоем. */
	readonly residual: number;
	/** Логический размер ноги: остаток минус часть, уведённая синтетикой. */
	readonly logicalResidual: number;
	/** Суммарный лок связок этой ноги, USDT. */
	readonly lockedUsdt: number;
}

/** Группирует неоспоренные связки по закрываемой ноге; спорные связки не
    участвуют ни в локе, ни в логическом размере. */
function summarizeSyntheticLegs(card: ConstructionCard): readonly SyntheticLegSummary[] {
	const byLeg = new Map<string, { quantity: number; locked: number }>();
	for (const close of card.syntheticCloses) {
		if (close.disputed) {
			continue;
		}

		const total = byLeg.get(close.closedLegSymbol) ?? { quantity: 0, locked: 0 };
		total.quantity += close.quantity;
		total.locked += close.lockedValueUsdt;
		byLeg.set(close.closedLegSymbol, total);
	}

	return [...byLeg.entries()].map(([symbol, total]) => {
		// Логический размер ноги считается от инструментального остатка
		// позиции; позиция ноги всегда присутствует в карточке.
		// Traceability: openspec:ui/screens#scenario-detail-synthetic-locked-line-shown
		const residual = card.positions.find((position) => position.symbol === symbol)?.residual ?? 0;
		return {
			symbol,
			residual,
			logicalResidual: residual - total.quantity,
			lockedUsdt: total.locked,
		};
	});
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

export function ConstructionMetricStrip({ card, onDispute, onRevert }: ConstructionMetricStripProps) {
	const metrics = card.metrics;
	// Узкий экран сохраняет ту же геометрию и подписи в среднем размере.
	// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
	const Indicator = useIsMobile() ? MediumFinResultIndicator : FullFinResultIndicator;
	// Строка синтетики выводится, пока есть распознанные связки — даже когда
	// все они оспорены: тогда строка остаётся свёрнутым переключателем без
	// величин, и возврат связки остаётся достижим (design.md, решение 3).
	// Traceability: openspec:ui/screens#scenario-dispute-list-available-when-all-disputed
	const hasSyntheticCloses = card.syntheticCloses.length > 0;
	// Величины строки (лок и логический размер ноги) считаются только по
	// неоспоренным связкам: спорная связка в логический слой не входит.
	// Traceability: openspec:ui/screens#requirement-detail-synthetic-close-line
	const syntheticLegs = summarizeSyntheticLegs(card);
	const syntheticLockedUsdt = syntheticLegs.reduce((sum, leg) => sum + leg.lockedUsdt, 0);
	// Раскрытие списка связок — состояние просмотра, а не фильтр: живёт
	// локально в компоненте и не попадает в URL (design.md, решение 2).
	// Traceability: change:add-synthetic-close-dispute-ui/design#d2
	const [syntheticListOpen, setSyntheticListOpen] = React.useState(false);

	return (
		<section data-slot="construction-metric-strip" className="flex flex-col gap-4">
			{/* Сводка показателей — примитив Метрика (нода jtDmV) в карточке мастера
			    «Сводка метрик» (h69OG) Body #2: поверхность с каймой, радиус 12,
			    паддинги [14,18], строка из девяти метрик с зазором 20; итог (M1)
			    акцентируется кеглем 16, разбивка (M2–M9) — кегль 14. Цвет знака
			    величин — доменная семантика финрезультата, в мастере не задан.
			    Под строкой метрик — разделитель и строка «зафиксировано
			    синтетикой» из мастера сводки (5.1 change synthetic-close-logical-layer). */}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<div
				data-slot="construction-metrics-card"
				className="flex flex-col gap-3 rounded-lg border bg-surface px-[18px] py-3.5"
			>
				<div className="flex flex-wrap gap-5">
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

				{hasSyntheticCloses && (
					<>
						{/* Разделитель строки синтетики от метрик — линия по мастеру h69OG. */}
						<div className="h-px bg-divider" />
						{/* Строка «зафиксировано синтетикой» (мастер Z5uVG): подпись 11
						    textMuted с трекингом 0.3, величина лока к экспирации 14/600
						    акцентом знака, логический размер ноги 12 textSecondary.
						    Величина не пересчитывает «Реализованный результат» и
						    индикатор финрезультата — те остаются инструментальными.
						    Вся строка — переключатель раскрытия списка связок. */}
						{/* Traceability: openspec:ui/screens#requirement-detail-synthetic-close-line */}
						<button
							type="button"
							data-slot="synthetic-locked-line"
							aria-expanded={syntheticListOpen}
							className="flex flex-wrap items-center gap-2.5 text-left"
							onClick={() => setSyntheticListOpen((open) => open === false)}
						>
							<span className="text-[11px] tracking-[0.3px] text-text-muted">
								зафиксировано синтетикой
							</span>
							{/* Величины строки живут только при неоспоренных связках:
							    когда все связки оспорены, остаётся подпись с
							    переключателем — без лока и логических размеров. */}
							{/* Traceability: openspec:ui/screens#scenario-dispute-list-available-when-all-disputed */}
							{syntheticLegs.length > 0 && (
								<>
									<span
										data-slot="synthetic-locked-value"
										className={cn("tabular-nums text-[14px] font-semibold", toneClass(syntheticLockedUsdt))}
									>
										{`${formatSignedAmount(syntheticLockedUsdt)} USDT`}
									</span>
									<span data-slot="synthetic-locked-legs" className="text-[12px] text-text-secondary">
										{syntheticLegs
											.map((leg) => `${leg.symbol} · нога ${formatAmount(leg.residual)} → ${formatAmount(leg.logicalResidual)}`)
											.join(" · ")}
									</span>
								</>
							)}
							{/* Хвостовой chevron переключателя раскрытия списка связок. */}
							{/* Traceability: change:add-synthetic-close-dispute-ui/design#d2 */}
							<ChevronDown
								aria-hidden
								className={cn("size-3.5 text-text-muted transition-transform", syntheticListOpen && "rotate-180")}
							/>
						</button>
						{/* Раскрытый список распознанных связок: каждая связка видна,
						    включая оспоренные — приглушённые с признаком спора; действие
						    «Оспорить»/«Вернуть» в конце строки вызывает команду карточки,
						    а карточка после действия перечитывается целиком. */}
						{/* Traceability: openspec:ui/screens#requirement-detail-synthetic-close-dispute-list */}
						{syntheticListOpen && (
							<div role="list" data-slot="synthetic-closes-list" className="flex flex-col gap-1 pl-2.5">
								{card.syntheticCloses.map((close) => (
									<div
										key={`${close.linearExecId}/${close.counterOptionExecId}`}
										role="listitem"
										data-slot="synthetic-close-row"
										data-disputed={close.disputed}
										className={cn(
											"flex flex-wrap items-center gap-2 rounded-sm px-2 py-1 text-[12px]",
											close.disputed && "text-text-muted",
										)}
									>
										<span className="text-text-secondary">{close.closedLegSymbol}</span>
										<span className="tabular-nums">{`· ${formatAmount(close.quantity)}`}</span>
										<span className="tabular-nums">{`· лок ${formatSignedAmount(close.lockedValueUsdt)} USDT`}</span>
										{/* Признак спора оспоренной связки — спорная связка
										    остаётся видимой в списке, но из логического
										    слоя (лока и размера ноги) исключена. */}
										{/* Traceability: openspec:ui/screens#scenario-dispute-list-expands-shows-links */}
										{close.disputed && (
											<span data-slot="synthetic-close-disputed-mark" className="text-[11px] tracking-[0.3px]">
												оспорена
											</span>
										)}
										{/* Действие адресует связку парой execId: спор идёт
										    через команду карточки с перечитыванием снимка. */}
										{/* Traceability: openspec:ui/screens#scenario-revert-action-restores-link */}
										<Button
											variant="ghost"
											size="sm"
											className="ml-auto h-6 px-2 text-[11px]"
											onClick={() =>
												close.disputed
													? onRevert({ linearExecId: close.linearExecId, counterOptionExecId: close.counterOptionExecId })
													: onDispute({ linearExecId: close.linearExecId, counterOptionExecId: close.counterOptionExecId })}
										>
											{close.disputed ? "Вернуть" : "Оспорить"}
										</Button>
									</div>
								))}
							</div>
						)}
					</>
				)}
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
					// Открытые остатки карточки из read-модели: без них сбой
					// марок не делает индикатор неполным.
					// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
					hasOpenResidual: card.hasOpenResidual,
					// Пара «число + статус» из контракта API: конечный риск
					// даёт насечку, unbounded/unavailable — состояние
					// без насечки.
					// Traceability: openspec:ui/screens#requirement-risk-profit-hint
					// Traceability: change:show-unbounded-finresult-risk/design#d1
					realRisk: metrics.realRiskUsdt,
					realRiskStatus: metrics.realRiskStatus,
				}}
			/>
		</section>
	);
}
