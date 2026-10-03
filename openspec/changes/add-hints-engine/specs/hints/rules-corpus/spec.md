# Spec Delta

## ADDED Requirements

### Requirement: Движок читает каталог корпуса в неизменяемый снимок на каждом проходе

Движок SHALL читать каталог корпуса правил (по умолчанию `rules/` рядом с exe, путь переопределяется настройкой) целиком в начале каждого прохода агента в неизменяемый снимок; правки файлов корпуса SHALL действовать со следующего прохода без пересборки и перезапуска приложения ([ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md), [#26](https://github.com/menshov-anatoliy/transaction-journal/issues/26)). Карточка со `status: retired` SHALL входить в снимок для гашения живых записей, но не SHALL исполняться.

Traceability ID: requirement-corpus-snapshot-per-pass

#### Scenario: Замена карточек действует со следующего прохода
Traceability ID: scenario-corpus-reload-next-pass
- **WHEN** файлы каталога `rules/` заменены (git pull или копирование) между проходами
- **THEN** следующий проход читает обновлённый корпус
- **AND** приложение не пересобирается и не перезапускается

#### Scenario: Путь каталога переопределяется настройкой
Traceability ID: scenario-corpus-path-configurable
- **WHEN** в настройках задан иной путь к каталогу корпуса
- **THEN** движок читает корпус по заданному пути

### Requirement: Валидность корпуса — предусловие прохода

Карточка, YAML которой не парсится или которая не соответствует схеме карточки, как и отсутствующий или пустой каталог корпуса, SHALL ломать проход целиком до построения снимка и любых чтений журнала и рынка: ни подсказки, ни записи, ни сводка не производятся. Движок SHALL за одно чтение собрать проблемы всех карточек и поднять одну агрегированную ошибку с полным списком. Хост приложения SHALL продолжать работать; ошибка SHALL быть видна на каждом входе запуска: лог планового прохода и UI ручного запуска (Telegram `/summary` — следующим change) ([ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md), [#28](https://github.com/menshov-anatoliy/transaction-journal/issues/28)).

Traceability ID: requirement-corpus-validity-precondition

#### Scenario: Битая карточка останавливает проход агрегированной ошибкой
Traceability ID: scenario-corpus-broken-card-fails-pass
- **WHEN** в каталоге корпуса есть карточка с непарсируемым YAML или несоответствием схеме
- **THEN** проход не начинается: нет снимка, чтений журнала и рынка, подсказок и записей
- **AND** поднятая ошибка перечисляет проблемы всех битых карточек, а не только первой

#### Scenario: Отсутствующий или пустой каталог равен битому корпусу
Traceability ID: scenario-corpus-missing-empty-fails-pass
- **WHEN** каталог корпуса отсутствует или не содержит карточек
- **THEN** проход останавливается той же агрегированной ошибкой
- **AND** приложение продолжает работать

#### Scenario: Ошибка видна на каждом входе запуска
Traceability ID: scenario-corpus-error-visible-on-entry-points
- **WHEN** владелец запускает проход (лог планового прохода или кнопка ручного запуска в UI)
- **THEN** агрегированная ошибка корпуса показывается на этом входе

### Requirement: Неизвестный и нулевой ключ триггера — чек-лист, не подсказка

Карточка с ключом `trigger.implementation`, отсутствующим в коде движка, SHALL оставаться валидной: не порождать подсказку, попадать в статический чек-лист сводки и в лог «непокрытых кодом»; проход SHALL продолжаться. Карточка с `implementation: null` SHALL вести себя так же ([#26](https://github.com/menshov-anatoliy/transaction-journal/issues/26), [#28](https://github.com/menshov-anatoliy/transaction-journal/issues/28)).

Traceability ID: requirement-corpus-unimplemented-trigger-checklist

#### Scenario: Неизвестный ключ не ломает проход
Traceability ID: scenario-corpus-unknown-key-continues
- **WHEN** карточка ссылается на ключ триггера, которого нет в коде движка
- **THEN** проход продолжается
- **AND** правило попадает в статический чек-лист сводки и лог «непокрытых кодом»

#### Scenario: Нулевая реализация — статический чек-лист
Traceability ID: scenario-corpus-null-implementation-checklist
- **WHEN** у карточки `trigger.implementation: null`
- **THEN** она не порождает подсказок и входит в статический чек-лист сводки

### Requirement: Объявленные конфликтные пары проверяются валидностью корпуса

Необязательное поле `conflicts_with` карточки SHALL объявлять идентификаторы правил, которые не могут быть активны одновременно с ней; объявление SHALL трактоваться симметрично — достаточно записи с одной из сторон. Корпус, в котором оба члена объявленной пары имеют `status: active`, SHALL быть невалидным: проход останавливается агрегированной ошибкой до построения снимка и любых чтений — тем же механизмом, что битая карточка ([#36](https://github.com/menshov-anatoliy/transaction-journal/issues/36)). Пара с `retired`-карточкой SHALL быть валидной; объявление SHALL сохраняться как документация известного напряжения источников.

Traceability ID: requirement-corpus-declared-conflicts-validated

#### Scenario: Два активных члена пары останавливают проход
Traceability ID: scenario-corpus-conflict-pair-active-fails-pass
- **WHEN** в корпусе две карточки связаны объявлением `conflicts_with` и обе имеют `status: active`
- **THEN** проход не начинается: нет снимка, подсказок и сводки
- **AND** агрегированная ошибка называет обе карточки пары

#### Scenario: Retired-член пары не ломает корпус
Traceability ID: scenario-corpus-conflict-pair-retired-valid
- **WHEN** в объявленной паре одна карточка имеет `status: retired`
- **THEN** корпус валиден, проход продолжается
- **AND** retired-карточка не исполняется, но гасит живые записи
