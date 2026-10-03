# Spec Delta

## ADDED Requirements

### Requirement: Скелет сводки — пять секций с обязательным as-of

Сводка SHALL состоять из пяти секций: (1) заголовок с типом прохода (авто: начало/середина/конец дня; ручной) и as-of двумя строками — «Журнал: …» (момент последней синхронизации) и «Марки: …» (время рыночных марок), время в локальном поясе exe; (2) портфельная секция — подсказки субъекта «журнал»; (3) секции по конструкциям с живыми подсказками; (4) статический чек-лист; (5) технический футер одной строкой. Конструкции без живых подсказок SHALL не упоминаться — полная картина остаётся в UI ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34), [#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-digest-five-section-skeleton

#### Scenario: Заголовок несёт as-of журнала и марок
Traceability ID: scenario-digest-header-asof
- **WHEN** собирается сводка
- **THEN** заголовок содержит тип прохода и две строки as-of — момент последней синхронизации журнала и время рыночных марок в локальном поясе exe

#### Scenario: Конструкции без живых подсказок не упоминаются
Traceability ID: scenario-digest-skips-silent-constructions
- **WHEN** у конструкции нет живых подсказок
- **THEN** сводка не содержит её секции

### Requirement: Секции конструкций группируются по группам v1

Секции подсказок SHALL группироваться по справочнику групп v1: «Риск-режим» → «Управление конструкцией» → «Фьючерсная нога»; пустые группы SHALL не показываться ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34)).

Traceability ID: requirement-digest-section-groups

#### Scenario: Порядок групп фиксирован, пустые скрыты
Traceability ID: scenario-digest-groups-order-and-hiding
- **WHEN** сводка содержит подсказки нескольких групп
- **THEN** группы идут в порядке справочника v1
- **AND** группы без подсказок не отображаются

### Requirement: Полный набор живых подсказок с маркером новых

Сводка SHALL содержать полный набор живых (статус `new`) подсказок с маркером «+M новых» от последней доставленной сводки; сводка SHALL быть самоописательной, без памяти о прошлых текстах. Рыночный блок SHALL быть компактным и стоять в конце: по каждому уникальному baseCoin открытых конструкций — underlyingPrice и фандинг; IV и доска опционов — только в UI ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34)).

Traceability ID: requirement-digest-full-live-set-with-new-marker

#### Scenario: Сводка самоописательна
Traceability ID: scenario-digest-self-contained
- **WHEN** собирается сводка
- **THEN** в ней перечислены все живые подсказки на момент прохода
- **AND** маркер «+M новых» считается от последней доставленной сводки, без цитирования прошлых текстов

#### Scenario: Рыночный блок компактен
Traceability ID: scenario-digest-market-block-compact
- **WHEN** у открытых конструкций несколько baseCoin
- **THEN** рыночный блок показывает по каждому baseCoin underlyingPrice и фандинг
- **AND** IV и доска опционов в сводку не входят

### Requirement: Строка подсказки — значок характера и формулировка по чёткости

Строка подсказки SHALL состоять из значка характера действия и текста с ключевыми фактами. Чёткое правило SHALL формулироваться императивом; размытое — префиксом «[решение]». rule id и теги источников в Telegram SHALL не показываться — они в UI-карточке ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34)).

Traceability ID: requirement-digest-hint-line-format

#### Scenario: Чёткая подсказка — императив с фактами
Traceability ID: scenario-digest-crisp-line
- **WHEN** в сводку попадает чёткая подсказка
- **THEN** строка — значок характера и императив с ключевыми фактами (например «Закрыть конструкцию: убыток −4.8% при лимите −5%»)

#### Scenario: Размытая подсказка — пометка решения
Traceability ID: scenario-digest-fuzzy-line
- **WHEN** в сводку попадает размытая подсказка
- **THEN** строка начинается с «[решение]» (например «[решение] Оценить ролл: до экспирации ≤7 дней»)

### Requirement: Чек-лист — полный только в начале дня и ручном запросе

Полный чек-лист (значок характера + короткое имя карточки) SHALL показываться только в сводке начала дня и в ручном `/summary`; середина и конец дня SHALL показывать строку-счётчик «Чек-лист: N» ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34), [#25](https://github.com/menshov-anatoliy/transaction-journal/issues/25)).

Traceability ID: requirement-digest-checklist-placement

#### Scenario: Начало дня раскрывает чек-лист
Traceability ID: scenario-digest-checklist-full-morning
- **WHEN** собирается сводка начала дня или ручной `/summary`
- **THEN** чек-лист показывается целиком — элемент за элементом

#### Scenario: Остальные проходы считают элементы
Traceability ID: scenario-digest-checklist-counter
- **WHEN** собирается сводка середины или конца дня
- **THEN** чек-лист сворачивается в строку «Чек-лист: N»

### Requirement: Пустой автопроход — короткое подтверждение живости

Пустой автоматический проход SHALL доставлять короткое сообщение «Активных подсказок нет · as-of …»; ручной `/summary` SHALL всегда возвращать полный формат ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34)).

Traceability ID: requirement-digest-empty-pass-short

#### Scenario: Пустой автопроход подтверждает свежесть
Traceability ID: scenario-digest-empty-auto-short
- **WHEN** автопроход не нашёл живых подсказок
- **THEN** доставляется короткое сообщение с as-of, без полного скелета

### Requirement: Длинные сводки режутся по границам секций

Длинная сводка SHALL доставляться несколькими сообщениями по границам секций (заголовок с as-of — в первом), без обрезки содержания; при экстремальном объёме секция конструкций SHALL сворачиваться до имён конструкций с числом подсказок ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34)).

Traceability ID: requirement-digest-split-by-sections

#### Scenario: Разбивка не режет подсказки
Traceability ID: scenario-digest-split-keeps-hints
- **WHEN** сводка превышает лимит сообщения канала
- **THEN** она разбивается по границам секций и доставляется целиком

#### Scenario: Экстремальный объём сворачивает секцию конструкций
Traceability ID: scenario-digest-collapse-extreme
- **WHEN** объём сводки экстремален
- **THEN** секция конструкций сворачивается до имён конструкций с числом подсказок

### Requirement: Сводка эфемерна, недоставленное буферизуется маркерами

Текст сводки SHALL нигде не храниться и не доставляться повторно; хранятся только подсказки. При недоставке первый успешный проход SHALL рендерить текущую картину с маркерами «включая N пропущенных проходов» и «+M новых» от последней доставки ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34)).

Traceability ID: requirement-digest-ephemeral-buffered

#### Scenario: Пропущенные проходы отмечаются маркером
Traceability ID: scenario-digest-skipped-passes-marker
- **WHEN** несколько автопроходов не были доставлены (exe был выключен или канал недоступен)
- **THEN** первый успешный проход доставляет текущую картину с маркером «включая N пропущенных проходов»
