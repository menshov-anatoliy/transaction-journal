# Tasks

## 1. Детект пограничной ошибки

- [ ] 1.1 В `BybitApiException` добавить свойство `RetMsg`, прокинуть его из основного конструктора, и добавить статический хелпер `IsHistoryBoundaryError`: `RetCode == 10001` и подстрока `earlier than 2 years` в `RetMsg` без учёта регистра; проверить тестами в `tests/TransactionJournal.Tests/Bybit`: хелпер распознаёт реальный текст ошибки биржи, не срабатывает на другом сообщении с кодом 10001 и на retCode 10006
- [ ] 1.2 Снабдить хелпер и свойство traceability-меткой `openspec:sync/bybit-history#requirement-history-boundary-guard` с человекочитаемым комментарием о защитном контуре границы хранения; проверить rg-поиском, что ссылка разрешается в `Traceability ID` delta-spec этого change

## 2. Серверное время в шлюзе истории

- [ ] 2.1 Расширить `IBybitHistoryGateway` методом `GetServerTimeMsAsync(CancellationToken)` и реализовать в `BybitHistoryGateway` поверх `BybitApiClient` (`SyncTimeAsync`); обновить фиктивные шлюзы в тестах `tests/TransactionJournal.Tests/Sync`; проверить сборкой `dotnet build` и зелёным прогоном затронутых тестов
- [ ] 2.2 Проверить тестом реального шлюза (по образцу существующих тестов `BybitHistoryGateway`): метод возвращает серверное время клиента и пробрасывает `BybitApiException` при отказе биржи

## 3. Хелпер границы хранения

- [ ] 3.1 Создать в `src/TransactionJournal/Sync` общий статический хелпер границы: константы `ExchangeStorageDepthMs` (730 дней) и `BoundarySafetyMarginMs` (7 дней), вычисление `allowedEarliestMs = serverNowMs - ExchangeStorageDepthMs + BoundarySafetyMarginMs` и clamp пола `Math.Max(configuredFloorMs, allowedEarliestMs)`; проверить тестами арифметики: пол из конфига глубже границы — зажат, мельче — не тронут, совпадение — идемпотентно
- [ ] 3.2 Снабдить константы и вычисление traceability-меткой `openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary` с комментарием о смысле запаса в одно execution-окно; проверить разрешение ссылки rg-поиском

## 4. Execution-движок: clamp и graceful-исчерпание

- [ ] 4.1 В `ExecutionCategorySync` запрашивать серверное время шлюза один на запуск (при отказе — fallback на `TimeProvider`), вычислять пол backfill и нижнюю границу инкремента через clamp хелпера; проверить тестами `ExecutionCategorySyncTests`: конфиг глубже границы — запросы окон не уходят раньше зажатого пола (scenario-floor-clamped-to-exchange-boundary), инкремент с водяным знаком у границы зажимается (scenario-incremental-boundary-clamped), отказ серверного времени не роняет запуск
- [ ] 4.2 Добавить в backfill- и инкрементальный циклы перехват `BybitApiException` от прохода окна: при `IsHistoryBoundaryError` повторить окно один раз с началом на границе; при повторном отказе прервать перебор оставшихся окон и областей категории с `historyExhausted = true` и штатной фиксацией водяного знака; проверить тестами на фиктивном шлюзе с retCode 10001: ретрай уходит с зажатым `startTime` и записи зажатого окна сохраняются (scenario-boundary-window-retried-clamped), повторный отказ завершает запуск успешно с водяным знаком и без запросов остальных областей (scenario-boundary-refusal-ends-walk)

## 5. Delivery-движок: clamp и graceful-исчерпание

- [ ] 5.1 Провести те же изменения в `DeliveryCategorySync`: серверное время, clamp пола backfill и нижней границы инкремента, ретрай окна с зажатым началом, исчерпание при повторном отказе; проверить тестами `DeliveryCategorySyncTests` по сценариям `scenario-floor-clamped-to-exchange-boundary`, `scenario-boundary-window-retried-clamped`, `scenario-boundary-refusal-ends-walk`
- [ ] 5.2 Снабдить изменённые смысловые блоки обоих движков traceability-метками соответствующих сценариев delta-spec с человекочитаемыми комментариями; проверить rg-аудитом, что каждая ссылка разрешается в `Traceability ID` и не осталось меток с несколькими целями через `;`

## 6. Документация и сквозная проверка

- [ ] 6.1 Обновить `docs/research/bybit-api.md`: закрыть открытый вопрос №1 фактом границы (retCode 10001, точный текст «Can't query order earlier than 2 years, please check your params: startTime or endTime!») и ссылкой на clamp и graceful-обработку в коде; проверить чтением секции
- [ ] 6.2 Запустить `dotnet test` по всем тестам проекта и убедиться в зелёном прогоне; проверить `openspec validate --change add-bybit-history-boundary-guard --strict`
