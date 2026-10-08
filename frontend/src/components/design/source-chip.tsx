import { Check, type LucideIcon } from "lucide-react";
import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Примитив «Чип/Источник» (нода fYadZ, фрейм «Примитивы» s0YfZ) перенесён
	1:1 с макета design.pen: pill радиус 999, заливка surface, бордер border,
	паддинги [5,10], иконка Lucide 12×12 цветом accent (мастер — check),
	подпись Inter 12/normal textSecondary. Инстансы дизайн-нод меняют иконку
	(check → globe/cpu) и её цвет, поэтому иконка настраивается пропсами
	icon/iconClassName. Используется в следе источников и форме чатов
	(задачи 5.2 и 7.5 change reconcile-frontend-with-design).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

function SourceChip({
	className,
	icon: Icon = Check,
	iconClassName,
	children,
	...props
}: React.ComponentProps<"span"> & {
	/** Иконка источника из Lucide; мастер-нода использует check. */
	icon?: LucideIcon;
	/** Переопределение цвета иконки (инстанс «GLM-5.3» — textSecondary). */
	iconClassName?: string;
}) {
	return (
		<span
			data-slot="source-chip"
			className={cn(
				"inline-flex items-center gap-1.5 rounded-full border bg-card px-2.5 py-[5px] text-[12px] text-text-secondary whitespace-nowrap",
				className,
			)}
			{...props}
		>
			<Icon
				aria-hidden="true"
				className={cn("size-3 shrink-0 text-primary", iconClassName)}
			/>
			{children}
		</span>
	);
}

export { SourceChip };
