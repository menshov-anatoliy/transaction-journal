# Spec Delta

## MODIFIED Requirements

### Requirement: Модель чата задаётся отдельной конфигурацией

Модель чата SHALL подключаться отдельным именованным `IChatClient`; её модель SHALL читаться из подсекции `Chat` общей секции провайдера `Llm` (`Llm:Chat:Model`, дефолт GLM-5.3), общие параметры провайдера задаются `config/llm-provider`; дефолт — провайдер z.ai (OpenAI-совместимый доступ); модель SHALL сменяться конфигурацией без правки кода; бюджет SHALL удерживаться ниже $5/мес компактным контекстом ([ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-tools-chat-model-configurable

#### Scenario: Смена модели без кода

Traceability ID: scenario-tools-model-switch-config

- **WHEN** конфигурация указывает другого провайдера (`Llm:Provider` с явным `Llm:BaseUrl`) или модель (`Llm:Chat:Model`)
- **THEN** консультации используют её без правки кода
