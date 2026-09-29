# Tasks

## 1. Агрегаты журнала в аналитике

- [x] 1.1 Добавить в `JournalMetrics` поля `RealizedPnL` (всегда доступен) и `UnrealizedPnL` (null при сбое марок) с XML-документацией и метками `Traceability:` к `requirement-journal-pnl-aggregates`; вычислить их в `JournalMetricsReadModel` суммированием по всем конструкциям, включая архивные. Проверить: `dotnet build` без предупреждений документации.
- [x] 1.2 Покрыть агрегаты тестами в `JournalMetricsReadModelTests` по сценариям `scenario-journal-breakdown-sums-constructions`, `scenario-journal-unrealized-null-on-any-mark-failure`, `scenario-journal-aggregates-include-archived`, `scenario-journal-breakdown-excludes-adjustments`; тесты пометить метками `Traceability:`. Проверить: `dotnet test --filter JournalMetricsReadModelTests` зелёный.

## 2. Сводка экрана «Конструкции»

- [ ] 2.1 Расширить `ConstructionListData` полями разбивки и перенести их в `ConstructionListReadModel` из `JournalMetrics` без пересчёта. Проверить: `dotnet test --filter ConstructionListReadModelTests` с новыми утверждениями о переносе разбивки.
- [ ] 2.2 Вывести ячейки «реализованный» и «нереализованный» в сводке `Constructions.razor` рядом с итогом: формат знака как у итога, при `UnrealizedPnL == null` — признак сбоя марок по образцу отметки времени. Проверить: `dotnet test --filter ConstructionListScreenTests` — сценарии `scenario-list-summary-shows-pnl-breakdown` и деградация при сбое марок.

## 3. Верхняя панель

- [ ] 3.1 Расширить `MainLayout.TotalLabel` разбивкой «(реализов. A / нереализов. B)»; при сбое марок — «неполный (сбой марок), реализов. A, нереализов. —»; состояния «недоступен» и «…» сохранить. Проверить: `dotnet test --filter AppFrameTests` — разбивка на полном итоге, деградация при сбое, загрузка и недоступность.

## 4. Сквозная проверка

- [ ] 4.1 Прогнать затронутые наборы целиком (`dotnet test --filter "JournalMetricsReadModelTests|ConstructionListReadModelTests|ConstructionListScreenTests|AppFrameTests"`) и убедиться, что новые метки `Traceability:` указывают на существующие `Traceability ID` этого change, а поведение вне разбивки не изменилось.
