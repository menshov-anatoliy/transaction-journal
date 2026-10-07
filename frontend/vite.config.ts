/// <reference types="vitest/config" />
import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// SPA смонтирован в префиксе /spa, чтобы не конфликтовать за маршруты
// с работающим Blazor-UI в корне хоста; переезд в корень — задача 7.2
// переключения журнала на новый интерфейс.
// Traceability: doc:.wf-research/ui-concept/concept.md#2-каркас-приложения
// Traceability: adr:docs/adr/0010-frontend-spa-react-stack.md
export default defineConfig({
	plugins: [react(), tailwindcss()],
	base: "/spa/",
	resolve: {
		alias: {
			"@": path.resolve(import.meta.dirname, "src"),
		},
	},
	test: {
		environment: "jsdom",
		setupFiles: ["src/test/setup.ts"],
	},
});
