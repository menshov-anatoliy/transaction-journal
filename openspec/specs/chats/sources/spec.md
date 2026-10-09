# chats/sources Specification

## Purpose

Источники данных чата агента: закрытый справочник и параметр чата, read-only реестр инструментов, лимиты биржевых запросов, деградация при недоступности рынка и ИИ-модель как параметр чата.

## Requirements

### Requirement: Источники данных — закрытый справочник из трёх категорий

ИИ-помощнику SHALL быть доступны ровно три категории источников данных, только для чтения: журнал (снимок конструкции или портфеля), корпус правил, рынок Bybit. Набор источников SHALL быть параметром чата — владелец выбирает подмножество при создании, по умолчанию все три; пишущих источников SHALL не существовать ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), карта [#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50), [CONTEXT.md](../../../../../../CONTEXT.md) «Источник данных»).

Traceability ID: requirement-sources-closed-catalog

#### Scenario: Справочник содержит ровно три категории
Traceability ID: scenario-sources-three-categories
- **WHEN** проверяется справочник источников данных
- **THEN** он содержит журнал, корпус правил и рынок Bybit — и ничего сверх

#### Scenario: Набор источников параметризует чат
Traceability ID: scenario-sources-subset-parameter
- **WHEN** чат создан с подмножеством источников
- **THEN** ИИ-помощнику доступны только выбранные категории

#### Scenario: Пишущих источников не существует
Traceability ID: scenario-sources-write-never
- **WHEN** проверяется реестр инструментов при любом наборе источников
- **THEN** пишущие инструменты в нём отсутствуют

### Requirement: Инструменты строго read-only из одного реестра

Источники SHALL предоставляться инструментами из единого реестра: `read_rule_card(cardId)` — полная карточка правила; `get_market_snapshot(baseCoin)` — спот/фьючерс и ставка фандинга; `get_option_board(baseCoin)` — компактная проекция доски опционов (IV/OI/греки/бид-аск). Инструменты по чужим конструкциям SHALL отсутствовать; реестр инструментов чата SHALL соответствовать его набору источников ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-sources-read-only-tool-registry

#### Scenario: Карточка читается целиком по id
Traceability ID: scenario-sources-card-by-id
- **WHEN** ИИ-помощник вызывает `read_rule_card` с id из индекса корпуса
- **THEN** инструмент возвращает полный текст карточки правила

#### Scenario: Доска отдаётся компактной проекцией
Traceability ID: scenario-sources-option-board-projection
- **WHEN** ИИ-помощник вызывает `get_option_board` по baseCoin
- **THEN** возвращается компактная проекция страйков с IV/OI/греками и бид-аском, а не сырые данные биржи

#### Scenario: Реестр соответствует выбранным источникам
Traceability ID: scenario-sources-registry-matches-chat-sources
- **WHEN** чат создан без источника «рынок Bybit»
- **THEN** рыночные инструменты недоступны в его реестре

### Requirement: Один вызов рыночного инструмента — один биржевой запрос

Каждый вызов рыночного инструмента SHALL выполнять ровно один HTTP-запрос через единый клиент Bybit с его троттлером и resilience; агентный цикл SHALL иметь потолок итераций (`MaximumIterationsPerRequest = 6`), ограничивающий глубину инструментальных вызовов до ~6 биржевых запросов на сообщение; отдельные счётчики запросов SHALL не вводиться ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-sources-single-request-per-call

#### Scenario: Потолок итераций ограничивает биржевые запросы
Traceability ID: scenario-sources-iteration-cap
- **WHEN** модель пытается зациклиться на инструментальных вызовах
- **THEN** цикл останавливается потолком итераций агентного цикла
- **AND** каждый выполненный вызов соответствовал одному HTTP-запросу к бирже

### Requirement: Недоступность рынка деградирует в кэш с явным as-of

При недоступности Bybit рыночный инструмент SHALL возвращать структурированный ответ «недоступно + последняя кэшированная проекция с явным as-of» из кэша марок; ИИ-помощник SHALL продолжать отвечать по журналу и корпусу, помечать устаревший as-of и отказываться от рыночно-зависимых рекомендаций ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-sources-degradation-cached-asof

#### Scenario: Bybit недоступен — инструмент отдаёт кэш
Traceability ID: scenario-sources-market-down-cached-projection
- **WHEN** биржа недоступна в момент вызова рыночного инструмента
- **THEN** инструмент возвращает «недоступно» и последнюю кэшированную проекцию с её as-of

#### Scenario: Ответ без рыночно-зависимых рекомендаций
Traceability ID: scenario-sources-stale-asof-no-market-advice
- **WHEN** доступные рыночные данные устарели
- **THEN** ИИ-помощник помечает их as-of и строит ответ на журнале и корпусе
- **AND** рыночно-зависимые рекомендации не выдаются

### Requirement: ИИ-модель — параметр чата со сменой без кода

ИИ-модель SHALL быть параметром чата с дефолтом GLM-5.3; владелец SHALL иметь право сменить модель чата в любой момент: новые сообщения отправляются выбранной модели, а история SHALL не переписываться. Провайдер и модель SHALL задаваться конфигурацией (`Llm:Chat:Model`, `config/llm-provider`, дефолт z.ai) без правки кода ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-sources-model-is-chat-parameter

#### Scenario: Новый чат создаётся с моделью по умолчанию
Traceability ID: scenario-sources-default-model-glm
- **WHEN** владелец создаёт чат, не выбирая модель
- **THEN** чат использует GLM-5.3

#### Scenario: Модель сменяема на лету
Traceability ID: scenario-sources-model-switch-mid-chat
- **WHEN** владелец меняет модель существующего чата
- **THEN** последующие сообщения уходят новой модели
- **AND** история чата сохраняется как есть
