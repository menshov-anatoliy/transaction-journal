import { useCallback, useMemo, useState } from "react";
import { NavLink, Outlet, matchPath, useLocation } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Menu, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { NavItem } from "@/components/design";
import { appSections } from "@/config/sections";
import { cn } from "@/lib/utils";
import { fetchInboxCount } from "@/lib/api/inbox";
import { useConstructionsOverviewQuery } from "@/lib/api/constructions-overview-query";
import { formatSignedAmount } from "@/lib/format/quantity";
import { useIsMobile } from "@/lib/use-mobile";
import { PanelFooter } from "./PanelFooter";
import { PanelMiniatureRail } from "./PanelMiniatureRail";
import { PanelResizer } from "./PanelResizer";
import { COLLAPSED_LEFT_WIDTH_REM, usePanelGeometry } from "./layout-geometry";
import type { ContextPanelApi, ContextPanelRegistration } from "./context-panel";

/**
 * Каркас приложения «тонкий топбар + три зоны» (design.md D1): левая
 * панель навигации, центр с содержимым экрана (Outlet) и правая
 * контекстная панель. Ширины панелей тянутся сплиттерами (левая
 * 4.25–20 rem, правая 20–40 rem) и вместе со свёрнутостью хранятся в
 * localStorage («tj.layout.*»), восстанавливаясь на монтировании; URL
 * панельную геометрию не несёт. Обе панели несут футер с кнопкой управления
 * в обоих состояниях; свёрнутые показывают над ним рейку миниатюр.
 * Топбар панелями больше не управляет (на мобильном остаётся
 * только кнопка drawer-шторки, механика панелей на мобильный не
 * расширяется — Non-Goals design.md). Топбар, кроме бренда, несёт
 * компактный итог журнала с разбивкой на реализованный и нереализованный
 * P&L — он обязателен на любом экране и читает общий с экраном списка
 * кэш обзора «Конструкций» (детальная сводка с отметкой времени марок
 * остаётся в шапке списка). Слой дизайн-системы: топбар и нав-пункты по
 * мастер-нодам «Каркаса» g0z20, пункты панели — примитив NavItem.
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 * Traceability: openspec:ui/screens#scenario-journal-total-always-visible
 * Traceability: openspec:ui/screens#scenario-frame-marks-failure-degrades-unrealized-only
 * Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
 * Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-panel-footer
 * Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
 * Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
 * Traceability: change:rework-spa-shell-and-screens/design#D1
 * Traceability: change:rework-panel-collapse-footer/design#D1
 * Traceability: change:rework-panel-collapse-footer/design#D2
 * Traceability: change:rework-spa-shell-and-screens/design#D3
 * Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
 */

export function AppLayout() {
	const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
	const isMobile = useIsMobile();
	const location = useLocation();
	const inboxCount = useQuery({
		queryKey: ["inbox-count"],
		queryFn: fetchInboxCount,
		refetchInterval: 30_000,
	});

	// Геометрия панелей: восстановление из localStorage происходит в
	// инициализаторе состояния usePanelGeometry, запись — при каждом
	// изменении (ширины и свёрнутость переживают перезагрузку).
	const left = usePanelGeometry("left");
	const right = usePanelGeometry("right");

	// Слот контекстной панели: страницы регистрируют содержимое через
	// useContextPanel (Outlet-контекст). Без регистрации правая панель
	// не выводится вовсе — центр занимает всю ширину; контекст плюс
	// свёрнутость пользователя = панель схлопнута до рейки миниатюр.
	const [registration, setRegistration] = useState<ContextPanelRegistration | null>(null);
	const setRegistrationStable = useCallback((next: ContextPanelRegistration | null) => {
		setRegistration((previous) => (previous === next ? previous : next));
	}, []);
	const contextPanelApi = useMemo<ContextPanelApi>(
		() => ({ setRegistration: setRegistrationStable }),
		[setRegistrationStable],
	);

	const collapsedDesktop = left.collapsed && isMobile == false;
	const contextVisible = registration != null && isMobile == false;

	const navigation = (
		<nav
			aria-label="Разделы журнала"
			className={cn(
				"flex flex-col gap-1 bg-background",
				isMobile
					? "h-full w-72 border-r border-divider px-3 py-4"
					: collapsedDesktop
						? "w-full px-1"
						: "min-w-0 flex-1 overflow-y-auto px-3 py-4",
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
							title={collapsedDesktop ? section.label : undefined}
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
		Свернуть/развернуть левую панель топбар больше не может: управление
		свёрнутостью переехало в футер самой панели. Кнопка в топбаре
		остаётся только на мобильном — это управление
		drawer-шторкой, а не панелью каркаса. Справа — компактный итог журнала:
		по требованию каркаса он виден на любом экране, поэтому дополняет
		бренд мастер-ноды, а детальная сводка остаётся в шапке списка.
	*/
	// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
	// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
	// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-panel-footer
	// Traceability: change:reconcile-frontend-with-design/design#D2
	return (
		<div className="flex h-svh flex-col">
			<header className="flex h-[54px] shrink-0 items-center gap-2.5 border-b border-divider bg-surface px-6">
				{isMobile && (
					<Button
						variant="ghost"
						size="icon"
						aria-label={mobileMenuOpen ? "Закрыть меню" : "Открыть меню"}
						onClick={() => setMobileMenuOpen((value) => !value)}
					>
						{mobileMenuOpen ? <X /> : <Menu />}
					</Button>
				)}
				<span className="text-sm font-semibold">Журнал Bybit</span>
				<JournalTotal />
			</header>
			<div className="flex min-h-0 flex-1">
				{isMobile == false && (
					<aside
						aria-label="Панель навигации"
						data-slot="layout-panel-left"
						data-testid="layout-panel-left"
						className="flex h-full min-h-0 shrink-0 border-r border-divider bg-background"
						style={{ width: `${left.collapsed ? COLLAPSED_LEFT_WIDTH_REM : left.width}rem` }}
					>
						<div className="flex min-h-0 min-w-0 flex-1 flex-col">
							{left.collapsed ? (
								<PanelMiniatureRail side="left">
									{navigation}
								</PanelMiniatureRail>
							) : (
								navigation
							)}
							<PanelFooter
								side="left"
								collapsed={left.collapsed}
								onToggle={() => left.setCollapsed(left.collapsed == false)}
								label={left.collapsed ? "Развернуть панель навигации" : "Свернуть панель навигации"}
							/>
						</div>
						{left.collapsed == false && (
							<PanelResizer
								side="left"
								label="Изменить ширину панели навигации"
								onResize={(deltaRem) => left.setWidth(left.width + deltaRem)}
							/>
						)}
					</aside>
				)}
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
				{/*
					Центральная зона: содержимое экрана. Без контекста правой
					панели занимает всю оставшуюся ширину.
				*/}
				<main className="min-w-0 flex-1 overflow-y-auto">
					<Outlet context={contextPanelApi} />
				</main>
				{contextVisible && (
					<aside
						aria-label="Контекстная панель"
						data-slot="layout-panel-right"
						data-testid="context-panel"
						className="flex h-full min-h-0 shrink-0 border-l border-divider bg-background"
						style={{ width: `${right.collapsed ? COLLAPSED_LEFT_WIDTH_REM : right.width}rem` }}
					>
						{right.collapsed == false && (
							<PanelResizer
								side="right"
								label="Изменить ширину контекстной панели"
								onResize={(deltaRem) => right.setWidth(right.width + deltaRem)}
							/>
						)}
						<div className="flex min-h-0 min-w-0 flex-1 flex-col">
							{right.collapsed ? (
								<PanelMiniatureRail side="right">
									{registration?.iconActions != null && (
										<div title="Действия контекста" className="flex flex-col items-center gap-1">
											{registration.iconActions}
										</div>
									)}
								</PanelMiniatureRail>
							) : (
								<div className="min-h-0 min-w-0 flex-1 overflow-y-auto px-3 pt-3.5 pb-3">
									{registration?.content}
								</div>
							)}
							<PanelFooter
								side="right"
								collapsed={right.collapsed}
								onToggle={() => right.setCollapsed(right.collapsed == false)}
								label={right.collapsed ? "Развернуть контекстную панель" : "Свернуть контекстную панель"}
							/>
						</div>
					</aside>
				)}
			</div>
		</div>
	);
}

/**
 * Компактный итог журнала в правом крае топбара: итог с разбивкой на
 * реализованный и нереализованный P&L обязателен на любом экране. Данные
 * читаются из общего с экраном «Конструкции» кэша React Query (один ключ —
 * один запрос), поэтому дублирующих обращений к API не появляется, а
 * детальная сводка с отметкой времени марок и счётчиками остаётся в шапке
 * списка. Токены каркаса: итог 14/600 $accentStrong, разбивка 11
 * $textMuted, признак сбоя — деструктивный цвет дизайн-системы.
 */
// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
function JournalTotal() {
	const overviewQuery = useConstructionsOverviewQuery();
	const summary = overviewQuery.data?.summary;

	return (
		<div data-slot="journal-total" data-testid="journal-total" className="ml-auto flex items-baseline gap-x-2">
			{summary === undefined ? (
				<span className="text-[11px] text-text-muted">чтение журнала…</span>
			) : (
				<>
					{/*
						Итог всегда виден; при сбое марок он помечается неполным,
						нереализованная часть получает признак сбоя, реализованная
						часть не исчезает.
					*/}
					{/* Traceability: openspec:ui/screens#scenario-frame-marks-failure-degrades-unrealized-only */}
					<span className="text-sm font-semibold text-accent-strong">
						{summary.totalPnL === null ? "недоступен" : `${formatSignedAmount(summary.totalPnL)} USDT`}
					</span>
					{summary.hasMarkFailure && <span className="text-[11px] text-text-muted">неполный</span>}
					<span className="text-[11px] text-text-muted">
						{`реализов. ${formatSignedAmount(summary.realizedPnL)} / нереализов. `}
						<span className={summary.unrealizedPnL === null ? "text-destructive" : undefined}>
							{summary.unrealizedPnL === null ? "сбой котировок" : formatSignedAmount(summary.unrealizedPnL)}
						</span>
					</span>
				</>
			)}
		</div>
	);
}
