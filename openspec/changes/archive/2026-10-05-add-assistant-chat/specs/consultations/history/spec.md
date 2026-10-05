# Spec Delta

## Purpose

Запись окружения «Консультация»: структура диалогов и сообщений, рыночный след, персистентность per construction и жизненный цикл вместе с конструкцией.

## ADDED Requirements

### Requirement: Консультация — контейнер диалогов «один вопрос — один диалог»

Консультация SHALL существовать в контексте одной конструкции как совокупность диалогов; диалог SHALL создаваться первым сообщением владельца и явно удаляться им целиком (полное удаление, без корзины); ассистент SHALL видеть только историю собственного диалога, но не соседних ([#47](https://github.com/menshov-anatoliy/transaction-journal/issues/47), [CONTEXT.md](../../../../../../CONTEXT.md)).

Traceability ID: requirement-history-dialogue-per-question

#### Scenario: Диалог создаётся первым сообщением
Traceability ID: scenario-history-created-by-first-message
- **WHEN** владелец отправляет первое сообщение нового вопроса
- **THEN** создаётся новый диалог внутри консультации конструкции

#### Scenario: Диалог удаляется целиком
Traceability ID: scenario-history-hard-delete-dialogue
- **WHEN** владелец удаляет диалог
- **THEN** удаляются все его сообщения без возможности восстановления

#### Scenario: Соседние диалоги изолированы
Traceability ID: scenario-history-dialogues-isolated
- **WHEN** ассистент отвечает в диалоге
- **THEN** ему видна история только этого диалога

### Requirement: Сообщение хранит роль, текст, as-of и рыночный след

Сообщение SHALL хранить роль автора, текст и отметку as-of; ответ ассистента SHALL дополнительно сохранять рыночный след — какие инструменты вызывались и с какими as-of их данные ([#47](https://github.com/menshov-anatoliy/transaction-journal/issues/47), [CONTEXT.md](../../../../../../CONTEXT.md)).

Traceability ID: requirement-history-message-composition

#### Scenario: Рыночный след сохраняется в ответе
Traceability ID: scenario-history-market-trace-persisted
- **WHEN** ответ ассистента использовал рыночные инструменты
- **THEN** в сообщении сохранены вызванные инструменты и as-of их данных

### Requirement: История — запись окружения per construction

История SHALL храниться в SQLite по одной базе на конструкцию через порт хранилища с адаптером в Infrastructure; домен SHALL о консультации не знать; пересбор конструкции SHALL стирать её консультации ([#45](https://github.com/menshov-anatoliy/transaction-journal/issues/45), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-history-environment-record

#### Scenario: Пересбор стирает консультации
Traceability ID: scenario-history-rebuild-wipes
- **WHEN** конструкция пересобирается из inbox
- **THEN** консультации этой конструкции удаляются вместе со старой записью

#### Scenario: Домен не знает о консультациях
Traceability ID: scenario-history-domain-agnostic
- **WHEN** проверяются зависимости доменного проекта
- **THEN** в них отсутствуют ссылки на проект консультаций и его типы

### Requirement: Компакция истории отсутствует

Диалог SHALL вестись полной историей без компакции и суммаризации; при выходе объёма за разумные пределы владелец SHALL начинать новый диалог с чистого листа ([#47](https://github.com/menshov-anatoliy/transaction-journal/issues/47)).

Traceability ID: requirement-history-no-compaction

#### Scenario: Новый диалог — чистый лист
Traceability ID: scenario-history-fresh-dialogue
- **WHEN** владелец начинает новый диалог
- **THEN** ассистент не получает истории предыдущих диалогов консультации
