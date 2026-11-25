# Tasks

## 1. Аналитика: модель вкладов в вычислителе маркеров

- [x] 1.1 Перестроить `ConstructionResultMarkersCalculator` на накопление вкладов: окно опционных исполнений — вклад = Σ сделок окна; связка — +лок на `Since`, −лок на `Until`; корректировка — сумма; закрывающая запись — количество × эффективная цена. Нулевой вклад события не создаёт. Проверка: юнит-тесты `scenario-first-window-gives-first-marker`, `scenario-roll-window-single-marker`, `scenario-marker-value-includes-locks-and-adjustments`, `scenario-marker-open-residual-entry-cost-counts`, `scenario-marker-value-excludes-unrealized`, `scenario-link-dispute-recomputes-history`.
- [x] 1.2 Реализовать фьючерсные циклы: событие «Закрытие фьючерса» только в момент обнуления остатка позиции, вклад = прирост реализованного PnL потока от предыдущего нуля (FIFO, с комиссиями); открытие и частичные закрытия событий не создают. Проверка: юнит-тесты `scenario-futures-closed-trade-marker`, `scenario-futures-open-partial-no-marker`, `scenario-futures-open-position-not-counted`.
- [x] 1.3 Классифицировать окна по составу сделок (переиспользовать правило `IsClosingTrade`): Открытие / Закрытие / Ролл; связки — Синтетическое закрытие / Снятие синтетики; закрывающие записи — Поставка/Закрытие по виду; корректировка — Корректировка PnL. Проверка: юнит-тесты `scenario-otm-expiry-no-event`, `scenario-adjustment-and-closing-entry-marker`, `scenario-manual-mark-priced-contribution`.
- [x] 1.4 Формировать перечень операций расшифровки каждого маркера: операции с предыдущего выведенного маркера, включая подавленные порогом; инварианты «Σ перечня = изменение» и «итог N−1 + Σ = итог N». Порог и нумерацию не менять. Проверка: юнит-тесты `scenario-marker-operations-sum-to-change`, `scenario-marker-below-threshold-suppressed`, `scenario-marker-numbering-displayed-only`.

## 2. Контракты API

- [x] 2.1 Опубликовать `operations[]` (вид, дата, сумма вклада) в `ResultMarker` контракта карточки и в `lastResultMarker` контрактов списка и превью; обновить читатель `ConstructionResultMarkersReader` и read-модели. Проверка: API-тесты `scenario-card-contract-publishes-markers`, `scenario-list-preview-publish-last-marker`, `scenario-marker-operations-sum-to-change` на уровне контрактов.

## 3. Макет design.pen (строго через MCP Pen, до фронта)

- [x] 3.1 Удалить из `design.pen` область описания маркеров (оба состояния — со списком и пустое) и изобразить подсказку-расшифровку бейджа: перечень «вид · дата · сумма вклада» без группировки, со скроллом при переполнении; последний маркер превью и компактного вида — с расшифровкой как подсказкой. Проверка: мастер в Pen отражает `requirement-design-finresult-result-markers` (сценарии `scenario-design-card-markers-with-breakdown`, `scenario-design-preview-compact-last-marker`, `scenario-design-markers-empty-state`, `scenario-design-markers-on-tokens`).
- [x] 3.2 Обновить в `design.pen` текст попапа «Как читать индикатор» по ADR-0022: вклады операций, расшифровка меток, без формулировок «минус входная стоимость открытых остатков» и насечки. Проверка: сценарии `scenario-design-card-reading-tooltip-open`, `scenario-design-card-reading-tooltip-tokens`.

## 4. Фронтенд: расшифровка вместо области описания (по обновлённому макету)

- [x] 4.1 Удалить `MarkerDescriptionArea` и её тесты; бейджи маркеров полного вида и последний маркер среднего/компактного видов получить подсказку-расшифровку (паттерн `MetricHint`, построчный перечень без группировки, скролл при переполнении). Проверка: компонентные тесты `scenario-marker-badge-breakdown-tooltip`, `scenario-marker-breakdown-no-grouping`, `scenario-medium-compact-last-marker-only`, `scenario-result-marker-below-threshold-hidden`.
- [x] 4.2 Переписать тексты `METRIC_TOOLTIPS` и попапа «Как читать индикатор» под модель вкладов; выполнить grep-аудит устаревших формулировок («область описания», «входная стоимость открытых остатков», старые id сценариев `scenario-futures-trade-marker`, `scenario-marker-description-area-lists-markers`) в src и тестах. Проверка: компонентные тесты `scenario-reading-tooltip-explains-current-model`, `scenario-indicator-number-tooltip-coexists`; аудит пуст.

## 5. Интеграционная сверка

- [x] 5.1 Скриншот-сверка живой карточки конструкции (localhost) с обновлённым `design.pen` через MCP Pen — обязательный критерий закрытия UI-части. Проверка: сверка зафиксирована, расхождения устранены или приняты владельцем.
- [x] 5.2 Полный прогон `dotnet test` и фронтенд-тестов; ручная проверка `scenario-closed-construction-keeps-history` и `scenario-result-marker-last-vs-current` на конструкции с открытой фьючерсной позицией. Проверка: все тесты зелёные, расхождение последнего маркера и маркера текущего результата объяснимо вкладами.

## 6. Дельта: подпись бейджа изменением и детализация расшифровки сделками

- [x] 6.1 Дополнить `ConstructionResultMarkersCalculator` детализацией операций расшифровки: сделки окна (инструмент, сторона, количество × цена, сумма вклада сделки; Σ сделок = вклад операции), сделки фьючерсного цикла без индивидуальных сумм, инструмент и количество × эффективная цена закрывающей записи, период действия связки; корректировки — без детализации. Проверка: юнит-тесты `scenario-marker-operations-sum-to-change` (расширить детализацией), новые проверки сумм сделок окна.
- [x] 6.2 Опубликовать детализацию в `ResultMarkerOperation`, `ConstructionResultMarkersReader`, read-моделях и контрактах карточки, списка и превью. Проверка: API-тесты `scenario-card-contract-publishes-markers`, `scenario-list-preview-publish-last-marker` (расширить детализацией).
- [x] 6.3 Обновить `design.pen` строго через MCP Pen до фронта: подписи бейджей — суммы изменения (тон — по знаку изменения, позиция — на накопленном итоге), в подсказке-расшифровке — накопленный итог в заголовке и компактные строки сделок под строкой операции; оба состояния — заполненное и пустое. Проверка: сценарии `scenario-design-card-markers-with-breakdown`, `scenario-design-markers-empty-state`, `scenario-design-markers-on-tokens`.
- [x] 6.4 Перенести в `fin-result-indicator.tsx` и `metric-tooltips.ts`: подпись и тон бейджа по `changeUsdt`, заголовок расшифровки с накопленным итогом, вложенные строки сделок; обновить тексты `METRIC_TOOLTIPS` и попапа «Как читать индикатор». Проверка: компонентные тесты `scenario-result-markers-badges-above-bar`, `scenario-result-marker-last-vs-current`, `scenario-marker-badge-breakdown-tooltip`, `scenario-marker-breakdown-no-grouping`, `scenario-indicator-number-tooltip-coexists`, `scenario-reading-tooltip-explains-current-model`.
- [x] 6.5 Дополнить GLOSSARY.md («Маркер изменения результата» — подпись метки суммой изменения; «Расшифровка маркера» — детализация сделками) и ADR-0022 (решения о подписи бейджа изменением и детализации сделками). Проверка: grep-аудит формулировок «со своей суммой: накопленный» и «краткий перечень» в GLOSSARY.md.
- [x] 6.6 Скриншот-сверка живой карточки конструкции (localhost) с обновлённым `design.pen` через MCP Pen. Проверка: сверка зафиксирована, расхождения устранены или приняты владельцем.
- [x] 6.7 Полный прогон `dotnet test` и фронтенд-тестов. Проверка: все тесты зелёные.
