# Tasks

## 1. Модели хранения

- [x] 1.1 Создать EF-модели `Construction` (имя, статус {открыта, закрыта, архив}, выделенный капитал USDT, комментарий) и `PnLAdjustment` (конструкция, знаковая сумма, комментарий) с миграцией; проверить unit-тестами отказ удаления конструкции с корректировками
- [x] 1.2 Создать EF-модели пользовательских данных: `TradeUserdata` (уникальный `execId`: привязка к конструкции, комментарий), `PositionComment` (конструкция + инструмент), `ManualCloseMark` (конструкция, инструмент, цена, время) с миграцией; проверить unit-тестами уникальность ключей и каскадное удаление осиротевших записей

## 2. Управление конструкциями

- [x] 2.1 Реализовать use-cases создания, переименования, смены статуса и архивации; проверить сценарии «новая конструкция открывается со статусом „открыта“», «переименование не трогает остальное», «архивная скрыта из активных списков, но видна в аналитике»
- [x] 2.2 Реализовать удаление с отказом при наличии сделок или корректировок; проверить сценарий «удаление возможно только без сделок и корректировок»

## 3. Привязка сделок

- [x] 3.1 Реализовать привязку из «Входящих», перенос между конструкциями, возврат во «Входящие» и массовую привязку; проверить unit-тестами инвариант единственной принадлежности во всех четырёх сценариях требования
- [x] 3.2 Реализовать read-модель «Входящие»: сделки без привязки с атрибутами материализатора sync; проверить интеграционным тестом на стыке с `RawExecution`

## 4. Производные позиции и закрытие

- [x] 4.1 Реализовать `PositionReadModel`: чистый остаток по «конструкция × инструмент» из сделок и закрывающих записей sync-слоя; проверить сценарии «остаток вычисляется из сделок при чтении» и «позиция не редактируется напрямую» (отсутствие мутирующего API)
- [x] 4.2 Влить ручные пометки в поток закрывающих записей: хронологический порядок, цена пользователя, по умолчанию последняя марка, количество — остаток на момент применения; проверить сценарии fallback-закрытия, цены по умолчанию и «удаление пометки возвращает позицию в открытую»
- [x] 4.3 Показывать предупреждение об избыточной закрывающей записи (пометка при уже нулевом остатке, конкуренция с delivery); проверить unit-тестом

## 5. Корректировки и комментарии

- [x] 5.1 Реализовать use-cases внешних корректировок PnL: добавить, править, удалить; проверить сценарии «корректировка робота входит в результат» и «правка и удаление свободны» (суммирование — через тестовую заглушку, сам расчёт — capability аналитики)
- [x] 5.2 Реализовать комментарии сделок, позиций и конструкций; проверить сценарии «комментарий позиции переживает пересчёт» и «комментарии не влияют на результат»

## 6. Приёмка

- [x] 6.1 Прогнать приёмочный чек-лист по всем сценариям `specs/domain/constructions/spec.md` и зафиксировать результат; считать change готовым к архивации при полном прохождении

### Результат приёмочного чек-листа (полный прогон 319/319, 2026-09-20)

| Сценарий | Проверка |
|---|---|
| scenario-new-construction-default-open | `ConstructionServiceTests.TryIfNewConstructionStartsOpenAndAppearsInActiveList` |
| scenario-rename-preserves-everything | `ConstructionServiceTests.TryIfRenamePreservesBindingsCapitalStatusAndComment` |
| scenario-archived-hidden-but-analyzed | `ConstructionServiceTests.TryIfArchivedConstructionHiddenFromActiveListButVisibleInAnalytics` |
| scenario-delete-only-when-empty | `ConstructionServiceTests` (отказы со сделками/корректировками, удаление пустой), `DomainStorageTests` |
| scenario-capital-change-affects-percent-only | `ConstructionServiceTests.TryIfCapitalChangeAffectsPercentOnly` — на приёмке закрыт пробел: добавлен `ConstructionService.UpdateAllocatedCapitalAsync` (требование «капитал правится пользователем» не был покрыт задачами 2.x) |
| scenario-binding-removes-from-inbox | `TradeBindingServiceTests`, `InboxReadModelTests` |
| scenario-rebind-recomputes-both | `TradeBindingServiceTests.TryIfTransferMovesTradeBetweenConstructionsWithoutTrace` |
| scenario-return-to-inbox | `TradeBindingServiceTests.TryIfUnbindReturnsTradeToInboxAndKeepsComment`, `InboxReadModelTests` |
| scenario-batch-binding | `TradeBindingServiceTests.TryIfBatchBindingPutsEveryTradeIntoSingleConstruction` |
| scenario-residual-computed-from-trades | `PositionReadModelTests.TryIfResidualComputedFromTradesAtRead` |
| scenario-position-not-directly-editable | `PositionReadModelTests.TryIfPositionExposesNoMutatingApi` |
| scenario-close-by-offsetting-trades | `PositionReadModelTests.TryIfOffsettingTradesClosePosition` |
| scenario-manual-mark-delistings | `PositionReadModelTests.TryIfManualMarkClosesPositionWithoutExchangeRecords` |
| scenario-manual-mark-default-last-mark | `PositionReadModelTests.TryIfManualMarkTakesLastMarkPriceByDefault` |
| scenario-mark-removal-reopens | `PositionReadModelTests` (удаление пометки возвращает позицию в открытую) |
| scenario-robot-adjustment-in-result | `PnLAdjustmentServiceTests.TryIfRobotAdjustmentEntersConstructionResult` |
| scenario-adjustment-attributes-stored | `PnLAdjustmentServiceTests.TryIfAdjustmentAttributesStoredAndReturnedWhole` |
| scenario-adjustment-edit-delete-free | `PnLAdjustmentServiceTests.TryIfAdjustmentEditAndDeleteAreFree` |
| scenario-position-comment-survives-recompute | `CommentServiceTests.TryIfPositionCommentSurvivesRecompute` |
| scenario-comments-inert | `CommentServiceTests.TryIfCommentsDoNotAffectResult` |

Все сценария пройдены, избыточная закрывающая запись покрыта задачей 4.3 (`PositionReadModelTests`, предупреждения). Change готов к архивации.
