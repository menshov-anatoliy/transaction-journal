# architecture/solution-structure Specification

## Purpose

Инварианты целевой структуры решения: состав проектов DDD-слоёв, раскладка кода по слоям, направление зависимостей, namespace, зеркальные тесты и неизменность контракта запуска (`--mcp`, имя exe, `App_Data`). Проживает после миграции как контракт структуры для всех последующих change.

## Requirements

### Requirement: Решение состоит из проектов DDD-слоёв с фиксированной раскладкой

Решение SHALL состоять из проектов `TransactionJournal.Domain`, `TransactionJournal.Application`, `TransactionJournal.Infrastructure` и `TransactionJournal` (Web); проект `TransactionJournal.Hints` добавляется отдельным change по ADR-0007. Раскладка SHALL следовать принципу: чистая логика без ввода-вывода — Domain; оркестрация с хранилищем — Application; адаптеры и внешние клиенты — Infrastructure; UI и composition root — Web (ADR-0008).

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

### Requirement: Зависимости проектов направлены внутрь

Ссылки проектов SHALL быть направлены внутрь: Web → Application → Domain; Infrastructure SHALL ссылаться на Application и Domain, реализуя их порты; будущий проект Hints SHALL зависеть только от Domain. Прямая ссылка, нарушающая направление, SHALL отсутствовать в solution (ADR-0008).

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

### Requirement: Domain — POCO без зависимостей инфраструктуры

`TransactionJournal.Domain` SHALL содержать POCO-сущности без EF-атрибутов и fluent-выражений; вся fluent-конфигурация и миграции EF Core SHALL жить в `TransactionJournal.Infrastructure`. Сборка Domain SHALL проходить без ссылок на пакеты EF Core, Bybit-клиента и Ops (ADR-0008).

Traceability ID: requirement-domain-poco-purity

#### Scenario: Конфигурация сущностей целиком в Infrastructure
Traceability ID: scenario-fluent-config-in-infrastructure
- **WHEN** сущность отображается в SQLite
- **THEN** её отображение описано fluent-конфигурацией в Infrastructure
- **AND** файл сущности в Domain не содержит ссылок на EF Core

### Requirement: Namespace приводятся к проектам

Root namespace каждого проекта SHALL совпадать с именем проекта; внутренние подпространства SHALL сохраняться при переносе (например, слой Sync становится `TransactionJournal.Application.Sync`) (ADR-0008).

Traceability ID: requirement-namespaces-follow-projects

#### Scenario: Перенос слоя сохраняет подпространство
Traceability ID: scenario-namespace-move-keeps-subspace
- **WHEN** папка `Sync` переносится из `src/TransactionJournal` в `src/TransactionJournal.Application`
- **THEN** её типы получают namespace `TransactionJournal.Application.Sync`
- **AND** обновляются только `using`-директивы и объявления namespace, без изменения логики

### Requirement: Тесты — один проект с папками, зеркалящими проекты

Тесты SHALL оставаться одним проектом `tests/TransactionJournal.Tests`; папки тестов SHALL зеркалить проекты целевой структуры (`Domain`, `Application`, `Infrastructure`, `Web`) (ADR-0008).

Traceability ID: requirement-tests-mirror-projects

#### Scenario: Тесты перенесённого кода переезжают в зеркальную папку
Traceability ID: scenario-tests-move-to-mirror-folder
- **WHEN** продуктивный код переносится в новый проект
- **THEN** его тесты переносятся в соответствующую зеркальную папку `tests/TransactionJournal.Tests`
- **AND** assertions тестов не изменяются

### Requirement: Контракт запуска приложения не изменяется

Реструктуризация SHALL сохранять имя исполняемого файла, расположение `App_Data` и режимы запуска: `--mcp` остаётся режимом того же exe с ранним ветвлением до построения веб-хоста (ADR-0003).

Traceability ID: requirement-startup-contract-unchanged

#### Scenario: Режим --mcp ветвится до веб-хоста
Traceability ID: scenario-mcp-branches-before-web-host
- **WHEN** exe запускается с аргументом `--mcp`
- **THEN** ветвление происходит до построения веб-хоста, как до реструктуризации

#### Scenario: Имя exe и App_Data прежние
Traceability ID: scenario-exe-name-appdata-unchanged
- **WHEN** решение собрано после реструктуризации
- **THEN** имя выходного исполняемого файла и путь `App_Data` совпадают с прежними

### Requirement: Миграция идёт шагами с зелёными тестами и без изменения поведения

Реструктуризация SHALL выполняться последовательными шагами Domain → Infrastructure → Application → Web; после каждого шага решение SHALL собираться, а тесты — проходить без правки ассертов (допустимы только переносы файлов и обновления namespace). Изменение поведения, обнаруженное по ходу миграции, SHALL фиксироваться отдельным change, а не правкой внутри шага ([#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31)).

Traceability ID: requirement-migration-steps-keep-green

#### Scenario: Каждый шаг оставляет решение зелёным
Traceability ID: scenario-each-step-green
- **WHEN** шаг миграции завершён (перенос очередного слоя завершён и закоммичен)
- **THEN** `dotnet build` решения проходит и весь тестовый набор зелёный
- **AND** ассерты тестов идентичны исходным — меняются только переносы и namespace

#### Scenario: Находка поведения уходит в отдельный change
Traceability ID: scenario-behavior-finding-separate-change
- **WHEN** при переносе обнаруживается дефект или недокументированное поведение
- **THEN** оно не исправляется внутри шага миграции
- **AND** на него заводится отдельный change с собственной дельтой
