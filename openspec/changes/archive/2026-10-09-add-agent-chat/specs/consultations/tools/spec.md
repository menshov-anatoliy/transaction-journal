# Spec Delta

## Purpose

Удаление capability `consultations/tools`: инструменты и конфигурация модели замещены источниками данных чата агента (`chats/sources`).

## REMOVED Requirements

### Requirement: Инструменты строго read-only из одного реестра

Ассистенту SHALL быть доступны ровно три инструмента: `read_rule_card(cardId)` — полная карточка правила; `get_market_snapshot(baseCoin)` — спот/фьючерс и ставка фандинга; `get_option_board(baseCoin)` — компактная проекция доски опционов (IV/OI/греки/бид-аск). Пишущие инструменты и инструменты по чужим конструкциям SHALL отсутствовать ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-read-only-registry

**Reason**: Инструменты стали техническим представлением источников данных — доменным понятием, параметризуемым на уровне чата ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-sources-read-only-tool-registry` (те же три инструмента) и `requirement-sources-closed-catalog` (справочник источников и параметр чата).

### Requirement: Один вызов рыночного инструмента — один биржевой запрос

Каждый вызов рыночного инструмента SHALL выполнять ровно один HTTP-запрос через единый клиент Bybit с его троттлером и resilience; агентный цикл SHALL иметь потолок итераций (`MaximumIterationsPerRequest = 6`), ограничивающий глубину инструментальных вызовов до ~6 биржевых запросов на сообщение; отдельные счётчики запросов SHALL не вводиться ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-single-request-per-call

**Reason**: Ограничение переживает замену сущности без изменений ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-sources-single-request-per-call`.

### Requirement: Недоступность рынка деградирует в кэш с явным as-of

При недоступности Bybit рыночный инструмент SHALL возвращать структурированный ответ «недоступно + последняя кэшированная проекция с явным as-of» из кэша марок; ассистент SHALL продолжать отвечать по журналу и корпусу, помечать устаревший as-of и отказываться от рыночно-зависимых рекомендаций ([#45](https://github.com/menshov-anatoliy/transaction-journal/issues/45), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-degradation-cached-asof

**Reason**: Деградация переживает замену сущности без изменений ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-sources-degradation-cached-asof`.

### Requirement: Модель чата задаётся отдельной конфигурацией

Модель чата SHALL подключаться отдельным именованным `IChatClient`; её модель SHALL читаться из подсекции `Chat` общей секции провайдера `Llm` (`Llm:Chat:Model`, дефолт GLM-5.3), общие параметры провайдера задаются `config/llm-provider`; дефолт — провайдер z.ai (OpenAI-совместимый доступ); модель SHALL сменяться конфигурацией без правки кода; бюджет SHALL удерживаться ниже $5/мес компактным контекстом ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-chat-model-configurable

**Reason**: Модель стала ещё и параметром чата, сменяемым владельцем на лету, — конфигурация задаёт провайдера и дефолт, выбор per chat ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53)).
**Migration**: `requirement-sources-model-is-chat-parameter` (сценарий смены конфигурацией расширен сменой в чате).
