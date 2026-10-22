# Spec Delta

## MODIFIED Requirements

### Requirement: Проход движка детерминирован — отбор подсказок делает код
Traceability ID: requirement-engine-deterministic-selection

Проход агента SHALL читать журнал, рыночные котировки Bybit и снимок корпуса и порождать подсказки только детерминированным кодом движка по ключам `trigger.implementation`; LLM не SHALL участвовать в отборе подсказок — правило «Никаких прогнозов, только действия по факту совершившихся событий» гарантировано структурно ([ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)). Движок SHALL опираться на цели риска/профита конструкции из сущности Construction (Risk/Profit, Percent|Usdt) и собственные расчёты журнала.

#### Scenario: Одинаковые входы дают одинаковые подсказки
Traceability ID: scenario-engine-same-inputs-same-hints
- **WHEN** два прохода видят одинаковые журнал, котировки и снимок корпуса
- **THEN** набор сработавших правил одинаков
- **AND** LLM не вызывается для отбора или формулирования подсказок

### Requirement: Машинные триггеры корпуса v1 покрыты движком
Traceability ID: requirement-engine-v1-trigger-set

Движок SHALL реализовывать все машинные ключи корпуса v1: `risk-limit-period`, `uncovered-sale-margin`, `profit-target-reached`, `edge-sale-cap`, `roll-time-window`, `roll-threshold`, `atm-decay-window`, `min-straddle-size`, `flat-win-streak`, `unfreeze-profit-ratio`, `synthetic-close-itm` ([#27](https://github.com/menshov-anatoliy/transaction-journal/issues/27)). Критерием машинности триггера SHALL быть машинная проверяемость условия по факту журнала и рынка; правила с непроверяемым условием в движок не входят ([#25](https://github.com/menshov-anatoliy/transaction-journal/issues/25)).

#### Scenario: Каждый ключ корпуса v1 исполняется движком
Traceability ID: scenario-engine-implements-all-v1-keys
- **WHEN** снимок корпуса v1 содержит карточку с машинным ключом триггера
- **THEN** движок вычисляет её условие по журналу и котировкам
- **AND** фикстурные e2e покрывают каждый ключ парами «сработало / не сработало»

#### Scenario: Условие вне корпуса не исполняется
Traceability ID: scenario-engine-no-triggers-beyond-corpus
- **WHEN** условие не выражено карточкой корпуса с машинным ключом
- **THEN** движок не порождает подсказок по нему

### Requirement: Недоступность рыночных данных пропускает проход
Traceability ID: requirement-engine-market-unavailable-skips-pass

При недоступности рыночных данных проход SHALL пропускаться с диагностикой; подсказки на неполных данных не SHALL выпускаться ([#21](https://github.com/menshov-anatoliy/transaction-journal/issues/21)).

#### Scenario: Сбой марок отменяет проход
Traceability ID: scenario-engine-mark-failure-skips-pass
- **WHEN** на момент прохода рыночные котировки недоступны
- **THEN** проход не производит подсказок и записей
- **AND** диагностика фиксирует причину пропуска
