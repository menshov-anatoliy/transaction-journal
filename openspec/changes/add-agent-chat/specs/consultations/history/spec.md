# Spec Delta

## Purpose

Удаление capability `consultations/history`: структура «консультация → диалоги» замещена плоским чатом агента (`chats/history`).

## REMOVED Requirements

### Requirement: Консультация — контейнер диалогов «один вопрос — один диалог»

Консультация SHALL существовать в контексте одной конструкции как совокупность диалогов; диалог SHALL создаваться первым сообщением владельца и явно удаляться им целиком (полное удаление, без корзины); ассистент SHALL видеть только историю собственного диалога, но не соседних ([#47](https://github.com/menshov-anatoliy/transaction-journal/issues/47), [CONTEXT.md](../../../../../../CONTEXT.md)).

Traceability ID: requirement-history-dialogue-per-question

**Reason**: Двухуровневая модель не переживает перенос в новый UI: изоляция тем достигается плоским чатом «на тему», а список чатов «Новые/Архив» дизайн-референса не оставляет места контейнеру ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-flat-full-history` (создание первым сообщением, изоляция соседей) и `requirement-chat-manual-completion-and-deletion` (удаление целиком без корзины).

### Requirement: Сообщение хранит роль, текст, as-of и рыночный след

Сообщение SHALL хранить роль автора, текст и отметку as-of; ответ ассистента SHALL дополнительно сохранять рыночный след — какие инструменты вызывались и с какими as-of их данные ([#47](https://github.com/menshov-anatoliy/transaction-journal/issues/47), [CONTEXT.md](../../../../../../CONTEXT.md)).

Traceability ID: requirement-history-message-composition

**Reason**: Рыночный след обобщён до следа источников: новый UI требует ссылок не только на рынок, но и на карточки правил и данные журнала ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-message-composition` (след источников, рыночный след — частный случай).

### Requirement: История — запись окружения per construction

История SHALL храниться в SQLite по одной базе на конструкцию через порт хранилища с адаптером в Infrastructure; домен SHALL о консультации не знать; пересбор конструкции SHALL стирать её консультации ([#45](https://github.com/menshov-anatoliy/transaction-journal/issues/45), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-history-environment-record

**Reason**: Чаты без конструкции не помещаются в per-construction базы; хранение становится единым, а пересбор стирает только привязанные чаты ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-environment-record` (единое хранилище; сценарий выживания непривязанных добавлен, «домен не знает» сохранён).

### Requirement: Компакция истории отсутствует

Диалог SHALL вестись полной историей без компакции и суммаризации; при выходе объёма за разумные пределы владелец SHALL начинать новый диалог с чистого листа ([#47](https://github.com/menshov-anatoliy/transaction-journal/issues/47)).

Traceability ID: requirement-history-no-compaction

**Reason**: Правило переживает замену сущности без изменений ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-chat-no-compaction` («новый диалог» → «новый чат»).
