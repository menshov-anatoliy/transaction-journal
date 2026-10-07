# Proposal

## Why

Карта «Концептуально новый frontend журнала сделок» ([#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50)) завершена: концепция интерфейса утверждена ([#54](https://github.com/menshov-anatoliy/transaction-journal/issues/54), [concept.md](../../.wf-research/ui-concept/concept.md)), дизайн принят ([#56](https://github.com/menshov-anatoliy/transaction-journal/issues/56), `design.pen`), домен чата агента специфицирован (change `add-agent-chat`), стек зафиксирован в [ADR-0010](../../../docs/adr/0010-frontend-spa-react-stack.md) по исследованию [#51](https://github.com/menshov-anatoliy/transaction-journal/issues/51). Осталось реализовать: этот change — декомпозиция переноса всей функциональности (карта переноса №1–29, [concept.md](../../.wf-research/ui-concept/concept.md) §12) и переключения журнала на новый SPA.

## What Changes

- Новый frontend-проект `frontend/`: React 19 + TypeScript на Vite, Tailwind CSS 4, shadcn/ui, react-router, TanStack Query; каркас «тонкий топбар + левая панель», светлая тема по референсу, единый слой форматирования (русские числительные, локальное время, деградация котировок).
- Новый HTTP API-слой в существующем .NET-приложении: единая точка входа для всех разделов SPA, OpenAPI-описание, SSE-стриминг чата; контракты эндпоинтов фиксируются инкрементально по разделам после реализации DDD-реструктуризации (карта [#19](https://github.com/menshov-anatoliy/transaction-journal/issues/19)).
- Общие UI-инструменты: MD-вьюер (react-markdown + remark-gfm) и модальный split-редактор (@uiw/react-md-editor, без WYSIWYG); индикатор финрезультата «полный/средний/компактный» по `design.pen` с юнит-тестами геометрии на эталонных кейсах C1–C7.
- Чат-инфраструктура: assistant-ui поверх адаптера SSE (ExternalStoreRuntime), параметры чата (модель, конструкция, источники) по домену `add-agent-chat`.
- Перенос разделов в порядке: Конструкции → Карточка конструкции → Агент (с корпусом правил) → Синхронизация → Входящие → Подсказки; мобильная адаптация одним responsive-контуром.
- Паритет-аудит по карте переноса №1–29, переключение на новый SPA, удаление Blazor-UI и старых per-construction баз консультаций (без миграции — решение [#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).

## Capabilities

### New Capabilities

- `http-api/transport`: транспортные принципы API нового SPA — единая версионированная точка входа JSON, публикуемое OpenAPI-описание, SSE-стриминг ответов ИИ-помощника. Эндпоинт-состав по разделам фиксируется в реализационных задачах, а не в этой capability.

## Impact

- `frontend/` — новый проект в решении (Node/TypeScript в сборке; бэкенд остаётся C#/.NET по [ADR-0003](../../../docs/adr/0003-stack-single-exe-dotnet-sqlite.md)); статика раздаётся .NET-хостом.
- `src/` — API-слой без изменения доменных правил; DDD-реструктуризация из [#19](https://github.com/menshov-anatoliy/transaction-journal/issues/19) идёт параллельно и определяет форму контрактов эндпоинтов.
- Blazor-UI удаляется только после закрытия паритет-аудита №1–29; экраны `/sync` (№26) и транзитная вкладка (№23) не переносятся по решению [#54](https://github.com/menshov-anatoliy/transaction-journal/issues/54).
- Настройки LLM-провайдера не выводятся в UI (только `appsettings.Local.json`) — решение [#54](https://github.com/menshov-anatoliy/transaction-journal/issues/54).
- Приёмка: юнит-тесты геометрии индикатора (кейсы C1–C7), паритет-чек-лист №1–29, живая приёмка владельцем по протоколу.
