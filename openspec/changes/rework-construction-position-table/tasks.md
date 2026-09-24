# Tasks

## 1. FIFO-движок: открывающие и закрывающие части

- [x] 1.1 Расширить `PositionFifoResult` полями `AverageEntryPrice`, `AverageClosePrice` (`decimal?`, `null` при отсутствии частей соответствующего вида) с XML-документацией и traceability-метками `scenario-position-average-entry-from-opening-parts`, `scenario-position-average-close-from-closing-parts`; проверить сборку `dotnet build src/TransactionJournal`
- [x] 1.2 В `PositionFifoEngine.Match` копить в том же проходе знаковые количества и количество-взвешенные стоимости открывающих (ушедших в слои) и закрывающих (сопоставленных слоям) частей; вычислять средние цены из накопителей с метками из 1.1; проверить тестами из 1.3
- [x] 1.3 Дополнить `PositionFifoEngineTests`: набор частей при частичных закрытиях (вход взвешен по всем открывающим частям, включая закрытые), средняя закрытия по встречным сделкам и закрывающим записям, `null` средних при отсутствии частей; запуск `dotnet test tests/TransactionJournal.Tests --filter PositionFifoEngineTests`

## 2. Метрики позиции: вход, выход, общий PnL

- [x] 2.1 Расширить `PositionMetrics` полями `AverageEntryPrice`, `AverageClosePrice` (`decimal?`), `TotalPnL` (`decimal?`) с XML-документацией, разводящей «среднюю цену входа» и «среднюю цену открытого остатка»; метка `scenario-position-total-pnl-includes-unrealized`; проверить сборку
- [x] 2.2 В `PositionMetricsCalculator.Calculate` прокинуть средние цены из FIFO-результата, поставить `TotalPnL = RealizedPnL` закрытой позиции и `null` открытой; проверить тестами из 2.3
- [x] 2.3 Дополнить `PositionMetricsCalculatorTests`: средние цены у закрытой позиции присутствуют, у открытой `TotalPnL = null`, у закрытой `TotalPnL = RealizedPnL`; запуск `dotnet test tests/TransactionJournal.Tests --filter PositionMetricsCalculatorTests`
- [x] 2.4 В `UnrealizedPnlMarkEvaluator` после оценки открытых остатков заполнить `TotalPnL = RealizedPnL + UnrealizedPnL`, при сбое марок оставить `null`; метки `scenario-position-total-pnl-includes-unrealized`, `scenario-position-total-pnl-mark-failure`; дополнить тесты оценщика и запустить `dotnet test tests/TransactionJournal.Tests --filter UnrealizedPnlMarkEvaluatorTests`

## 3. Read-модель деталей: строка таблицы позиций

- [x] 3.1 Привести `ConstructionPositionRow` к выводимым столбцам: `Symbol`, `Residual`, `AverageEntryPrice`, `AverageClosePrice`, `TotalPnL`, `TotalPnLPercent`, `AccumulatedFees`, `OpenedAt`, `ClosedAt`, `IsOpen`, `Comment`; убрать `AverageOpenPrice`, `MarkPrice`, `UnrealizedPnL`; обновить `ReadPositionsAsync`; проверить сборку
- [x] 3.2 Вычислять `TotalPnLPercent = TotalPnL / AllocatedCapitalUsdt * 100` с nullability общего PnL; метка `scenario-detail-position-row-entry-close-total` на строке-контракте; дополнить `ConstructionDetailReadModelTests` (процент от капитала, сохранение null при сбое марок, отсутствие убранных полей) и запустить `dotnet test tests/TransactionJournal.Tests --filter ConstructionDetailReadModelTests`

## 4. Экран деталей: таблица позиций

- [x] 4.1 Перестроить таблицу позиций в `ConstructionDetail.razor`: колонки «Инструмент», «Остаток», «Сред. цена входа», «Сред. цена закрытия», «Общий P&L» (абсолют со знаком, цвет по знаку, процент от капитала в скобках), «Всего комиссий», «Время открытия», «Время закрытия» (прочерк открытой), «Статус», «Комментарий», «Действия»; удалить колонки «Средняя», «Марка», «Нереализов.», «Результат» с их ветками сбоя марок; метки `scenario-detail-position-row-entry-close-total` на блоке колонок; проверить сборку
- [x] 4.2 Признак сбоя марок для открытой позиции выводить в «Общий P&L» (существующий стиль `markfail`), не трогая остальные колонки; метка `scenario-detail-position-total-pnl-marks-failure`; дополнить `ConstructionDetailScreenTests` (состав колонок, процент в скобках, сбой марок, пустая таблица) и запустить `dotnet test tests/TransactionJournal.Tests --filter ConstructionDetailScreenTests`

## 5. Проверка change

- [x] 5.1 Запустить полный набор тестов `dotnet test tests/TransactionJournal.Tests` и убедиться в отсутствии регрессий аналитики и UI
- [x] 5.2 Прогнать `openspec validate rework-construction-position-table --strict` и устранить замечания валидации

## 6. Раздельные части P&L в таблице позиций

- [ ] 6.1 Добавить в `ConstructionPositionRow` поля `RealizedPnL` (`decimal`) и `UnrealizedPnL` (`decimal?`, `null` при сбое марок) с пробросом из метрик позиции и XML-документацией; метка `scenario-detail-position-pnl-parts` на строке-контракте; дополнить `ConstructionDetailReadModelTests` (проброс раздельных частей, `null` нереализованной при сбое марок) и запустить `dotnet test tests/TransactionJournal.Tests --filter ConstructionDetailReadModelTests`; проверить сборку
- [ ] 6.2 Дополнить таблицу позиций в `ConstructionDetail.razor` колонками «Реализованный» и «Нереализованный» перед «Общий P&L»: абсолют со знаком и цвет по знаку, у закрытой позиции в «Нереализованном» — прочерк; при сбое марок стиль `markfail` выводить в «Нереализованном» и «Общий P&L», не трогая «Реализованный» и остальные колонки; метки `scenario-detail-position-pnl-parts` и `scenario-detail-position-total-pnl-marks-failure` на блоке колонок; дополнить `ConstructionDetailScreenTests` (состав колонок, значения частей, прочерк закрытой, сбой марок) и запустить `dotnet test tests/TransactionJournal.Tests --filter ConstructionDetailScreenTests`; проверить сборку
- [ ] 6.3 Прогнать полный набор `dotnet test tests/TransactionJournal.Tests` и `openspec validate rework-construction-position-table --strict`, устранить замечания
