import * as React from "react";
import { ChevronDown, CircleHelp } from "lucide-react";
import type {
	ConstructionCard,
	SyntheticCloseDisputeInput,
	SyntheticCloseManualLinkInput,
} from "@/lib/api/construction-card";
import { Metric } from "@/components/design";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { FullFinResultIndicator, MediumFinResultIndicator } from "@/components/finresult/fin-result-indicator";
import { useIsMobile } from "@/lib/use-mobile";
import { DASH } from "@/lib/format/degradation";
import { formatDay, formatMoment } from "@/lib/format/display-time";
import { EXPIRY_WARNING_DAYS } from "@/lib/lifetime";
import { formatAmount, formatSignedAmount, formatSignedPercent } from "@/lib/format/quantity";
import { ManualSyntheticLinkForm } from "./manual-synthetic-link-form";
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
	/** Состояние команд ручной связки (создание/удаление) — блокирует действия. */
	readonly manualLinkPending: boolean;
	/** Человекочитаемая причина отказа команд ручной связки. */
	readonly manualLinkError: string | null;
	/** Открытая форма ручной связки — состояние владеет страница карточки. */
	readonly manualLinkFormOpen: boolean;
	/** Создание ручной синтетической связки из формы. */
	readonly onCreateManualLink: (input: SyntheticCloseManualLinkInput) => void;
	/** Удаление ручной связки по идентификатору из ответа создания. */
	readonly onDeleteManualLink: (linkId: number) => void;
	/** Смена состояния формы ручной связки. */
	readonly onManualLinkFormOpenChange: (open: boolean) => void;
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

export function ConstructionMetricStrip({
	card,
	onDispute,
	onRevert,
	manualLinkPending,
	manualLinkError,
	manualLinkFormOpen,
	onCreateManualLink,
	onDeleteManualLink,
	onManualLinkFormOpenChange,
}: ConstructionMetricStripProps) {
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
					{/* Саммари времени жизни открытых опционных ног: остаток суток до
					    ближайшей экспирации (красный при ≤ 14), нетто-распад в сутки
					    и суммарная временная стоимость; без опционных ног — прочерк. */}
					{/* Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary */}
					{/* Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen */}
					<Metric
						label="до экспирации"
						valueClassName={`tabular-nums ${VALUE_PART} ${metrics.daysToNearestExpiry !== null && metrics.daysToNearestExpiry <= EXPIRY_WARNING_DAYS ? "text-destructive" : ""}`}
					>
						{metrics.daysToNearestExpiry === null ? (
							<span className="text-text-muted" title="Открытых опционных ног нет — времени жизни нет">
								{DASH}
							</span>
						) : (
							<span title="Остаток суток до ближайшей экспирации открытых опционных ног">{metrics.daysToNearestExpiry} дн.</span>
						)}
					</Metric>
					<Metric
						label="распад / сутки"
						valueClassName={`tabular-nums ${VALUE_PART} ${toneClass(metrics.netDailyDecayUsdt ?? 0)}`}
					>
						{metrics.netDailyDecayUsdt === null ? (
							<span className="text-text-muted" title="Тикеры опционных ног недоступны — распад не оценён">
								{DASH}
							</span>
						) : (
							formatSignedAmount(metrics.netDailyDecayUsdt)
						)}
					</Metric>
					<Metric label="временная стоимость" valueClassName={`tabular-nums ${VALUE_PART}`}>
						{metrics.totalTimeValueUsdt === null ? (
							<span className="text-text-muted" title="Тикеры опционных ног недоступны — временная стоимость не оценена">
								{DASH}
							</span>
						) : (
							formatSignedAmount(metrics.totalTimeValueUsdt)
						)}
					</Metric>
				</div>

				{/* Зона синтетики: строка «зафиксировано синтетикой» с раскрытым
				    списком связок, действие «Ручная связка» и форма создания.
				    Действие доступно и без действующих связок — первую ручную
				    связку иначе не создать; при связках оно встроено в строку
				    синтетики справа, без связок строка не выводится (строка
				    синтетики отсутствует, действие живёт отдельным рядом).
				    Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link */}
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
							className="flex min-w-0 flex-1 flex-wrap items-center gap-2.5 text-left"
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
					</>
				)}
				{/* Действие «Ручная связка»: точка входа формы создания ручной
				    синтетической связки из двух сделок конструкции; при строке
				    синтетики встроено в её ряд, без строки — единственный элемент
				    ряда справа. */}
				{/* Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link */}
				<div data-slot="synthetic-manual-link-row" className="flex items-center gap-2.5">
					{hasSyntheticCloses === false && <span className="h-px flex-1" aria-hidden="true" />}
					<Button
						variant="ghost"
						size="sm"
						className="h-auto px-2 py-1 text-[11px]"
						disabled={manualLinkPending}
						onClick={() => onManualLinkFormOpenChange(true)}
					>
						Ручная связка
					</Button>
				</div>
				{/* Раскрытый список связок: каждая связка видна, включая оспоренные
				    — приглушённые с признаком спора; действие в конце строки адресует
				    команду карточки — «Оспорить»/«Вернуть» распознанной связки парой
				    execId, «Удалить» ручной связке по её идентификатору. */}
				{/* Traceability: openspec:ui/screens#requirement-detail-synthetic-close-dispute-list */}
				{hasSyntheticCloses && syntheticListOpen && (
					<div role="list" data-slot="synthetic-closes-list" className="flex flex-col gap-1 pl-2.5">
						{card.syntheticCloses.map((close, index) => {
							// Ручная связка узнаваема по идентификатору строки связки
							// в строке снимка: удаление адресуется списком напрямую
							// в любой сессии; у ручной связки спора нет.
							// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
							const isManualLink = close.manualLinkId !== null;
							return (
								<div
									key={`${index}:${close.linearExecId}/${close.counterOptionExecId}`}
									role="listitem"
									data-slot="synthetic-close-row"
									data-disputed={close.disputed}
									className={cn(
										"flex flex-wrap items-center gap-2 rounded-[2px] px-2 py-1 text-[12px]",
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
									{isManualLink ? (
										// Ручная связка удаляется свободно по идентификатору
										// из строки снимка; спор правилами не предусмотрен,
										// поэтому «Оспорить» ручной строке не показывается.
										// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
										<Button
											variant="ghost"
											size="sm"
											className="ml-auto h-6 px-2 text-[11px]"
											disabled={manualLinkPending}
											onClick={() => onDeleteManualLink(close.manualLinkId!)}
										>
											<span className="text-neg">Удалить</span>
										</Button>
									) : (
										// Действие адресует связку парой execId: спор идёт
										// через команду карточки с перечитыванием снимка.
										// Traceability: openspec:ui/screens#scenario-revert-action-restores-link
										<Button
											variant="ghost"
											size="sm"
											className="ml-auto h-6 px-2 text-[11px]"
											onClick={() => {
												// Ключ связки — пара execId: она адресует и JSX-ключ
												// строки списка, и команду спора или возврата.
												const execIdKey = { linearExecId: close.linearExecId, counterOptionExecId: close.counterOptionExecId };
												if (close.disputed) {
													onRevert(execIdKey);
												} else {
													onDispute(execIdKey);
												}
											}}
										>
											{close.disputed ? "Вернуть" : "Оспорить"}
										</Button>
									)}
								</div>
							);
						})}
					</div>
				)}
				{manualLinkFormOpen && (
					// Форма ручной синтетической связки (мастер vG1f8): выбор линейной
					// сделки и встречного опциона из сделок конструкции, количество;
					// правила пары проверяет сервер, причина отказа показывается формой.
					// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
					<ManualSyntheticLinkForm
						trades={card.trades}
						pending={manualLinkPending}
						error={manualLinkError}
						onSubmit={onCreateManualLink}
						onCancel={() => onManualLinkFormOpenChange(false)}
					/>
				)}
			</div>

			{/* Полный индикатор финансового результата: зоны планового риска и
			    профита, насечка худшего исхода конструкции, маркер итога (§9);
			    граница шкалы именуется худшим исходом по ADR-0015, а не
			    величиной реального риска. */}
			{/* Подсказка чтения индикатора карточки: «?» справа от полосы по
			    клику открывает попап с пояснением чтения шкалы — зоны планового
			    риска, профита и сверхприбыли, насечка худшего исхода
			    конструкции, маркер итога и состояния вне шкалы; закрывается
			    кликом вне, Esc и повторным кликом по триггеру. Подсказка живёт
			    только в карточке: компактный вид таблиц и краткая карточка
			    контекстной панели её не получают и сохраняют доступные
			    названия состояний риска (aria-label/title компактного вида). */}
			{/* Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-opens */}
			{/* Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-explains-current-model */}
			{/* Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-closes */}
			{/* Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-card-only */}
			<div className="relative">
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
						// без насечки. Худший исход ставит позицию границы.
						// Traceability: openspec:ui/screens#requirement-risk-profit-hint
						// Traceability: change:show-unbounded-finresult-risk/design#d1
						realRisk: metrics.realRiskUsdt,
						worstCase: metrics.worstCaseUsdt,
						realRiskStatus: metrics.realRiskStatus,
					}}
				/>
				{/* Триггер прижат к правому краю полосы на её вертикальном
				    уровне: деления шкалы над полосой занимают одну строку
				    (14px полного пресета) плюс зазор секций 10px, полоса 14px —
				    центр полосы на 31px от верха индикатора, поэтому триггер
				    20px ставится с top 21px; средний вид геометрически близок
				    (13px + 8px, центр полосы 28px). Попап открывается справа
				    от триггера, на узком экране Radix разворачивает его
				    столкновениями с границей вьюпорта. */}
				<Popover>
					<PopoverTrigger
						aria-label="Как читать индикатор"
						className="absolute top-[21px] right-0 flex size-5 items-center justify-center rounded-full border bg-surface-2 text-text-secondary"
					>
						<CircleHelp className="size-3.5" aria-hidden="true" />
					</PopoverTrigger>
					{/* Состав пояснения дословно повторяет мастер design.pen
					    «Подсказка/Чтение индикатора»: заголовок, свотчи зон,
					    насечка худшего исхода, маркер итога и примечание про
					    состояния вне шкалы. */}
					<PopoverContent
						side="right"
						align="start"
						sideOffset={12}
						className="flex w-[340px] max-w-[calc(100vw-1.5rem)] flex-col gap-2"
					>
						<p className="text-[12.5px] font-semibold text-text-primary">Как читать индикатор</p>
						<div className="flex items-center gap-2 text-[11px] text-text-secondary">
							<span aria-hidden="true" className="size-3 shrink-0 rounded-[3px] bg-risk-zone" />
							<span>плановый риск — красная зона от −Риск до 0</span>
						</div>
						<div className="flex items-center gap-2 text-[11px] text-text-secondary">
							<span aria-hidden="true" className="size-3 shrink-0 rounded-[3px] bg-profit-zone" />
							<span>плановый профит — зелёная зона от 0 до +Профит</span>
						</div>
						<div className="flex items-center gap-2 text-[11px] text-text-secondary">
							<span aria-hidden="true" className="size-3 shrink-0 rounded-[3px] bg-super-zone" />
							<span>сверхприбыль — золотая зона за +Профит</span>
						</div>
						<div className="flex items-center gap-2 text-[11px] text-text-secondary">
							<span aria-hidden="true" className="h-[14px] w-[3px] shrink-0 bg-text-primary" />
							<span>насечка — худший исход конструкции, подпись «худший исход · X USDT · N%» от плановой границы зоны (насечка может стоять правее реализованного результата)</span>
						</div>
						<div className="flex items-center gap-2 text-[11px] text-text-secondary">
							<span aria-hidden="true" className="size-[11px] shrink-0 rounded-full bg-primary" />
							<span>маркер — оценка итога «заберу при ликвидации сейчас»</span>
						</div>
						<p className="text-[10.5px] text-text-muted">
							Состояния вне шкалы показывают отдельно от полосы: «риск не ограничен» и «не удалось рассчитать»; «риска нет» — при нулевом реальном риске.
						</p>
					</PopoverContent>
				</Popover>
			</div>
		</section>
	);
}
