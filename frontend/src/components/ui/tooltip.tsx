"use client";

import * as TooltipPrimitive from "@radix-ui/react-tooltip";
import * as React from "react";

import { cn } from "@/lib/utils";

// Компонент shadcn/ui: код живёт в репозитории (ADR-0010), тема —
// в src/index.css через CSS-переменные Tailwind CSS 4.

// Примитив «Подсказка метрики»: триггер — само значение или заголовок
// колонки с пунктирным подчёркиванием, без иконок-триггеров; попап строится
// на токенах паттерна «Подсказка метрики» макета design.pen. Макет изображает
// оба состояния — свёрнутое значение с пунктиром и открытый попап
// с заголовком метрики и разделами по показателям.
// Traceability: openspec:ui/design-system#requirement-design-metric-tooltip-pattern

// Провайдер задаёт общую задержку открытия подсказок приложения: небольшая
// пауза отсекает случайные проводки курсора над значениями.
function TooltipProvider({
	delayDuration = 150,
	...props
}: React.ComponentProps<typeof TooltipPrimitive.Provider>) {
	return (
		<TooltipPrimitive.Provider
			data-slot="tooltip-provider"
			delayDuration={delayDuration}
			{...props}
		/>
	);
}

// Корень подсказки: открытие по наведению курсора и по фокусу с клавиатуры
// (триггер Radix фокусируем), закрытие по уходу указателя или фокуса,
// тапу/клику вне триггера и по Esc. Контент не перехватывает указатель
// (disableHoverableContent): на тач-устройствах тап вне триггера закрывает
// подсказку.
// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
function Tooltip({ ...props }: React.ComponentProps<typeof TooltipPrimitive.Root>) {
	return (
		<TooltipPrimitive.Root data-slot="tooltip" disableHoverableContent {...props} />
	);
}

// Триггер — обёртка над самим значением метрики: строчный фокусируемый
// элемент с пунктирным подчёркиванием слабоконтрастным серым токеном
// (textMuted), без фонов, рамок и иконок-триггеров; шрифт наследуется,
// чтобы значение выглядело как окружающий текст.
// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
// Traceability: openspec:ui/design-system#scenario-design-tooltip-both-states
function TooltipTrigger({
	className,
	...props
}: React.ComponentProps<typeof TooltipPrimitive.Trigger>) {
	return (
		<TooltipPrimitive.Trigger
			data-slot="tooltip-trigger"
			className={cn(
				"inline-flex items-baseline cursor-help bg-transparent p-0 text-left text-inherit leading-[inherit] outline-none underline decoration-dashed decoration-from-font decoration-text-muted underline-offset-4",
				className,
			)}
			{...props}
		/>
	);
}

// Попап строится на токенах паттерна «Подсказка метрики» макета:
// фон $surface (--popover), граница $border, радиус 10, паддинг 12 (p-3),
// тень, слой z-50; текст мелкий (11px), ширина ограничена
// (max-w-72 ≈ 288px при ширине попапа макета 300).
// Traceability: openspec:ui/design-system#scenario-design-tooltip-token-alignment
function TooltipContent({
	className,
	sideOffset = 6,
	children,
	...props
}: React.ComponentProps<typeof TooltipPrimitive.Content>) {
	return (
		<TooltipPrimitive.Portal data-slot="tooltip-portal">
			<TooltipPrimitive.Content
				data-slot="tooltip-content"
				sideOffset={sideOffset}
				className={cn(
					"bg-popover text-popover-foreground border z-50 max-w-72 rounded-[10px] p-3 text-[11px] leading-snug shadow-lg outline-none data-[state=delayed-open]:animate-in data-[state=delayed-open]:fade-in-0 data-[state=delayed-open]:zoom-in-95 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95",
					className,
				)}
				{...props}
			>
				{children}
			</TooltipPrimitive.Content>
		</TooltipPrimitive.Portal>
	);
}

export {
	Tooltip,
	TooltipContent,
	TooltipProvider,
	TooltipTrigger,
};
