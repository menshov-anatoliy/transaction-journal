"use client";

import * as PopoverPrimitive from "@radix-ui/react-popover";
import * as React from "react";

import { cn } from "@/lib/utils";

// Компонент shadcn/ui: код живёт в репозитории (ADR-0010), тема —
// в src/index.css через CSS-переменные Tailwind CSS 4.

// Открытие кликом по триггеру, закрытие — Esc, клик вне, повторный клик;
// фокус возвращается на триггер.
// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-opens
// Traceability: change:add-finresult-reading-tooltip/specs/ui/screens/spec#scenario-reading-tooltip-closes
function Popover({ ...props }: React.ComponentProps<typeof PopoverPrimitive.Root>) {
	return <PopoverPrimitive.Root data-slot="popover" {...props} />;
}

function PopoverTrigger({
	...props
}: React.ComponentProps<typeof PopoverPrimitive.Trigger>) {
	return (
		<PopoverPrimitive.Trigger data-slot="popover-trigger" {...props} />
	);
}

// Якорь для привязки позиционирования без управления открытием.
function PopoverAnchor({
	...props
}: React.ComponentProps<typeof PopoverPrimitive.Anchor>) {
	return (
		<PopoverPrimitive.Anchor data-slot="popover-anchor" {...props} />
	);
}

function PopoverClose({
	...props
}: React.ComponentProps<typeof PopoverPrimitive.Close>) {
	return (
		<PopoverPrimitive.Close data-slot="popover-close" {...props} />
	);
}

// Попап строится на токенах паттерна «Попап правила/Краткий» макета:
// фон $surface, граница $border, радиус 10.
// Traceability: change:add-finresult-reading-tooltip/specs/ui/design-system/spec#scenario-design-card-reading-tooltip-tokens
function PopoverContent({
	className,
	align = "center",
	sideOffset = 4,
	...props
}: React.ComponentProps<typeof PopoverPrimitive.Content>) {
	return (
		<PopoverPrimitive.Portal data-slot="popover-portal">
			<PopoverPrimitive.Content
				data-slot="popover-content"
				align={align}
				sideOffset={sideOffset}
				className={cn(
					"bg-popover text-popover-foreground border data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95 z-50 rounded-[10px] p-3 shadow-lg outline-none",
					className,
				)}
				{...props}
			/>
		</PopoverPrimitive.Portal>
	);
}

export {
	Popover,
	PopoverAnchor,
	PopoverClose,
	PopoverContent,
	PopoverTrigger,
};
