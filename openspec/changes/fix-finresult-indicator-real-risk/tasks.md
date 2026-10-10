# Tasks — fix-finresult-indicator-real-risk

## 1. Метрика реального риска (бэкенд)

- [x] 1.1 Калькулятор реального риска в `Application/Analytics`: группировка открытых остатков по `(BaseCoin, ExpiryDate)`, совместный минимум платежа по узлам-страйкам, консервативная сумма групп, правила null; юнит-тесты (дебетовый спред, много-экспирационность, неограниченный случай, неразбор символа, без остатков, независимость от марок)
- [x] 1.2 `RealRiskUsdt` в `ConstructionMetrics` и расчёт в `ConstructionMetricsCalculator`; обновление тестов калькулятора метрик
- [x] 1.3 `realRiskUsdt` в контрактах: `ConstructionListItem`, превью, `ConstructionDetailRows`/метрики карточки; проводка в `ConstructionListReadModel`, `ConstructionDetailReadModel`, `JournalMetricsReadModel`
- [x] 1.4 DTO endpoints (`ConstructionsEndpoints`, `ConstructionCardEndpoints`) — поле в списке, превью и метриках карточки; прогон тестов API

## 2. Геометрия и рендер индикатора (фронтенд)

- [x] 2.1 `FinResultInput.realRisk`; формула границы `realized − realRiskEff` с заглушкой и клипом слева; упразднение спец-случая #62; золотая зона до `max(total, borderValue)` без капа; упразднение `superEnd`; подпись границы «риск есть/риска нет · USDT · %»; подпись маркера — итог
- [x] 2.2 Разнос меток: оценка ширины, назначение `{side, level, align}` в калькуляторе, прижатие крайних; рендер сторон/уровней в `fin-result-indicator.tsx` (полный и средний виды)
- [x] 2.3 Типы `frontend/src/lib/api/*`: `realRiskUsdt` в строке, превью, метриках карточки; проводка в `constructions-table`, `construction-preview`, `metric-strip`
- [x] 2.4 Обновление тестов `geometry.test.ts` и `fin-result-indicator.test.tsx` под новую семантику

## 3. Визуальное и документация

- [ ] 3.1 Контраст зон: значения `riskZone`/`profitZone`/`superZone` в design.pen через MCP pen → `frontend/src/index.css`
- [ ] 3.2 Скриншот-сверка трёх видов индикатора (карточка, сводка, таблица)
- [ ] 3.3 Правка `.wf-research/ui-concept/concept.md` §9 под новую семантику

## 4. Приёмка

- [ ] 4.1 Полный прогон тестов (бэкенд + фронтенд), сборка без предупреждений
- [ ] 4.2 Сверка delta-спек `openspec validate`; коммиты Conventional Commits с `refs #75`
