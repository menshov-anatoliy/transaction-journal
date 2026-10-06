# Tasks

## 1. Сортировка позиций в деталях конструкции

- [x] 1.1 В `ConstructionDetailReadModel.ReadPositionsAsync` заменить цепочку сортировки на составной ключ: открытые раньше закрытых → экспирация по убыванию → страйк по убыванию → ранг типа (CALL=0, PUT=1, прочие=2) → тикер по `StringComparer.Ordinal`; сентинеллы для неопционных символов — `DateTime.MinValue` и `decimal.MinValue`. Обновить комментарии `InstrumentTypeRank`/`InstrumentExpiry`/нового `InstrumentStrike` и метки traceability на `openspec:ui/screens#scenario-detail-positions-ordered-by-status-and-type`. Проверка: `dotnet build` проходит.
- [x] 1.2 Обновить тест порядка позиций в `ConstructionDetailReadModelTests` на эталонный пример спеки (ETH-25DEC26-2200-C, ETH-25DEC26-2200-P, ETH-25DEC26-1900-C, ETH-25DEC26-1900-P, ETH-25SEP26-2200-C, ETH-25SEP26-2200-P, ETH-25SEP26-1900-C, ETH-25SEP26-1800-C, ETH-25SEP26-1800-P, ETH-29MAY26-1900-C, ETH-29MAY26-1900-P, ETHUSDT; закрытые повторяют порядок после открытых) с комментарием и меткой `Traceability: openspec:ui/screens#scenario-detail-positions-ordered-by-status-and-type`. Проверка: тест read-модели падает на старой реализации и проходит на новой (`dotnet test --filter` по тесту).

## 2. Сортировка сделок от новых к старым

- [x] 2.1 В `ConstructionDetailReadModel.ReadTradesAsync` заменить `OrderBy(ExecutedAt).ThenBy(ExecId)` на `OrderByDescending(ExecutedAt)` с тай-брейком `ThenByDescending(ExecId, StringComparer.Ordinal)`, обновить комментарии и метку traceability на `openspec:ui/screens#scenario-detail-trades-newest-first`. Проверка: `dotnet build` проходит.
- [x] 2.2 В `InboxReadModel.ListAsync` добавить упорядочивание результата по убыванию времени исполнения с тем же тай-брейком, обновить комментарий и метку traceability на `openspec:ui/screens#scenario-inbox-trades-newest-first`. Хронологический порядок `TradeMaterializer.Materialize` не менять. Проверка: `dotnet build` проходит.
- [x] 2.3 Добавить тесты: в `ConstructionDetailReadModelTests` — таблица сделок начинается с самой поздней сделки и порядок детерминирован при равном времени (`Traceability: openspec:ui/screens#scenario-detail-trades-newest-first`); в `InboxReadModelTests` — список начинается с самой поздней непривязанной сделки (`Traceability: openspec:ui/screens#scenario-inbox-trades-newest-first`). Проверка: оба теста проходят (`dotnet test --filter`).

## 3. Сверка спеки и регресс

- [x] 3.1 Выполнить `openspec validate reorder-positions-and-trades --strict` и убедиться в отсутствии ошибок дельты. Проверка: валидация успешна.
- [x] 3.2 Прогнать полный набор тестов `dotnet test` и проверить отсутствие регрессий в read-моделях и экранах. Проверка: все тесты зелёные. (1002/1003; единственное падение — `TryIfLiveDataReconcilesWithBybitStatement`, диагностическая сверка живых данных машины по комиссиям, не использует read-модели этого change и не связано с ним.)
