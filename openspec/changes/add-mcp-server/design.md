# Design

## Context

Стек и архитектура зафиксированы: C#/.NET, EF Core + SQLite (WAL), Blazor Server, локальный инструмент одного пользователя; всё производное пересчитывается при чтении (CONTEXT.md, карта [#1](https://github.com/menshov-anatoliy/transaction-journal/issues/1)). Исследование [#3](https://github.com/menshov-anatoliy/transaction-journal/issues/3) подтвердило: пакет `ModelContextProtocol` 2.2.0 GA со stdio-транспортом; паттерн `--mcp` — раннее ветвление до `WebApplication.CreateBuilder` на `Host.CreateApplicationBuilder`; read-only читатель той же базы работает параллельно пишущему веб-приложению благодаря WAL (`docs/research/mcp-csharp-sdk.md`, раздел «Выводы для спеки MCP», 11 пунктов). Контракт tools зафиксирован гриллингом [#7](https://github.com/menshov-anatoliy/transaction-journal/issues/7). Change `add-core-domain` ([#9](https://github.com/menshov-anatoliy/transaction-journal/issues/9)) даёт `PositionReadModel`, «Входящие» и корректировки; `add-analytics` ([#10](https://github.com/menshov-anatoliy/transaction-journal/issues/10)) — читающий слой метрик с провайдером марок и семантикой деградации; MCP-слой проецирует их, не дублируя расчётов.

## Goals / Non-Goals

**Goals:**

- Один exe: `--mcp` поднимает stdio-сервер MCP, без `--mcp` — веб-приложение; взаимоисключающие режимы.
- Read-only доступ к общей SQLite параллельно веб-приложению, без миграций в MCP-режиме.
- Шесть tools как тонкая проекция читающих сервисов домена и аналитики.
- Стабильные машинно-читаемые форматы: snake_case, точность чисел, ISO 8601 UTC, предсказуемые ошибки на русском.
- Аддитивная эволюция контракта без версионирования.

**Non-Goals:**

- Мутации через MCP: привязка сделок, корректировки, пометки — только UI (решение [#7](https://github.com/menshov-anatoliy/transaction-journal/issues/7)).
- Resources и prompts — исключены; при необходимости добавляются аддитивно позже.
- Tool автоподсказок привязки сделок из «Входящих» — туман карты.
- Альтернативные транспорты (SSE, HTTP) — stdio достаточно; мультипользовательность и торговые операции — вне скоупа усилия.
- Собственная расчётная логика в MCP-слое — PnL и метрики берутся из `add-analytics`.

## Decisions

### D1. Один exe, раннее ветвление `--mcp`

Проверка `args.Contains("--mcp")` — первым делом в точке входа, до `WebApplication.CreateBuilder`; в MCP-ветке — отдельный минимальный generic host (`Host.CreateApplicationBuilder`), `WebApplication` не строится вовсе. `StdioServerTransport` монополизирует stdout, поэтому режимы web и `--mcp` взаимоисключающие. Альтернатива — отдельный exe для MCP: отклонена, два артефакта сборки и две версии на машине ради редкого режима (прецеденты сообщества: `Azure.GeneratorAgent --mcp-server`, `PaperTodo --mcp`).

### D2. Каркас сервера — ModelContextProtocol 2.2.0, логи в stderr, завершение по EOF

`builder.Services.AddMcpServer().WithStdioServerTransport()` + регистрация tools; все логи — только в stderr (`LogToStandardErrorThreshold = LogLevel.Trace`), страховка `Console.SetOut(Console.Error)` от случайных записей в stdout. Завершение по EOF stdin SDK делает сам (`SingleSessionMcpServerHostedService` → `StopApplication()`); фоновых сервисов, сопротивляющихся остановке, в MCP-ветке нет — клиент ждёт остановки 5 с.

### D3. Read-only соединения поверх WAL

`SqliteConnectionStringBuilder { DataSource, Mode = SqliteOpenMode.ReadOnly, Pooling = true }`, без `Cache=Shared`; миграции и `EnsureCreated` — только в веб-режиме. База EF Core уже в WAL, поэтому read-only читатель работает параллельно пишущему веб-приложению без `SQLITE_BUSY`; busy-таймаут встроен (`Default Timeout`). Поведение при отсутствии файла базы — открытый вопрос research: закрываем решением «понятная McpException на русском вместо краша» (сценарий в спеке).

### D4. Tools — тонкая проекция читающих сервисов

Тулы — статические классы `[McpServerToolType]` с методами `[McpServerTool]` и `[Description]` на русском; read-only сервисы (доступ к данным, метрики аналитики, провайдер марок) разрешаются через DI-параметры метода и не попадают в JSON-схему. Никакой собственной логики расчёта: `get_analytics` и нереализованные оценки делегируют читающему слою `add-analytics`, конструкции и сделки — read-моделям `add-core-domain`. Форматирование (snake_case, точность, ISO 8601 UTC) — на DTO-границе tools.

### D5. Structured output с пагинацией

Машинно-читаемые выборки возвращаются DTO с `UseStructuredContent = true` (не-объектные значения отдаются как есть, без обёртки `result`); текстовый возврат `Task<string>` остаётся для описательных ответов, если появится. Все списковые выборки пагинированы (`limit`/`offset` 50/500), детали конструкции ограничены 200 сделками с `truncated`/`total_count` — большие выборки не расширяют окно ответа (вывод research №5).

### D6. Ошибки и стабильность контракта

Ожидаемые ошибки — `throw new McpException("сообщение на русском")` → `IsError = true`; баги — обычные исключения, SDK отдаёт generic-сообщение без утечки деталей. Эволюция — только аддитивная (решение [#7](https://github.com/menshov-anatoliy/transaction-journal/issues/7)): новые tools и поля допустимы, удаления и смены семантики — нет; версионных префиксов в именах нет.

## Risks / Trade-offs

- [Несколько параллельных MCP-процессов от разных клиентов] → каждый процесс read-only по WAL; корректность данных сохраняется, расходы — по одному процессу на клиентскую сессию; наблюдаемо, но не опасно (открытый вопрос research закрыт конфигурацией соединений).
- [Генерация схем для enum/DateTime/naming] → открытый вопрос research; проверяется экспериментально в задаче 1.1, при расхождении — строковые параметры и ручная сериализация DTO.
- [Аддитивность держится дисциплиной] → автоматического валидатора нет; контроль на ревью изменений контракта, требование зафиксировано в спеке.
- [Отсутствие базы при первом запуске MCP раньше веб-режима] → сценарий «отсутствующий файл базы» возвращает понятную ошибку; создание схемы остаётся ответственностью веб-режима.

## Migration Plan

Greenfield: новых хранилищ нет — MCP-слой читает существующие таблицы домена, синка и кэш марок. Единственные новые зависимости — NuGet-пакеты `ModelContextProtocol` 2.2.0 и `Microsoft.Extensions.Hosting`. Порядок реализации: после `add-core-domain` и `add-analytics` (tools проецируют их сервисы); от `add-ui-screens` не зависит. Данных нет, отката не требуется.
