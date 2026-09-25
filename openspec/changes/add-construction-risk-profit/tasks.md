# Tasks

## 1. Модель данных и миграция

- [ ] 1.1 В `Data/Construction.cs` сменить `AllocatedCapitalUsdt` на `decimal?`, добавить enum `TargetUnit { Percent, Usdt }` и nullable-поля `RiskValue`, `RiskUnit`, `ProfitValue`, `ProfitUnit` с XML-документацией и traceability-метками (`openspec:domain/constructions#requirement-risk-profit-params`, `#requirement-allocated-capital`); проверить сборку `dotnet build`
- [ ] 1.2 Добавить EF-миграцию (nullable-капитал + четыре колонки риск/профит), убедиться, что снапшот модели обновился и миграция применяется к пустой и существующей базе без потери значений капитала

## 2. Домен: сервис контрактов и тесты

- [ ] 2.1 Обновить `IConstructionService`/`ConstructionService`: `CreateAsync` принимает `decimal?` капитал, `UpdateAllocatedCapitalAsync` принимает `decimal?` (null убирает значение), добавить `UpdateRiskAsync`/`UpdateProfitAsync(long, decimal? value, TargetUnit? unit)` с инвариантом «оба заданы или оба null» и положительным значением; проверить `dotnet build` после правки всех вызовов
- [ ] 2.2 Доменные тесты: создание без капитала (`scenario-construction-created-without-capital`), убирание капитала у существующей (`scenario-capital-removable`), установка риска в одной единице (`scenario-risk-entered-in-single-unit`), правка с заменой единицы (`scenario-risk-profit-edit-replaces-unit`), очистка (`scenario-risk-profit-clear-removes-param`), независимость риск/профит (`scenario-risk-profit-independent`), отказ неполной пары; убедиться, что `dotnet test --filter ConstructionService` проходит

## 3. Аналитика: проценты и конвертация единиц

- [ ] 3.1 Сменить в `ConstructionMetricsCalculator.Calculate` и `ConstructionMetrics` капитал на `decimal?`; функция `Percent` возвращает null при null/0 капитала; тест: незаданный капитал → процентные метрики null, абсолютные/даты/длительность без изменений (`scenario-no-capital-no-percent-metrics`)
- [ ] 3.2 Добавить чистый конвертер единиц риск/профит от капитала (`Percent↔USDT`; null/0 капитал → вычисляемая единица null); тесты сценариев `scenario-risk-percent-to-usdt`, `scenario-profit-usdt-to-percent`, `scenario-conversion-needs-capital`, `scenario-unset-param-no-values`

## 4. Read-модели экранов

- [ ] 4.1 `ConstructionListReadModel`: капитал в DTO как `decimal?`, добавить величины риск/профит обеими единицами через конвертер; тест read-модели списка с конструкцией без капитала
- [ ] 4.2 `ConstructionDetailReadModel`: проценты позиций null при незаданном капитале, добавить величины риск/профит в DTO сводки; тест read-модели деталей

## 5. UI: формы, подсказка, проценты

- [ ] 5.1 `Inbox.razor`: подпись «Выделенный капитал, USDT», поле необязательно (валидация числа только при непустом вводе, пусто → `CreateAsync(name, null)`); экранный тест создания без капитала (`scenario-inbox-create-without-capital`)
- [ ] 5.2 `ConstructionDetail.razor`, форма капитала: подпись с заглавной, пустое поле при сохранении убирает капитал, пояснение «пусто — капитал не задан»; экранный тест `scenario-detail-capital-removal-hides-percent`
- [ ] 5.3 `ConstructionDetail.razor`: команды «Риск…»/«Профит…» и формы с двумя полями единиц (ровно одно заполнено, вторая — вычисленное readonly-значение при заданном капитале, «Убрать» для очистки); экранные тесты `scenario-detail-risk-profit-edit` и валидации двух заполненных полей
- [ ] 5.4 Подсказка «риск — итог — профит»: разметка шкалы в строке списка и сводке деталей (нормировка на USDT-величины, заполнение от нуля до итога, признак пробоя, признак сбоя марок вместо полосы, скрытие без USDT-величин) и стили; экранные тесты `scenario-hint-inside-bounds`, `scenario-hint-boundary-breakout`, `scenario-hint-single-bound`, `scenario-hint-absent-without-params`, `scenario-hint-unavailable-on-marks-failure`
- [ ] 5.5 Скрытие процентов при незаданном капитале: список — прочерки в «Капитал» и «% капитала», детали — прочерк процента в сводке и отсутствие скобок «(—)» в строках позиций; экранные тесты `scenario-list-no-capital-percent-dash`, `scenario-detail-no-capital-no-percent`

## 6. Сквозная проверка

- [ ] 6.1 Полные `dotnet build` и `dotnet test`; убедиться grep-ом, что в решении не осталось вызовов старых сигнатур `CreateAsync(decimal`/`Calculate(` с ненулевым капиталом
- [ ] 6.2 Прогнать `openspec validate "add-construction-risk-profit" --strict` и проверить, что новые требования имеют обратные ссылки из кода/тестов; отметить в change `add-mcp-server`, что контракты сервиса изменились
