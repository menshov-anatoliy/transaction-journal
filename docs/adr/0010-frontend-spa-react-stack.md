# Frontend — React 19 SPA (Vite, TypeScript) с Tailwind CSS 4, shadcn/ui и assistant-ui; Blazor-UI удаляется после паритета

Новый интерфейс журнала решено строить как отдельное SPA на React 19 + TypeScript (сборка Vite), поверх нового HTTP API существующего .NET-приложения; визуальный слой — Tailwind CSS 4 + shadcn/ui (lucide-react), AI-чат — assistant-ui (runtime поверх нашего SSE API с опорой на примитивы Vercel AI SDK), Markdown — react-markdown + remark-gfm (просмотр) и @uiw/react-md-editor в split-режиме (редактирование «тулбар + live-превью, без WYSIWYG»), таблицы — TanStack Table + shadcn table, графики — Recharts, данные — TanStack Query, роутинг — react-router. Исследование сравнило React, Vue 3, Angular и Svelte 5 по AI-чат-компонентам, MD-инструментам, кастомизации под «воздушный» светлый референс и скорости solo-разработки: только React даёт полное покрытие нестандартных требований (assistant-ui, AI Elements), при этом SSR не нужен и Next.js — лишний слой. Решение принято при работе карты «Концептуально новый frontend журнала сделок» ([#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50)) по исследованию стека ([#51](https://github.com/menshov-anatoliy/transaction-journal/issues/51)): `.wf-research/frontend-stack/research.md`, разд. 3 и 10.

## Considered Options

- **Vue 3 + Vite + shadcn-vue/bits-ui + @assistant-ui/vue** — запасной вариант: экосистема законна, но поверхность assistant-ui для Vue урезана и моложе, больше звеньев проверять самостоятельно.
- **Angular** — отклонён: «батарейки» (роутер, формы, DI) привлекательны для .NET-разработчика, но нет ни assistant-ui, ни AI Elements — ключевые требования (AI-чат, MD-инструменты) пришлось бы собирать с нуля.
- **Svelte 5** — отклонён: компактный приятный код, но минимум готовых AI-чат/MD решений под наши требования.
- **Next.js вместо чистого Vite SPA** — отклонён: журнал — локальный персональный инструмент за своим API, SSR/SEO не нужны; лишний слой и ограничения серверной модели.
- **Material UI / enterprise-наборы** — отклонены: конфликтуют с «воздушным» светлым референсом; shadcn/ui даёт полный контроль через CSS-переменные (код компонентов в нашем репо).

## Consequences

- С момента решения у журнала два UI до достижения паритета: новый SPA и существующий Blazor Server; Blazor-UI удаляется только после закрытия карты переноса №1–29 (`.wf-research/ui-concept/concept.md`, §12).
- Код shadcn/ui живёт в нашем репозитории: обновления, совместимость (включая миграцию Radix → Base UI) — наша забота; флейвор фиксируется при подключении компонентов.
- assistant-ui до 1.0: churn изолируется одним адаптером (ExternalStoreRuntime/ChatModelAdapter) поверх нашего SSE-протокола; фреймворк-зависимого кода в домене нет.
- Редактор @uiw/react-md-editor функционально скромен; план Б (Vditor или DIY на CodeMirror 6) не меняет остальной стек.
- Второй язык/рантайм (TypeScript/Node) в сборке приложения — осознанная плата за готовые AI-чат и MD-решения; бэкенд остаётся C#/.NET (см. [0003](0003-stack-single-exe-dotnet-sqlite.md)).
