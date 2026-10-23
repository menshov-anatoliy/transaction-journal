# Proposal

## Why

Агентский харнес журнала — чат консультаций ([ADR-0009](../../../docs/adr/0009-consultation-chat-architecture.md)) и проход подсказок ([ADR-0007](../../../docs/adr/0007-hints-agent-architecture.md)) — работает как чёрный ящик: число итераций тулов, задержки фаз и вызовов модели, отмены хода и исходы проходов видны только косвенно. Решения карты [#79](https://github.com/menshov-anatoliy/transaction-journal/issues/79) зафиксированы: единый OTel-механизм трассировки обеих поверхностей с локальным JSONL-sink ([#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82)), корреляция трасс с доменными записями через W3C TraceId ([#83](https://github.com/menshov-anatoliy/transaction-journal/issues/83)), dev-опция просмотра — Arize Phoenix ([#86](https://github.com/menshov-anatoliy/transaction-journal/issues/86)); архитектурное решение — [ADR-0020](../../../docs/adr/0020-agent-harness-tracing.md).

## What Changes

- Единый механизм операционной трассы агента поверх `ActivitySource` (`TransactionJournal.Chats`, `TransactionJournal.Hints`): корневые спаны `chat_turn` и `hint_pass` (у прохода — с дочерними спанами фаз), встроенные спаны Microsoft.Extensions.AI (`chat {model}`, `orchestrate_tools`, `execute_tool {name}`) используются как есть, без дубликатов.
- Локальный sink — OTel SDK + собственный JSONL-экспортёр: плоский span-per-line в `App_Data/traces/traces.jsonl`, ротация по размеру 10 МБ с хранением 5 архивов, фоновая запись без задевания стриминга.
- Metadata-only: полные тексты prompt/completion не пишутся никогда (`EnableSensitiveData` всегда `false`); аргументы тулов — событие `tj.tool.args` на спане `execute_tool` (JSON, кап 2 КБ с маркером усечения), только на уровне `detailed`.
- Уровни детализации `basic|detailed` (дефолт `detailed`) из секции `Tracing` (`Detail`/`Dir`/`MaxFileBytes`/`KeepFiles`); трассировка всегда включена, сэмплирования нет.
- Корреляция: W3C TraceId корневого спана в nullable `messages.trace_id` (ответ помощника; база чатов EnsureCreated) и `hints.trace_id` (EF-миграция журнала); обратная связь `tj.chat.id`/`tj.chat.message.id` на `chat_turn`; trace id в контракте сообщения чата и в ответе `POST /api/hints/pass`; доменные правила колонки не читают.
- Dev-опция просмотра трасс — Arize Phoenix по OTLP, включается конфигурацией, по умолчанию выключена; артефакт истины — JSONL-файл.

## Capabilities

### New Capabilities

- `ops/agent-tracing`: единый механизм операционной трассы — модель спанов обеих поверхностей, плоский JSONL-формат с ротацией, metadata-only захват, уровни детализации, dev-опция просмотра Phoenix.
- `config/tracing`: секция `Tracing` — `Detail`, `Dir`, `MaxFileBytes`, `KeepFiles`, дефолты и смена параметров без правки кода.

### Modified Capabilities

- `chats/history`: ответ помощника несёт nullable trace id операционной трассы; контракт сообщения чата расширяется опциональным полем.
- `hints/engine-pass`: созданные подсказки несут trace id прохода; `POST /api/hints/pass` возвращает trace id прохода.

## Impact

- `TransactionJournal.Chats`: спан `chat_turn` вокруг `ChatService.StreamAssistantAnswerAsync`, `UseOpenTelemetry` в конвейере `ChatAgent` (строго под `UseFunctionInvocation`), обёртка тулов для события `tj.tool.args`, расширение `ChatMessageDraft`/порта хранилища trace id.
- `TransactionJournal.Hints`: спаны `hint_pass` и фаз в `HintAgentPass.RunAsync`, расширение `HintRecord` trace id.
- `TransactionJournal.Infrastructure`: JSONL-экспортёр и регистрация OTel SDK, колонка `messages.trace_id` в `ChatStore` (EnsureCreated), `hints.trace_id` в `HintEntity` + EF-миграция.
- `TransactionJournal` (Web): секция `Tracing` конфигурации, опция OTLP-экспорта (Phoenix), поля trace id в контракте `GET .../chats/{chatId}/messages` и `POST /api/hints/pass`.
- Визуальный слой не меняется (`design.pen` не затрагивается): trace id — данные отладки, UI просмотра трасс — вне карты.
