"use client";

import * as React from "react";

import type { MetricTooltip } from "@/lib/metric-tooltips";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

// Примитив «Подсказка метрики» изображается в макете двумя состояниями:
// свёрнутым (значение или заголовок с пунктирным подчёркиванием) и открытым
// (попап с заголовком метрики и разделами по показателям).
// Traceability: openspec:ui/design-system#requirement-design-metric-tooltip-pattern

// Содержимое попапа по паттерну макета: заголовок метрики (12.5/600),
// назначение — всегда, метод расчёта — когда зафиксирован в справочнике,
// по разделу на каждый показатель многопоказательной метрики
// (показатель 11/600 вторичным тоном + описание 11 обычное), построчные
// записи перечня (например, расшифровка метки маркера) и примечание
// в конце подсказки.
export function MetricTooltipContent({
	tooltip,
	className,
}: {
	readonly tooltip: MetricTooltip;
	readonly className?: string;
}) {
	return (
		<span data-slot="metric-tooltip-content" className={cn("flex flex-col gap-2", className)}>
			<span className="text-[12.5px] font-semibold">{tooltip.title}</span>
			{tooltip.purpose ? <span className="text-text-secondary">{tooltip.purpose}</span> : null}
			{tooltip.method ? (
				<span className="text-text-secondary">{tooltip.method}</span>
			) : null}
			{tooltip.sections?.map((section) => (
				<span className="flex flex-col gap-0.5" key={section.label}>
					<span className="font-semibold text-text-secondary">{section.label}</span>
					<span className="text-text-secondary">{section.text}</span>
				</span>
			))}
			{tooltip.rows?.map((row, index) => {
				// Ряд может быть строкой либо вложенной детализацией: indent-ряд
				// (строки сделок, период связки под строкой операции) рендерится
				// с отступом, меньшим кеглем и приглушённым тоном, остальные —
				// прежние.
				// Traceability: openspec:ui/screens#scenario-marker-badge-breakdown-tooltip
				const item = typeof row === "string" ? { text: row } : row;

				return item.indent ? (
					<span className="pl-3 text-[10.5px] text-text-muted" key={`${index}:${item.text}`}>
						{item.text}
					</span>
				) : (
					<span className="text-text-secondary" key={`${index}:${item.text}`}>
						{item.text}
					</span>
				);
			})}
			{tooltip.note ? <span className="text-text-muted">{tooltip.note}</span> : null}
		</span>
	);
}

// Готовая связка подсказки метрики: триггер — обёртка над самим значением
// или заголовком колонки (пунктирное подчёркивание из примитива, без иконок),
// попап — из справочника текстов. Наведение и фокус открывают, blur и тап
// вне закрывают — механика примитива Tooltip. В режиме triggerAsChild
// обвязка не создаёт свою кнопку: свойства триггера переносятся на
// переданный элемент (нужен tabIndex для фокуса) — это позволяет вешать
// подсказку на величины внутри другой кнопки без вложения интерактивных
// элементов.
// Traceability: openspec:ui/screens#requirement-metric-tooltips-on-numeric-metrics
export function MetricHint({
	tooltip,
	children,
	className,
	contentClassName,
	triggerAsChild,
}: {
	readonly tooltip: MetricTooltip;
	readonly children: React.ReactNode;
	readonly className?: string;
	readonly contentClassName?: string;
	readonly triggerAsChild?: boolean;
}) {
	return (
		<Tooltip>
			<TooltipTrigger asChild={triggerAsChild} className={className}>{children}</TooltipTrigger>
			<TooltipContent className={contentClassName}>
				<MetricTooltipContent tooltip={tooltip} />
			</TooltipContent>
		</Tooltip>
	);
}
