# Tasks

## 1. Метрика процента изменения цены

- [ ] 1.1 Добавить в `PositionMetrics` поле процента изменения цены открытого остатка (null у закрытой позиции и при сбое марок) и вычислять его в `UnrealizedPnlMarkEvaluator` рядом с `MarkValue`: (марка − средняя цена остатка) / средняя цена остатка × 100 × знак остатка. Проверить тестами `UnrealizedPnlMarkEvaluatorTests`: длинный остаток +10%/−5%, короткий −10%/+10%, закрытая позиция — null, сбой марок — null при живых остальных метриках (спек: `requirement-open-remainder-price-change-percent`)

## 2. Строка позиции деталей

- [ ] 2.1 Добавить в `ConstructionPositionRow` поля стоимости и процента изменения цены и передавать их из `PositionMetrics` в `ConstructionDetailReadModel.ReadPositionsAsync`. Проверить тестами `ConstructionDetailReadModelTests`: открытая позиция с марками получает обе величины, закрытая — null, сбой марок — null при живых реализованных величинах

## 3. Колонки таблицы позиций

- [ ] 3.1 Вывести в `ConstructionDetail.razor` колонки «Стоимость» и «Изм. цены, %» после «Сред. цена закрытия»: значение со знаком у открытой позиции с марками, прочерк у закрытой, признак сбоя котировок у открытой без марок; поправить colspan пустой строки с 13 до 15. Проверить тестами `ConstructionDetailScreenTests`: колонки с величинами, прочерки закрытой позиции, признак сбоя и сохранность остальных колонок (спек: `scenario-detail-position-value-and-price-change-columns`, `scenario-detail-position-value-price-change-degradation`)

## 4. Интеграционная проверка

- [ ] 4.1 Прогнать полный набор тестов решения и убедиться, что новых падений нет; прогнать `openspec validate add-position-price-change-columns --strict` без замечаний
