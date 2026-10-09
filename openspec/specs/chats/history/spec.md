# chats/history Specification

## Purpose

Сущность «Чат агента» как запись окружения: плоская история сообщений, параметры и жизненный цикл, единое хранилище и стирание пересбором только привязанных чатов.

## Requirements

### Requirement: Чат агента — плоская полная история обмена

Чат агента SHALL создаваться первым сообщением владельца с параметрами — ИИ-модель, опциональная привязка к конструкции, набор источников данных — и вестись единой полной историей сообщений без вложенных диалогов; ИИ-помощник SHALL видеть всю историю своего чата; истории соседних чатов SHALL оставаться ему невидимыми ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [CONTEXT.md](../../../../../../CONTEXT.md)).

Traceability ID: requirement-chat-flat-full-history

#### Scenario: Чат создаётся первым сообщением
Traceability ID: scenario-chat-created-by-first-message
- **WHEN** владелец отправляет первое сообщение нового чата
- **THEN** создаётся чат с выбранными параметрами: модель, привязка (или её отсутствие), источники

#### Scenario: Соседние чаты изолированы
Traceability ID: scenario-chat-neighbour-isolation
- **WHEN** ИИ-помощник отвечает в чате
- **THEN** ему видна история только этого чата

### Requirement: Привязка чата к конструкции опциональна и неизменяема

Чат SHALL существовать как с привязкой к конструкции, так и без неё; привязка SHALL фиксироваться при создании чата и SHALL не меняться после; смена контекста конструкции SHALL означать начало нового чата ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), карта [#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50)).

Traceability ID: requirement-chat-construction-binding-immutable

#### Scenario: Чат без конструкции существует
Traceability ID: scenario-chat-without-construction-allowed
- **WHEN** владелец создаёт чат без выбора конструкции
- **THEN** чат живёт и консультируется на портфельном уровне журнала

#### Scenario: Перепривязка невозможна
Traceability ID: scenario-chat-binding-cannot-change
- **WHEN** владелец пытается сменить конструкцию существующего чата
- **THEN** привязка остаётся прежней
- **AND** сменить контекст можно только создав новый чат

### Requirement: Завершение и удаление — ручные действия владельца

Чат SHALL быть активен или завершён; завершение SHALL быть только ручным действием владельца и SHALL скрывать чат в список завершённых; продолжление завершённого чата SHALL возвращать его в активные; автоматическое завершение SHALL отсутствовать. Удаление чата SHALL быть явным действием владельца и удалять чат целиком со всеми сообщениями, без корзины ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), карта [#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50), [CONTEXT.md](../../../../../../CONTEXT.md) «Завершение чата»).

Traceability ID: requirement-chat-manual-completion-and-deletion

#### Scenario: Завершение скрывает чат в список завершённых
Traceability ID: scenario-chat-completion-hides-to-completed-list
- **WHEN** владелец завершает активный чат
- **THEN** чат исчезает из активных и появляется в списке завершённых
- **AND** его история сохраняется

#### Scenario: Продолжение возвращает чат в активные
Traceability ID: scenario-chat-resume-returns-to-active
- **WHEN** владелец отправляет сообщение в завершённый чат
- **THEN** чат возвращается в активные

#### Scenario: Удаление целиком без корзины
Traceability ID: scenario-chat-hard-delete
- **WHEN** владелец удаляет чат
- **THEN** удаляются чат и все его сообщения без возможности восстановления

### Requirement: Сообщение чата хранит роль, текст, as-of и след источников

Сообщение SHALL хранить роль автора, текст и отметку as-of; ответ ИИ-помощника SHALL дополнительно сохранять след источников — использованные источники и инструменты, ссылки на карточки правил и данные журнала с их отметками as-of ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [CONTEXT.md](../../../../../../CONTEXT.md)).

Traceability ID: requirement-chat-message-composition

#### Scenario: След источников сохраняется в ответе
Traceability ID: scenario-chat-source-trace-persisted
- **WHEN** ответ ИИ-помощника использовал источники данных
- **THEN** в сообщении сохранены вызванные инструменты и ссылки на карточки правил и данные журнала с их as-of

### Requirement: Чат — запись окружения с жизненным циклом, привязанным к конструкции

Чаты SHALL храниться в SQLite через порт хранилища с адаптером в Infrastructure; домен SHALL о чатах не знать. Пересбор SHALL стирать чаты, привязанные к стираемым конструкциям, вместе с ними; чаты без привязки SHALL пересбор переживать ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-chat-environment-record

#### Scenario: Пересбор стирает привязанные чаты
Traceability ID: scenario-chat-rebuild-wipes-bound-chats
- **WHEN** конструкция пересобирается из inbox
- **THEN** чаты, привязанные к ней, удаляются вместе со старой записью

#### Scenario: Непривязанный чат переживает пересбор
Traceability ID: scenario-chat-unbound-chat-survives-rebuild
- **WHEN** выполняется полный пересбор журнала
- **THEN** чаты без привязки к конструкции сохраняются

#### Scenario: Домен не знает о чатах
Traceability ID: scenario-chat-domain-agnostic
- **WHEN** проверяются зависимости доменного проекта
- **THEN** в них отсутствуют ссылки на проект чатов и его типы

### Requirement: Компакция истории отсутствует

Чат SHALL вестись полной историей без компакции и суммаризации; при выходе объёма за разумные пределы владелец SHALL начинать новый чат с чистого листа ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).

Traceability ID: requirement-chat-no-compaction

#### Scenario: Новый чат — чистый лист
Traceability ID: scenario-chat-fresh-chat-clean-slate
- **WHEN** владелец начинает новый чат
- **THEN** ИИ-помощник не получает истории предыдущих чатов
