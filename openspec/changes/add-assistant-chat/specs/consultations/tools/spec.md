# Spec Delta

## Purpose

Инструменты рыночных данных и корпуса, агентный цикл с пределом биржевых запросов, деградация при недоступности рынка и конфигурация модели чата.

## ADDED Requirements

### Requirement: Инструменты строго read-only из одного реестра

Ассистенту SHALL быть доступны ровно три инструмента: `read_rule_card(cardId)` — полная карточка правила; `get_market_snapshot(baseCoin)` — спот/фьючерс и ставка фандинга; `get_option_board(baseCoin)` — компактная проекция доски опционов (IV/OI/греки/бид-аск). Пишущие инструменты и инструменты по чужим конструкциям SHALL отсутствовать ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-read-only-registry

#### Scenario: Карточка читается целиком по id
Traceability ID: scenario-tools-card-by-id
- **WHEN** ассистент вызывает `read_rule_card` с id из индекса корпуса
- **THEN** инструмент возвращает полный текст карточки правила

#### Scenario: Доска отдаётся компактной проекцией
Traceability ID: scenario-tools-option-board-projection
- **WHEN** ассистент вызывает `get_option_board` по baseCoin
- **THEN** возвращается компактная проекция страйков с IV/OI/греками и бид-аском, а не сырые данные биржи

#### Scenario: Пишущих инструментов нет
Traceability ID: scenario-tools-no-write-tools
- **WHEN** реестр инструментов ассистента проверяется
- **THEN** он содержит только три read-only инструмента

### Requirement: Один вызов рыночного инструмента — один биржевой запрос

Каждый вызов рыночного инструмента SHALL выполнять ровно один HTTP-запрос через единый клиент Bybit с его троттлером и resilience; агентный цикл SHALL иметь потолок итераций (`MaximumIterationsPerRequest = 6`), ограничивающий глубину инструментальных вызовов до ~6 биржевых запросов на сообщение; отдельные счётчики запросов SHALL не вводиться ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-single-request-per-call

#### Scenario: Потолок итераций ограничивает биржевые запросы
Traceability ID: scenario-tools-iteration-cap
- **WHEN** модель пытается зациклиться на инструментальных вызовах
- **THEN** цикл останавливается потолком итераций агентного цикла
- **AND** каждый выполненный вызов соответствовал одному HTTP-запросу к бирже

### Requirement: Недоступность рынка деградирует в кэш с явным as-of

При недоступности Bybit рыночный инструмент SHALL возвращать структурированный ответ «недоступно + последняя кэшированная проекция с явным as-of» из кэша марок; ассистент SHALL продолжать отвечать по журналу и корпусу, помечать устаревший as-of и отказываться от рыночно-зависимых рекомендаций ([#45](https://github.com/menshov-anatoliy/transaction-journal/issues/45), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-degradation-cached-asof

#### Scenario: Bybit недоступен — инструмент отдаёт кэш
Traceability ID: scenario-tools-market-down-cached-projection
- **WHEN** биржа недоступна в момент вызова рыночного инструмента
- **THEN** инструмент возвращает «недоступно» и последнюю кэшированную проекцию с её as-of

#### Scenario: Ответ без рыночно-зависимых рекомендаций
Traceability ID: scenario-tools-stale-asof-no-market-advice
- **WHEN** доступные рыночные данные устарели
- **THEN** ассистент помечает их as-of и строит ответ на журнале и корпусе
- **AND** рыночно-зависимые рекомендации не выдаются

### Requirement: Модель чата задаётся отдельной конфигурацией

Модель чата SHALL подключаться отдельным именованным `IChatClient` со своей секцией конфигурации; дефолт — провайдер z.ai, модель GLM-5.3 (OpenAI-совместимый доступ); провайдер и модель SHALL сменяться конфигурацией без правки кода; бюджет SHALL удерживаться ниже $5/мес компактным контекстом ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-chat-model-configurable

#### Scenario: Смена модели без кода
Traceability ID: scenario-tools-model-switch-config
- **WHEN** конфигурация указывает другого провайдера или модель
- **THEN** консультации используют её без правки кода
