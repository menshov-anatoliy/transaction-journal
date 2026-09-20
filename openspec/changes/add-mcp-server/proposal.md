# Proposal

## Why

Журнал рассчитан не только на просмотр глазами: сводные итоги, разборы конструкций и сквозной мета-анализ удобно вести в разговоре с ИИ-ассистентом. Контракт такого доступа зафиксирован гриллингом [#7](https://github.com/menshov-anatoliy/transaction-journal/issues/7) — шесть read-only tools, форматы ответов, аддитивная стабильность; техническая основа подтверждена исследованием [#3](https://github.com/menshov-anatoliy/transaction-journal/issues/3) — пакет `ModelContextProtocol` 2.2.0, stdio-сервер в том же exe, read-only читатель общей SQLite поверх WAL. Этот change переносит зафиксированный контракт в формальную спеку.

## What Changes

- Режим `--mcp` того же exe: раннее ветвление до создания веб-приложения, отдельный минимальный generic host со stdio-транспортом; режимы web и `--mcp` взаимоисключающие.
- Stdio-гигиена: stdout — только JSON-RPC, все логи в stderr; завершение по EOF stdin.
- Read-only доступ к той же SQLite-базе: соединения `Mode=ReadOnly` с пулом, параллельная работа с пишущим веб-приложением, без миграций в MCP-режиме.
- Шесть read-only tools: `list_constructions`, `get_construction`, `list_trades`, `get_positions`, `list_incoming`, `get_analytics`; только tools, без resources и prompts; мутации журнала остаются в UI.
- Пагинация `limit`/`offset` (дефолт 50, максимум 500), фильтры по применимости, фиксированная сортировка свежими первыми; в `get_construction` до 200 сделок с признаком `truncated` и `total_count`.
- Форматы ответов: поля — английские snake_case, пользовательский контент — русский как есть; итоги — 2 знака, количества и цены — 8; время — ISO 8601 UTC; пустая выборка — пустой массив.
- Нереализованный PnL — по маркам публичных тикеров на момент запроса с отметкой `marks_as_of`; при сбое марок — null только у нереализованной части.
- Ожидаемые ошибки — MCP-ошибки с сообщением на русском; непредвиденные сбои — generic-сообщение без деталей; контракт развивается только аддитивно.

## Capabilities

### New Capabilities

- `mcp/tools`: read-only MCP-доступ ИИ-ассистента к журналу — режим `--mcp` того же exe, шесть tools, параметры и форматы ответов, ошибки и стабильность контракта.

### Modified Capabilities

<!-- Изменяемых capability нет: границы с sync, domain, analytics и ui проходят по читающим сервисам и общей базе; их спеки не затрагиваются. -->

## Impact

- Новый приёмник читающего слоя: tools проекцируют `PositionReadModel` и «Входящие» домена (change `add-core-domain`) и метрики аналитики (change `add-analytics`); собственной расчётной логики в MCP-слое не появляется.
- Новые зависимости: NuGet `ModelContextProtocol` 2.2.0 и `Microsoft.Extensions.Hosting` (решение research-тикета [#3](https://github.com/menshov-anatoliy/transaction-journal/issues/3)); HTTP-транспорт и AspNetCore-пакет SDK не нужны.
- Регистрация у клиентов (VS Code, Claude Desktop, Cursor): `command` — путь к exe, `args` = `["--mcp"]`; конфиги документируются в задачах change.
- Глоссарий и ADR не меняются: MCP — инфраструктурный адаптер над сложившимися доменными понятиями.
