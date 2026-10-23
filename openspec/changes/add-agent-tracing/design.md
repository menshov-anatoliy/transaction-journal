# Design

## Context

Карта [#79](https://github.com/menshov-anatoliy/transaction-journal/issues/79) и её решения: [#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82) (схема спанов и JSONL), [#83](https://github.com/menshov-anatoliy/transaction-journal/issues/83) (корреляция), [#86](https://github.com/menshov-anatoliy/transaction-journal/issues/86) (Phoenix); архитектурная фиксация — [ADR-0020](../../../docs/adr/0020-agent-harness-tracing.md). Факты research: [#80](https://github.com/menshov-anatoliy/transaction-journal/issues/80) — встроенная инструментация M.E.AI покрывает вызовы модели, стриминг и тулы, отдельного пакета нет; [#81](https://github.com/menshov-anatoliy/transaction-journal/issues/81) — готового file-экспортёра нет, рекомендован OTel SDK + `BaseExporter<Activity>` поверх `BatchActivityExportProcessor`; [#85](https://github.com/menshov-anatoliy/transaction-journal/issues/85) — Phoenix единственный одно-контейнерный приёмник с LLM-семантикой из `gen_ai.*`. Стек: .NET 9, single-exe + SQLite ([ADR-0003](../../../docs/adr/0003-stack-single-exe-dotnet-sqlite.md)); чат — конвейер `IChatClient` с `FunctionInvokingChatClient` (потолок 6 итераций) и стримингом ответа ([ADR-0009](../../../docs/adr/0009-consultation-chat-architecture.md)); проход подсказок детерминирован, без LLM ([ADR-0007](../../../docs/adr/0007-hints-agent-architecture.md)). Хранилища: база чатов — отдельная SQLite с `EnsureCreated` (без миграций), подсказки — таблица `Hints` базы журнала с EF-миграциями.

## Goals / Non-Goals

**Goals:**

- Единая операционная трасса обеих поверхностей: водопад хода чата и прохода подсказок с метриками модели.
- Локальный JSONL-файл — артефакт истины: grep-пригодный, самодостаточный по строке, ротируемый.
- Корреляция «доменная запись → строка трассы → вьюер» без переводников: W3C TraceId.
- Dev-опция просмотра Phoenix без обязательной инфраструктуры.

**Non-Goals:**

- UI просмотра трасс в приложении (`design.pen`) — отдельный effort.
- Агрегированные сводки/метрики по дням, моделям, тулам: метрики — per-trace.
- Полные дампы содержимого запросов/ответов модели; сэмплирование.
- Изменение доменных правил журнала: колонки trace id — метаданные отладки.

## Decisions

### D1. Двухслойная модель спанов чата

Встроенные спаны M.E.AI из источника `Experimental.Microsoft.Extensions.AI` используются как есть — `chat {model}`, `orchestrate_tools`, `execute_tool {name}` с метриками `gen_ai.*`; `UseOpenTelemetry` добавляется в builder конвейера `ChatAgent` строго под `UseFunctionInvocation`, чтобы спаны охватывали исполнение тулов. Свои корневые спаны — источник `TransactionJournal.Chats`: `chat_turn` вокруг `StreamAssistantAnswerAsync` с `tj.chat.iterations` (число запросов модели, подсчёт по механике `ChatAgent`), `tj.chat.cancelled=true` при отмене (статус — не-ошибка) и `tj.chat.id`/`tj.chat.message.id` (значения известны до старта стрима).

### D2. Проход подсказок — корневой `hint_pass` и дочерние фазы

Источник `TransactionJournal.Hints`: корневой `hint_pass` вокруг `HintAgentPass.RunAsync` с атрибутами исхода (`HintPassOutcome`) и счётчиков `HintPassResult`; дочерний спан на каждую фазу (снимок корпуса, гашение выведенных правил, снимок журнала, гашение закрытых субъектов, котировки, оценка триггеров) — задержка сетевого вызова котировок видна на водопаде отдельно. Детерминированный проход без LLM: никаких `gen_ai.*` на этих спанах.

### D3. JSONL-sink — OTel SDK + собственный экспортёр

`TraceJsonlExporter : BaseExporter<Activity>` пишет плоский span-per-line в файл через `BatchActivityExportProcessor`: строка `ts/service/source/name/kind/trace_id/span_id/parent_span_id/duration_ms/status/status_desc/attrs/events`, события — `{ts, name, attrs}`, `service` в каждой строке; имена атрибутов внутри `attrs` — оригинальные (`gen_ai.*`, `tj.*`). Файл `App_Data/traces/traces.jsonl`, ротация при 10 МБ → `traces-{timestamp}.jsonl`, хранится 5 архивов. Сбой записи деградирует в диагностику, наружу не бросается — трассировка не может ломать журнал. OTLP-экспорт (Phoenix) — отдельный OTLP-экспортёр, включаемый настройкой эндпоинта приёмника; пусто — выключен.

### D4. Metadata-only и компактные аргументы тулов

`EnableSensitiveData` всегда `false` — он пишет полные сообщения модели, что запрещено standing-решением карты. Аргументы тулов пишутся своей обёрткой над `AIFunction`: в момент исполнения на текущий встроенный спан `execute_tool` добавляется событие `tj.tool.args` — JSON аргументов, обрезанный до 2 КБ с маркером усечения.

### D5. Уровни детализации и конфигурация

Два уровня: `basic` (имена, длительности, статусы, метрики `gen_ai.*`, корневые спаны) и `detailed` (плюс события `tj.tool.args`); дефолт `detailed`. Отдельных событий итераций нет — задержки читаются из длительностей встроенных `chat {model}`. Секция `Tracing`: `Detail`, `Dir`, `MaxFileBytes`, `KeepFiles` (и ключ OTLP-эндпоинта для dev-опции); отсутствие секции — дефолты.

### D6. Корреляция — W3C TraceId без новых таблиц

Идентификатор — 32 hex `ActivityTraceId` корневого спана (`chat_turn`, `hint_pass`), тот же, что поле `trace_id` в JSONL и во вьюере. Точки записи: ответ помощника — колонка `messages.trace_id` в момент фиксации (`ChatMessageDraft` → база чатов EnsureCreated: колонка на новой базе, существующая пересоздаётся); созданные подсказки — колонка `hints.trace_id` (EF-миграция), все записи прохода несут один id, гашение в `expired` не получает. Обратная связь — атрибуты `tj.chat.id`/`tj.chat.message.id` на `chat_turn`, покрывающие отменённые ходы без записи-носителя. API: trace id в контракте сообщения чата и в ответе `POST /api/hints/pass`, поля опциональные. Отдельной таблицы проходов нет — журнал проходов это файл трасс.

### D7. Просмотр — Phoenix как именованная dev-опция

Arize Phoenix (1 контейнер, OTLP gRPC :4317 + HTTP :6006, LLM-семантика из `gen_ai.*` на приёме); запасная «нулевая» альтернатива — .NET Aspire Dashboard. Внешний backend — опция разработки, не обязанность: прод и истина (JSONL) от Phoenix не зависят. Живой прогон до внедрения — тикет [#87](https://github.com/menshov-anatoliy/transaction-journal/issues/87) карты.

## Risks / Trade-offs

- [Свой экспортёр — код в репо] → Десятки строк поверх стабильного `BaseExporter<Activity>`; тесты формата строки и ротации; готового пакета нет (research #81).
- [Зависимость `gen_ai.*`-семантики от версии M.E.AI] → Свои атрибуты только `tj.*`; обновления пакетов сопровождаются сверкой спанов.
- [Файл растёт] → Ротация по размеру с `KeepFiles`; `basic` отсекает подробный захват.
- [Phoenix ELv2] → Только внутреннее dev-использование; в проде файла достаточно.
- [Стриминг и трассировка] → Запись фоновая (batch-обработчик), горячий путь стрима не блокируется.

## Migration Plan

База чатов — EnsureCreated: колонка `messages.trace_id` появляется на новой базе, существующая пересоздаётся (store окружения, одноразовый по дизайну). Журнал — EF-миграция `hints.trace_id` (nullable, null у записей до внедрения). Трассировка всегда включена с дефолтами секции `Tracing`; откат настройки — переопределение секции (уровень `basic`, другой каталог, лимиты). Внешние компоненты не требуются: без настройки OTLP трассы живут только в JSONL.
