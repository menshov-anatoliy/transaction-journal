import { PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { LayoutPanel } from "./layout-geometry";

// В обоих состояниях кнопка управления остаётся в нижнем футере панели.
// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-panel-footer
// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
export function PanelFooter({
	side,
	collapsed,
	onToggle,
	label,
}: {
	side: LayoutPanel;
	collapsed: boolean;
	onToggle: () => void;
	label: string;
}) {
	const Icon = side === "left"
		? (collapsed ? PanelLeftOpen : PanelLeftClose)
		: (collapsed ? PanelRightOpen : PanelRightClose);

	return (
		<div
			data-slot="panel-footer"
			data-side={side}
			className={cn(
				"flex h-11 shrink-0 items-center border-t border-divider",
				collapsed ? "px-1" : "px-3",
				side === "left" ? "justify-end" : "justify-start",
			)}
		>
			<Button variant="ghost" size="icon" className="size-[30px]" aria-label={label} title={label} onClick={onToggle}>
				<Icon className="size-[15px]" />
			</Button>
		</div>
	);
}
