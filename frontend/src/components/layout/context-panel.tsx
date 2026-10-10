import { useEffect, type ReactNode } from "react";
import { useOutletContext } from "react-router";

/**
 * Слот контекстной правой панели (design.md D3): содержимое задаётся
 * текущим экраном через Outlet-контекст. Без регистрации панель свёрнута
 * и центр занимает всю ширину; появление контекста разворачивает панель
 * на сохранённую ширину.
 *
 * Контракт для страниц: хук `useContextPanel({ content, iconActions })`
 * регистрирует содержимое на время жизни вызывающего компонента.
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 * Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
 * Traceability: change:rework-spa-shell-and-screens/design#D3
 */

/** Регистрация контекста правой панели экраном. */
export interface ContextPanelRegistration {
	/** Основное содержимое панели (например, краткая карточка конструкции). */
	content: ReactNode;
	/** Иконочные действия в колонке панели над кнопкой сворачивания. */
	iconActions?: ReactNode;
}

/** API слота, который AppLayout отдаёт страницам через Outlet-контекст. */
export interface ContextPanelApi {
	setRegistration: (registration: ContextPanelRegistration | null) => void;
}

/**
 * Регистрирует контекст правой панели на время жизни вызывающего
 * компонента; при размонтировании или сбросе в null регистрация
 * снимается, и каркас сворачивает панель.
 *
 * Внимание: объект регистрации обязан быть стабильным между рендерами
 * (useMemo) — эффект перезапускается при смене его идентичности.
 * Вне Outlet (страница без каркаса) регистрация превращается в no-op,
 * чтобы страницы оставались рендерибельными автономно.
 *
 * Traceability: openspec:ui/screens#scenario-frame-context-panel-collapsed-without-context
 */
export function useContextPanel(registration: ContextPanelRegistration | null): void {
	const api = useOutletContext<ContextPanelApi | null>();

	useEffect(() => {
		api?.setRegistration(registration);
		return () => api?.setRegistration(null);
	}, [api, registration]);
}
