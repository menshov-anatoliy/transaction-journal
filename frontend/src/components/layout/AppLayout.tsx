import { useCallback, useMemo, useState } from "react";
import { NavLink, Outlet, matchPath, useLocation } from "react-router";
import { useQuery } from "@tanstack/react-query";
import {
	LineChart,
	Menu,
	PanelLeftClose,
	PanelLeftOpen,
	PanelRightClose,
	PanelRightOpen,
	RefreshCw,
	X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { NavItem, StatusChip } from "@/components/design";
import { appSections } from "@/config/sections";
import { cn } from "@/lib/utils";
import { fetchInboxCount } from "@/lib/api/inbox";
import { useConstructionsOverviewQuery } from "@/lib/api/constructions-overview-query";
import { fetchSyncRuns } from "@/lib/api/sync";
import { formatSignedAmount } from "@/lib/format/quantity";
import { formatClock } from "@/lib/format/display-time";
import { useIsMobile } from "@/lib/use-mobile";
import { PanelMiniatureRail } from "./PanelMiniatureRail";
import { PanelResizer } from "./PanelResizer";
import { COLLAPSED_LEFT_WIDTH_REM, usePanelGeometry } from "./layout-geometry";
import type { AppSection } from "@/config/sections";
import type { ContextPanelApi, ContextPanelRegistration } from "./context-panel";
import type { MobileFocusApi } from "./mobile-focus";
import type { PageHeaderApi, PageHeaderRegistration } from "./page-header";
import type { AppOutletContext } from "./outlet-context";

/**
 * Каркас приложения «К1: контекстный топбар + три зоны» (ADR-0016,
 * design.md D1): топбар 64 px из трёх сегментов, выровненных по колонкам,
 * левая панель навигации, центр с содержимым экрана (Outlet) и правая
 * контекстная панель. Ширины панелей тянутся сплиттерами (левая 4.25–20
 * rem, правая 20–40 rem) и вместе со свёрнутостью хранятся в localStorage
 * («tj.layout.*»), восстанавливаясь на монтировании; URL панельную
 * геометрию не несёт. Футеров у панелей нет: управление свёрнутостью
 * живёт в сегментах топбара — развёрнутая панель даёт кнопку сворачивания
 * у внутреннего края своего сегмента, свёрнутая сжимает сегмент до 44 px
 * с кнопкой разворачивания, а сама панель превращается в рейку миниатюр
 * на всю высоту. Сегмент страницы наполняется крошкой и инвариантами
 * каркаса (итог журнала, статус синхронизации), а заголовок и чипы
 * регистрирует текущий экран (usePageHeader); сегмент контекста показывает
 * кнопку сворачивания и чип короткого названия из регистрации контекста,
 * без регистрации контекста панель и сегмент скрыты. Сегментная механика
 * существует только на десктопе (lg+): мобильный топбар 52 px (раунд-сверка
 * 10.0, мастер iGlhI) несёт лишь кнопку drawer-шторки, бренд и статус
 * синхронизации — итог журнала, крошка, заголовок, чипы страницы и
 * контекст-сегмент на мобильном отсутствуют, механика шторки не меняется;
 * фокусный мобильный экран — активный чат агента — скрывает топбар целиком
 * запросом useMobileFocus (мастер OvuY9).
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 * Traceability: openspec:ui/screens#scenario-journal-total-always-visible
 * Traceability: openspec:ui/screens#scenario-frame-marks-failure-degrades-unrealized-only
 * Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
 * Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-topbar-segment
 * Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
 * Traceability: openspec:ui/screens#scenario-frame-topbar-segments-align-to-columns
 * Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
 * Traceability: openspec:ui/screens#scenario-frame-page-header-registration
 * Traceability: openspec:ui/screens#scenario-frame-sync-status-last-successful-run
 * Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
 * Traceability: change:rework-contextual-topbar/design#D1
 * Traceability: change:rework-contextual-topbar/design#D2
 * Traceability: change:rework-contextual-topbar/design#D3
 * Traceability: change:rework-contextual-topbar/design#D4
 * Traceability: change:rework-contextual-topbar/design#D5
 * Traceability: change:rework-contextual-topbar/design#D6
 * Traceability: change:rework-contextual-topbar/tasks#10.0
 * Traceability: change:rework-spa-shell-and-screens/design#D1
 * Traceability: change:rework-spa-shell-and-screens/design#D3
 * Traceability: adr:docs/adr/0016-contextual-topbar-frame.md
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
	// useContextPanel (Outlet-контекст). Без регистрации правая панель и
	// сегмент контекста не выводятся вовсе — центр занимает всю ширину;
	// контекст плюс свёрнутость пользователя = панель схлопнута до рейки
	// миниатюр.
	const [registration, setRegistration] = useState<ContextPanelRegistration | null>(null);
	const setRegistrationStable = useCallback((next: ContextPanelRegistration | null) => {
		setRegistration((previous) => (previous === next ? previous : next));
	}, []);

	// Слот сегмента «страница»: экраны регистрируют заголовок и чипы шапки
	// через usePageHeader (тот же Outlet-контекст, design.md D3).
	const [pageHeader, setPageHeader] = useState<PageHeaderRegistration | null>(null);
	const setPageHeaderStable = useCallback((next: PageHeaderRegistration | null) => {
		setPageHeader((previous) => (previous === next ? previous : next));
	}, []);

	const contextPanelApi = useMemo<ContextPanelApi>(
		() => ({ setRegistration: setRegistrationStable }),
		[setRegistrationStable],
	);
	const pageHeaderApi = useMemo<PageHeaderApi>(
		() => ({ setRegistration: setPageHeaderStable }),
		[setPageHeaderStable],
	);
	// Слот фокусного мобильного хрома (раунд-сверка 10.0): фокусный экран
	// (активный чат агента) просит скрыть мобильный топбар, чтобы занять
	// экран собственной шапкой 52 px; на десктопе запрос не влияет на каркас.
	// Traceability: change:rework-contextual-topbar/tasks#10.0
	const [mobileFocusActive, setMobileFocusActive] = useState(false);
	const setMobileFocusActiveStable = useCallback((active: boolean) => {
		setMobileFocusActive(active);
	}, []);
	const mobileFocusApi = useMemo<MobileFocusApi>(
		() => ({ setActive: setMobileFocusActiveStable }),
		[setMobileFocusActiveStable],
	);
	const outletContext = useMemo<AppOutletContext>(
		() => ({ contextPanel: contextPanelApi, pageHeader: pageHeaderApi, mobileFocus: mobileFocusApi }),
		[contextPanelApi, pageHeaderApi, mobileFocusApi],
	);

	const collapsedDesktop = left.collapsed && isMobile == false;
	const contextVisible = registration != null && isMobile == false;

	// Крошка сегмента страницы всегда из конфигурации разделов; для
	// вложенных маршрутов (карточка конструкции) срабатывает префиксный
	// запасной матчинг — карточка относится к разделу «Конструкции».
	const section = resolveSection(location.pathname);

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
		Топбар по мастер-ноде jGvOM экрана «Каркас» g0z20: высота 64, заливка
		$surface, нижний разделитель $divider; три сегмента по колонкам
		каркаса (design.md D1): над левой панелью — навигация [0,16], над
		центром — страница [0,20], над правой — контекст [0,16]. Ширины
		сегментов навигации и контекста берутся из той же геометрии панелей,
		что и ширины aside, — колонка топбара продолжается в панель без
		расхождений; свёрнутая панель сжимает сегмент до 44 px.
	*/
	// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
	// Traceability: openspec:ui/screens#scenario-frame-topbar-segments-align-to-columns
	// Traceability: openspec:ui/screens#scenario-frame-collapse-button-in-topbar-segment
	// Traceability: change:reconcile-frontend-with-design/design#D2
	// Traceability: change:rework-contextual-topbar/design#D1
	return (
		<div className="flex h-svh flex-col">
			{/* Топбар: на десктопе (lg+) — сегментный, 64 px по мастер-ноде
			    jGvOM; на мобильном — топбар 52 px мастера iGlhI (раунд-сверка
			    10.0). Фокусный мобильный экран (активный чат агента) не
			    получает топбара вовсе: страница запрашивает скрытие через
			    useMobileFocus, и каркас не рендерит элемент. */}
			{isMobile && mobileFocusActive == false && (
				/*
					Мобильный топбар (раунд-сверка 10.0, мастер iGlhI):
					высота 52 px, заливка $surface, нижний разделитель
					$divider, паддинги [0,16], гэп 10; состав слева направо —
					кнопка drawer-шторки (иконка menu 18×18 $textPrimary,
					aria-label и механика шторки не меняются), бренд 14/600
					$textPrimary, flex-спейсер и статус синхронизации справа
					(иконка refresh-cw 13×13 + «HH:MM» 11 normal, оба
					$textMuted; источник тот же fetchSyncRuns, инвариант D5:
					пусто/нет успешных/сбой загрузки — блок скрыт). Итог
					журнала, крошка, заголовок, чипы страницы и
					контекст-сегмент на мобильном отсутствуют — сегментная
					механика топбара только на десктопе (lg+).

					Traceability: openspec:ui/screens#requirement-app-frame-navigation
					Traceability: openspec:ui/screens#scenario-frame-sync-status-last-successful-run
					Traceability: change:rework-contextual-topbar/design#D5
					Traceability: change:rework-contextual-topbar/tasks#10.0
				*/
				<header
					data-slot="mobile-topbar"
					data-testid="mobile-topbar"
					className="flex h-13 shrink-0 items-center gap-2.5 border-b border-divider bg-surface px-4"
				>
					<>
						<Button
							variant="ghost"
							size="icon"
							aria-label={mobileMenuOpen ? "Закрыть меню" : "Открыть меню"}
							onClick={() => setMobileMenuOpen((value) => !value)}
						>
							{mobileMenuOpen ? <X className="size-4.5 text-text-primary" /> : <Menu className="size-4.5 text-text-primary" />}
						</Button>
						<span className="text-sm font-semibold text-text-primary">Журнал Bybit</span>
						<span aria-hidden="true" className="flex-1" />
						<SyncStatus variant="mobile" />
					</>
				</header>
			)}
			{isMobile == false && (
				<header className="flex h-16 shrink-0 items-stretch border-b border-divider bg-surface">
					<>
						{/*
							Сегмент навигации (design.md D2): лого-глиф, бренд и
							ghost-кнопка сворачивания у внутреннего (правого) края;
							свёрнутая панель — только кнопка разворачивания.
						*/}
						<div
							data-slot="topbar-segment"
							data-side="nav"
							data-testid="topbar-segment-nav"
							className={cn(
								"flex shrink-0 items-center gap-2.5 px-4",
								collapsedDesktop && "px-1",
							)}
							style={{ width: `${collapsedDesktop ? COLLAPSED_LEFT_WIDTH_REM : left.width}rem` }}
						>
							{collapsedDesktop ? (
								<Button
									variant="ghost"
									size="icon"
									className="size-7"
									aria-label="Развернуть панель навигации"
									title="Развернуть панель навигации"
									onClick={() => left.setCollapsed(false)}
								>
									<PanelLeftOpen className="size-[15px]" />
								</Button>
							) : (
								<>
									{/*
										Лого-глиф по ноде srW2i мастера: квадрат 26×26
										(size-6.5) с радиусом 8 на паре
										accentSoft/accentStrong — эта пара в «Каркасе»
										принадлежит логотипу топбара; по центру иконка
										line-chart 14×14 цветом $accentStrong.
									*/}
									{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
									<span
										data-slot="topbar-logo"
										data-testid="topbar-logo"
										aria-hidden="true"
										className="flex size-6.5 shrink-0 items-center justify-center rounded-sm bg-accent-soft"
									>
										<LineChart className="size-3.5 text-accent-strong" />
									</span>
									<span className="whitespace-nowrap text-sm font-semibold">Журнал Bybit</span>
									{/* Кнопки панелей — 28×28 по мастеру J8C2x/z6tizc, иконка 15 сохраняется. */}
									<Button
										variant="ghost"
										size="icon"
										className="ml-auto size-7"
										aria-label="Свернуть панель навигации"
										title="Свернуть панель навигации"
										onClick={() => left.setCollapsed(true)}
									>
										<PanelLeftClose className="size-[15px]" />
									</Button>
								</>
							)}
						</div>
						<PageHeaderSegment
							section={section}
							registration={pageHeader}
							inboxCount={inboxCount.isError == false ? (inboxCount.data ?? null) : null}
						/>
						{contextVisible && (
							/*
								Сегмент контекста (design.md D4): кнопка сворачивания у
								внутреннего (левого) края и чип короткого названия
								содержимого; свёрнутая панель — только кнопка
								разворачивания. Существует только при регистрации
								контекста, иначе скрыт вместе с панелью.
							*/
							<div
								data-slot="topbar-segment"
								data-side="context"
								data-testid="topbar-segment-context"
								className={cn(
									"flex shrink-0 items-center gap-2.5 px-4",
									right.collapsed && "px-1",
								)}
								style={{ width: `${right.collapsed ? COLLAPSED_LEFT_WIDTH_REM : right.width}rem` }}
							>
								{right.collapsed ? (
									<Button
										variant="ghost"
										size="icon"
										className="size-7"
										aria-label="Развернуть контекстную панель"
										title="Развернуть контекстную панель"
										onClick={() => right.setCollapsed(false)}
									>
										<PanelRightOpen className="size-[15px]" />
									</Button>
								) : (
									<>
										<Button
											variant="ghost"
											size="icon"
											className="size-7"
											aria-label="Свернуть контекстную панель"
											title="Свернуть контекстную панель"
											onClick={() => right.setCollapsed(true)}
										>
											<PanelRightClose className="size-[15px]" />
										</Button>
										{/*
											Чип «что открыто» на паре $infoSoft/$info по мастеру
											bOfzl/s5fwGV — примитив StatusChip тона info; клик
											по чипу не проектируется.
										*/}
										{/* Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content */}
										<StatusChip tone="info" data-testid="context-chip">
											{registration.chipLabel}
										</StatusChip>
									</>
								)}
							</div>
						)}
					</>
				</header>
			)}
			<div className="flex min-h-0 flex-1">
				{isMobile == false && (
					<aside
						aria-label="Панель навигации"
						data-slot="layout-panel-left"
						data-testid="layout-panel-left"
						className="flex h-full min-h-0 shrink-0 border-r border-divider bg-background"
						style={{ width: `${left.collapsed ? COLLAPSED_LEFT_WIDTH_REM : left.width}rem` }}
					>
						{/*
							Футеров у панелей нет (design.md D6): свёрнутая панель —
							рейка миниатюр на всю высоту, развёрнутая — навигация.
						*/}
						{/* Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail */}
						{left.collapsed ? (
							<PanelMiniatureRail side="left">
								{navigation}
							</PanelMiniatureRail>
						) : (
							navigation
						)}
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
					<Outlet context={outletContext} />
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
					</aside>
				)}
			</div>
		</div>
	);
}

/**
 * Раздел для крошки сегмента страницы: сначала точный матч как у пунктов
 * навигации (активен точный «/» и потомки остальных разделов), для
 * вложенных маршрутов вне конфигурации (карточка конструкции) —
 * префиксный запасной матчинг; неизвестный путь даёт null.
 */
function resolveSection(pathname: string): AppSection | null {
	const exact = appSections.find(
		(candidate) => matchPath({ path: candidate.path, end: candidate.path === "/" }, pathname) != null,
	);
	if (exact != null) {
		return exact;
	}

	return (
		appSections.find((candidate) => matchPath({ path: candidate.path, end: false }, pathname) != null) ??
		null
	);
}

/**
 * Сегмент «страница» топбара (design.md D3): слева крошка «Журнал /
 * <раздел>» из конфигурации разделов с хвостом из регистрации шапки
 * страницы («Журнал / <раздел> / <хвост>») и бейджем «Входящих» у крошки,
 * далее заголовок и чипы из регистрации (без регистрации — заголовок
 * раздела); справа — инварианты каркаса: статус синхронизации и итог
 * журнала с разбивкой, видимые на любом экране. Паддинги [0,20] по
 * мастеру jGvOM.
 *
 * Traceability: openspec:ui/screens#scenario-frame-page-header-registration
 * Traceability: openspec:ui/screens#scenario-inbox-badge-counts-unassigned
 * Traceability: openspec:ui/screens#scenario-journal-total-always-visible
 * Traceability: change:rework-contextual-topbar/design#D3
 */
function PageHeaderSegment({
	section,
	registration,
	inboxCount,
}: {
	section: AppSection | null;
	registration: PageHeaderRegistration | null;
	/** Число непривязанных сделок или null при отсутствии/сбое данных. */
	inboxCount: number | null;
}) {
	/*
		Бейдж «Входящих» дублируется у крошки по решению владельца: на экране
		«Входящие» счётчик виден и в пункте меню, и у крошки сегмента; данные
		— тот же запрос inbox-count, что и у пункта меню.
	*/
	// Traceability: openspec:ui/screens#scenario-inbox-badge-counts-unassigned
	const badgeVisible = section?.path === "/inbox" && (inboxCount ?? 0) > 0;
	const title = registration?.title ?? section?.label;

	return (
		<div
			data-slot="topbar-segment"
			data-side="page"
			data-testid="topbar-segment-page"
			className="flex min-w-0 flex-1 items-center gap-2.5 px-5"
		>
			{/* Крошка «Журнал / <раздел>» — одна строка 12px $textMuted без
				выделения хвоста раздела (мастер jGvOM). */}
			<nav
				aria-label="Крошка раздела"
				className="flex shrink-0 items-center gap-1.5 whitespace-nowrap text-xs text-text-muted"
			>
				<span>Журнал</span>
				<span aria-hidden="true">/</span>
				{section != null && <span>{section.label}</span>}
				{/*
					Хвост крошки страницы из регистрации шапки: вложенный
					маршрут (карточка конструкции, вкладка «Правила» агента)
					дополняет крошку третьим уровнем — «Журнал / Конструкции /
					Карточка», «Журнал / Агент / Правила»; без регистрации
					крошка двухуровневая.
				*/}
				{/* Traceability: openspec:ui/screens#scenario-frame-page-header-registration */}
				{/* Traceability: openspec:ui/screens#scenario-frame-page-header-breadcrumb-tail */}
				{/* Traceability: change:rework-contextual-topbar/tasks#3.0 */}
				{/* Traceability: change:rework-contextual-topbar/tasks#8.0 */}
				{registration?.breadcrumbTail != null && (
					<>
						<span aria-hidden="true">/</span>
						<span>{registration.breadcrumbTail}</span>
					</>
				)}
				{badgeVisible && (
					<span
						data-slot="topbar-badge"
						data-testid="topbar-badge"
						className="inline-flex items-center justify-center rounded-full bg-primary px-2 py-0.5 text-[11px] font-semibold text-primary-foreground"
					>
						{inboxCount}
					</span>
				)}
			</nav>
			{/* Заголовок страницы — 15/600 по мастеру jGvOM. */}
			{title != null && (
				<span data-testid="topbar-page-title" className="truncate text-[15px] font-semibold text-text-primary">
					{title}
				</span>
			)}
			{registration?.chips != null && (
				<div className="flex min-w-0 items-center gap-2">{registration.chips}</div>
			)}
			<div className="ml-auto flex shrink-0 items-center gap-2.5">
				<SyncStatus />
				<JournalTotal />
			</div>
		</div>
	);
}

/**
 * Статус синхронизации (design.md D5): «Обновлено HH:MM» по последнему
 * успешному запуску из журнала синхронизаций (записи приходят новыми
 * сверху), время — в часовой зоне браузера. Пустой журнал, отсутствие
 * успешных запусков или сбой загрузки скрывают блок целиком — деградации
 * остальных элементов топбара нет; клик не проектируется. Десктопная
 * инстанс живёт в сегменте страницы, мобильная (variant «mobile», мастер
 * iGlhI) — в мобильном топбаре; запрос у инстансов общий.
 *
 * Traceability: openspec:ui/screens#scenario-frame-sync-status-last-successful-run
 * Traceability: change:rework-contextual-topbar/design#D5
 */
function SyncStatus({ variant = "desktop" }: { variant?: "desktop" | "mobile" }) {
	const runsQuery = useQuery({
		queryKey: ["sync-runs"],
		queryFn: () => fetchSyncRuns(20),
	});
	// Последний успешный запуск — первая запись со статусом succeeded в
	// журнале «новыми сверху»; момент обновления — завершение запуска.
	const lastSucceeded = runsQuery.data?.find((run) => run.status === "succeeded");
	const clock = formatClock(lastSucceeded?.finishedAt ?? lastSucceeded?.startedAt);
	if (clock === null) {
		return null;
	}

	/*
		Мобильная инстанс (раунд-сверка 10.0, мастер iGlhI): иконка
		refresh-cw 13×13 + «HH:MM» 11 normal, оба $textMuted; источник и
		условия скрытия те же, что у десктопного статуса.
	*/
	// Traceability: change:rework-contextual-topbar/tasks#10.0
	if (variant === "mobile") {
		return (
			<div
				data-slot="sync-status"
				data-testid="sync-status"
				className="flex items-center gap-1.5 whitespace-nowrap text-[11px] text-text-muted"
				title="Последний успешный запуск синхронизации"
			>
				<RefreshCw aria-hidden="true" className="size-[13px]" />
				<span>{clock}</span>
			</div>
		);
	}

	// 11.5px $textMuted по мастеру jGvOM (нода SyncStatus).
	return (
		<div
			data-slot="sync-status"
			data-testid="sync-status"
			className="flex items-center gap-1.5 whitespace-nowrap text-[11.5px] text-text-muted"
			title="Последний успешный запуск синхронизации"
		>
			<RefreshCw aria-hidden="true" className="size-3.5" />
			<span>{`Обновлено ${clock}`}</span>
		</div>
	);
}

/**
 * Итог журнала в правом крае сегмента страницы: итог с разбивкой на
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
		<div data-slot="journal-total" data-testid="journal-total" className="flex items-baseline gap-x-2">
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
