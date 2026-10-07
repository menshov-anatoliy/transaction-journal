import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@/index.css";

// Точка входа SPA журнала: каркас собирается в последующих задачах 1.1
// (роутер, слой запросов, раскладка), здесь — минимальная загрузка стилей.
createRoot(document.getElementById("root")!).render(
	<StrictMode>
		<div className="min-h-screen bg-background text-foreground">Журнал Bybit</div>
	</StrictMode>,
);
