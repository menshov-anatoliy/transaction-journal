import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Примитив «Метрика» (нода jtDmV, фрейм «Примитивы» s0YfZ) перенесён 1:1
	с макета design.pen: вертикальный блок с зазором 3, подпись Inter
	11/normal textMuted с трекингом 0.3, значение Inter 15/600 textPrimary.
	Инстансы во фрейме «Карточки» (sUDDX: M1–M9) переопределяют размер
	(14/16) и цвет (accentStrong) значения — поэтому значение принимает
	любой контент и настраивается valueClassName. Применяется в карточке
	конструкции (задача 7.2 change reconcile-frontend-with-design).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

function Metric({
	className,
	label,
	valueClassName,
	children,
	...props
}: React.ComponentProps<"div"> & {
	/** Подпись показателя (нода Caption: 11/normal textMuted, трекинг 0.3). */
	label: string;
	/** Классы значения: инстансы меняют размер и цвет (accentStrong, 14/16). */
	valueClassName?: string;
}) {
	return (
		<div
			data-slot="metric"
			className={cn("flex flex-col gap-[3px]", className)}
			{...props}
		>
			<span className="text-[11px] tracking-[0.3px] text-text-muted">
				{label}
			</span>
			<span
				data-slot="metric-value"
				className={cn(
					"text-[15px] font-semibold text-text-primary",
					valueClassName,
				)}
			>
				{children}
			</span>
		</div>
	);
}

export { Metric };
