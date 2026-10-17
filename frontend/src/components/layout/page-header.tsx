import { useEffect, type ReactNode } from "react";
import { useOutletContext } from "react-router";
import type { AppOutletContext } from "./outlet-context";

/**
 * Сегмент «страница» топбара каркаса (design.md D3): заголовок и ключевые
 * чипы/действия задаёт текущий экран через Outlet-контекст — по образцу
 * слота контекстной панели. Крошка «Журнал / <раздел>», бейдж «Входящих»,
 * итог журнала и статус синхронизации страницами не регистрируются: это
 * инварианты каркаса. Страница может дополнить крошку хвостом третьего
 * уровня («Журнал / <раздел> / <хвост>») для вложенных маршрутов.
 *
 * Контракт для страниц: хук `usePageHeader({ title, breadcrumbTail, chips })`
 * регистрирует шапку на время жизни вызывающего компонента.
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 * Traceability: openspec:ui/screens#scenario-frame-page-header-registration
 */

/** Регистрация шапки страницы в сегменте «страница» топбара. */
export interface PageHeaderRegistration {
	/** Заголовок страницы рядом с крошкой; без поля показывается раздел из конфигурации. */
	title?: ReactNode;
	/**
	 * Хвост трёхуровневой крошки страницы: «Журнал / <раздел> / <хвост>».
	 * Вложенный маршрут (карточка конструкции, вкладка «Правила» агента)
	 * регистрирует хвост, обычные разделы поле не задают и остаются
	 * двухуровневыми.
	 *
	 * Traceability: openspec:ui/screens#scenario-frame-page-header-registration
	 * Traceability: openspec:ui/screens#scenario-frame-page-header-breadcrumb-tail
	 * Traceability: change:rework-contextual-topbar/tasks#3.0
	 * Traceability: change:rework-contextual-topbar/tasks#8.0
	 */
	breadcrumbTail?: ReactNode;
	/** Ключевые чипы/действия страницы рядом с заголовком. */
	chips?: ReactNode;
}

/** API сегмента страницы, который AppLayout отдаёт страницам через Outlet-контекст. */
export interface PageHeaderApi {
	setRegistration: (registration: PageHeaderRegistration | null) => void;
}

/**
 * Регистрирует шапку страницы на время жизни вызывающего компонента;
 * при размонтировании или сбросе в null регистрация снимается, и сегмент
 * показывает крошку с заголовком раздела из конфигурации разделов.
 *
 * Внимание: объект регистрации обязан быть стабильным между рендерами
 * (useMemo) — эффект перезапускается при смене его идентичности.
 * Вне Outlet (страница без каркаса) регистрация превращается в no-op,
 * чтобы страницы оставались рендерибельными автономно.
 *
 * Traceability: openspec:ui/screens#scenario-frame-page-header-registration
 */
export function usePageHeader(registration: PageHeaderRegistration | null): void {
	const api = useOutletContext<AppOutletContext | null>();
	const headerApi = api?.pageHeader ?? null;

	useEffect(() => {
		headerApi?.setRegistration(registration);
		return () => headerApi?.setRegistration(null);
	}, [headerApi, registration]);
}
