# Tasks

## 1. Sink трассировки

- [ ] 1.1 Пакеты OTel SDK; `TraceJsonlExporter : BaseExporter<Activity>` — плоская строка span-per-line (`ts/service/source/name/kind/trace_id/span_id/parent_span_id/duration_ms/status/status_desc/attrs/events`; `events` — `{ts, name, attrs}`; имена атрибутов оригинальные) поверх `BatchActivityExportProcessor`; сбой записи — диагностика без исключения наружу. Проверить: `requirement-trace-jsonl-sink`, `scenario-trace-jsonl-line-greppable`, `scenario-trace-jsonl-write-failure-degrades`.
- [ ] 1.2 Ротация по `MaxFileBytes` → `traces-{timestamp}.jsonl` с хранением `KeepFiles` архивов; запись продолжается в новый файл. Проверить: `scenario-trace-jsonl-rotation`.
- [ ] 1.3 Регистрация в composition root: источники `TransactionJournal.Chats`, `TransactionJournal.Hints`, `Experimental.Microsoft.Extensions.AI`; всегда включено, без сэмплирования. Проверить: `requirement-trace-otel-two-surfaces`.
- [ ] 1.4 Опция OTLP-экспорта (Phoenix): отдельный экспортёр по настройке приёмника, пусто — выключен. Проверить: `requirement-trace-phoenix-dev-option`, `scenario-trace-phoenix-off-by-default`.

## 2. Чат — спаны и захват

- [ ] 2.1 Корневой спан `chat_turn` (источник `TransactionJournal.Chats`) вокруг `ChatService.StreamAssistantAnswerAsync`: `tj.chat.iterations` (подсчёт запросов модели по механике `ChatAgent`), `tj.chat.id`, `tj.chat.message.id`; отмена — `tj.chat.cancelled=true`, статус не-ошибка. Проверить: `scenario-trace-chat-turn-root`, `scenario-trace-chat-cancel-not-error`.
- [ ] 2.2 `UseOpenTelemetry` в builder конвейера `ChatAgent` строго под `UseFunctionInvocation` — встроенные спаны `chat {model}`, `orchestrate_tools`, `execute_tool {name}` как есть, без дубликатов. Проверить: `scenario-trace-meai-spans-verbatim`.
- [ ] 2.3 Обёртка `AIFunction`: в момент исполнения событие `tj.tool.args` на спане `execute_tool` — JSON аргументов с капом 2 КБ и маркером усечения; `EnableSensitiveData` всегда `false`. Проверить: `requirement-trace-metadata-only`, `scenario-trace-no-prompt-completion-texts`, `scenario-trace-tool-args-capped`.
- [ ] 2.4 Фильтрация по уровню: `basic` без событий `tj.tool.args`, `detailed` с ними; дефолт `detailed`; отдельных событий итераций нет. Проверить: `requirement-trace-detail-levels`, `scenario-trace-basic-without-tool-args`, `scenario-trace-detailed-with-tool-args`.

## 3. Подсказки — спаны и корреляция

- [ ] 3.1 Корневой спан `hint_pass` (источник `TransactionJournal.Hints`) вокруг `HintAgentPass.RunAsync`: атрибуты исхода `HintPassOutcome` и счётчиков `HintPassResult`; дочерний спан на каждую фазу (снимок корпуса, гашение выведенных правил, снимок журнала, гашение закрытых субъектов, котировки, оценка триггеров); без `gen_ai.*`. Проверить: `scenario-trace-hint-pass-phases`.
- [ ] 3.2 Trace id в записи подсказки: `HintRecord` (+ порт) — nullable W3C TraceId корневого `hint_pass`; `HintEntity` + EF-миграция (`hints.trace_id`); заполнение в `HintStore.AddAsync`; гашение в `expired` id не получает. Проверить: `requirement-engine-trace-id-on-created-hints`, `scenario-engine-pass-single-trace-id`, `scenario-engine-expiry-no-trace-id`, `scenario-engine-hints-before-adoption-null`.

## 4. Корреляция в чатах и API

- [ ] 4.1 `messages.trace_id`: `ChatMessageDraft`/`ChatRecord` + порт и `ChatStore` (EnsureCreated); trace id корневого `chat_turn` пишется только на ответ помощника в момент фиксации; сообщения владельца — null. Проверить: `requirement-chat-trace-id-on-answer`, `scenario-chat-answer-carries-trace-id`, `scenario-chat-owner-message-no-trace-id`, `scenario-chat-cancelled-turn-trace-only`.
- [ ] 4.2 Контракт `GET .../chats/{chatId}/messages`: опциональное поле trace id сообщения; домен колонку не читает. Проверить: `scenario-chat-trace-id-not-domain`.
- [ ] 4.3 `POST /api/hints/pass`: опциональное поле trace id прохода в ответе. Проверить: `scenario-engine-pass-response-trace-id`.

## 5. Конфигурация

- [ ] 5.1 Секция `Tracing`: `Detail` (`basic|detailed`, дефолт `detailed`), `Dir` (дефолт `App_Data/traces`), `MaxFileBytes` (дефолт 10 МБ), `KeepFiles` (дефолт 5), настройка OTLP-приёмника; отсутствие секции — дефолты; смена без правки кода. Проверить: `requirement-tracing-config-section`, `scenario-tracing-config-defaults`, `scenario-tracing-config-switch-without-code`.

## 6. Сквозная приёмка

- [ ] 6.1 Тесты-сценарии спанов: ход чата (включая отмену), проход подсказок с фазами, встроенные спаны без дубликатов — на фейковом приёмнике ActivityListener. Проверить: `requirement-trace-otel-two-surfaces` (все сценарии).
- [ ] 6.2 Сквозной прогон на живом exe: ход чата и проход пишут `App_Data/traces/traces.jsonl`, отмена и ротация не ломают файл, trace id из записи (`messages.trace_id`, `hints.trace_id`) находит строки трассы grep'ом; `openspec validate add-agent-tracing --strict`.
