# Spec Delta

## Purpose

Общая конфигурация LLM-провайдера: единая секция `Llm` задаёт параметры провайдера (эндпоинт, ключ доступа, правило стороннего провайдера), подсекции `Chat` и `Hint` задают рабочие модели потребителей — чата консультанта и изложения сводок подсказок.

## ADDED Requirements

### Requirement: Секция Llm задаёт общие параметры провайдера

Параметры LLM-провайдера SHALL задаваться единой секцией `Llm`: `Provider` (дефолт `zai`), `BaseUrl` (дефолт `https://api.z.ai/api/paas/v4`), `ApiKey`. Провайдер, отличный от `zai`, SHALL требовать явный `BaseUrl` — тихий откат на адрес z.ai запрещён. Эндпоинт SHALL приниматься и со слэшем, и без него. Незастроенный ключ SHALL обнаруживаться только потребителем в момент обращения к модели, не мешая остальному журналу работать.

Traceability ID: requirement-llm-provider-shared-section

#### Scenario: Дефолты без секции

Traceability ID: scenario-llm-provider-defaults

- **WHEN** секция `Llm` в конфигурации отсутствует или пуста
- **THEN** провайдер — `zai`, эндпоинт — `https://api.z.ai/api/paas/v4`, ключ пуст

#### Scenario: Сторонний провайдер без эндпоинта отклоняется

Traceability ID: scenario-llm-provider-foreign-requires-base-url

- **WHEN** `Llm:Provider` указывает провайдера, отличного от `zai`, а `Llm:BaseUrl` не задан
- **THEN** конфигурация отклоняется с явной ошибкой настройки вместо тихого использования адреса z.ai

#### Scenario: Эндпоинт без слэша принимается

Traceability ID: scenario-llm-provider-base-url-slash-normalized

- **WHEN** `Llm:BaseUrl` задан без завершающего слэша (например `https://api.z.ai/api/paas/v4`)
- **THEN** запросы к провайдеру уходят на тот же адрес, что и со слэшем

#### Scenario: Незастроенный ключ не ломает журнал

Traceability ID: scenario-llm-provider-missing-key-lazy

- **WHEN** `Llm:ApiKey` не задан, а потребитель обращается к модели
- **THEN** потребитель сообщает об ошибке настройки в момент обращения
- **AND** остальной журнал продолжает работать

### Requirement: Рабочие модели задаются подсекциями Chat и Hint

Рабочая модель каждого LLM-потребителя SHALL задаваться своей подсекцией секции `Llm`: чат консультанта — `Chat` (дефолт `glm-5.3`), изложение сводок подсказок — `Hint` (дефолт `glm-5.3-flash`). Подсекции SHALL задавать только модель; эндпоинт и ключ общие для всех потребителей. Смена модели SHALL выполняться правкой конфигурации без правки кода.

Traceability ID: requirement-llm-model-subsections

#### Scenario: Смена модели чата без кода

Traceability ID: scenario-llm-models-chat-switch

- **WHEN** конфигурация задаёт `Llm:Chat:Model`, отличный от дефолта
- **THEN** чат консультанта использует указанную модель без правки кода

#### Scenario: Смена модели изложения без кода

Traceability ID: scenario-llm-models-hint-switch

- **WHEN** конфигурация задаёт `Llm:Hint:Model`, отличный от дефолта
- **THEN** изложение сводок подсказок использует указанную модель без правки кода

#### Scenario: Пустая подсекция даёт дефолт модели

Traceability ID: scenario-llm-models-blank-defaults

- **WHEN** подсекция `Chat` или `Hint` отсутствует или её `Model` пуст
- **THEN** потребитель работает на дефолтной модели (`glm-5.3` для `Chat`, `glm-5.3-flash` для `Hint`)
