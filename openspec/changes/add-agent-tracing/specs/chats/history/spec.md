# Spec Delta

## ADDED Requirements

### Requirement: Ответ помощника несёт trace id операционной трассы

Ответ ИИ-помощника SHALL сохранять nullable trace id операционной трассы — W3C TraceId (32 hex) корневого спана `chat_turn` — в момент фиксации ответа; сообщение владельца SHALL оставаться без trace id ([#83](https://github.com/menshov-anatoliy/transaction-journal/issues/83)). Отменённый ход ответа не создаёт (существующее правило) — связь такого хода SHALL жить только на стороне трассы (атрибуты `tj.chat.id`/`tj.chat.message.id` корневого спана). Колонка `messages.trace_id` SHALL быть метаданными отладки: null у сообщений до внедрения, ни одно доменное правило её не читает; схема базы чатов разворачивается EnsureCreated. Контракт сообщения чата SHALL включать trace id как опциональное поле.

Traceability ID: requirement-chat-trace-id-on-answer

#### Scenario: Ответ получает trace id корневого спана

Traceability ID: scenario-chat-answer-carries-trace-id

- **WHEN** ход чата завершился и ответ помощника фиксируется
- **THEN** сообщение ответа сохраняет W3C TraceId корневого спана `chat_turn` этого хода

#### Scenario: Сообщение владельца без trace id

Traceability ID: scenario-chat-owner-message-no-trace-id

- **WHEN** фиксируется сообщение владельца
- **THEN** его trace id остаётся null

#### Scenario: Отменённый ход связывается на стороне трассы

Traceability ID: scenario-chat-cancelled-turn-trace-only

- **WHEN** владелец отменяет генерацию и запись ответа не создаётся
- **THEN** связь хода с чатом и вопросом восстанавливается по атрибутам `tj.chat.id`/`tj.chat.message.id` строки трассы

#### Scenario: Домен не читает колонку

Traceability ID: scenario-chat-trace-id-not-domain

- **WHEN** выполняются доменные правила журнала
- **THEN** ни одно из них не читает и не изменяет `messages.trace_id`
