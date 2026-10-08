import * as React from "react";
import { cn } from "@/lib/utils";

// Компонент shadcn/ui: код живёт в репозитории (ADR-0010), тема — в
// src/index.css через CSS-переменные Tailwind CSS 4.

/*
	Тема поля ввода перенесена с дизайн-нод «Фильтры» Body #5 «Входящие»
	(TECU5) макета design.pen (DateFrom VYItk / DateTo euvt0 / Instr AYWJR):
	белая поверхность $surface, кайма $border #E5E5DF, радиус 8 (шаг
	--radius-sm лестницы --radius), паддинги [7,10], Inter 12/normal
	$textPrimary. Инстансы мастера дополняют поле иконками (календарь/шеврон
	13×13 $textMuted) — иконки композируются на странице поверх примитива
	абсолютным позиционированием, поэтому базовое поле остаётся чистым.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2

function Input({ className, type, ...props }: React.ComponentProps<"input">) {
	return (
		<input
			type={type}
			data-slot="input"
			className={cn(
				"min-w-0 rounded-sm border bg-card px-2.5 py-[7px] text-xs text-foreground outline-none",
				"placeholder:text-text-muted transition-[border-color,box-shadow]",
				"focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50",
				"aria-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50",
				className,
			)}
			{...props}
		/>
	);
}

export { Input };
