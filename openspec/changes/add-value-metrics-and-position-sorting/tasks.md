# Tasks

## 1. Метрики стоимости в Application/Analytics

- [ ] 1.1 Добавить `PositionMetrics.MarkValue` (decimal?) и вычислять его в `UnrealizedPnlMarkEvaluator.EvaluatePosition` как `MarkPrice × Residual` тем же жестом, что `MarkPrice`/`UnrealizedPnL`/`TotalPnL`: закрытая позиция и позиция без марки остаются с null, остальные метрики не меняются. Юнит-тесты: открытый лонг и шорт дают положительную и отрицательную стоимость; закрытая позиция и сбой марки оставляют null. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.
- [ ] 1.2 Добавить `ConstructionMetrics.MarkValue` и `CapitalUsagePercent` в `ConstructionMetricsCalculator`: стоимость — null без открытых остатков и при неоцененном открытом остатке, иначе сумма стоимостей открытых позиций; занятость — `Percent(MarkValue)` с сохранением знака. Юнит-тесты: сумма позиций; закрытая конструкция — null; капитал задан/незадан; отрицательная стоимость даёт отрицательный процент. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.

## 2. Read-model: поля и сортировка

- [ ] 2.1 В `ConstructionDetailReadModel.ReadPositionsAsync` расширить `ConstructionPositionRow` полями `MarkValue` и `TotalPnLPercentOfValue` (процент — только при положительной стоимости и доступном общем PnL, по образцу `PercentOfCapital`) и заменить сортировку по символу на композицию: открытые раньше закрытых → ранг типа CALL/PUT/прочие из `OptionSymbolParser.TryParse` → тикер `StringComparer.Ordinal` → экспирация по возрастанию. Юнит-тесты read-model: порядок строк для смешанного набора (открытые/закрытые, CALL/PUT/прочий); процент при положительной стоимости; «—»-случаи (null) при неположительной стоимости. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.
- [ ] 2.2 В `ConstructionListReadModel` добавить в строку списка `MarkValue` и `CapitalUsagePercent`, переиспользуя существующий признак сбоя марок строки. Юнит-тест: строка с марками и капиталом несёт стоимость и занятость; без капитала занятость null, стоимость остаётся. Проверка: `dotnet test tests\TransactionJournal.Tests` зелёный.

## 3. UI: список и детали

- [ ] 3.1 В `ConstructionDetail.razor`: значения «Стоимость» и «Занято капитала, %» в kstrip рядом с «% капитала»; в таблице позиций колонки «Стоимость» и «% P&L от стоимости» после «Общий P&L». Формат по образцу существующих: USDT 2 знака со знаком, проценты 1 знак со знаком, null — «—», сбой марок — признак «сбой марок». Проверка: `dotnet build` без ошибок; ручная проверка страницы деталей (открытая конструкция с капиталом, кредитовая, закрытая).
- [ ] 3.2 В `Constructions.razor`: колонка «Стоимость» рядом с «Капитал» и «Занято капитала, %» рядом с «% капитала»; сводку списка не менять. Формат и деградация — как в 3.1. Проверка: `dotnet build` без ошибок; ручная проверка списка (конструкции с марками/без капитала/закрытая).

## 4. Интеграционная проверка

- [ ] 4.1 Прогнать `dotnet build` и полный `dotnet test tests\TransactionJournal.Tests`; убедиться, что новые метки `Traceability:` в коде и тестах ссылаются на fragment-id из спек изменения (`requirement-mark-value-of-position-and-construction`, `requirement-position-pnl-percent-of-value`, `requirement-capital-usage-from-value`, `requirement-allocated-capital`, `requirement-construction-list-screen`, `requirement-construction-detail-screen`) и сопровождены человекочитаемыми комментариями. Проверка: сборка и тесты зелёные; `rg "Traceability: (openspec|change):" src tests` показывает метки только с разрешимыми ссылками.
