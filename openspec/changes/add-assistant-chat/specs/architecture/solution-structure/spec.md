# Spec Delta

## MODIFIED Requirements

### Requirement: Решение состоит из проектов DDD-слоёв с фиксированной раскладкой

Решение SHALL состоять из проектов `TransactionJournal.Domain`, `TransactionJournal.Application`, `TransactionJournal.Infrastructure` и `TransactionJournal` (Web); проект `TransactionJournal.Hints` добавляется отдельным change по ADR-0007; проект `TransactionJournal.Consultations` (чат консультаций) — по [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md). Раскладка SHALL следовать принципу: чистая логика без ввода-вывода — Domain; оркестрация с хранилищем — Application; адаптеры и внешние клиенты — Infrastructure; UI и composition root — Web (ADR-0008).

Traceability ID: requirement-solution-five-projects

#### Scenario: Чистые движки и парсеры живут в Domain
Traceability ID: scenario-pure-engines-in-domain
- **WHEN** код не совершает ввода-вывода (движки сборки конструкций, FIFO, построение имён, парсеры символов)
- **THEN** он размещается в `TransactionJournal.Domain`
- **AND** store-порты домена (интерфейсы хранения снапшотов журнала и справочника инструментов) также живут в Domain

#### Scenario: EF, Bybit и Ops живут в Infrastructure
Traceability ID: scenario-ef-bybit-ops-in-infrastructure
- **WHEN** код зависит от EF Core, HTTP-клиента Bybit или файловых операций Ops
- **THEN** он размещается в `TransactionJournal.Infrastructure`

#### Scenario: Use-case-сервисы и read-модели живут в Application
Traceability ID: scenario-usecases-in-application
- **WHEN** сервис читает или пишет хранилище в рамках use-case или собирает read-модель
- **THEN** контракт потребителя (интерфейс use-case-сервиса или read-модели с его DTO) объявлен в `TransactionJournal.Application`
- **AND** реализация, читающая SQLite, размещается в `TransactionJournal.Infrastructure` и регистрируется в composition root Web
- **AND** слои Materialization, Sync и Analytics входят в Application как оркестрация

#### Scenario: Blazor и Program.cs живут в Web
Traceability ID: scenario-blazor-in-web
- **WHEN** код является Blazor-компонентом, страницей или точкой входа приложения
- **THEN** он размещается в проекте `TransactionJournal`
- **AND** composition root (регистрация зависимостей) остаётся в `Program.cs` Web

#### Scenario: Чат консультаций живёт в собственном проекте окружения
Traceability ID: scenario-consultations-own-environment-project
- **WHEN** реализуется чат консультаций (агентный цикл, инструменты, стриминг-ответ)
- **THEN** он размещается в `TransactionJournal.Consultations`
- **AND** его порты объявлены в самом проекте, а адаптеры — в `TransactionJournal.Infrastructure` и composition root Web

### Requirement: Зависимости проектов направлены внутрь

Ссылки проектов SHALL быть направлены внутрь: Web → Application → Domain; Infrastructure SHALL ссылаться на Application и Domain, реализуя их порты; проекты окружений Hints и Consultations SHALL зависеть только от Domain; Consultations SHALL не ссылаться на Hints — доступ к корпусу правил идёт через собственный порт с адаптером в composition root. Прямая ссылка, нарушающая направление, SHALL отсутствовать в solution ([ADR-0008](../../../../../../docs/adr/0008-ddd-solution-structure.md), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-dependencies-point-inward

#### Scenario: Нарушение направления невозможно сборкой
Traceability ID: scenario-dependency-violation-fails-build
- **WHEN** проект нижнего слоя получает прямую ссылку на верхний (например, Domain → Infrastructure)
- **THEN** такая ссылка отсутствует в solution, и попытка её добавить обнаруживается ревью структуры как ошибка

#### Scenario: Read-only контракт чтения — порты в Application, реализация в Infrastructure
Traceability ID: scenario-readonly-ports-in-application
- **WHEN** внешний потребитель читает журнал (режим `--mcp` по ADR-0003, R3-сессия по ADR-0007)
- **THEN** контракт выражен портами в `TransactionJournal.Application`
- **AND** реализация чтения из SQLite живёт в `TransactionJournal.Infrastructure`

#### Scenario: Окружения не связываются друг с другом
Traceability ID: scenario-environments-not-linked
- **WHEN** проекту Consultations нужны данные корпуса правил из Hints
- **THEN** прямой ссылки между проектами нет
- **AND** корпус подключается портом Consultations с адаптером в composition root
