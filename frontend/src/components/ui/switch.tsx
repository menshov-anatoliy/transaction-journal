import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Дизайн-примитив тумблера по решению раунд-сверки 9.0 (мастер uxrZO
	«Экран/Синхронизация»): дорожка 36×20 радиусом 999, включённое состояние —
	заливка $accent (токен primary), выключенное — нейтральный токен $border,
	ручка 16×16 #FFFFFF ($surface). Нативный input сохраняет семантику
	браузера: роль переопределена на switch, состояние отдаётся через
	aria-checked, клавиатура (Space) и ассоциация с label работают без
	дополнительного кода; ручка — абсолютный слой поверх input, не
	перехватывающий указатель (peer-checked сдвигает её только во включённом
	состоянии).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:rework-contextual-topbar/tasks#9.0

interface SwitchProps extends Omit<React.ComponentProps<"input">, "type" | "onChange"> {
	checked: boolean;
	onCheckedChange?: (checked: boolean) => void;
}

function Switch({ checked, onCheckedChange, className, ...props }: SwitchProps) {
	return (
		<span data-slot="switch" className="relative inline-flex h-5 w-9 shrink-0 items-center align-middle">
			<input
				type="checkbox"
				role="switch"
				aria-checked={checked}
				checked={checked}
				onChange={(event) => onCheckedChange?.(event.currentTarget.checked)}
				className={cn(
					"peer size-full cursor-pointer appearance-none rounded-full border border-border bg-border",
					"transition-[background-color,border-color,box-shadow] outline-none",
					"checked:border-primary checked:bg-primary",
					"focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50",
					"disabled:cursor-not-allowed disabled:opacity-50",
					className,
				)}
				{...props}
			/>
			{/* Ручка #FFFFFF ($surface): центрируется по вертикали без transform,
				чтобы peer-checked:translate-x-4 двигал её только по горизонтали. */}
			<span
				aria-hidden="true"
				className="pointer-events-none absolute inset-y-0 left-0.5 m-auto size-4 rounded-full bg-surface transition-transform peer-checked:translate-x-4"
			/>
		</span>
	);
}

export { Switch };
