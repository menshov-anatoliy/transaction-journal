# Tasks

## 1. Шаг Domain — каркас решения и чистое ядро

- [x] 1.1 Создать проекты `src/TransactionJournal.Domain` (net9.0, библиотека) и включить его в solution; корневой namespace — `TransactionJournal.Domain`. Проверить: `dotnet build` решения зелёный, новый проект без пакетных ссылок.
- [x] 1.2 Перенести POCO-сущности папки `Domain` и чистые движки/парсеры (ConstructionAssembler, ConstructionNameBuilder, PositionFifoEngine, OptionSymbolParser, LinearSymbolParser) с сохранением подпространств; обновить namespace и `using`. Проверить: сборка Domain без ссылок на EF/Bybit/Ops (`requirement-domain-poco-purity`).
- [x] 1.3 Объявить в Domain store-порты (IJournalRawSnapshotStore, IInstrumentReferenceStore, probe-интерфейсы), временно реализовав их в текущем проекте, чтобы решение собиралось. Проверить: сборка и `dotnet test` зелёные без правки ассертов (`requirement-migration-steps-keep-green`).
- [x] 1.4 Перенести тесты перенесённого кода в зеркальную папку `tests/TransactionJournal.Tests/Domain`, обновив только namespace. Проверить: полный `dotnet test` зелёный, ассерты не менялись.
- [x] 1.5 Отклонение от чистой механики (Risks design.md): вместе с портами в Domain перенесены их контрактные типы (`JournalRawSnapshot`, `DeliveryRecordKey`, `RawInstrument`/`RawExecution`/`RawDelivery`/`SyncRun`, `BybitInstrumentInfo`) — иначе тип остаётся «в слое своей зависимости» и Domain не может описать контракт порта. В Designer/Snapshot-файлах EF-миграций обновлены строки имён сущностей `TransactionJournal.Data.*` → `TransactionJournal.Domain.Data.*`; схема БД и поведение не менялись. Реализации портов (`JournalSyncStore`) остаются в текущем проекте до шага 2.2. Отклонение войдёт в отчёт 5.2.

## 2. Шаг Infrastructure — EF, Bybit, Ops

- [x] 2.1 Создать проект `src/TransactionJournal.Infrastructure` со ссылками на Domain; перенести `Data` (JournalDbContext, fluent-конфигурации, миграции), `Bybit`-клиент и `Ops`; namespace — `TransactionJournal.Infrastructure.*`. Проверить: `dotnet build` решения и `dotnet test` зелёные (`scenario-ef-bybit-ops-in-infrastructure`).
- [x] 2.2 Перенести реализации store-портов Domain в Infrastructure. Проверить: `dotnet test --filter` по тестам портов зелёный, поведение неизменно.
- [x] 2.3 Перенести тесты EF/Bybit/Ops в `tests/TransactionJournal.Tests/Infrastructure`. Проверить: полный `dotnet test` зелёный без правки ассертов.
- [x] 2.4 Отклонения от чистой механики (Risks design.md, D6): (а) вместе с реализацией `JournalSyncStore` в Domain перенесены ещё 5 storage-портов из `TransactionJournal.Sync` (IExecutionSyncStateStore, IRawExecutionBatchWriter, IRawDeliveryBatchWriter, ISyncRunJournal, IOptionRawBaseCoinReader) и их контрактные типы (RawExecutionBatchResult, RawDeliveryBatchResult, SyncRunWarnings, BybitExecution, BybitDeliveryRecord) — иначе Infrastructure, ссылающийся на Web, образует цикл. (б) Декоратор `BackupGuardedSyncService` остался в Web-проекте: он зависит от порта `IJournalSyncService`/`JournalSyncResult`, чья реализация и результат тянут цепочку Sync/Materialization-типов, непереносимую в Domain; на шаге 3 декоратор уедет в Application вместе с оркестрацией. (в) В Infrastructure добавлены `InternalsVisibleTo` для Web и тестов: `BybitJson` и другие internal-типы клиента Bybit в монолите были доступны всей сборке. (г) Пакеты EF Sqlite и Polly переехали в Infrastructure; Web сохраняет EF Sqlite (UseSqlite в Program.cs) и EF Design (dotnet-ef). Отклонения войдут в отчёт 5.2. Диагностический тест сверки на живых данных (`BybitStatementDiagnosticRunTests`) красный и на базовом коммите — расхождение данных машины (16 записей), к переносу не относится.

## 3. Шаг Application — оркестрация, сервисы, read-модели

- [ ] 3.1 Создать проект `src/TransactionJournal.Application` со ссылкой на Domain; перенести оркестрацию `Materialization`/`Sync`/`Analytics`, use-case-сервисы (ConstructionService, TradeBindingService, CommentService, PnLAdjustmentService, ManualCloseMarkService) и read-модели (Inbox/PositionReadModel и модели страниц); namespace — `TransactionJournal.Application.*`. Проверить: сборка и тесты зелёные (`scenario-usecases-in-application`).
- [ ] 3.2 Объявить в Application шлюзы-порты (IBybitHistoryGateway, IFreshInstrumentMarkSource) и read-only контракт чтения; реализации остаются в Infrastructure. Проверить: направление зависимостей Web → Application → Domain, Infrastructure → порты (`requirement-dependencies-point-inward`).
- [ ] 3.3 Перенести тесты в `tests/TransactionJournal.Tests/Application`. Проверить: полный `dotnet test` зелёный без правки ассертов.

## 4. Шаг Web — Components и composition root

- [ ] 4.1 Перенести `Components`, `Properties`, `wwwroot` и `Program.cs` в проект `TransactionJournal` (Web); composition root регистрирует Application-сервисы и Infrastructure-адаптеры. Проверить: `dotnet build`, имя exe и `App_Data` прежние (`scenario-exe-name-appdata-unchanged`).
- [ ] 4.2 Проверить контракт запуска: запуск без аргументов поднимает веб-интерфейс, запуск с `--mcp` ветвится до веб-хоста (`scenario-mcp-branches-before-web-host`). Проверить: ручной запуск обоих режимов.
- [ ] 4.3 Перенести UI-тесты в `tests/TransactionJournal.Tests/Web`; прогнать весь набор. Проверить: полный `dotnet test` зелёный, ассерты идентичны исходным.

## 5. Сквозная сверка реструктуризации

- [ ] 5.1 Сверить итоговую структуру с инвариантами `architecture/solution-structure` (пять проектов, направление ссылок, namespace, зеркальные тесты) и обновить документацию сборки/CI на новые пути. Проверить: `openspec validate restructure-ddd-solution --strict`, сборка и полный тестовый прогон зелёные.
- [ ] 5.2 Зафиксировать в отчёте шага все отклонения от чистой механики (разрывы связей по D6/Risks) с обоснованием. Проверить: каждое отклонение либо закрыто отдельным change, либо обосновано как перенос без изменения поведения.
