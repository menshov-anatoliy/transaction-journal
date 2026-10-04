# hints/hint-lifecycle Specification

## Purpose

TBD - update Purpose after archive

## Requirements

### Requirement: Подсказка — самоописательная запись окружения, невидимая домену

Подсказка SHALL храниться всей историей в SQLite как запись окружения с полями: rule id, субъект, характер действия, чёткость, теги источников, отрендеренный текст, факты триггера, отметка as-of прохода, статус, `firstSeenAt`. Поля группировки/сортировки SHALL отсутствовать: слои отображения выводят их из субъекта и характера при показе. Доменные сущности и движки SHALL оставаться без ссылок на подсказки ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30), [ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-hint-self-describing-record

#### Scenario: Домен не знает о подсказках
Traceability ID: scenario-hint-invisible-to-domain
- **WHEN** собирается проект Domain
- **THEN** его типы не ссылаются на записи подсказок

#### Scenario: История хранится целиком
Traceability ID: scenario-hint-full-history-retained
- **WHEN** подсказка достигает терминального статуса
- **THEN** запись сохраняется в SQLite со всеми полями момента генерации

### Requirement: Субъект подсказки v1 — открытая конструкция или журнал

Субъектом подсказки v1 SHALL быть закрытый набор: открытая конструкция или журнал (портфельный уровень, например лимиты риска периода ac-01). Инструмент вне портфеля SHALL остаться вне субъектов до v2 ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-hint-subject-v1-closed-set

#### Scenario: Портфельное правило адресовано журналу
Traceability ID: scenario-hint-portfolio-rule-subject-journal
- **WHEN** срабатывает правило уровня портфеля (например `risk-limit-period`)
- **THEN** подсказка создаётся с субъектом «журнал»

#### Scenario: Правило конструкции адресовано конструкции
Traceability ID: scenario-hint-construction-rule-subject-construction
- **WHEN** срабатывает правило конкретной открытой конструкции
- **THEN** подсказка создаётся с субъектом «конструкция» и ссылкой на неё

### Requirement: Жизненный цикл new → applied | dismissed | expired без reopen

Перевод подсказки из `new` в `applied` или `dismissed` SHALL выполнять только человек и только в UI (Telegram read-only). Переход в `expired` SHALL выполнять агент: условие перестало выполняться, субъект закрыт или правило выведено из корпуса. Все финальные статусы SHALL быть терминальными без reopen. `firstSeenAt` SHALL быть автопометкой первого показа в UI, а не статусом ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30), [#21](https://github.com/menshov-anatoliy/transaction-journal/issues/21)).

Traceability ID: requirement-hint-lifecycle-transitions

#### Scenario: Человек применяет подсказку в UI
Traceability ID: scenario-hint-applied-by-human-in-ui
- **WHEN** владелец нажимает «Применено» на живой подсказке
- **THEN** запись получает статус `applied` и дальнейшие переходы запрещены

#### Scenario: Агент гасит подсказку истечением условия
Traceability ID: scenario-hint-expired-by-agent
- **WHEN** на очередном проходе условие подсказки больше не выполняется
- **THEN** агент переводит запись в `expired`

#### Scenario: Повторный показ не меняет запись
Traceability ID: scenario-hint-first-seen-once
- **WHEN** живая подсказка впервые показывается в UI
- **THEN** фиксируется `firstSeenAt`
- **AND** последующие показы не изменяют запись

### Requirement: Дедупликация «правило × субъект × окно»

Новая подсказка SHALL подавляться любой существующей записью того же правила и субъекта в текущем окне, включая `applied` и `dismissed` — отклонённое не повторяется. Окно SHALL быть непрерывным отрезком выполнения условия; для периодных правил смена периода SHALL начинать новое окно. Условие ушло — запись гасится в `expired`; вернулось — порождается новая подсказка ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-hint-dedup-rule-subject-window

#### Scenario: Непрерывное условие не дублирует подсказку
Traceability ID: scenario-hint-dedup-suppresses-in-window
- **WHEN** условие выполняется на нескольких проходах подряд
- **THEN** существует одна живая запись «правило × субъект»
- **AND** повторные проходы не создают новых записей

#### Scenario: Отклонённая подсказка не повторяется в окне
Traceability ID: scenario-hint-dismissed-suppresses-window
- **WHEN** подсказка переведена в `dismissed`, а условие продолжает выполняться
- **THEN** новая подсказка того же правила и субъекта не создаётся до конца окна

#### Scenario: Смена периода периодного правила — новое окно
Traceability ID: scenario-hint-period-change-new-window
- **WHEN** периодное правило (например лимит недели) переходит в следующий период при непрерывном выполнении условия
- **THEN** начинается новое окно и может создаться новая подсказка

#### Scenario: Возвращение условия после истечения — новая подсказка
Traceability ID: scenario-hint-condition-returns-new-hint
- **WHEN** условие было погашено в `expired` и затем снова начало выполняться
- **THEN** создаётся новая подсказка того же правила и субъекта

### Requirement: Закрытие субъекта и вывод правила гасят живые записи

Закрытие конструкции SHALL гасить её живые подсказки в `expired` («субъект закрыт») на первом же проходе. Правило, выведенное из корпуса (`retired` или удалённое), SHALL гасить свои живые записи при загрузке снимка корпуса («правило выведено»). Журнал сделок в обоих случаях не участвует ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-hint-expiry-on-subject-close-and-retirement

#### Scenario: Закрытая конструкция гасит свои подсказки
Traceability ID: scenario-hint-expired-on-construction-close
- **WHEN** конструкция с живыми подсказками закрывается
- **THEN** на первом проходе после закрытия движок переводит их записи в `expired`

#### Scenario: Вывод правила гасит его живые записи
Traceability ID: scenario-hint-expired-on-rule-retirement
- **WHEN** карточка правила помечена `retired` или удалена из корпуса
- **THEN** при загрузке снимка живые записи этого правила гасятся в `expired`
