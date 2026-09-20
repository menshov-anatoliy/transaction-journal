# Spec Delta

## Purpose

Capability MCP-доступа к журналу для ИИ-ассистента: режим `--mcp` того же exe поднимает read-only MCP-сервер поверх общей SQLite-базы (stdio-транспорт), шесть tools покрывают обзор журнала, разбор конструкции и сквозной мета-анализ, форматы ответов стабильны для машинного разбора. Журнал через MCP не мутируется; регистрация клиентов — конфиг `command`/`args=["--mcp"]`.

## ADDED Requirements

### Requirement: Режим `--mcp` запускает MCP-сервер того же exe
Система SHALL запускать MCP-сервер ранним ветвлением по аргументу `--mcp` до создания веб-приложения: в MCP-ветке строится отдельный минимальный generic host со stdio-транспортом, веб-приложение не инициализируется вовсе. Режимы web и `--mcp` SHALL быть взаимоисключающими в одном процессе.

Traceability ID: requirement-mcp-mode-same-exe

#### Scenario: Аргумент `--mcp` поднимает сервер вместо веб-приложения
Traceability ID: scenario-dash-mcp-runs-server-not-web
- **WHEN** exe запущен с аргументом `--mcp`
- **THEN** процесс обслуживает MCP-протокол по stdio
- **AND** веб-сервер, Blazor и HTTP-конфигурация не инициализируются

#### Scenario: Без `--mcp` процесс остаётся веб-приложением
Traceability ID: scenario-no-flag-keeps-web
- **WHEN** exe запущен без аргумента `--mcp`
- **THEN** работает веб-приложение журнала, stdio-транспорт не занимается

### Requirement: Stdio-гигиена — stdout только для JSON-RPC, завершение по EOF
MCP-режим SHALL держать stdout исключительно для JSON-RPC-сообщений протокола; все логи и диагностика SHALL направляться в stderr. Завершение stdin (EOF) SHALL приводить к корректной остановке сервера без фоновых процессов, сопротивляющихся остановке.

Traceability ID: requirement-stdio-hygiene

#### Scenario: Логи не попадают в stdout
Traceability ID: scenario-logs-stay-in-stderr
- **WHEN** MCP-сервер логирует свою работу
- **THEN** записи появляются только в stderr
- **AND** stdout содержит только JSON-RPC-сообщения

#### Scenario: EOF stdin останавливает сервер
Traceability ID: scenario-eof-stops-server
- **WHEN** MCP-клиент закрывает stdin процесса
- **THEN** сервер корректно завершает работу в пределах таймаута клиента

### Requirement: Доступ к общей SQLite — только на чтение
MCP-режим SHALL читать ту же SQLite-базу, что и веб-приложение, соединениями в read-only режиме, работая параллельно пишущему веб-приложению благодаря WAL. Миграции и создание схемы в MCP-режиме SHALL быть исключены; отсутствие файла базы SHALL возвращать понятную ошибку, а не аварийное завершение процесса.

Traceability ID: requirement-readonly-shared-sqlite

#### Scenario: Читатель работает параллельно пишущему веб-приложению
Traceability ID: scenario-parallel-reader-with-web
- **WHEN** веб-приложение пишет в базу при работающем MCP-сервере
- **THEN** MCP-запросы читают данные без блокировок и ошибок занятости

#### Scenario: MCP-режим не создаёт и не мигрирует базу
Traceability ID: scenario-no-migrations-in-mcp-mode
- **WHEN** exe запущен в режиме `--mcp`
- **THEN** соединения открываются в read-only режиме
- **AND** миграции и изменения схемы не выполняются

#### Scenario: Отсутствующий файл базы — понятная ошибка
Traceability ID: scenario-missing-db-clear-error
- **WHEN** файла базы данных нет при запросе tool
- **THEN** клиент получает ошибку MCP с сообщением на русском
- **AND** процесс не завершается аварийно

### Requirement: Шесть read-only tools — весь контракт
Контракт SHALL состоять из шести tools: `list_constructions` — список конструкций со сводкой результата, `get_construction` — детализация с позициями, сделками и внешними корректировками PnL, `list_trades` — плоский журнал сделок с фильтрами, `get_positions` — открытые позиции с нереализованной оценкой, `list_incoming` — сделки «Входящих» для триажа, `get_analytics` — агрегаты журнала. Ресурсы и prompts SHALL отсутствовать. Все tools SHALL быть строго read-only: мутации журнала (привязка сделок, корректировки, пометки) остаются операциями UI.

Traceability ID: requirement-six-readonly-tools

#### Scenario: Все шесть tools зарегистрированы
Traceability ID: scenario-all-six-tools-registered
- **WHEN** клиент запрашивает список tools
- **THEN** доступны ровно `list_constructions`, `get_construction`, `list_trades`, `get_positions`, `list_incoming`, `get_analytics`
- **AND** имена tools — английские snake_case, описания — на русском

#### Scenario: Только tools, без resources и prompts
Traceability ID: scenario-tools-only
- **WHEN** клиент запрашивает ресурсы или prompts
- **THEN** контракт не содержит ничего, кроме шести tools

#### Scenario: Мутации журнала через MCP недоступны
Traceability ID: scenario-no-mutations-via-mcp
- **WHEN** ИИ-ассистент работает с журналом через MCP
- **THEN** доступны только чтение и агрегация
- **AND** привязка сделок из «Входящих» и правки остаются операциями UI

### Requirement: Пагинация и фильтры списковых tools
Списковые tools (`list_constructions`, `list_trades`, `list_incoming`) SHALL принимать `limit`/`offset` с дефолтом 50 и максимумом 500 и фильтры по применимости (`construction_id`, `date_from`/`date_to`, `instrument`, `status`); `get_positions` SHALL принимать фильтр `instrument`. Сортировка SHALL быть фиксированной — свежие записи первыми, параметра сортировки нет. `get_construction` SHALL включать до 200 свежих сделок; при превышении — признак `truncated: true` и `total_count`, более старые сделки догружаются `list_trades` со смещением.

Traceability ID: requirement-list-pagination-and-filters

#### Scenario: Дефолт и максимум пагинации
Traceability ID: scenario-limit-default-and-max
- **WHEN** списковый tool вызван без `limit` или с `limit` больше 500
- **THEN** возвращается 50 записей или максимум 500 соответственно

#### Scenario: Фильтры сужают выборку
Traceability ID: scenario-filters-narrow-results
- **WHEN** `list_trades` вызван с `construction_id` и диапазоном дат
- **THEN** возвращаются только сделки этой конструкции в заданном диапазоне

#### Scenario: Переполнение сделок в get_construction помечается
Traceability ID: scenario-trades-truncation-flagged
- **WHEN** у конструкции больше 200 сделок
- **THEN** `get_construction` возвращает 200 свежих с `truncated: true` и `total_count`
- **AND** более старые сделки догружаются через `list_trades` со смещением

### Requirement: Формат ответов для машинного разбора
Поля ответов SHALL быть английскими snake_case; пользовательский контент (названия конструкций, комментарии) — на русском как есть. Деньги и количества SHALL передаваться JSON-числами: итоги с точностью 2 знака, количества и цены — 8 знаков. Время SHALL передаваться в ISO 8601 UTC. Пустая выборка SHALL возвращаться пустым массивом, а не null и не ошибкой.

Traceability ID: requirement-response-format

#### Scenario: Поля английские, контент русский
Traceability ID: scenario-english-fields-russian-content
- **WHEN** tool возвращает конструкцию с русским названием и комментарием
- **THEN** имена полей — английские snake_case
- **AND** значения пользовательского контента передаются на русском без изменений

#### Scenario: Числа с фиксируемой точностью
Traceability ID: scenario-number-precision
- **WHEN** tool возвращает суммы PnL и количества
- **THEN** итоги округлены до 2 знаков, количества и цены — до 8

#### Scenario: Время в ISO 8601 UTC
Traceability ID: scenario-iso8601-utc-time
- **WHEN** tool возвращает моменты времени
- **THEN** они сериализуются в ISO 8601 UTC

#### Scenario: Пустая выборка — пустой массив
Traceability ID: scenario-empty-list-empty-array
- **WHEN** фильтры не находят ни одной записи
- **THEN** возвращается пустой массив
- **AND** это не считается ошибкой

### Requirement: Нереализованный PnL — по маркам на момент запроса
Tools, возвращающие нереализованный PnL (`list_constructions`, `get_construction`, `get_positions`, `get_analytics`), SHALL оценивать его по маркам публичных тикеров на момент запроса и сопровождать оценку отметкой `marks_as_of`. При недоступности марок нереализованная часть SHALL возвращаться как null, остальной ответ SHALL оставаться валидным.

Traceability ID: requirement-unrealized-marks-at-request

#### Scenario: Оценка сопровождается временем марок
Traceability ID: scenario-marks-accompanied-by-asof
- **WHEN** tool возвращает нереализованный PnL открытого остатка
- **THEN** оценка соответствует маркам на момент запроса
- **AND** рядом передаётся `marks_as_of`

#### Scenario: Сбой марок обнуляет только нереализованную часть
Traceability ID: scenario-mark-failure-nulls-unrealized-only
- **WHEN** публичные тикеры недоступны в момент запроса
- **THEN** `unrealized_pnl` и `marks_as_of` равны null
- **AND** реализованные метрики, комиссии, даты и проценты возвращаются без изменений

### Requirement: Ожидаемые ошибки — MCP-ошибки на русском
Обращение к несуществующему объекту (например, неизвестный `construction_id`) SHALL возвращать ошибку MCP с человекочитаемым сообщением на русском. Непредвиденные сбои SHALL скрываться за generic-сообщением без утечки внутренних деталей.

Traceability ID: requirement-expected-errors-in-russian

#### Scenario: Неизвестный construction_id — ошибка MCP
Traceability ID: scenario-unknown-construction-id-error
- **WHEN** `get_construction` вызван с несуществующим `construction_id`
- **THEN** клиент получает `IsError = true` с сообщением на русском

#### Scenario: Баг не раскрывает деталей
Traceability ID: scenario-bug-generic-message
- **WHEN** внутри tool возникает непредвиденное исключение
- **THEN** клиент получает generic-сообщение об ошибке без стека и внутренних деталей

### Requirement: Контракт развивается только аддитивно
Изменения контракта SHALL быть аддитивными: допускаются новые tools и новые поля ответов; семантика и имена существующих не меняются, существующие tools и поля не удаляются. Версионных префиксов в именах нет.

Traceability ID: requirement-additive-contract-stability

#### Scenario: Новое поле не ломает клиента
Traceability ID: scenario-new-field-additive
- **WHEN** в ответ tool добавляется новое поле
- **THEN** клиенты, собранные под прежнюю версию контракта, продолжают работать без изменений
