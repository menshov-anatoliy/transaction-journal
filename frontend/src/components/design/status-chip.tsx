import { cva, type VariantProps } from "class-variance-authority";
import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Примитив «Чип/Статус» (нода aa6cK, фрейм «Примитивы» s0YfZ) перенесён
	1:1 с макета design.pen: pill радиус 999, паддинги [4,10], Inter 11.5/500.
	Интерлиньяж мастера — шрифтовой normal Inter, поэтому пилюля фиксирует
	leading-normal и не растягивается line-height контекста (напр. титула).
	Тональные варианты собраны из фактических инстансов дизайн-нод:
	  pos     → «открыта», «Мягкое», «Применено»  (accentSoft/accentStrong, мастер)
	  info    → «Однозначное»                     (infoSoft/info, нода U6mVZ)
	  neutral → «закрыта», «Субъект: стратегия»   (surface2/textSecondary, EIqx3)
	  muted   → «архив»                           (surface2/textMuted, dAcLW)
	  neg     → «Отклонено»                       (negSoft/neg, Body #6: rdODt/AutfO)
	  risk    → «Мягкое»                           (riskSoft/risk, Body #7: nrPzY/E3YiR)
	Тон neg добавлен по инстансам карточек подсказок Body #6 макета:
	чип статуса «Отклонено» перекрашивается в пару $negSoft/$neg. Тон
	risk добавлен по инстансам карточек правил Body #7 (нода nrPzY
	«Мягкое», пара $riskSoft/$risk) — дизайн-узел появился, расширение
	разрешено исходным комментарием примитива.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

const statusChipVariants = cva(
	"inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11.5px] leading-[normal] font-medium whitespace-nowrap",
	{
		variants: {
			tone: {
				pos: "bg-accent-soft text-accent-strong",
				info: "bg-info-soft text-info",
				neutral: "bg-surface-2 text-text-secondary",
				muted: "bg-surface-2 text-text-muted",
				neg: "bg-neg-soft text-neg",
				risk: "bg-risk-soft text-risk",
			},
		},
		defaultVariants: {
			tone: "pos",
		},
	},
);

function StatusChip({
	className,
	tone,
	...props
}: React.ComponentProps<"span"> & VariantProps<typeof statusChipVariants>) {
	return (
		<span
			data-slot="status-chip"
			className={cn(statusChipVariants({ tone, className }))}
			{...props}
		/>
	);
}

export { StatusChip, statusChipVariants };
