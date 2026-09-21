# Протокол приёмки

- **Дата прогона:** 2026-09-21
- **Команда:** `dotnet test`
- **Итог:** пройдено 469, не пройдено 0, пропущено 0 (всего 469, длительность 11 s)
- **Тестовые данные:** bUnit-рендер настоящих Razor-компонентов (каркас `MainLayout` и страницы) над настоящими use-case сервисами домена (`ConstructionService`, `TradeBindingService`) и sealed read-моделями, поднятых на временной SQLite-базе с применёнными миграциями; заглушка провайдера марок для сценария сбоя; `JournalChangeSignal` со счётчиком срабатываний для проверки бейджа; реальные символы BTC/ETH с фиксированными моментами времени.

## Чек-лист по сценариям `specs/ui/screens/spec.md`

### Requirement: Каркас приложения — панель, вкладки, транзитная вкладка конструкции

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-journal-total-always-visible` | `AppFrameTests.TryIfJournalTotalIsVisibleOnEveryScreen`; `JournalMetricsReadModelTests.TryIfJournalTotalSumsConstructionTotals` | ✅ пройден |
| `scenario-inbox-badge-counts-unassigned` | `FrameNavigationTests.TryIfInboxBadgeCountsUnassignedTrades`; `FrameNavigationTests.TryIfInboxBadgeDisappearsWhenAllTradesBound` | ✅ пройден |
| `scenario-transit-tab-closes-to-list` | `FrameNavigationTests.TryIfTransitTabCloseReturnsToConstructionList`; `FrameNavigationTests.TryIfStaticNavigationKeepsTransitTabUntilClosed` | ✅ пройден |

Дополнительно на уровне требования: `AppFrameTests.TryIfStaticTabsOfferNavigationBetweenScreens`, `FrameNavigationTests.TryIfOpenedConstructionGetsNamedTransitTabWithStatusDot`.

### Requirement: Экран «Конструкции» — сводка журнала и таблица конструкций

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-list-row-opens-detail` | `ConstructionListScreenTests.TryIfRowClickOpensConstructionDetail` | ✅ пройден |
| `scenario-archived-not-in-list` | `ConstructionListReadModelTests.TryIfArchivedConstructionHiddenFromListAndCounter` | ✅ пройден |
| `scenario-list-marks-failure-indicated` | `ConstructionListReadModelTests.TryIfMarkFailurePassesFromProviderStubToScreenData`; `ConstructionListScreenTests.TryIfMarkFailureIndicatedInUnrealizedColumns` | ✅ пройден |

Дополнительно на уровне требования: `ConstructionListScreenTests.TryIfSummaryShowsJournalTotalMarksAndCounts`, `TryIfTableShowsMetricColumnsAndStatus`, `TryIfToolbarOffersSyncAndInboxCommandsWithCount`, `TryIfSyncCommandRunsShowsResultAndRefreshesList`, `TryIfInboxCommandNavigatesToInboxScreen`.

### Requirement: Экран деталей конструкции — сводка и таблицы записей

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-detail-summary-metrics-period` | `ConstructionDetailReadModelTests.TryIfSummaryJoinsMetricsPeriodMarksAndComment`; `ConstructionDetailScreenTests.TryIfSummaryShowsMetricsPeriodAndMarks`; `ConstructionDetailScreenTests.TryIfSummaryShowsPeriodWithDatesAndDuration` | ✅ пройден |
| `scenario-detail-closing-entries-shown` | `ConstructionDetailReadModelTests.TryIfClosingEntriesListedWithKindSymbolAndAmount`; `ConstructionDetailScreenTests.TryIfClosingEntriesListedWithKindSymbolAndAmount` | ✅ пройден |
| `scenario-detail-empty-table-message` | `ConstructionDetailReadModelTests.TryIfEmptyConstructionGivesFourEmptyTables`; `ConstructionDetailScreenTests.TryIfEmptyTablesShowExplicitMessages` | ✅ пройден |

Дополнительно на уровне требования: `ConstructionDetailScreenTests.TryIfConstructionCommentShownBesideSummary`, `ConstructionDetailReadModelTests.TryIfMarkFailureDegradesUnrealizedAndMarksOnly`.

### Requirement: Действия над конструкцией в деталях

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-detail-capital-change-percent-only` | `ConstructionDetailScreenTests.TryIfCapitalChangeUpdatesOnlyPercentages` | ✅ пройден |
| `scenario-detail-delete-refused-with-reason` | `ConstructionDetailScreenTests.TryIfDeleteRefusalShowsReasonAndKeepsConstruction` | ✅ пройден |
| `scenario-detail-status-change-indicated` | `ConstructionDetailScreenTests.TryIfStatusChangeUpdatesBadgeImmediately`; `FrameNavigationTests.TryIfTransitTabStatusDotReflectsStatusChangeFromDetail` | ✅ пройден |

Дополнительно на уровне требования: `ConstructionDetailScreenTests.TryIfRenameSavesNewNameAndShowsItImmediately`, `TryIfArchiveAndReturnChangeIndicationAndCommands`, `TryIfDeleteRequiresConfirmation`, `TryIfDeleteNotOfferedForNonEmptyConstruction`.

### Requirement: Комментарии редактируются на месте своего уровня

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-trade-comment-inline-edit` | `ConstructionDetailScreenTests.TryIfTradeCommentSavesAndShowsImmediately` | ✅ пройден |
| `scenario-position-comment-without-residual-edit` | `ConstructionDetailScreenTests.TryIfPositionCommentEditLeavesResidualReadOnly` | ✅ пройден |

Дополнительно на уровне требования: `ConstructionDetailScreenTests.TryIfConstructionCommentSavesFromHeader`.

### Requirement: Действия над сделками из деталей конструкции

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-detail-return-trade-to-inbox` | `ConstructionDetailScreenTests.TryIfReturnTradeToInboxUnbindsAndRemovesRowFromTable` | ✅ пройден |
| `scenario-detail-move-trade-choose-target` | `ConstructionDetailScreenTests.TryIfMoveTradeOffersTargetChoiceAndBindsToChosen` | ✅ пройден |

Дополнительно на уровне требования: `ConstructionDetailScreenTests.TryIfMoveCancelClosesFormWithoutBinding`.

### Requirement: Ручная пометка закрытия ставится из строки позиции

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-mark-form-defaults-last-mark` | `ConstructionDetailScreenTests.TryIfCloseMarkFormDefaultsToLastKnownMark` | ✅ пройден |
| `scenario-mark-removal-reflects-open` | `ConstructionDetailScreenTests.TryIfMarkRemovalReopensPositionWithFormerResidual` | ✅ пройден |
| `scenario-redundant-closing-entry-warned` | `ConstructionDetailReadModelTests.TryIfRedundantMarkWarningSurfacedToDetail`; `ConstructionDetailScreenTests.TryIfRedundantClosingEntryWarningShown` | ✅ пройден |

Дополнительно на уровне требования: `ConstructionDetailScreenTests.TryIfManualMarkEditedFromClosingEntries`, `ConstructionDetailReadModelTests.TryIfManualMarkRowCarriesMarkId`.

### Requirement: Экран «Входящие» — выбор и привязка сделок

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-inbox-select-all-or-none` | `InboxScreenTests.TryIfSelectAllTogglesAllOrNothing`; `InboxScreenTests.TryIfSelectAllAfterPartialSelectionSelectsEverything` | ✅ пройден |
| `scenario-inbox-batch-bind-clears` | `InboxScreenTests.TryIfBatchBindClearsListSelectionAndRaisesSignal` (сигнал `JournalChangeSignal`, по которому каркас уменьшает бейдж) | ✅ пройден |
| `scenario-inbox-create-construction-from-selected` | `InboxScreenTests.TryIfCreateFromSelectedOpensConstructionAndBindsAllSelected` | ✅ пройден |

Дополнительно на уровне требования: `InboxScreenTests.TryIfTableShowsUnboundTradesWithExchangeAttributes`, `TryIfEmptyInboxShowsExplicitMessage`, `TryIfBindingActionsDisabledWithoutSelection`, `TryIfCreateFormValidatesNameAndCapital`, `TryIfCancelClosesFormsWithoutBindingOrCreating`.

### Requirement: Корректировки PnL живут в деталях конструкции

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-adjustment-added-from-detail` | `ConstructionDetailScreenTests.TryIfAdjustmentAddedFromDetailShowsInTableAndSummary` | ✅ пройден |
| `scenario-adjustment-edited-and-deleted-inline` | `ConstructionDetailScreenTests.TryIfAdjustmentEditedAndDeletedInline` | ✅ пройден |

### Requirement: Экран «Настройки» — подключение, синхронизация, переразбор

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-settings-secret-never-displayed` | `SettingsScreenTests.TryIfSecretIsNeverDisplayed` | ✅ пройден |
| `scenario-settings-sync-log-mode-warnings` | `SettingsScreenTests.TryIfSyncLogShowsModeResultStatusAndWarnings`; `SettingsScreenTests.TryIfFailedRunShowsErrorResultAndStatusInJournal` | ✅ пройден |
| `scenario-settings-reparse-confirmation` | `SettingsScreenTests.TryIfReparseRequiresExplicitConfirmation` | ✅ пройден |

## Замечания прогона

- Пробелов в покрытии не обнаружено: все 27 сценариев десяти требований имеют целевые тесты, связанные с ними `Traceability: openspec:ui/screens#scenario-*`-метками.
- Сценарии с утверждениями «AND» подтверждены дополнительными тестами за пределами основного утверждения: исчезновение бейджа после привязки всех сделок (`TryIfInboxBadgeDisappearsWhenAllTradesBound`), сохранение транзитной вкладки при уходе на другую статическую вкладку (`TryIfStaticNavigationKeepsTransitTabUntilClosed`), пересчёт метрик read-моделью при чтении (`ConstructionDetailReadModelTests`, включая деградацию только нереализованных величин при сбое марок).
- Прочих расхождений между спекой и тестовым покрытием не выявлено; прогон полного набора (469 тестов, включая домен, аналитику и синхронизацию) зелёный.

## Вердикт

Все сценарии `specs/ui/screens/spec.md` пройдены на тестовых данных. Change **add-ui-screens** готов к архивации.
