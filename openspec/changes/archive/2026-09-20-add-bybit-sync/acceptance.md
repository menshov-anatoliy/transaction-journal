# Протокол приёмки

- **Дата прогона:** 2026-09-20
- **Команда:** `dotnet test TransactionJournal.sln`
- **Итог:** пройдено 226, не пройдено 0, пропущено 0 (всего 226)
- **Тестовые данные:** фиктивный HTTP-транспорт и фиктивный шлюз с зафиксированными ответами Bybit, реальные символы BTC/ETH из документации биржи, SQLite-база во временной папке, виртуальные часы.

## Чек-лист по сценариям `specs/sync/bybit-history/spec.md`

### Requirement: Синхронизация запускается вручную и выбирает режим сама

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-first-run-backfill` | `ExecutionCategorySyncTests.TryIfFirstRunWithoutWatermarkChoosesBackfillAndFixesWatermarkAndBoundary`; `DeliveryCategorySyncTests.TryIfFirstRunWithoutWatermarkChoosesBackfillAndFixesWatermark`; `ExecutionHistorySyncTests.TryIfSuccessfulFirstRunWritesBatchesAndCounts`; `JournalSyncServiceTests.TryIfFirstRunBackfillsAllCategoriesAndBuildsProjection`; `SyncPageTests.TryIfCompletedSyncRendersStatusCountersAndWarnings` | ✅ пройден |
| `scenario-subsequent-run-incremental` | `ExecutionCategorySyncTests.TryIfWatermarkPresentChoosesIncrementalWithOverlapAndEarlyStop`; `DeliveryCategorySyncTests.TryIfIncrementalWalksWindowsOnlyDownToWatermarkMinusOverlap`; `JournalSyncServiceTests.TryIfSecondRunIncrementsWithoutDuplicates` | ✅ пройден |

### Requirement: Backfill читает всю доступную историю окнами

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-backfill-pages-until-exhaustion` | `DeliveryCategorySyncTests.TryIfFirstRunWithoutWatermarkChoosesBackfillAndFixesWatermark` (пустое окно завершает проход доставки); остановка прохода исполнения — `ExecutionWindowPassTests` (requirement-level) | ✅ пройден |
| `scenario-backfill-depth-boundary` | `ExecutionCategorySyncTests.TryIfFirstRunWithoutWatermarkChoosesBackfillAndFixesWatermarkAndBoundary`; `ExecutionCategorySyncTests.TryIfBackfillStopsAtFixedBoundaryOnRepeatedFullPass` | ✅ пройден |

### Requirement: Новые записи попадают во «Входящие»

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-new-trades-land-in-inbox` | `TradeMaterializerTests.TryIfIncrementalSyncBringsOnlyNewTradesToInbox` | ✅ пройден |

### Requirement: Повторные синки идемпотентны

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-repeat-sync-no-duplicates` | `JournalSyncServiceTests.TryIfSecondRunIncrementsWithoutDuplicates`; `ExecutionHistorySyncTests.TryIfInterruptedRunResumesFromStopWithoutDuplicates`; `JournalSyncStoreTests.TryIfWriteAsyncInsertsOnlyUnknownExecutionsAndSkipsKnownOnes`; `JournalSyncStoreTests.TryIfDeliveryWriteAsyncInsertsOnlyUnknownKeysAndSkipsKnownOnes`; `TradeMaterializerTests.TryIfRepeatMaterializationKeepsInboxUnchanged`; `DeliveryCategorySyncTests.TryIfOverlappingIncrementalWindowProducesNoDuplicates` | ✅ пройден |
| `scenario-window-overlap-no-duplicates` | `ExecutionWindowPassTests.TryIfWindowPassStopsEarlyOnEntirelyKnownPage`; `DeliveryWindowPassTests.TryIfOverlappingWindowFiltersKnownDeliveriesBySymbolAndDeliveryTime`; `DeliveryCategorySyncTests.TryIfOverlappingIncrementalWindowProducesNoDuplicates` | ✅ пройден |

### Requirement: Необработанные записи хранятся целиком

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-raw-records-persisted-for-reparse` | `JournalSyncStoreTests.TryIfWriteAsyncStoresWholeRecordAsRawJsonWithFetchTime`; `JournalSyncStoreTests.TryIfDeliveryWriteAsyncStoresWholeRecordAsRawJsonWithFetchTime`; `JournalMaterializerTests.TryIfRebuildAfterParseRuleChangeGivesConsistentResult` | ✅ пройден |

### Requirement: Экспирации становятся закрывающими записями

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-itm-delivery-split-across-constructions` | `ExpiryMaterializerTests.TryIfItmDeliverySplitsAcrossConstructionsProportionally` | ✅ пройден |
| `scenario-otm-expiry-auto-close` | `ExpiryMaterializerTests.TryIfOtmExpiryAutoClosesAtZeroPriceInDeliveryTime` | ✅ пройден |
| `scenario-delivery-reconciliation-warning` | `ExpiryMaterializerTests.TryIfDeliveryRplMismatchWarnsWithoutOverridingOwnResult`; `JournalSyncServiceTests.TryIfFirstRunBackfillsAllCategoriesAndBuildsProjection`; `SyncPageTests.TryIfCompletedSyncRendersStatusCountersAndWarnings` | ✅ пройден |

### Requirement: Справочник инструментов строится из биржи

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-new-instrument-registered` | `InstrumentReferenceSyncTests.TryIfUnknownSymbolsAreFetchedBySymbolFilterAndSaved`; `InstrumentResolverTests.ThrowOnUnknownSymbol`; `TradeMaterializerTests.ThrowOnUnknownOptionSymbol` | ✅ пройден |

### Requirement: Лимиты API соблюдаются, ошибки обрабатываются ретраями

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-rate-limit-backoff` | `BybitResilienceTests.TryIfExhaustedLimitStatusDelaysNextRequestUntilReset`; `BybitResilienceTests.TryIfRateLimitRetCodeIsRetriedAfterSecondsPause` | ✅ пройден |
| `scenario-interrupted-sync-resumable` | `ExecutionHistorySyncTests.TryIfInterruptedRunResumesFromStopWithoutDuplicates`; `ExecutionCategorySyncTests.TryIfFreshWindowBatchesArePassedToRawWriterWithProgressRun`; `ExecutionCategorySyncTests.ThrowIfFailedWindowLeavesStateUnfixed`; `DeliveryCategorySyncTests.TryIfFreshWindowBatchesArePassedToRawWriterWithProgressRun`; `DeliveryCategorySyncTests.ThrowIfFailedWindowLeavesStateUnfixed`; `JournalSyncServiceTests.ThrowOnExchangeErrorClosesRunFailedWithoutStateFixation`; `JournalSyncStoreTests.TryIfRunJournalClosesRunWithSuccessOrFailure`; `JournalSyncStoreTests.TryIfWriteAsyncAdvancesProgressRunCounterWithEachBatch`; `JournalSyncStoreTests.TryIfDeliveryWriteAsyncAdvancesProgressRunCounterWithEachBatch`; `SyncPageTests.TryIfFailedSyncShowsErrorAndKeepsButtonForRetry` | ✅ пройден |

### Requirement: Доступ к бирже только на чтение

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-sync-with-read-only-key` | `ReadOnlyAccessAcceptanceTests.TryIfFullSyncUsesOnlyReadOnlyGetEndpoints` — добавлен на приёмке | ✅ пройден |

## Замечания прогона

- При подготовке прогона обнаружен единственный пробел: сценарий `scenario-sync-with-read-only-key` был покрыт только на уровне требования (GET-запрос в `BybitApiClientTests`), без сценарной проверки полного конвейера. Пробел закрыт композитным приёмочным тестом `ReadOnlyAccessAcceptanceTests.TryIfFullSyncUsesOnlyReadOnlyGetEndpoints`: полный запуск оркестратора на production-шлюзе `BybitHistoryGateway` поверх реального подписанного `BybitApiClient` и реального SQLite-хранилища против фиктивного HTTP-транспорта — все запросы синка оказались GET-запросами четырёх read-only эндпоинтов.
- Прочих расхождений между спекой и тестовым покрытием не выявлено; все 15 сценариев девяти требований подтверждены зелёными тестами.

## Вердикт

Все сценарии `specs/sync/bybit-history/spec.md` пройдены на тестовых данных. Change **add-bybit-sync** готов к архивации.
