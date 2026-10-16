import type { ContextPanelApi } from "./context-panel";
import type { PageHeaderApi } from "./page-header";

/**
 * Составной контекст Outlet каркаса (design.md D3–D4): страницы получают
 * оба слота топбара и контекстной панели одним объектом. Хуки слотов
 * (`useContextPanel`, `usePageHeader`) читают свою часть; вне каркаса
 * контекст равен null и регистрации превращаются в no-op.
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 */
export interface AppOutletContext {
	/** Слот правой контекстной панели. */
	contextPanel: ContextPanelApi;
	/** Слот сегмента «страница» топбара. */
	pageHeader: PageHeaderApi;
}
