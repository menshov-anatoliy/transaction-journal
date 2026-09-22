# Tasks

## 1. Пропуск недоступной спецификации в пополнении справочника

- [x] 1.1 В `InstrumentReferenceSync` заменить возвращаемое значение `SyncAsync` на результат `InstrumentSyncResult` со счётчиком вставленных спецификаций и перечнем неразрешённых символов; перехватывать `BybitApiException` с `IsContractUnavailableError` вокруг запроса спецификации одного символа — символ фиксируется в перечне без ретрая, обработка продолжается; проверить тестами `tests/TransactionJournal.Tests/Sync/InstrumentReferenceSyncTests` на фиктивном источнике: отказ 110023 по одному символу не прерывает остальные (scenario-unavailable-instrument-spec-skipped), посторонний код ошибки пробрасывается как раньше
- [x] 1.2 Провести перечень неразрешённых символов через `JournalSyncService` в `JournalSyncResult`; проверить тестом: запуск с отказом 110023 в справочнике завершается успешно (водяной знак зафиксирован, статус Succeeded) и несёт перечень символов
- [x] 1.3 Снабдить изменённые блоки traceability-меткой `openspec:sync/bybit-history#scenario-unavailable-instrument-spec-skipped` с человекочитаемым комментарием; проверить rg-поиском, что ссылка разрешается в `Traceability ID` delta-spec этого change

## 2. Деградация материализации по неразрешённым символам

- [x] 2.1 В `TradeMaterializer` обрабатывать причину `UnknownSymbol` сверки как откладывание записи: запись исполнения не материализуется, символ собирается в перечень неразрешённых; результат `Materialize` пополняется этим перечнем; прочие причины сверки и повреждения остаются исключениями; проверить тестами `tests/TransactionJournal.Tests/Materialization/TradeMaterializerTests`: запись с символом без спецификации пропущена и символ перечислен (scenario-unresolved-symbol-degrades-to-warning), расхождение базового актива по-прежнему бросает `InstrumentResolveException` (scenario-instrument-mismatch-still-fails), linear-записи материализуются без изменений
- [x] 2.2 Провести то же в `ExpiryMaterializer` для delivery-записей: закрывающая запись и OTM-автозакрытие по символу без спецификации не строятся, символ попадает в перечень неразрешённых; проверить тестами `tests/TransactionJournal.Tests/Materialization/ExpiryMaterializerTests`
- [x] 2.3 Добавить `UnresolvedInstruments` в `JournalMaterializationResult` и объединять перечни сделок и экспираций в `JournalMaterializer`; проверить тестом: проекция с символом без спецификации строится из разрешимых записей и несёт перечень, запуск не помечен ошибкой
- [x] 2.4 Убедиться, что read-модели деградируют транзитивно: `InboxReadModel` возвращает сделки без исключения, когда среди сырых исполнений есть символ опциона без спецификации; проверить тестом `tests/TransactionJournal.Tests/Domain/InboxReadModelTests`: такие сделки не появляются в «Входящих» (scenario-unresolved-symbols-reported-to-user), остальные выводятся
- [x] 2.5 Показать перечень неразрешённых символов на странице синхронизации заметкой рядом с результатом запуска; проверить тестами `tests/TransactionJournal.Tests/Ui/SyncPageTests`: перечень виден при непустом значении, заметки нет при пустом

## 3. Видимость непокрытых базовых активов доски

- [ ] 3.1 Дополнить `ExecutionCategorySyncResult` перечнем фактически пройденных базовых активов доски (области option запуска, включая пропущенные как недоступные; для linear — пустой перечень) и заполнять его в `ExecutionCategorySync`; проверить тестами `tests/TransactionJournal.Tests/Sync/ExecutionCategorySyncTests`: перечень отражает области пройденные и пропущенные по 110023
- [ ] 3.2 В `JournalSyncService` вычислять непокрытые активы: базовые активы символов option-записей полного снимка сырья (префикс до первого дефиса, нормализация `OptionBaseCoinSource`) минус пройденные области запуска; результат проводить в `JournalSyncResult`; проверить тестом оркестратора на фиктивном снимке: delivery-записи ETH при пройденной только области BTC дают непокрытый ETH, полный охват даёт пустой перечень (scenario-uncovered-base-coin-reported, scenario-covered-board-no-warning)
- [ ] 3.3 Показать непокрытые активы на странице синхронизации заметкой с подсказкой лечения — конфигурация дополнительных активов доски и сброс состояния категории; проверить тестами `tests/TransactionJournal.Tests/Ui/SyncPageTests`: заметка с подсказкой видна при непустом перечне, отсутствует при пустом

## 4. Документация и сквозная проверка

- [ ] 4.1 Обновить `docs/research/bybit-api.md`: задокументировать фактический отказ 110023 «The contract is not available for trades» на эндпоинте спецификаций при запросе по символу делистнутого инструмента и graceful-пропуск в пополнении справочника; проверить чтением секции
- [ ] 4.2 Запустить `dotnet test` по всем тестам проекта и убедиться в зелёном прогоне; выполнить `openspec validate --change add-delisted-instrument-resilience --strict`
