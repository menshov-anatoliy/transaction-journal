# Tasks

## 1. Окно прохода с базовым активом

- [x] 1.1 Добавить nullable-поле `BaseCoin` в `ExecutionWindow` и прокладывать его в `BybitExecutionListQuery.BaseCoin` внутри `ExecutionWindowPass`; проверить тестом `ExecutionWindowPassTests`: запрос содержит `baseCoin` когда поле задано и не содержит когда null
- [x] 1.2 Снабдить затронутые смысловые блоки traceability-метками `openspec:sync/bybit-history#requirement-option-base-coin-coverage`; проверить rg-поиском, что каждая ссылка разрешается в `Traceability ID` delta-spec

## 2. Источник списка базовых активов

- [x] 2.1 Создать `IOptionBaseCoinSource` и реализацию над `IBybitInstrumentSource`: листать `instruments-info?category=option` страницами `limit=1000` курсором до исчерпания, собрать distinct `baseCoin`, объединить с конфигурируемым списком дополнительных активов, вернуть в детерминированном порядке; проверить тестами: несколько страниц курсором, пустая доска, дедуп повторных активов, доп. актив из конфигурации попадает в список
- [x] 2.2 Провести зависимость в DI (`Program.cs`) как transient-реализацию над шлюзом; проверить сборкой `dotnet build`

## 3. Движок синхронизации исполнений

- [x] 3.1 Научить `ExecutionCategorySync` строить области прохода: linear — одна область без фильтра, option — область на каждый актив из `IOptionBaseCoinSource`; обойти области в режимах backfill и инкремента, водяной знак фиксировать после успешного прохода всех активов; проверить тестом `ExecutionCategorySyncTests`: backfill option отправляет запросы с каждым активом из заглушки источника, линейная категория — без `baseCoin`
- [x] 3.2 Добавить `MaxBackfillDepthMs` в `ExecutionCategorySyncOptions` (дефолт 730 дней): backfill листает окна назад до пола глубины, последнее окно усекается до пола; удалить остановку по пустому окну и кламп окна по `BackfillBoundaryMs`, границу продолжать записывать как факт; проверить тестами: пустые окна не завершают проход (scenario-trading-gap-does-not-truncate-history), проход останавливается на полу (scenario-backfill-pages-until-exhaustion), короткая `MaxBackfillDepthMs` в опциях ускоряет тесты
- [x] 3.3 Обновить существующие тесты `ExecutionCategorySync`/`ExecutionHistorySync` под новую семантику остановки (тесты «исчерпание пустым окном» заменить на «пол глубины»); проверить `dotnet test` без падений

## 4. Delivery-движок

- [x] 4.1 Добавить `MaxBackfillDepthMs` в `DeliveryCategorySyncOptions` (дефолт 730 дней) с той же семантикой пола глубины: без остановки по пустому окну; проверить тестами `DeliveryCategorySyncTests`/`DeliveryWindowPassTests`: перерыв в delivery-истории не обрезает проход, остановка на полу
- [x] 4.2 Снабдить смысловые блоки traceability-метками `openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion`; проверить разрешение ссылок rg-поиском

## 5. Сброс состояния категории

- [x] 5.1 Расширить `IExecutionSyncStateStore` методом `ResetAsync(category)` и реализовать в `JournalSyncStore` удалением строки `SyncStates` категории; проверить тестом `JournalSyncStoreTests`: сброс удаляет состояние только своей категории, сырые записи и запуски остаются
- [x] 5.2 Добавить на страницу `Sync.razor` блок обслуживания с командой сброса состояния категории (linear/option), подтверждением и блокировкой на время синка; проверить UI-тестом `SyncPageTests`: команда требует подтверждения, вызывает сброс выбранной категории, скрыта/заблокирована при выполнении синка
- [x] 5.3 Проверить интеграцией через `JournalSyncServiceTests`: после сброса состояния option очередной `SyncAsync` выполняет backfill категории и не создаёт дублей известных записей (scenario-reset-forces-backfill)

## 6. Конфигурация и проводка

- [x] 6.1 Читать `Sync:MaxBackfillDepthDays` (дефолт 730) и `Sync:ExtraOptionBaseCoins` (дефолт пуст) в `Program.cs` при сборке опций движков; проверить: приложение стартует без секции `Sync` в конфигурации, значения передаются в опции
- [x] 6.2 Запустить `dotnet test` по всем тестам проекта и убедиться в зелёном прогоне; проверить grep-аудитом, что новые traceability-метки сопровождаются человекочитаемыми комментариями
