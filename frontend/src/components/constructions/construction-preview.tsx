import { Link } from "react-router";
import { ExternalLink } from "lucide-react";
import type { ConstructionPreview } from "@/lib/api/constructions";
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

			<dl className="grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm">
				<MetricRow label="итог">
					<SignedValue value={preview.totalPnL} degraded={preview.hasMarkFailure} format={formatSignedAmount} bold />
				</MetricRow>
				<MetricRow label="реализов.">
					<span className="tabular-nums">{formatSignedAmount(preview.realizedPnL)}</span>
				</MetricRow>
				<MetricRow label="нереализов.">
					<NullableValue value={preview.unrealizedPnL} format={formatSignedAmount} />
				</MetricRow>
				<MetricRow label="коррект.">
					<span className="tabular-nums">{formatSignedAmount(preview.adjustmentsPnL)}</span>
				</MetricRow>
				<MetricRow label="капитал">
					<NullableValue value={preview.allocatedCapitalUsdt} format={formatAmount} />
				</MetricRow>
				<MetricRow label="% капитала">
					<NullableValue value={preview.totalPnLPercent} format={formatSignedPercent} />
				</MetricRow>
				<MetricRow label="стоимость">
					<NullableValue value={preview.markValue} format={formatSignedAmount} />
				</MetricRow>
				<MetricRow label="занято %">
					<NullableValue value={preview.capitalUsagePercent} format={formatSignedPercent} />
				</MetricRow>
			</dl>

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

/** Строка метрики сводки: подпись и значение. */
function MetricRow({ label, children }: { label: string; children: React.ReactNode }) {
	return (
		<div className="flex items-baseline justify-between gap-2">
			<dt className="text-muted-foreground">{label}</dt>
			<dd>{children}</dd>
		</div>
	);
}

/** Недоступная величина — прочерк; сбой котировок у итога — «неполный». */
function SignedValue({
	value,
	degraded,
	format,
	bold = false,
}: {
	value: number | null;
	degraded: boolean;
	format: (value: number) => string;
	bold?: boolean;
}) {
	if (value === null) {
		return (
			<span
				className={bold ? "font-semibold text-muted-foreground" : "text-muted-foreground"}
				title={degraded ? "Итог неполный: нереализованная часть не оценена из-за сбоя котировок" : undefined}
			>
				{degraded ? "неполный (сбой котировок)" : DASH}
			</span>
		);
	}

	return <span className={bold ? "font-semibold tabular-nums" : "tabular-nums"}>{format(value)}</span>;
}

/** Величина без специальной семантики сбоя: null — прочерк. */
function NullableValue({ value, format }: { value: number | null; format: (value: number) => string }) {
	return value === null ? <span className="text-muted-foreground">{DASH}</span> : <span className="tabular-nums">{format(value)}</span>;
}
