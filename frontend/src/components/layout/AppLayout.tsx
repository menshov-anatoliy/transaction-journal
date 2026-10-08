import { useState } from "react";
import { NavLink, Outlet } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { appSections } from "@/config/sections";
import { cn } from "@/lib/utils";
import { fetchInboxCount } from "@/lib/api/inbox";
import { useIsMobile } from "@/lib/use-mobile";

/**
 * Каркас приложения «тонкий топбар + левая панель» по концепции §2:
 * бренд «Журнал Bybit» в топбаре без итога журнала, навигация по пяти
 * разделам из единого конфига, панель сворачивается до иконок.
 * Мобильный drawer панели и бейдж «Входящих» — задачи 6.1 и 5.5.
 *
 * Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
 * Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
 */
export function AppLayout() {
	const [collapsed, setCollapsed] = useState(false);
	const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
	const isMobile = useIsMobile();
	const inboxCount = useQuery({
		queryKey: ["inbox-count"],
		queryFn: fetchInboxCount,
		refetchInterval: 30_000,
	});

	const navigation = (
		<nav
			aria-label="Разделы журнала"
			className={cn(
				"flex flex-col gap-1 border-r bg-card p-3",
				isMobile ? "h-full w-72 border-r" : collapsed ? "w-[4.25rem]" : "w-64",
			)}
		>
			{appSections.map((section) => (
				<NavLink
					key={section.path}
					to={section.path}
					end={section.path === "/"}
					aria-label={section.label}
					onClick={() => setMobileMenuOpen(false)}
					className={({ isActive }) =>
						cn(
							"flex h-10 items-center gap-3 rounded-lg px-3 text-sm text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground",
							isActive && "bg-accent font-medium text-accent-foreground",
							collapsed && isMobile == false && "justify-center px-0",
						)
					}
				>
					<section.icon className="size-5 shrink-0" aria-hidden="true" />
					{(collapsed && isMobile == false) == false && (
						<div className="flex min-w-0 items-center gap-2">
							<span className="truncate">{section.label}</span>
							{section.path === "/inbox" && inboxCount.isError == false && (inboxCount.data ?? 0) > 0 && (
								<span className="rounded-full bg-primary/15 px-2 py-0.5 text-xs text-primary" aria-label={`входящих сделок: ${inboxCount.data}`}>
									{inboxCount.data}
								</span>
							)}
						</div>
					)}
				</NavLink>
			))}
		</nav>
	);

	return (
		<div className="flex h-svh flex-col">
			<header className="flex h-14 shrink-0 items-center gap-2 border-b bg-card px-4">
				<Button
					variant="ghost"
					size="icon"
					aria-label={isMobile ? (mobileMenuOpen ? "Закрыть меню" : "Открыть меню") : (collapsed ? "Развернуть панель навигации" : "Свернуть панель навигации")}
					onClick={() => {
						if (isMobile) {
							setMobileMenuOpen((value) => !value);
							return;
						}

						setCollapsed((value) => !value);
					}}
				>
					{isMobile || collapsed ? <PanelLeftOpen /> : <PanelLeftClose />}
				</Button>
				<span className="text-base font-semibold tracking-tight">Журнал Bybit</span>
			</header>
			<div className="flex min-h-0 flex-1">
				{isMobile == false && <div className="flex">{navigation}</div>}
				{isMobile && mobileMenuOpen && (
					<div className="fixed inset-0 z-40 lg:hidden">
						<button
							type="button"
							aria-label="Закрыть меню"
							className="absolute inset-0 bg-black/40"
							onClick={() => setMobileMenuOpen(false)}
						/>
						<div className="relative z-10 h-full">{navigation}</div>
					</div>
				)}
				<main className="min-w-0 flex-1 overflow-y-auto">
					<Outlet />
				</main>
			</div>
		</div>
	);
}
