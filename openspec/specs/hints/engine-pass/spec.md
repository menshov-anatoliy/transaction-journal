# hints/engine-pass Specification

## Purpose

TBD - update Purpose after archive

## Requirements

### Requirement: Проход движка детерминирован — отбор подсказок делает код

Проход агента SHALL читать журнал, рыночные марки Bybit и снимок корпуса и порождать подсказки только детерминированным кодом движка по ключам `trigger.implementation`; LLM не SHALL участвовать в отборе подсказок — правило «Никаких прогнозов, только действия по факту совершившихся событий» гарантировано структурно ([ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)). Движок SHALL опираться на цели риска/профита конструкции из сущности Construction (Risk/Profit, Percent|Usdt) и собственные расчёты журнала.

Traceability ID: requirement-engine-deterministic-selection

#### Scenario: Одинаковые входы дают одинаковые подсказки
Traceability ID: scenario-engine-same-inputs-same-hints
- **WHEN** два прохода видят одинаковые журнал, марки и снимок корпуса
- **THEN** набор сработавших правил одинаков
- **AND** LLM не вызывается для отбора или формулирования подсказок

### Requirement: Машинные триггеры корпуса v1 покрыты движком

Движок SHALL реализовывать все машинные ключи корпуса v1: `risk-limit-period`, `uncovered-sale-margin`, `profit-target-reached`, `edge-sale-cap`, `roll-time-window`, `roll-threshold`, `atm-decay-window`, `min-straddle-size`, `flat-win-streak`, `unfreeze-profit-ratio`, `synthetic-close-itm` ([#27](https://github.com/menshov-anatoliy/transaction-journal/issues/27)). Критерием машинности триггера SHALL быть машинная проверяемость условия по факту журнала и рынка; правила с непроверяемым условием в движок не входят ([#25](https://github.com/menshov-anatoliy/transaction-journal/issues/25)).

Traceability ID: requirement-engine-v1-trigger-set

#### Scenario: Каждый ключ корпуса v1 исполняется движком
Traceability ID: scenario-engine-implements-all-v1-keys
- **WHEN** снимок корпуса v1 содержит карточку с машинным ключом триггера
- **THEN** движок вычисляет её условие по журналу и маркам
- **AND** фикстурные e2e покрывают каждый ключ парами «сработало / не сработало»

#### Scenario: Условие вне корпуса не исполняется
Traceability ID: scenario-engine-no-triggers-beyond-corpus
- **WHEN** условие не выражено карточкой корпуса с машинным ключом
- **THEN** движок не порождает подсказок по нему

### Requirement: Чёткость правила задаёт формулировку подсказки

Чёткое правило (`clarity: crisp`) SHALL формулироваться императивом прямого действия; размытое (`clarity: fuzzy`) — префиксом «[решение]», оставляя действие решению человека. Чёткость SHALL задавать формулировку, а не факт срабатывания ([#24](https://github.com/menshov-anatoliy/transaction-journal/issues/24), [#25](https://github.com/menshov-anatoliy/transaction-journal/issues/25)).

Traceability ID: requirement-engine-clarity-shapes-wording

#### Scenario: Чёткое правило — императив
Traceability ID: scenario-engine-crisp-imperative
- **WHEN** срабатывает чёткое правило (например «Закрыть конструкцию: убыток −4.8% при лимите −5%»)
- **THEN** текст подсказки — прямое предписание действием

#### Scenario: Размытое правило — пометка решения
Traceability ID: scenario-engine-fuzzy-decision-prefix
- **WHEN** срабатывает размытое правило
- **THEN** текст подсказки начинается с «[решение]» и излагает предмет оценки человеком

### Requirement: Подсказка фиксируется самоописательно в момент генерации

При срабатывании движок SHALL записывать подсказку с отрендеренным по `hintTemplate` карточки текстом, фактами триггера (пары ключ-значение, заполнившие шаблон), характером, чёткостью, тегами источников и отметкой as-of прохода; значения SHALL денормализоваться при генерации, чтобы позднейшие правки, retiring или удаление карточки не искажали историю ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-engine-self-describing-record

#### Scenario: Правка карточки не искажает записанную подсказку
Traceability ID: scenario-engine-hint-immutable-to-corpus-edits
- **WHEN** после генерации подсказки карточку правила отредактировали или вывели из корпуса
- **THEN** ранее записанная подсказка сохраняет текст, факты, теги источников и as-of момента генерации

#### Scenario: Факты триггера заполняют шаблон
Traceability ID: scenario-engine-facts-fill-template
- **WHEN** подсказка генерируется по карточке с `hintTemplate`
- **THEN** значения фактов триггера (например `lossPct=4.8, limitPct=5`) подставляются в текст и сохраняются в записи

### Requirement: Недоступность рыночных данных пропускает проход

При недоступности рыночных данных проход SHALL пропускаться с диагностикой; подсказки на неполных данных не SHALL выпускаться ([#21](https://github.com/menshov-anatoliy/transaction-journal/issues/21)).

Traceability ID: requirement-engine-market-unavailable-skips-pass

#### Scenario: Сбой марок отменяет проход
Traceability ID: scenario-engine-mark-failure-skips-pass
- **WHEN** на момент прохода рыночные марки недоступны
- **THEN** проход не производит подсказок и записей
- **AND** диагностика фиксирует причину пропуска

### Requirement: Движок не разрешает конфликты правил

Одновременно сработавшие на одном субъекте правила SHALL порождать подсказки как есть: без подавления, приоритизации и маркера «конфликт»; семантическая детекция конкуренции действий SHALL не выполняться. Предотвращение противоречий — валидация корпуса до прохода, а не рантайм ([#36](https://github.com/menshov-anatoliy/transaction-journal/issues/36)).

Traceability ID: requirement-engine-no-runtime-conflict-resolution

#### Scenario: Конкурирующие подсказки входят в сводку как есть
Traceability ID: scenario-engine-competing-hints-both-shown
- **WHEN** в валидном корпусе два необъявленных парой правила сработали на одном субъекте
- **THEN** обе подсказки порождаются и входят в сводку без маркера и подавления
