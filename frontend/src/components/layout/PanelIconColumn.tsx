import { PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/**
 * Иконочная колонка панели каркаса (design.md D2): вертикальный ряд
 * действий вверху у внутреннего края панели; управление сворачиванием —
 * последняя иконка колонки, строго под остальными иконками.
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 * Traceability: change:rework-spa-shell-and-screens/design#D2
 */

export interface PanelIconColumnProps {
	/** Чья колонка: у левой панели внутренний край — правый, у правой — левый. */
	side: "left" | "right";
	/** Иконочные действия панели над кнопкой сворачивания. */
	actions?: React.ReactNode;
	/** Текущая свёрнутость панели (переключает иконку и подпись кнопки). */
	collapsed: boolean;
	/** Переключение свёрнутости (последняя иконка колонки). */
	onToggleCollapse: () => void;
	/** Подписи кнопки сворачивания/разворачивания. */
	collapseLabel: string;
	expandLabel: string;
}

export function PanelIconColumn({
	side,
	actions,
	collapsed,
	onToggleCollapse,
	collapseLabel,
	expandLabel,
}: PanelIconColumnProps) {
	const CollapseIcon = side === "left" ? PanelLeftClose : PanelRightClose;
	const ExpandIcon = side === "left" ? PanelLeftOpen : PanelRightOpen;
	const Icon = collapsed ? ExpandIcon : CollapseIcon;

	return (
		<div
			data-slot="panel-icon-column"
			data-side={side}
			className={cn(
				"flex w-9 shrink-0 flex-col items-center gap-1 py-2",
				side === "left" ? "border-l border-divider" : "border-r border-divider",
			)}
		>
			{actions}
			<Button
				variant="ghost"
				size="icon"
				aria-label={collapsed ? expandLabel : collapseLabel}
				onClick={onToggleCollapse}
			>
				<Icon />
			</Button>
		</div>
	);
}
