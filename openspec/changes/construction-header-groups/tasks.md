# Tasks

## 1. Backend: суммарная внутренняя стоимость в саммари времени жизни

- [ ] 1.1 Вычислить `totalIntrinsicValueUsdt` в вычислителе саммари времени жизни конструкции (TransactionJournal.Application): сумма внутренних стоимостей открытых опционных ног; null без открытых опционных ног и при недоступности тикеров ноги без подмены остальных слагаемых. Проверка: unit-тесты суммирования и деградаций по образцу существующих тестов саммари. `Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary`
- [ ] 1.2 Опубликовать `totalIntrinsicValueUsdt` в контрактах списка и карточки конструкции (API-контракт и типы `frontend/src/lib/api/constructions.ts`, `construction-card.ts`, nullable-поле рядом с `totalTimeValueUsdt`). Проверка: тесты сериализации контракта зелёные, фронтенд-типы компилируются.

## 2. Макет design.pen (первая задача UI-блока, строго через MCP Pen)

- [ ] 2.1 Перенести принятую ячейку Г1 (стенд «Заголовок конструкции · графические варианты», узел `Bvd1Z`) в мастер экрана «Карточка конструкции» (`QbFRE`): заменить сводку `h69OG` на группы ПЛАН · ПОЗИЦИЯ · РИСК · ВРЕМЯ · СТОИМОСТЬ — подложки `$surface2`, капс-метки, модульная сетка «350 + остаток / три равных», техстрока «котировки на» справа, линия `$divider` перед синтетикой. Проверка: bounds-проверка без клиппинга + скриншот мастера через MCP Pen. `Traceability: openspec:ui/screens#scenario-detail-header-modular-grid`
- [ ] 2.2 Упразднить Meta-строку шапки (`tKUF6` в `Uw04r`) на мастере: плановые риск/профит, реальный риск и выделенный капитал видны только в группах, дата открытия — в «периоде». Проверка: скриншот мастера — под шапкой мета-строки нет. `Traceability: openspec:ui/screens#scenario-detail-header-meta-line`

## 3. Frontend: заголовок конструкции по обновлённому макету

- [ ] 3.1 Ввести `MetricGroup` (капс + метрики, `bg-surface2`, radius 8) и конфигурацию групп в `frontend/src/components/constructions/card/metric-strip.tsx`; ряды — CSS grid (ряд 1: модуль + `1fr` для ПОЗИЦИИ; ряд 2: три равных), полная ширина карточки; денежные значения с единицей USDT. Проверка: обновить `metric-strip.test.tsx` (группы, состав, единицы) — зелёные. `Traceability: openspec:ui/screens#requirement-construction-detail-screen`
- [ ] 3.2 Наполнить группы показателями: ПЛАН — плановый риск/профит; ПОЗИЦИЯ — текущий результат, % капитала, реализованная, нереализованная, выделенный капитал; РИСК — реальный риск (статусы `unbounded`/`unavailable`) и худший исход; СТОИМОСТЬ — внутренняя (новое поле), временная, распад/сутки; скрыть «стоимость», «занято капитала», «корректировки». Деградации прочерком, подсказки `METRIC_TOOLTIPS` на значениях (добавить недостающие тексты: plannedProfit, realRisk, intrinsic, выделенный капитал), проставить Traceability-метки у смысловых блоков. Проверка: тесты групп и деградаций зелёные. `Traceability: openspec:ui/screens#scenario-detail-summary-value-and-usage`
- [ ] 3.3 «Котировки на» — обособленная техстрока (11px, muted, справа) между группами и линией синтетики; строки синтетики и линия `h-px bg-divider` без изменений. Проверка: тест техстроки зелёный. `Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period`
- [ ] 3.4 Удалить `headerMeta` и его рендер из `frontend/src/pages/construction-card-page.tsx`. Проверка: `construction-card-page.test.tsx` — мета-строки нет, плановые значения в группах сводки. `Traceability: openspec:ui/screens#scenario-detail-header-meta-line`

## 4. Интеграционная приёмка

- [ ] 4.1 Скриншот-сверка живой страницы карточки конструкции (localhost) с обновлённым мастером design.pen через MCP Pen: группы, сетка, техстрока, отсутствие мета-строки. Критерий закрытия UI-части. `Traceability: openspec:ui/screens#scenario-detail-header-modular-grid`
- [ ] 4.2 Полный прогон фронтенд-тестов и линта; `openspec validate construction-header-groups --strict` без замечаний.
