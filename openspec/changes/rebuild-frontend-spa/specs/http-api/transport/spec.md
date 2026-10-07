# http-api/transport — транспортные принципы API нового SPA

## ADDED Requirements

### Requirement: Единая версионированная точка входа JSON API
Traceability ID: requirement-http-api-single-entry

Новый SPA SHALL получать все данные и выполнять все операции журнала через один HTTP API существующего .NET-приложения: JSON поверх HTTP, версия API SHALL присутствовать в маршруте. Отдельных транспортов (кроме SSE-канала чата) и прямого доступа SPA к базе SHALL не быть ([ADR-0010](../../../../../docs/adr/0010-frontend-spa-react-stack.md), карта [#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50)).

#### Scenario: Все разделы SPA работают через один API
Traceability ID: scenario-spa-served-through-single-api

- **WHEN** SPA открывает любой раздел (Конструкции, Карточка, Агент, Входящие, Подсказки, Синхронизация)
- **THEN** все запросы данных и команды идут через единый версионированный JSON API одного хоста

#### Scenario: Версия API присутствует в маршруте
Traceability ID: scenario-api-version-in-route

- **WHEN** клиент обращается к любому эндпоинту API
- **THEN** маршрут содержит сегмент версии, позволяющий эволюционировать контракт без ломки выпущенного SPA

### Requirement: Контракт API публикуется как OpenAPI-описание
Traceability ID: requirement-http-api-openapi-description

Схема API SHALL автоматически публиковаться в виде OpenAPI-документа, пригодного для генерации типизированного клиента SPA и ревью контрактов эндпоинтов по разделам.

#### Scenario: OpenAPI-схема доступна из приложения
Traceability ID: scenario-openapi-schema-published

- **WHEN** приложение запущено
- **THEN** OpenAPI-документ текущей версии API доступен по известному маршруту и соответствует фактическим эндпоинтам

### Requirement: Ответы ИИ-помощника стримятся по SSE
Traceability ID: requirement-http-api-sse-chat-streaming

Генерация ответа ИИ-помощника SHALL передаваться в SPA серверными событиями (SSE): токены SHALL приходить потоком, начало/завершение/ошибка SHALL быть событиями протокола; polling по HTTP-каналу чата SHALL отсутствовать.

#### Scenario: Токены ответа приходят потоком
Traceability ID: scenario-chat-tokens-stream-over-sse

- **WHEN** владелец отправляет сообщение в чате агента
- **THEN** SPA получает ответ ИИ-помощника потоковыми событиями SSE по открытому соединению

#### Scenario: Ошибка генерации доставляется событием SSE
Traceability ID: scenario-chat-error-delivered-as-sse-event

- **WHEN** генерация ответа завершается ошибкой провайдера
- **THEN** SPA получает событие ошибки по тому же SSE-соединению и показывает деградацию без потери истории чата
