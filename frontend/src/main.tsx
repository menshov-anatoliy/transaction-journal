import { QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router";
import { TooltipProvider } from "@/components/ui/tooltip";
import { createAppQueryClient } from "@/lib/query-client";
import { createAppRouter } from "@/router";
import "@/index.css";

// Точка входа SPA журнала: react-router и TanStack Query собираются по
// зафиксированному стеку; роутер монтируется в корне .NET-хоста.
// Traceability: adr:docs/adr/0010-frontend-spa-react-stack.md

const queryClient = createAppQueryClient();
const router = createAppRouter();

createRoot(document.getElementById("root")!).render(
	<StrictMode>
		<QueryClientProvider client={queryClient}>
			{/* Radix Tooltip требует провайдера: подсказки метрик приложения
			    (значения сводки, числа индикатора, заголовки колонок) монтируются
			    в разных местах дерева, поэтому провайдер общий, в корне SPA. */}
			<TooltipProvider>
				<RouterProvider router={router} />
			</TooltipProvider>
		</QueryClientProvider>
	</StrictMode>,
);
