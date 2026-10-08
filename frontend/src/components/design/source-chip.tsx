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
	selected = true,
	children,
	...props
}: React.ComponentProps<"span"> & {
	/** Иконка источника из Lucide; мастер-нода использует check. */
	icon?: LucideIcon;
	/** Переопределение цвета иконки (инстанс «GLM-5.3» — textSecondary). */
	iconClassName?: string;
	/** Признак выбранного источника; по мастеру все чипы «выбраны». */
	selected?: boolean;
}) {
	return (
		<span
			data-slot="source-chip"
			data-selected={selected}
			className={cn(
				"inline-flex items-center gap-1.5 rounded-full border bg-card px-2.5 py-[5px] text-[12px] whitespace-nowrap",
				selected ? "text-text-secondary" : "text-text-muted",
				className,
			)}
			{...props}
		>
			{/*
				Состояние выбора для формы источников вкладки «Чаты» (§3.4:2
				аудита): в мастере все инстансы чипа несут иконку accent, поэтому
				выбранный чип — вид мастера; невыбранный приглушается до
				textMuted и иконкой, и подписью (цвет снятия выбора в design.pen
				отсутствует — по здравому смыслу).
			*/}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<Icon
				aria-hidden="true"
				className={cn("size-3 shrink-0", selected ? "text-primary" : "text-text-muted", iconClassName)}
			/>
			{children}
		</span>
	);
}

export { SourceChip };
