# Tasks

## 1. Расчёт худшего исхода (домен/приложение)

- [ ] 1.1 Расширить `RealRiskResult` неклэмпированным худшим платежом остатков (сегодня внутренняя переменная `worst` до `Math.Max(0, −worst)`): значение при `finite`, null при `unbounded`/`unavailable`, 0 без остатков; проверить новыми случаями в `RealRiskCalculatorTests` (гарантированная прибыль группы даёт положительный минимум, `realRiskUsdt` остаётся 0) с метками `Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric`
- [ ] 1.2 Добавить `WorstCaseUsdt` в `ConstructionMetrics` и собрать в `ConstructionMetricsCalculator` как `realized + worstPayoff`; покрыть юнит-тестами сценарии `scenario-worst-case-guaranteed-profit` и `scenario-worst-case-closed-equals-realized` с соответствующими traceability-метками

## 2. Контракты API

- [ ] 2.1 Расширить `RealRiskContract` сериализацией `worstCaseUsdt` рядом с `realRiskUsdt`/`realRiskStatus` (число только при `finite`); проверить в `RealRiskContractTests`, что unbounded/unavailable дают null + статус (`scenario-worst-case-null-with-status`)
- [ ] 2.2 Пробросить `worstCaseUsdt` в `ConstructionListRows`, `ConstructionsEndpoints` (список и превью) и `ConstructionCardEndpoints` (метрики карточки); проверить контрактными тестами `ConstructionsSectionApiTests` и `ConstructionCardApiTests` на присутствие поля в JSON

## 3. Фронтенд: геометрия и индикатор

- [ ] 3.1 Ввести `worstCase` в `FinResultInput` и ставить `borderAt` из него (убрать формулу `realized − realRisk`); обновить `geometry.test.ts`: граница на значении худшего исхода (`scenario-finresult-border-real-risk`), гарантированная прибыль ставит насечку правее реализованного (`scenario-finresult-guaranteed-profit-border`)
- [ ] 3.2 Пересобрать подпись границы в `resolveBorderLabels`: титул «худший исход» (при `realRisk = 0` — «риска нет»), значение со знаком, процент от плановой границы зоны по знаку с округлением до целого, без процента при незаданной/нулевой базе и нулевом значении; честный процент > 100% при клипе слева; покрыть тестами сценарии `scenario-finresult-fresh-position-real-risk`, `scenario-finresult-closed-no-risk`, `scenario-finresult-border-left-clip`, `scenario-finresult-percent-omitted-without-plan`
- [ ] 3.3 Тон подписи границы — по знаку худшего исхода (не по знаку итога); маркер остаётся в истинном положении без прижатия к границе; покрыть в `fin-result-indicator.test.tsx` и `geometry.test.ts` сценарий `scenario-finresult-marker-not-clamped`; компактный пресет не изменяется (регрессионный снимок существующих тестов компакта зелёный)

## 4. Интеграционная сверка

- [ ] 4.1 Прогнать полные наборы тестов бэкенда (`dotnet test`) и фронта (`npm test` во `frontend/`) — все зелёные
- [ ] 4.2 Ручная сверка на живых данных: карточка «XAUT стреддл 30OCT26 4400» — насечка стоит на худшем исходе (после частичного синтетического лока правее старой позиции), подпись «худший исход ±N · %», маркер не прижат; скриншот-сверка полного и среднего индикаторов
