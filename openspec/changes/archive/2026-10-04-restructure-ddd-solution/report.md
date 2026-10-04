# Отчёт о реструктуризации (шаг 5)

## 5.1 Сверка итоговой структуры с инвариантами `architecture/solution-structure`

| Инвариант | Проверка | Результат |
|---|---|---|
| Пять проектов DDD-слоёв | `TransactionJournal.sln` содержит `TransactionJournal.Domain`, `TransactionJournal.Application`, `TransactionJournal.Infrastructure`, `TransactionJournal` (Web) и `TransactionJournal.Tests`; `Hints` — отдельным change по ADR-0007 | ✓ |
| Направление ссылок внутрь | Application → Domain; Infrastructure → Application + Domain; Web → Application + Domain + Infrastructure; Tests → Web. Обратных ссылок нет | ✓ |
| Domain без пакетов | `TransactionJournal.Domain.csproj` — только `net9.0`, без `PackageReference` и `ProjectReference` | ✓ |
| Namespace = проект | Скриптовой обход всех `.cs`/`.razor` четырёх проектов: каждый namespace начинается с имени проекта; подпространства сохранены (`TransactionJournal.Application.Sync` и т. п.) | ✓ |
| Зеркальные тесты | `tests/TransactionJournal.Tests` содержит `Domain/`, `Application/`, `Infrastructure/`, `Web/` | ✓ |
| Контракт запуска | Имя exe и `App_Data` прежние; веб-режим проверен живым запуском в 4.2 | ✓ |

- `openspec validate restructure-ddd-solution --strict` — валидно.
- `dotnet build TransactionJournal.sln` — без ошибок.
- `dotnet test TransactionJournal.sln` — 809/810 зелёные; единственный красный — `BybitStatementDiagnosticRunTests.TryIfLiveDataReconcilesWithBybitStatement`, диагностический тест сверки на живых данных машины, красный на базовом коммите до миграции (см. 2.4), к переносу не относится.
- Документация/CI: CI (`copilot-setup-steps.yml`) путей проекта не содержит — правки не требует; обновлена устаревшая ссылка на путь `BybitHistoryBoundary` в `docs/research/bybit-api.md` (`src/TransactionJournal/Sync` → `src/TransactionJournal.Application/Sync`). Архивные change (`openspec/changes/archive/**`) не переписывались — это исторические записи.

## 5.2 Отклонения от чистой механики переноса

Все отклонения — разрывы скрытых связей слоёв, всплывших при переносе, и обработаны по Risks design.md: тип уезжает в слой своей зависимости либо зависимость инвертируется портом. Ни одно отклонение не меняет поведение приложения, поэтому отдельные change не требуются; каждое обосновано ниже как перенос без изменения поведения.

### Шаг Domain (1.5)

1. **Контрактные типы store-портов переехали в Domain вместе с портами**: `JournalRawSnapshot`, `DeliveryRecordKey`, `RawInstrument`/`RawExecution`/`RawDelivery`/`SyncRun`, `BybitInstrumentInfo`.
   *Обоснование:* Domain не может описать контракт порта, не владея типами этого контракта; оставить тип «в слое своей зависимости» означало бы потянуть слой-зависимость в Domain. Поведение не менялось — типы перенесены как есть.
2. **Designer/Snapshot-файлы EF-миграций**: строки имён сущностей `TransactionJournal.Data.*` → `TransactionJournal.Domain.Data.*`.
   *Обоснование:* метаданные миграций следуют за namespace сущностей; схема БД и поведение не менялись (проверено прогоном тестов и живой базой).

### Шаг Infrastructure (2.4)

3. **Ещё 5 storage-портов из `TransactionJournal.Sync` переехали в Domain**: `IExecutionSyncStateStore`, `IRawExecutionBatchWriter`, `IRawDeliveryBatchWriter`, `ISyncRunJournal`, `IOptionRawBaseCoinReader`, вместе с контрактными типами `RawExecutionBatchResult`, `RawDeliveryBatchResult`, `SyncRunWarnings`, `BybitExecution`, `BybitDeliveryRecord`.
   *Обоснование:* реализация `JournalSyncStore` уезжала в Infrastructure, который уже ссылается на Application; оставить порты в Sync (Application) — породить цикл Infrastructure → Web. Инверсия через Domain — штатный разрыв связи. Поведение не менялось.
4. **Декоратор `BackupGuardedSyncService` временно остался в Web** (переехал в `Application.Ops` на шаге 3, см. п. 8).
   *Обоснование:* на момент шага 2 декоратор зависел от `IJournalSyncService`/`JournalSyncResult`, чья реализация тянула непереносимую в Domain цепочку Sync/Materialization-типов. Задержка переезда не меняла поведение.
5. **`InternalsVisibleTo` в Infrastructure для Web и тестов**: `BybitJson` и другие internal-типы клиента Bybit в монолите были доступны всей сборке.
   *Обоснование:* сохранение прежней доступности без расширения публичной поверхности API. Поведение и видимость типов не изменились.
6. **Пакеты EF Sqlite и Polly переехали в Infrastructure**; Web сохраняет EF Sqlite (`UseSqlite` в Program.cs) и EF Design (dotnet-ef).
   *Обоснование:* пакеты следуют за кодом, который их использует; состав функциональности не менялся.

### Шаг Application (3.4)

7. **Разрез контракт/реализация (D3)**: интерфейсы use-case-сервисов и read-моделей с их DTO (`SyncRunRow`, `ConstructionHeader`, `ConstructionListData`/`ListItem`, `ConstructionDetailData` со строками таблиц), Bybit-контракты (`BybitJson`, `IBybitCredentialsProvider`, `BybitCredentials`, query/ответные DTO, `BybitApiException`) и backup-порты (`IJournalBackupService`, `IBackupPolicyStore`, `JournalBackupResult`) — в Application; реализации поверх `JournalDbContext` и HTTP-клиента Bybit — в Infrastructure.
   *Обоснование:* без разреза Application был бы привязан к EF, а потребители контрактов — к Infrastructure. Границы типов не менялись, только размещение.
8. **`BackupGuardedSyncService` переехал в `Application.Ops`** — закрытие отложки п. 4.
   *Обоснование:* декоратор — часть оркестрации синка; переезд механический.
9. **Константы конфигурации ключей Bybit выделены в `BybitCredentialsConfig` (`Application.Bybit`)**: раньше жили в статике `ConfigurationBybitCredentialsProvider`, на которую ссылались Program.cs и тесты.
   *Обоснование:* контракт/реализация разрезаны — константы контракта уезжают в Application; ссылки обновлены на новый класс, значения не менялись.
10. **Тесты зеркалят размещение контракт/реализация**: контракты и оркестрация — `tests/Application`, реализации — `tests/Infrastructure`; namespace обновлены на зеркальные папки.
    *Обоснование:* развитие D5 без изменения ассертов.

### Шаг Web (4.2)

11. **Ветвление `--mcp` в коде не реализовано** и не существовало до реструктуризации (проверено git-историей `git log -S '--mcp'`); контракт «как до реструктуризации» сохранён тривиально — аргумент игнорируется, exe поднимает веб-хост.
    *Обоснование:* сценарий `scenario-mcp-branches-before-web-host` описывает контракт как «как до реструктуризации»; поведение не менялось. Сама функция `--mcp` — предмет отдельного change (ADR-0003), не этой миграции.

## Итог

Все отклонения закрыты в рамках миграции и обоснованы как перенос без изменения поведения; находок, требующих отдельных change, не обнаружено. Диагностический тест `BybitStatementDiagnosticRunTests` красный на базовой линии независимо от миграции — расхождение живых данных машины (16 записей), к реструктуризации не относится.
