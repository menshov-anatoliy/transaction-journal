# Spec Delta

## ADDED Requirements

### Requirement: Канал доставки — Telegram-бот с read-only командами

Каналом доставки SHALL быть Telegram-бот, работающий long-polling'ом в том же приложении: push-сводки автоматических проходов, команда `/summary` (собрать и прислать сводку сейчас) и команда `/status` (свежесть данных). Пишущих команд бот SHALL не иметь; перевод подсказок в `applied`/`dismissed` остаётся в UI ([#21](https://github.com/menshov-anatoliy/transaction-journal/issues/21), [#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-delivery-telegram-read-only

#### Scenario: /summary собирает сводку немедленно
Traceability ID: scenario-delivery-summary-command
- **WHEN** владелец присылает боту `/summary`
- **THEN** бот выполняет проход и доставляет сводку полного формата

#### Scenario: Пишущих команд нет
Traceability ID: scenario-delivery-no-write-commands
- **WHEN** бот получает команду, изменяющую состояние журнала или подсказок
- **THEN** команда не выполняется

### Requirement: Каденция — три прохода в день внутри окна активности

Автоматические проходы SHALL выполняться трижды в день — начало, середина и конец дня, времена настраиваются — и только внутри настраиваемого окна активности; вне окна SHALL не быть ни проходов, ни отправок. Ручной `/summary` SHALL работать всегда, независимо от окна. Пропущенные автоматические проходы (exe был выключен) SHALL не догоняться ([#21](https://github.com/menshov-anatoliy/transaction-journal/issues/21)).

Traceability ID: requirement-delivery-cadence-window

#### Scenario: Проходы идут внутри окна
Traceability ID: scenario-delivery-passes-inside-window
- **WHEN** наступает время планового прохода внутри окна активности
- **THEN** проход выполняется и сводка доставляется

#### Scenario: Вне окна — тишина
Traceability ID: scenario-delivery-silent-outside-window
- **WHEN** время находится вне окна активности
- **THEN** автоматические проходы и отправки не производятся

#### Scenario: Ручной запрос вне окна разрешён
Traceability ID: scenario-delivery-manual-outside-window
- **WHEN** владелец присылает `/summary` вне окна активности
- **THEN** проход выполняется и сводка доставляется

#### Scenario: Выключенный exe не навёрстывает пропуски
Traceability ID: scenario-delivery-no-catch-up
- **WHEN** exe был выключен во время планового прохода
- **THEN** этот проход не выполняется позже
- **AND** недоставленное отражается маркерами следующей доставки

### Requirement: Недоступность рынка пропускает проход с диагностикой

При недоступности рыночных данных проход SHALL пропускаться с диагностикой — подсказки на неполных данных не выпускаются; буфер недоставленного не теряется ([#21](https://github.com/menshov-anatoliy/transaction-journal/issues/21)).

Traceability ID: requirement-delivery-market-unavailable-skip

#### Scenario: Сбой марок отменяет доставку
Traceability ID: scenario-delivery-mark-failure-skip
- **WHEN** марки недоступны в момент планового прохода
- **THEN** сводка не собирается и не доставляется
- **AND** диагностика причины фиксируется

### Requirement: /status — шаблонный ответ о свежести без LLM

Команда `/status` SHALL отвечать всегда шаблонно, без LLM: время последней синхронизации журнала, время рыночных марок, число живых подсказок, состояние LLM последнего прохода (изложение/шаблон), положение относительно окна активности и валидность корпуса ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34)).

Traceability ID: requirement-delivery-status-command

#### Scenario: /status показывает состояние агента
Traceability ID: scenario-delivery-status-reports-agent
- **WHEN** владелец присылает `/status`
- **THEN** ответ перечисляет свежесть журнала и марок, число живых подсказок, режим последнего изложения, положение в окне и валидность корпуса

#### Scenario: Ошибка корпуса видна в /summary
Traceability ID: scenario-delivery-corpus-error-in-summary
- **WHEN** корпус невалиден при запросе `/summary`
- **THEN** ответ содержит агрегированную ошибку корпуса со списком проблем
