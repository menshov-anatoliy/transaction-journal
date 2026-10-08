import { Link } from "react-router";
import { ExternalLink } from "lucide-react";
import type { ConstructionPreview } from "@/lib/api/constructions";
import { Metric } from "@/components/design";
import { FullFinResultIndicator } from "@/components/finresult/fin-result-indicator";
import { Button } from "@/components/ui/button";
import { DASH } from "@/lib/format/degradation";
import { formatDay, formatMoment } from "@/lib/format/display-time";
import { formatAmount, formatSignedAmount, formatSignedPercent } from "@/lib/format/quantity";
import { SPA_BASE_PATH } from "@/router";

// Превью выделенной конструкции правой области раздела по концепции §3:
// read-only сводка метрик, полный индикатор финрезультата, период, счётчики
// позиций/сделок/корректировок и кнопки «Открыть карточку» / «В новом окне».
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Пропсы превью конструкции. */
export interface ConstructionPreviewCardProps {
	/** Read-only снимок выделенной конструкции из API. */
	readonly preview: ConstructionPreview;
}

export function ConstructionPreviewCard({ preview }: ConstructionPreviewCardProps) {
	const constructionPath = `/constructions/${preview.constructionId}`;

	return (
		<section data-slot="construction-preview" className="flex flex-col gap-4">
			<header className="flex flex-col gap-1">
				<h2 className="text-base leading-tight font-semibold">{preview.name}</h2>
				{/* Котировки оценки: отметка времени или признак сбоя провайдера. */}
				<p className="text-muted-foreground text-xs">
					{preview.hasMarkFailure ? (
						"неполный (сбой котировок)"
					) : (
						<>
							котировки на <span>{preview.marksAsOf === null ? DASH : formatMoment(preview.marksAsOf)}</span>
						</>
					)}
				</p>
			</header>

			{/* Полный индикатор финрезультата: плановые границы риска/профита
			    и текущий результат выделенной конструкции. */}
			<FullFinResultIndicator
				input={{
					plannedRisk: preview.riskUsdt,
					plannedProfit: preview.profitUsdt,
					realized: preview.realizedPnL,
					unrealized: preview.unrealizedPnL,
					quotesDegraded: preview.hasMarkFailure,
				}}
			/>

			{/* Сводка показателей — примитив Метрика (нода jtDmV) по мастеру
			    «Карточка конструкции/Превью» (N6abN, секция Metrics): две колонки
			    с зазорами 12/10, значения — базовая типографика примитива
			    15/600 textPrimary; состав показателей остаётся доменным. */}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<div className="grid grid-cols-2 gap-x-3 gap-y-2.5">
				<Metric label="итог">
					<SignedValue value={preview.totalPnL} degraded={preview.hasMarkFailure} format={formatSignedAmount} />
				</Metric>
				<Metric label="реализов.">
					<span className="tabular-nums">{formatSignedAmount(preview.realizedPnL)}</span>
				</Metric>
				<Metric label="нереализов.">
					<NullableValue value={preview.unrealizedPnL} format={formatSignedAmount} />
				</Metric>
				<Metric label="коррект.">
					<span className="tabular-nums">{formatSignedAmount(preview.adjustmentsPnL)}</span>
				</Metric>
				<Metric label="капитал">
					<NullableValue value={preview.allocatedCapitalUsdt} format={formatAmount} />
				</Metric>
				<Metric label="% капитала">
					<NullableValue value={preview.totalPnLPercent} format={formatSignedPercent} />
				</Metric>
				<Metric label="стоимость">
					<NullableValue value={preview.markValue} format={formatSignedAmount} />
				</Metric>
				<Metric label="занято %">
					<NullableValue value={preview.capitalUsagePercent} format={formatSignedPercent} />
				</Metric>
			</div>

			{/* Период конструкции: открытие и закрытие календарными днями. */}
			<p className="text-muted-foreground text-xs">
				открыта <span>{preview.openedAt === null ? DASH : formatDay(preview.openedAt)}</span> · закрыта{" "}
				<span>{preview.closedAt === null ? DASH : formatDay(preview.closedAt)}</span>
			</p>

			{/* Счётчики записей конструкции: позиции с открытыми, сделки и корректировки. */}
			<ul className="text-muted-foreground flex flex-wrap gap-x-4 gap-y-1 text-xs">
				<li>позиции: {preview.counts.positions} ({preview.counts.openPositions} открыто)</li>
				<li>сделки: {preview.counts.trades}</li>
				<li>корректировки: {preview.counts.adjustments}</li>
			</ul>

			<div className="flex gap-2">
				<Button asChild size="sm">
					<Link to={constructionPath}>Открыть карточку</Link>
				</Button>
				<Button variant="outline" size="sm" onClick={() => window.open(`${SPA_BASE_PATH}${constructionPath}`, "_blank")}>
					<ExternalLink aria-hidden />
					В новом окне
				</Button>
			</div>
		</section>
	);
}

/** Недоступная величина — прочерк; сбой котировок у итога — «неполный». */
function SignedValue({
	value,
	degraded,
	format,
}: {
	value: number | null;
	degraded: boolean;
	format: (value: number) => string;
}) {
	if (value === null) {
		return (
			<span
				className="text-text-muted"
				title={degraded ? "Итог неполный: нереализованная часть не оценена из-за сбоя котировок" : undefined}
			>
				{degraded ? "неполный (сбой котировок)" : DASH}
			</span>
		);
	}

	return <span className="tabular-nums">{format(value)}</span>;
}

/** Величина без специальной семантики сбоя: null — прочерк. */
function NullableValue({ value, format }: { value: number | null; format: (value: number) => string }) {
	return value === null ? <span className="text-text-muted">{DASH}</span> : <span className="tabular-nums">{format(value)}</span>;
}
