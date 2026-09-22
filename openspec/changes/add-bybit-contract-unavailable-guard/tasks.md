# Tasks

## 1. Детект отказа «контракт недоступен»

- [x] 1.1 В `BybitApiException` добавить статический хелпер `IsContractUnavailableError`: `RetCode == 110023` без анализа `RetMsg`; проверить тестами в `tests/TransactionJournal.Tests/Bybit/BybitApiExceptionTests`: хелпер распознаёт отказ с реальным текстом биржи «The contract is not available for trades», не срабатывает на другом коде и не зависит от формулировки retMsg
- [x] 1.2 Снабдить хелпер traceability-меткой `openspec:sync/bybit-history#requirement-contract-unavailable-area-skip` с человекочитаемым комментарием о защитном контуре недоступных контрактов; проверить rg-поиском, что ссылка разрешается в `Traceability ID` delta-spec этого change

## 2. Исход «область недоступна» в защищённом проходе окна

- [x] 2.1 В `ExecutionCategorySync` и `DeliveryCategorySync` перевести `RunWindowWithBoundaryGuardAsync` с пары «результат/null» на исход прохода: успешный результат, пограничное исчерпание перебора, область недоступна; перехват `IsContractUnavailableError` без ретрая и без зажатия; поведение пограничного контура не меняется; проверить сборкой и зелёным прогоном существующих тестов границы (`ExecutionCategorySyncTests`, `DeliveryCategorySyncTests`)
- [x] 2.2 Снабдить изменённые guard-методы traceability-меткой `openspec:sync/bybit-history#requirement-contract-unavailable-area-skip` с комментарием о третьем исходе; проверить разрешение ссылки rg-поиском

## 3. Пропуск области в циклах движков

- [x] 3.1 В `ExecutionCategorySync` в backfill- и инкрементальном циклах областей на исходе «область недоступна» фиксировать метку пропущенной области (категория плюс базовый актив; для безфильтровой области — только категория), останавливать обход окон области и продолжать следующие области; проверить тестами `ExecutionCategorySyncTests` на фиктивном шлюзе с retCode 110023: option-область пропускается, остальные области догружаются, запуск успешен с водяным знаком в обоих режимах (scenario-unavailable-option-area-skipped); единственная безфильтровая область linear завершает проход категории без сбоя (scenario-unavailable-single-area-ends-walk)
- [x] 3.2 Провести то же в `DeliveryCategorySync` для единственной области delivery-прохода; проверить тестами `DeliveryCategorySyncTests` по scenario-unavailable-single-area-ends-walk: проход завершён, водяной знак delivery зафиксирован, статус запуска успешный
- [x] 3.3 Добавить в `ExecutionCategorySyncResult` и `DeliveryCategorySyncResult` перечень меток пропущенных областей (пуст при отсутствии пропусков); проверить тестами: перечень пуст без отказов, метки корректны после пропуска, `HistoryExhausted` не отмечает пропуск

## 4. Видимость пропуска пользователю

- [x] 4.1 В `Sync.razor` показать рядом с результатом запуска заметку с перечнем пропущенных областей, когда он непуст; проверить тестами `SyncPageTests`: перечень виден при непустом перечне (scenario-skipped-areas-reported-to-user), заметки нет при пустом, запуск не отображается как ошибка
- [x] 4.2 Снабдить UI-обработчик traceability-меткой `openspec:sync/bybit-history#scenario-skipped-areas-reported-to-user` с человекочитаемым комментарием; проверить разрешение ссылки rg-поиском

## 5. Документация и сквозная проверка

- [x] 5.1 Обновить `docs/research/bybit-api.md`: задокументировать отказ 110023 с фактическим текстом биржи «The contract is not available for trades» (расхождение с текстом официальной таблицы кодов) и graceful-пропуск области движками; проверить чтением секции
- [x] 5.2 Запустить `dotnet test` по всем тестам проекта и убедиться в зелёном прогоне; выполнить `openspec validate --change add-bybit-contract-unavailable-guard --strict`
