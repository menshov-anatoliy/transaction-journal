import type { ContextPanelApi } from "./context-panel";
import type { MobileFocusApi } from "./mobile-focus";
import type { PageHeaderApi } from "./page-header";

/**
 * Составной контекст Outlet каркаса (design.md D3–D4): страницы получают
 * слоты топбара и контекстной панели одним объектом. Хуки слотов
 * (`useContextPanel`, `usePageHeader`, `useMobileFocus`) читают свою
 * часть; вне каркаса контекст равен null и регистрации превращаются
 * в no-op.
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 */
export interface AppOutletContext {
	/** Слот правой контекстной панели. */
	contextPanel: ContextPanelApi;
	/** Слот сегмента «страница» топбара. */
	pageHeader: PageHeaderApi;
	/**
	 * Слот фокусного мобильного хрома (раунд-сверка 10.0): фокусный экран
	 * (активный чат агента) просит каркас скрыть мобильный топбар и занять
	 * экран своей шапкой 52 px; на десктопе запрос игнорируется.
	 *
	 * Traceability: change:rework-contextual-topbar/tasks#10.0
	 */
	mobileFocus: MobileFocusApi;
}
