import { useState } from "react";
import { NavLink, Outlet } from "react-router";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { appSections } from "@/config/sections";
import { cn } from "@/lib/utils";

/**
 * Каркас приложения «тонкий топбар + левая панель» по концепции §2:
 * бренд «Журнал Bybit» в топбаре без итога журнала, навигация по пяти
 * разделам из единого конфига, панель сворачивается до иконок.
 * Мобильный drawer панели и бейдж «Входящих» — задачи 6.1 и 5.5.
 *
 * Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
 */
export function AppLayout() {
	const [collapsed, setCollapsed] = useState(false);

	return (
		<div className="flex h-svh flex-col">
			<header className="flex h-14 shrink-0 items-center gap-2 border-b bg-card px-4">
				<Button
					variant="ghost"
					size="icon"
					aria-label={
						collapsed
							? "Развернуть панель навигации"
							: "Свернуть панель навигации"
					}
					onClick={() => setCollapsed((value) => !value)}
				>
					{collapsed ? <PanelLeftOpen /> : <PanelLeftClose />}
				</Button>
				<span className="text-base font-semibold tracking-tight">Журнал Bybit</span>
			</header>
			<div className="flex min-h-0 flex-1">
				<nav
					aria-label="Разделы журнала"
					className={cn(
						"flex shrink-0 flex-col gap-1 border-r bg-card p-3 transition-[width]",
						collapsed ? "w-[4.25rem]" : "w-64",
					)}
				>
					{appSections.map((section) => (
						<NavLink
							key={section.path}
							to={section.path}
							end={section.path === "/"}
							aria-label={section.label}
							className={({ isActive }) =>
								cn(
									"flex h-10 items-center gap-3 rounded-lg px-3 text-sm text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground",
									isActive && "bg-accent font-medium text-accent-foreground",
									collapsed && "justify-center px-0",
								)
							}
						>
							<section.icon className="size-5 shrink-0" aria-hidden="true" />
							{!collapsed && <span className="truncate">{section.label}</span>}
						</NavLink>
					))}
				</nav>
				<main className="min-w-0 flex-1 overflow-y-auto">
					<Outlet />
				</main>
			</div>
		</div>
	);
}
