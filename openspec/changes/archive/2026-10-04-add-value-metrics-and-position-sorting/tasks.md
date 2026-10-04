# Tasks

## 1. Метрики стоимости в Application/Analytics

- [x] 1.1 Добавить `PositionMetrics.MarkValue` (decimal?) и вычислять его в `UnrealizedPnlMarkEvaluator.EvaluatePosition` как `MarkPrice × Residual` тем же жестом, что `MarkPrice`/`UnrealizedPnL`/`TotalPnL`: закрытая позиция и позиция без марки остаются с null, остальные метрики не меняются. Юнит-тесты: открытый лонг и шорт дают положительную и отрицательную стоимость; закрытая позиция и сбой марки оставляют null. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.
- [x] 1.2 Добавить `ConstructionMetrics.MarkValue` и `CapitalUsagePercent` в `ConstructionMetricsCalculator`: стоимость — null без открытых остатков и при неоцененном открытом остатке, иначе сумма стоимостей открытых позиций; занятость — `Percent(MarkValue)` с сохранением знака. Юнит-тесты: сумма позиций; закрытая конструкция — null; капитал задан/незадан; отрицательная стоимость даёт отрицательный процент. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.

## 2. Read-model: поля и сортировка

- [x] 2.1 В `ConstructionDetailReadModel.ReadPositionsAsync` заменить сортировку по символу на композицию: открытые раньше закрытых → ранг типа CALL/PUT/прочие из `OptionSymbolParser.TryParse` → тикер `StringComparer.Ordinal` → экспирация по возрастанию. Юнит-тесты read-model: порядок строк для смешанного набора (открытые/закрытые, CALL/PUT/прочий). Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.
- [x] 2.2 В `ConstructionListReadModel` добавить в строку списка `MarkValue` и `CapitalUsagePercent`, переиспользуя существующий признак сбоя марок строки. Юнит-тест: строка с марками и капиталом несёт стоимость и занятость; без капитала занятость null, стоимость остаётся. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.

## 3. UI: список и детали

- [x] 3.1 В `ConstructionDetail.razor`: значения «Стоимость» и «Занято капитала, %» в kstrip рядом с «% капитала». Формат по образцу существующих: USDT 2 знака со знаком, проценты 1 знак со знаком, null — «—», сбой марок — признак «сбой марок». Проверка: `dotnet build` без ошибок; ручная проверка страницы деталей (открытая конструкция с капиталом, кредитовая, закрытая).
- [x] 3.2 В `Constructions.razor`: колонка «Стоимость» рядом с «Капитал» и «Занято капитала, %» рядом с «% капитала»; сводку списка не менять. Формат и деградация — как в 3.1. Проверка: `dotnet build` без ошибок; ручная проверка списка (конструкции с марками/без капитала/закрытая).

## 4. Интеграционная проверка

- [x] 4.1 Прогнать `dotnet build` и полный `dotnet test tests\TransactionJournal.Tests`; убедиться, что новые метки `Traceability:` в коде и тестах ссылаются на fragment-id из спек изменения (`requirement-mark-value-of-position-and-construction`, `requirement-capital-usage-from-value`, `requirement-allocated-capital`, `requirement-construction-list-screen`, `requirement-construction-detail-screen`) и сопровождены человекочитаемыми комментариями. Проверка: сборка и тесты зелёные; `rg "Traceability: (openspec|change):" src tests` показывает метки только с разрешимыми ссылками.

## 5. Уборка позиционных стоимостных колонок

- [x] 5.1 Убрать из `ConstructionPositionRow` и `ConstructionDetailReadModel.ReadPositionsAsync` поля `MarkValue` и `TotalPnLPercentOfValue` с вычислением процента от стоимости; удалить связанные тест-кейсы (процент при положительной стоимости, null-случаи) из `ConstructionDetailReadModelTests`. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.
- [x] 5.2 Убрать из `ConstructionDetail.razor` колонки «Стоимость» и «% P&L от стоимости» таблицы позиций и связанные проверки из `ConstructionDetailScreenTests`; метки Traceability на удалённые fragment-id (`requirement-position-pnl-percent-of-value`, `scenario-detail-position-value-columns`, `scenario-detail-position-value-degradation`, `scenario-positive-value-yields-pnl-percent`, `scenario-nonpositive-value-yields-no-percent`) удалить вместе с проверявшим их кодом. Проверка: `dotnet build` без ошибок.
- [x] 5.3 Прогнать `dotnet build` и полный `dotnet test tests\TransactionJournal.Tests`; убедиться, что `rg "Traceability: (openspec|change):" src tests` не содержит ссылок на удалённые fragment-id.
