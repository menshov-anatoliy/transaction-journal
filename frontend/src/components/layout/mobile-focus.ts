import { useEffect } from "react";
import { useOutletContext } from "react-router";
import type { AppOutletContext } from "./outlet-context";

/**
 * Слот фокусного мобильного хрома (раунд-сверка 10.0, мастер OvuY9):
 * страница может попросить каркас скрыть глобальный мобильный топбар,
 * чтобы фокусный экран — активный чат «Агента» — занял всю высоту со
 * своей собственной шапкой 52 px. На десктопе (lg+) каркас запрос
 * игнорирует: сегментная механика топбара существует только там; вне
 * каркаса запрос превращается в no-op.
 *
 * Traceability: change:rework-contextual-topbar/tasks#10.0
 */

/** API фокусного мобильного хрома, который AppLayout отдаёт страницам через Outlet-контекст. */
export interface MobileFocusApi {
	setActive: (active: boolean) => void;
}

/**
 * Запрашивает у каркаса скрытие мобильного топбара, пока `active` равен
 * true; при размонтировании или сбросе в false запрос снимается и топбар
 * возвращается. Эффект срабатывает при смене значения, поэтому передавать
 * следует стабильное булево выражение (как и другим слотам регистраций).
 *
 * Traceability: change:rework-contextual-topbar/tasks#10.0
 */
export function useMobileFocus(active: boolean): void {
	const api = useOutletContext<AppOutletContext | null>();
	const focusApi = api?.mobileFocus ?? null;

	useEffect(() => {
		focusApi?.setActive(active);
		return () => focusApi?.setActive(false);
	}, [focusApi, active]);
}
