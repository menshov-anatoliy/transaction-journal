# Протокол приёмки

- **Дата прогона:** 2026-09-20
- **Команда:** `dotnet test TransactionJournal.sln`
- **Итог:** пройдено 377, не пройдено 0, пропущено 0 (всего 377)
- **Тестовые данные:** фиктивный HTTP-транспорт с зафиксированными ответами тикеров официальной документации Bybit, мок источника свежих марок, SQLite-база во временной папке, виртуальные часы, реальные символы BTC/ETH из документации биржи.

## Чек-лист по сценариям `specs/analytics/performance/spec.md`

### Requirement: Реализованный PnL — собственный расчёт FIFO с комиссиями

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-fifo-matches-chronologically` | `PositionFifoEngineTests.TryIfOppositeEntriesCloseOldestOpenPartsChronologically`; `PositionFifoEngineTests.TryIfShortLayersCloseByBuyBackChronologically` | ✅ пройден |
| `scenario-fees-reduce-result` | `PositionFifoEngineTests.TryIfExecutionFeesReduceResult`; `PositionFifoEngineTests.TryIfUsdcFeesEnterResultAtParity` | ✅ пройден |
| `scenario-delivery-closing-enters-fifo` | `PositionFifoEngineTests.TryIfDeliveryClosingEntersFifoAtIntrinsicValue` | ✅ пройден |
| `scenario-funding-excluded` | `PositionFifoEngineTests.TryIfFundingPaymentsDoNotEnterResult` | ✅ пройден |
| `scenario-rebinding-recomputes-both` | `AnalyticsOnReadTests.TryIfTradeRebindingRecomputesBothConstructions` — добавлен на приёмке | ✅ пройден |

### Requirement: Нереализованный PnL оценивается по текущим маркам на момент запроса

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-open-residual-valued-at-request` | `UnrealizedPnlMarkEvaluatorTests.TryIfOpenResidualIsValuedAtMarkOnRequest` | ✅ пройден |
| `scenario-mark-failure-nulls-unrealized-only` | `UnrealizedPnlMarkEvaluatorTests.TryIfMarkFailureNullsOnlyUnrealizedPart`; `UnrealizedPnlMarkEvaluatorTests.TryIfNetworkFailureNullsUnrealizedPart`; `UnrealizedPnlMarkEvaluatorTests.TryIfTimeoutNullsUnrealizedPart`; `UnrealizedPnlMarkEvaluatorTests.TryIfMissingInstrumentMarkDegradesUnrealizedOnly`; `ConstructionMetricsCalculatorTests.TryIfUnevaluatedResidualNullsOnlyUnrealizedPart` | ✅ пройден |

### Requirement: Провайдер марок — публичные тикеры с кэшем последней марки

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-public-tickers-no-auth` | `BybitTickersClientTests.TryIfTickersParseRecordedResponsesIntoMarksWithoutAuthentication` | ✅ пройден |
| `scenario-last-known-mark-serves-manual-default` | `InstrumentMarkProviderTests.TryIfLastKnownMarkServesAsManualCloseMarkDefault` | ✅ пройден |
| `scenario-mark-cache-timestamped` | `InstrumentMarkProviderTests.TryIfFreshMarkStampesCacheWithPriceAndReceivedTime`; миграция кэша марок — `JournalDbContextMigrationTests` (requirement-level) | ✅ пройден |

### Requirement: Метрики позиции выводятся из её записей

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-position-aggregates` | `PositionMetricsCalculatorTests.TryIfPositionShowsAggregatesOfItsOwnEntries` | ✅ пройден |
| `scenario-open-position-average-and-mark` | `PositionMetricsCalculatorTests.TryIfOpenPositionShowsAveragePriceWithoutCloseDate` (средняя из непокрытых слоёв); `UnrealizedPnlMarkEvaluatorTests.TryIfOpenResidualIsValuedAtMarkOnRequest` (марка на момент запроса); `UnrealizedPnlMarkEvaluatorTests.TryIfMarkFailureNullsOnlyUnrealizedPart` (деградация марки в null вместе с нереализованной частью) | ✅ пройден |
| `scenario-closed-position-has-no-unrealized` | `PositionMetricsCalculatorTests.TryIfClosedPositionHasNoUnrealizedPart`; `UnrealizedPnlMarkEvaluatorTests.TryIfClosedPositionsRequireNoMarks` | ✅ пройден |
| `scenario-reopen-updates-dates` | `PositionMetricsCalculatorTests.TryIfReopeningShiftsPositionDates` | ✅ пройден |

### Requirement: Метрики конструкции суммируют позиции и корректировки

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-construction-total-includes-adjustments` | `ConstructionMetricsCalculatorTests.TryIfConstructionTotalIncludesAdjustments` | ✅ пройден |
| `scenario-percent-from-current-capital` | `ConstructionMetricsCalculatorTests.TryIfPercentsAreComputedFromCurrentCapital`; `ConstructionMetricsCalculatorTests.TryIfZeroCapitalLeavesPercentsNull`; правка капитала очередным чтением — `AnalyticsOnReadTests.TryIfInputEditsAreReflectedByNextRead` | ✅ пройден |
| `scenario-construction-dates-derived` | `ConstructionMetricsCalculatorTests.TryIfConstructionDatesAreDerivedFromEntries` | ✅ пройден |
| `scenario-open-construction-duration-to-now` | `ConstructionMetricsCalculatorTests.TryIfOpenConstructionDurationCountsToNow` | ✅ пройден |

### Requirement: Аналитика вычисляется при чтении и не хранится

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-no-stale-results` | `AnalyticsOnReadTests.TryIfInputEditsAreReflectedByNextRead` — добавлен на приёмке: правки пометки, корректировки, капитала, снятие и возврат привязки отражаются очередным чтением поверх реального SQLite-хранилища | ✅ пройден |
| `scenario-cache-transparent` | `AnalyticsOnReadTests.TryIfRepeatedReadsMatchRecomputedValues` — добавлен на приёмке | ✅ пройден |

## Замечания прогона

- Для подтверждения производности на стыке с хранилищем добавлен интеграционный класс `AnalyticsOnReadTests`: композиция читающего слоя (сырьё синхронизации → материализация → потоки позиций по правилам read-модели → FIFO-метрики позиции → метрики конструкции) пересобирается при каждом чтении поверх реальной базы; правки входных данных проходят через доменные сервисы.
- При подготовке прогона обнаружен единственный пробел трассируемости: сценарий `scenario-open-position-average-and-mark` доказывался тремя тестами без прямой сценарной метки. Метки добавлены к подтверждающим блокам, поведение не менялось.
- Сценарий `scenario-rebinding-recomputes-both` ранее покрывался доменным уровнем (`TradeBindingService`, change `add-core-domain`); на приёмке закрыт аналитической проверкой пересчёта обеих конструкций.
- Прочих расхождений между спекой и тестовым покрытием не выявлено; все 20 сценариев шести требований подтверждены зелёными тестами.

## Вердикт

Все сценарии `specs/analytics/performance/spec.md` пройдены на тестовых данных. Change **add-analytics** готов к архивации.
