# Spec Delta

## Purpose

Удаление capability `consultations/context`: снимок контекста и инструкции консультации замещены контекстом чата агента (`chats/context`).

## REMOVED Requirements

### Requirement: Снимок контекста собирается детерминированно кодом

Перед сообщением владельца SHALL собираться снимок контекста в markdown с явным as-of: конструкция с позициями и результатами, портфельные агрегаты и лимиты, компактный индекс корпуса правил (каждая карточка: id и краткое содержание). Снимок SHALL собираться кодом поверх read-моделей журнала без участия LLM; живые подсказки движка в контекст SHALL не входить; рыночные данные — только через инструменты в момент сообщения ([#45](https://github.com/menshov-anatoliy/transaction-journal/issues/45), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-context-deterministic-snapshot

**Reason**: Консультация заменена сущностью «Чат агента»: конструкция стала опциональной привязкой, поэтому снимок обязан существовать и без раздела конструкции ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-context-deterministic-snapshot`; сценарий портфельного снимка добавлен, остальные перенесены.

### Requirement: Инструкции агента — редактируемый файл со встроенным дефолтом

Поведение ассистента SHALL задаваться инструкциями из редактируемого файла `consultation-prompt.md` (отдельный настраиваемый путь, по умолчанию рядом с `rules/`), переопределяющими встроенный минимальный дефолт; отсутствие или битость файла SHALL не ломать чат — работают дефолтные инструкции ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md), паттерн [ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-context-agent-instructions-file

**Reason**: Термин «ассистент консультации» заменён «ИИ-помощником» чата агента; файл инструкций переименован вслед за словарём ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-context-agent-instructions-file` (файл `agent-prompt.md`, сценарии сохранены).

### Requirement: Ответы — сценарии «если/то» с опорой на корпус

Инструкции SHALL обязывать ассистента: формулировать планы действий «если/то» по свершившимся фактам с порогами — без направленных прогнозов цены; варианты в рамках корпуса сопровождать цитированием id карточек правил; варианты вне корпуса явно маркировать «вне корпуса правил»; план управления выдавать markdown-текстом в ответе, без сохранения ([#45](https://github.com/menshov-anatoliy/transaction-journal/issues/45)).

Traceability ID: requirement-context-scenario-conduct

**Reason**: Правило поведения не менялось, но носитель (инструкции консультации) удалён вместе с capability ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-context-scenario-conduct` — перенесено без изменений смысла.

### Requirement: Закрытая конструкция консультируется в пост-мортем

Для закрытой конструкции снимок SHALL собираться по финальному состоянию журнала; режим SHALL задаваться инструкциями как пост-мортем («работа над ошибками»); состав инструментов SHALL быть идентичен управленческому режиму — рыночные данные доступны ([#45](https://github.com/menshov-anatoliy/transaction-journal/issues/45)).

Traceability ID: requirement-context-postmortem-mode

**Reason**: Пост-мортем переносится в чат агента вместе со всей capability ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-context-postmortem-mode`.
