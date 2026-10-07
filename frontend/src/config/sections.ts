import type { ComponentType } from "react";
import type { LucideIcon } from "lucide-react";
import { Bot, Inbox, Layers, Lightbulb, RefreshCw } from "lucide-react";
import { AgentPage } from "@/pages/agent-page";
import { ConstructionsPage } from "@/pages/constructions-page";
import { HintsPage } from "@/pages/hints-page";
import { InboxPage } from "@/pages/inbox-page";
import { SyncSettingsPage } from "@/pages/sync-settings-page";

/** Маршрут раздела от корня SPA (монтажный префикс /spa не входит в путь). */
export type SectionPath = "/" | "/inbox" | "/hints" | "/agent" | "/sync-settings";

/** Один раздел журнала: пункт левой панели и страница роутера из одного конфига. */
export interface AppSection {
	path: SectionPath;
	label: string;
	icon: LucideIcon;
	component: ComponentType;
}

// Единый источник разделов каркаса: из него собираются и левая панель
// навигации, и маршруты SPA. Состав, порядок и маршруты закреплены
// концепцией интерфейса §2; наполнение разделов — задачи 5.x.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
export const appSections: readonly AppSection[] = [
	{
		path: "/",
		label: "Конструкции",
		icon: Layers,
		component: ConstructionsPage,
	},
	{
		path: "/inbox",
		label: "Входящие",
		icon: Inbox,
		component: InboxPage,
	},
	{
		path: "/hints",
		label: "Подсказки",
		icon: Lightbulb,
		component: HintsPage,
	},
	{
		path: "/agent",
		label: "Агент",
		icon: Bot,
		component: AgentPage,
	},
	{
		path: "/sync-settings",
		label: "Синхронизация",
		icon: RefreshCw,
		component: SyncSettingsPage,
	},
];
