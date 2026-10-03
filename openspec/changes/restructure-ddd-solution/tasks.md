# Tasks

## 1. Шаг Domain — каркас решения и чистое ядро

- [ ] 1.1 Создать проекты `src/TransactionJournal.Domain` (net9.0, библиотека) и включить его в solution; корневой namespace — `TransactionJournal.Domain`. Проверить: `dotnet build` решения зелёный, новый проект без пакетных ссылок.
- [ ] 1.2 Перенести POCO-сущности папки `Domain` и чистые движки/парсеры (ConstructionAssembler, ConstructionNameBuilder, PositionFifoEngine, OptionSymbolParser, LinearSymbolParser) с сохранением подпространств; обновить namespace и `using`. Проверить: сборка Domain без ссылок на EF/Bybit/Ops (`requirement-domain-poco-purity`).
- [ ] 1.3 Объявить в Domain store-порты (IJournalRawSnapshotStore, IInstrumentReferenceStore, probe-интерфейсы), временно реализовав их в текущем проекте, чтобы решение собиралось. Проверить: сборка и `dotnet test` зелёные без правки ассертов (`requirement-migration-steps-keep-green`).
- [ ] 1.4 Перенести тесты перенесённого кода в зеркальную папку `tests/TransactionJournal.Tests/Domain`, обновив только namespace. Проверить: полный `dotnet test` зелёный, ассерты не менялись.

## 2. Шаг Infrastructure — EF, Bybit, Ops

- [ ] 2.1 Создать проект `src/TransactionJournal.Infrastructure` со ссылками на Domain; перенести `Data` (JournalDbContext, fluent-конфигурации, миграции), `Bybit`-клиент и `Ops`; namespace — `TransactionJournal.Infrastructure.*`. Проверить: `dotnet build` решения и `dotnet test` зелёные (`scenario-ef-bybit-ops-in-infrastructure`).
- [ ] 2.2 Перенести реализации store-портов Domain в Infrastructure. Проверить: `dotnet test --filter` по тестам портов зелёный, поведение неизменно.
- [ ] 2.3 Перенести тесты EF/Bybit/Ops в `tests/TransactionJournal.Tests/Infrastructure`. Проверить: полный `dotnet test` зелёный без правки ассертов.

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
