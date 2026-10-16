import type { ReactNode } from "react";
import type { LayoutPanel } from "./layout-geometry";

/*
	Рейка занимает всю высоту свёрнутой панели: футеров у панелей нет,
	управление свёрнутостью живёт в сегментах топбара. При переполнении
	прокручивается только рейка.
*/
// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
export function PanelMiniatureRail({
	side,
	children,
}: {
	side: LayoutPanel;
	children?: ReactNode;
}) {
	return (
		<div data-slot="panel-miniature-rail" data-side={side} className="min-h-0 w-11 flex-1 overflow-x-hidden overflow-y-auto py-1.5">
			{children}
		</div>
	);
}
