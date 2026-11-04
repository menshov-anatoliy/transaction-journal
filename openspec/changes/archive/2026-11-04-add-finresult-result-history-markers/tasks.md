# Tasks

## 1. Аналитика: вычислитель маркеров изменения результата

- [x] 1.1 Реализовать вычислитель событий истории конструкции: окна опционных исполнений по правилам сборки v5 (ролл — одно окно), каждая фьючерсная сделка, изменение набора действующих связок, внешняя корректировка PnL, закрывающая запись; порядок — время записи, затем стабильный идентификатор. Проверка: юнит-тесты сценариев `scenario-first-window-gives-first-marker`, `scenario-roll-window-single-marker`, `scenario-futures-trade-marker`, `scenario-synthetic-link-change-marker`, `scenario-adjustment-and-closing-entry-marker`.
- [x] 1.2 Реализовать значение метки на срезе журнала: `RealizedPnL + Σ локов действующих связок + корректировки − входная стоимость ещё открытых остатков` на момент события (премия открытия фиксируется сразу), без нереализованной части; при закрытии всех остатков совпадает со сводной. Проверка: юнит-тесты `scenario-marker-value-includes-locks-and-adjustments`, `scenario-marker-open-residual-entry-cost-counts`, `scenario-marker-value-excludes-unrealized`, `scenario-link-dispute-recomputes-history`.
- [x] 1.3 Реализовать порог проявления (≥ 5% планового риска от последней выведенной; первая и последняя безусловны; без планового риска порога нет) и сплошную нумерацию по выведенным. Проверка: юнит-тесты `scenario-marker-below-threshold-suppressed`, `scenario-marker-threshold-cumulative-drift`, `scenario-marker-first-last-unconditional`, `scenario-marker-no-threshold-without-planned-risk`, `scenario-marker-numbering-displayed-only`.
- [x] 1.4 Удалить `worstCaseUsdt` из `RealRiskCalculator`/`RealRiskResult` и метрик конструкции, сохранив `realRiskUsdt`/`realRiskStatus`; выполнить grep-аудит оставшихся упоминаний `worstCase` (src, tests, frontend, чаты, подсказки) и вычистить. Проверка: сборка тестов без упоминаний `worstCase` в контрактах, существующие тесты реального риска зелёные.

## 2. Контракты API

- [x] 2.1 Опубликовать в контракте карточки `resultMarkers[]` (номер, время события, тип события, изменение, накопленный итог), в контрактах списка и превью — `lastResultMarker` (накопленный итог, время события); удалить `worstCaseUsdt` из всех DTO. Проверка: API-тесты `scenario-card-contract-publishes-markers`, `scenario-list-preview-publish-last-marker` и обновление существующих контракт-тестов без `worstCaseUsdt`.

## 3. Макет design.pen (строго через MCP Pen)

- [x] 3.1 Внести в `design.pen` блок индикатора карточки конструкции: номерные бейджи маркеров с суммами над полосой, область описания маркеров под индикатором в обоих состояниях (со списком и пустая), последний маркер без номера в превью и компактном виде. Проверка: мастер в Pen отражает `requirement-design-finresult-result-markers` (сценарии `scenario-design-card-markers-and-description`, `scenario-design-preview-compact-last-marker`, `scenario-design-markers-empty-state`).
- [x] 3.2 Обновить в `design.pen` текст попапа «Как читать индикатор»: маркеры изменения результата и маркер текущего результата, без насечки худшего исхода. Проверка: сценарии `scenario-design-card-reading-tooltip-open` (насечка в тексте отсутствует), `scenario-design-markers-on-tokens`.

## 4. Фронтенд: геометрия и индикатор (по обновлённому макету)

- [x] 4.1 Переработать `geometry.ts`: охват шкалы точками маркеров без клипа, удалить ветку `borderAt`/подпись насечки, позиции бейджей с вертикальным разносом при коллизии. Проверка: тесты геометрии `scenario-scale-expands-to-markers-no-clip`, `scenario-result-markers-badges-above-bar`, `scenario-finresult-labels-no-overlap`.
- [x] 4.2 Реализовать в полном виде индикатора бейджи маркеров над полосой, область описания под индикатором (список со скроллом, пустое состояние), удалить насечку и её подпись; обновить текст попапа и подсказки чисел. Проверка: компонентные тесты `scenario-result-marker-last-vs-current`, `scenario-result-marker-below-threshold-hidden`, `scenario-marker-description-area-lists-markers`, `scenario-reading-tooltip-explains-current-model`, `scenario-indicator-number-tooltip-coexists`.
- [x] 4.3 Реализовать в среднем и компактном видах только последний маркер без номера и сумм; компакт сохраняет бейджи «∞»/«?». Проверка: компонентные тесты `scenario-medium-compact-last-marker-only`, `scenario-finresult-compact-risk-state`.

## 5. Интеграционная сверка

- [x] 5.1 Скриншот-сверка живой карточки конструкции (localhost) с обновлённым `design.pen` через MCP Pen — обязательный критерий закрытия UI-части. Проверка: сверка зафиксирована, расхождения устранены или приняты владельцем.
- [x] 5.2 Полный прогон: `dotnet test` и фронтенд-тесты зелёные; ручная проверка сценария `scenario-closed-construction-keeps-history` на закрытой конструкции.
