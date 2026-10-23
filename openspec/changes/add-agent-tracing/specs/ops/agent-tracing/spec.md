# Spec Delta

## ADDED Requirements

### Requirement: Обе агентские поверхности трассируются единым OTel-механизмом

Чат консультаций и проход подсказок SHALL трассироваться единым механизмом операционной трассы агента поверх OpenTelemetry-совместимых источников `TransactionJournal.Chats` и `TransactionJournal.Hints` ([ADR-0020](../../../../../../docs/adr/0020-agent-harness-tracing.md), [#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82)). Ход чата SHALL покрываться корневым спаном `chat_turn` — от вопроса владельца до конца стрима — с атрибутами `tj.chat.iterations` (число запросов модели в ходе), `tj.chat.id` и `tj.chat.message.id`; проход подсказок — корневым спаном `hint_pass` с исходом и счётчиками прохода и дочерним спаном на каждую фазу прохода. Собственные атрибуты SHALL использовать префикс `tj.*`; трассировка SHALL быть всегда включена, без сэмплирования.

Traceability ID: requirement-trace-otel-two-surfaces

#### Scenario: Ход чата — корневой спан chat_turn

Traceability ID: scenario-trace-chat-turn-root

- **WHEN** выполняется ход чата — от вопроса владельца до конца стрима ответа
- **THEN** трасса содержит корневой спан `chat_turn` с числом запросов модели (`tj.chat.iterations`) и связью с чатом и вопросом (`tj.chat.id`, `tj.chat.message.id`)

#### Scenario: Отменённый ход не ошибка

Traceability ID: scenario-trace-chat-cancel-not-error

- **WHEN** владелец отменяет генерацию ответа
- **THEN** спан `chat_turn` помечается `tj.chat.cancelled=true`
- **AND** статус спана остаётся не-ошибкой

#### Scenario: Фазы прохода видны на водопаде

Traceability ID: scenario-trace-hint-pass-phases

- **WHEN** выполняется проход подсказок
- **THEN** трасса содержит корневой спан `hint_pass` с исходом и счётчиками прохода и дочерние спаны фаз (снимок корпуса, гашения, снимок журнала, котировки, оценка триггеров)
- **AND** задержка сетевого вызова котировок видна отдельным спаном
- **AND** на спанах прохода нет атрибутов `gen_ai.*`

#### Scenario: Встроенные спаны используются как есть

Traceability ID: scenario-trace-meai-spans-verbatim

- **WHEN** ход чата вызывает модель или инструменты
- **THEN** трасса содержит встроенные спаны Microsoft.Extensions.AI (`chat {model}`, `orchestrate_tools`, `execute_tool {name}`) с метриками `gen_ai.*`
- **AND** собственные дубликаты этих спанов не создаются

### Requirement: Запись трассы — плоский JSONL span-per-line с ротацией

Трасса SHALL писаться в локальный ротируемый файл — плоский JSONL, одна строка = один спан ([#81](https://github.com/menshov-anatoliy/transaction-journal/issues/81), [#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82)): `ts`, `service`, `source`, `name`, `kind`, `trace_id`, `span_id`, `parent_span_id`, `duration_ms`, `status`, `status_desc`, `attrs` (имена атрибутов оригинальные — `gen_ai.*`, `tj.*`), `events` (массив `{ts, name, attrs}`). Строка SHALL быть самодостаточной для grep — `service` в каждой строке. Запись SHALL быть фоновой и не SHALL блокировать стриминг ответа. Файл по умолчанию `App_Data/traces/traces.jsonl`; ротация SHALL выполняться по размеру с хранением архива `traces-{timestamp}.jsonl`. Сбой записи SHALL деградировать в диагностику и не SHALL ломать работу агента.

Traceability ID: requirement-trace-jsonl-sink

#### Scenario: Строка самодостаточна для grep

Traceability ID: scenario-trace-jsonl-line-greppable

- **WHEN** владелец ищет трассу по trace id в файле
- **THEN** каждая строка с этим `trace_id` содержит все поля спана, включая `service`

#### Scenario: Ротация сохраняет архивы и продолжает запись

Traceability ID: scenario-trace-jsonl-rotation

- **WHEN** файл трасс достигает предельного размера
- **THEN** он переименовывается в `traces-{timestamp}.jsonl`, запись продолжается в новый файл
- **AND** хранится не более настроенного числа архивов

#### Scenario: Сбой записи не ломает агента

Traceability ID: scenario-trace-jsonl-write-failure-degrades

- **WHEN** запись строки трассы не удалась (файл занят, диск полон)
- **THEN** агент продолжает работать, сбой фиксируется в диагностике

### Requirement: Захват трассы metadata-only

Трассировка SHALL записывать только метаданные: метрики `gen_ai.*` (токены, задержки, model id), статусы, отмены и ошибки. Полные тексты prompt/completion SHALL не записываться ни на каком уровне — `EnableSensitiveData` SHALL всегда быть `false` ([#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82)). Аргументы тулов SHALL записываться компактно: событие `tj.tool.args` на спане `execute_tool` в момент исполнения — JSON аргументов, обрезанный до 2 КБ с маркером усечения.

Traceability ID: requirement-trace-metadata-only

#### Scenario: Полные тексты не пишутся

Traceability ID: scenario-trace-no-prompt-completion-texts

- **WHEN** записывается любая трасса на любом уровне детализации
- **THEN** в ней нет полного содержимого запросов и ответов модели

#### Scenario: Аргументы тулов обрезаются до 2 КБ

Traceability ID: scenario-trace-tool-args-capped

- **WHEN** вызов тула получает аргументы объёмом более 2 КБ
- **THEN** событие `tj.tool.args` несёт JSON, обрезанный до 2 КБ, с маркером усечения

### Requirement: Уровень детализации задаёт состав захвата

Трассировка SHALL поддерживать два уровня детализации: `basic` — имена, длительности, статусы, метрики `gen_ai.*`, корневые спаны; `detailed` — плюс события `tj.tool.args` ([#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82)). Уровень по умолчанию SHALL быть `detailed`. Отдельных событий итераций SHALL не быть — задержки итераций SHALL читаться из длительностей встроенных спанов `chat {model}`.

Traceability ID: requirement-trace-detail-levels

#### Scenario: Basic отсекает события аргументов

Traceability ID: scenario-trace-basic-without-tool-args

- **WHEN** уровень детализации `basic`
- **THEN** трасса содержит спаны, статусы и метрики `gen_ai.*`, но не содержит событий `tj.tool.args`

#### Scenario: Detailed добавляет события аргументов

Traceability ID: scenario-trace-detailed-with-tool-args

- **WHEN** уровень детализации `detailed` (в том числе по умолчанию)
- **THEN** вызовы тулов несут событие `tj.tool.args` на спане `execute_tool`

### Requirement: Просмотр трасс — Phoenix как dev-опция

Рекомендованная dev-опция просмотра операционных трасс SHALL быть Arize Phoenix — приёмник OTLP с LLM-семантикой из `gen_ai.*` ([#86](https://github.com/menshov-anatoliy/transaction-journal/issues/86), [ADR-0020](../../../../../../docs/adr/0020-agent-harness-tracing.md)). Экспорт в Phoenix SHALL включаться конфигурацией и SHALL быть по умолчанию выключен; артефакт истины — локальный JSONL-файл — SHALL не зависеть от Phoenix. Запасная «нулевая» альтернатива — .NET Aspire Dashboard.

Traceability ID: requirement-trace-phoenix-dev-option

#### Scenario: Без настройки трассы живут только в JSONL

Traceability ID: scenario-trace-phoenix-off-by-default

- **WHEN** приёмник OTLP в конфигурации не задан
- **THEN** трассы пишутся только в локальный JSONL-файл, внешних зависимостей нет

#### Scenario: С настройкой трейсы читаются в Phoenix

Traceability ID: scenario-trace-phoenix-receives-otlp

- **WHEN** приёмник OTLP задан и Phoenix запущен
- **THEN** трейсы доставляются по OTLP и читаются в Phoenix с LLM-семантикой (модель, токены, вызовы тулов из `gen_ai.*`)
