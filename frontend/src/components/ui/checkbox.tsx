import { Check } from "lucide-react";
import * as React from "react";
import { cn } from "@/lib/utils";

// Компонент shadcn/ui: код живёт в репозитории (ADR-0010), тема — в
// src/index.css через CSS-переменные Tailwind CSS 4.

/*
	Дизайн-примитив чекбокса перенесён 1:1 с мастер-нод «CB» таблицы
	«Непривязанные сделки» Body #5 «Входящие» (TECU5) макета design.pen
	(lpeaS — checked, mtES4 — unchecked): квадрат 16×16, радиус 4, кайма 1.
	Состояния мастера:
	  unchecked → заливка $surface, кайма $border (галка скрыта: в мастере
	              контур #FFFFFF на белой заливке невидим);
	  checked   → заливка $accent #1FA36B, кайма $accent, галка Lucide
	              check 10×10 цветом $surface.
	Нативный input сохраняет семантику браузера (role=checkbox, клавиатура,
	ассоциация с label), галка — абсолютный слой поверх input, не перехватывающий
	указатель (peer-checked показывает её только в отмеченном состоянии).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2

function Checkbox({ className, ...props }: Omit<React.ComponentProps<"input">, "type">) {
	return (
		<span data-slot="checkbox" className="relative inline-flex size-4 shrink-0 items-center justify-center align-middle">
			<input
				type="checkbox"
				className={cn(
					"peer size-full cursor-pointer appearance-none rounded-[4px] border border-border bg-card",
					"transition-[background-color,border-color,box-shadow] outline-none",
					"checked:border-primary checked:bg-primary",
					"focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50",
					"disabled:cursor-not-allowed disabled:opacity-50",
					className,
				)}
				{...props}
			/>
			<Check aria-hidden="true" className="pointer-events-none absolute size-2.5 text-surface opacity-0 peer-checked:opacity-100" />
		</span>
	);
}

export { Checkbox };
