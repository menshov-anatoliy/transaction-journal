import { useState } from "react";
import { NavLink, Outlet, matchPath, useLocation } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { NavItem } from "@/components/design";
import { appSections } from "@/config/sections";
import { cn } from "@/lib/utils";
import { fetchInboxCount } from "@/lib/api/inbox";
import { useIsMobile } from "@/lib/use-mobile";

/**
 * Каркас приложения «тонкий топбар + левая панель» по концепции §2:
 * бренд «Журнал Bybit» в топбаре без итога журнала, навигация по пяти
 * разделам из единого конфига, панель сворачивается до иконок.
 * Мобильный drawer панели и бейдж «Входящих» — задачи 6.1 и 5.5.
 * Слой дизайн-системы (задача 3.1): топбар и нав-пункты перенесены по
 * мастер-нодам «Каркаса» g0z20, пункты панели рендерятся примитивом NavItem.
 *
 * Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
 * Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
 */
export function AppLayout() {
	const [collapsed, setCollapsed] = useState(false);
	const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
	const isMobile = useIsMobile();
	const location = useLocation();
	const inboxCount = useQuery({
		queryKey: ["inbox-count"],
		queryFn: fetchInboxCount,
		refetchInterval: 30_000,
	});

	const collapsedDesktop = collapsed && isMobile == false;

	const navigation = (
		<nav
			aria-label="Разделы журнала"
			className={cn(
				"flex flex-col gap-1 border-r border-divider bg-background px-3 py-4",
				isMobile ? "h-full w-72 border-r" : collapsed ? "w-[4.25rem]" : "w-64",
			)}
		>
			{appSections.map((section) => {
				/*
					Активный пункт вычисляется по текущему location теми же
					правилами end-матчинга, что и у NavLink: активен точный «/»
					и потомки остальных разделов; состояние стилизуется
					примитивом NavItem по мастер-ноде Vx9C2.
				*/
				// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
				// Traceability: change:reconcile-frontend-with-design/design#D2
				const isActive =
					matchPath({ path: section.path, end: section.path === "/" }, location.pathname) != null;
				/*
					Бейдж «Входящие» — по мастер-ноде q35Tj9 «Каркаса» (инстанс
					bKXol): pill 999, заливка $accent, белый текст 11/600,
					паддинги [2,8]. Отклонение от текста задачи 3.1 «на паре
					accentSoft/accentStrong»: прямое чтение g0z20 показывает, что
					эта пара в «Каркасе» принадлежит логотипу топбара (srW2i),
					а бейдж мастера залит акцентом; мастер — источник истины.
				*/
				const badgeVisible =
					section.path === "/inbox" &&
					inboxCount.isError == false &&
					(inboxCount.data ?? 0) > 0 &&
					collapsedDesktop == false;
				return (
					<NavItem
						key={section.path}
						asChild
						icon={section.icon}
						active={isActive}
						badge={badgeVisible ? inboxCount.data : undefined}
						className={cn(collapsedDesktop && "justify-center px-0")}
					>
						<NavLink
							to={section.path}
							end={section.path === "/"}
							aria-label={section.label}
							onClick={() => setMobileMenuOpen(false)}
						>
							{collapsedDesktop == false && section.label}
						</NavLink>
					</NavItem>
				);
			})}
		</nav>
	);

	/*
		Топбар по мастер-ноде jGvOM экрана «Каркас» g0z20: высота 54, паддинги
		[0,24], заливка $surface, нижний разделитель $divider, зазор 10.
		Отклонение от текста задачи 3.1 «52px»: высота 52 принадлежит другому
		фрейму IdwN5, в «Каркасе» топбар — jGvOM высотой 54; сверка ведётся по
		g0z20, поэтому берётся значение мастера.
	*/
	// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
	// Traceability: change:reconcile-frontend-with-design/design#D2
	return (
		<div className="flex h-svh flex-col">
			<header className="flex h-[54px] shrink-0 items-center gap-2.5 border-b border-divider bg-surface px-6">
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
				<span className="text-sm font-semibold">Журнал Bybit</span>
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

