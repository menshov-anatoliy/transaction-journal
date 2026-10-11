/// <reference types="vitest/config" />
import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// SPA смонтирован в корне: Blazor-UI удалён cutover'ом (issue #53), собранные
// ассеты и страницы раздаёт .NET-хост без префикса.
// Traceability: adr:docs/adr/0013-spa-served-from-host-root.md#fixed-mechanics
export default defineConfig({
	plugins: [react(), tailwindcss()],
	base: "/",
	// Dev-сервер сохраняет относительные API-запросы SPA и передаёт их .NET-хосту.
	// Порт фиксирован: занятый адрес должен приводить к ошибке, а не смене URL.
	// Traceability: doc:frontend/README.md#запуск-в-rider
	server: {
		host: "localhost",
		port: 5173,
		strictPort: true,
		proxy: {
			"^/api(?:/|$)": "http://localhost:5019",
		},
	},
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
