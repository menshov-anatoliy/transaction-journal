# Frontend журнала сделок (SPA)

SPA нового интерфейса журнала: React 19 + TypeScript на Vite, Tailwind CSS 4,
shadcn/ui (lucide-react), react-router, TanStack Query. Стек зафиксирован в
[ADR-0010](../docs/adr/0010-frontend-spa-react-stack.md).

## Команды

- `npm run dev` — dev-сервер Vite (порт 5173, префикс `/spa/`).
- `npm run build` — сборка в `dist/` (проверка типов + Vite).
- `npm test` — юнит-тесты vitest (однократный прогон).
- `npm run preview` — локальный просмотр собранного `dist/`.

## Раздача статики

Собранный `dist/` раздаёт существующий .NET-хост (`src/TransactionJournal`) по
префиксу `/spa`, пока Blazor-UI продолжает работать в корне. Сборка:

```powershell
npm run build          # здесь: обновляет frontend/dist
dotnet build ..\TransactionJournal.sln
```

## Каркас

Раскладка — «тонкий топбар + сворачиваемая левая панель» с пятью разделами
(Конструкции, Входящие, Подсказки, Агент, Синхронизация) по концепции
[concept.md §2](../.wf-research/ui-concept/concept.md). Разделы пока
заглушки-роуты; единый конфиг разделов — `src/config/sections.ts`.
