import { Loader } from "lucide-react";
import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Примитив «Tool-статус» (мастер-нода ix8ma, reusable-компонент design.pen)
	перенесён 1:1 с макета: pill радиус 999 на surface2, паддинги [4,10],
	зазор 7, иконка Lucide loader 12×12 textMuted, подпись Inter 11.5/normal
	italic textSecondary. Единственное состояние макета — «выполняется»
	(инстансы dK1P3 «корпус правил…» и tMmi0 «рынок Bybit…» меняют только
	текст); состояний «готово»/«ошибка» в дизайн-нодах нет, поэтому примитив
	вариантов не имеет — расширение только с появлением дизайн-узла. Вращение
	иконки — единственная надстройка над статичным макетом: ход tool-вызова
	должен читаться как активность, статичный кадр pen этого не передаёт.
	Интеграция в ленту ответов — задача 5.2 change.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

function ToolStatus({
	className,
	children,
	...props
}: React.ComponentProps<"span"> & {
	/** Текст статуса вызова: «источник — что делаю…» (нода PfMRJ, курсив). */
	children: React.ReactNode;
}) {
	return (
		<span
			data-slot="tool-status"
			className={cn(
				"inline-flex items-center gap-[7px] rounded-full bg-surface-2 px-2.5 py-1 whitespace-nowrap",
				className,
			)}
			{...props}
		>
			<Loader
				aria-hidden="true"
				className="size-3 shrink-0 animate-spin text-text-muted"
			/>
			<span
				data-slot="tool-status-label"
				className="text-[11.5px] italic text-text-secondary"
			>
				{children}
			</span>
		</span>
	);
}

export { ToolStatus };
