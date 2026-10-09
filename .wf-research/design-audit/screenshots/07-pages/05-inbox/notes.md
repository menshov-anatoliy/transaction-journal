# Скриншот-сверка 7.3 — фильтры на примитивах-полях, цели-конструкции, чекбоксы

Приёмка задачи 7.3 change `reconcile-frontend-with-design` по процессу
design.md D3: пара «макет/страница» для Body #5 «Входящие» по расхождениям
§3.6 аудита: 2 (фильтры — нативные input date/чекбоксы-инлайн →
примитивы), 3 (цели-конструкции — `border-primary bg-primary/5` → мягкий
зелёный акцент), 4 (чекбоксы — нативные браузерные → дизайн-примитивы).

Процесс: макет — нода `TECU5` (Body #5) из design.pen через MCP pen:
`Get(nodeId, {depth, resolveVariables})` по зонам (ZvdbR «Фильтры»,
zJIJt «Непривязанные сделки», P9U8QH «Конструкции-цели», tSfT8 «Шапка»)
для авторитетных значений + `Export` png 2×; страница — живой рендер
`http://localhost:5173/spa/inbox?from=2026-09-01&to=2026-10-31&sides=buy,sell`
(Vite 5173 + мок-API 5019, фикстуры повторяют мастера: 5 сделок
07.10–05.10 с инструментами/ценами строк Row:1–5, 4 цели из P9U8QH;
выбраны строки 1/2/4 — как Row:1/2/4 мастера) в headless Chrome 1440×760
@2x через CDP: computed-стили (app-styles.json, значения перенесены в
таблицу ниже и удалены) и PNG.

## Файлы пары

- `design-frame.png` — Body #5 «Входящие» (TECU5) из design.pen (2×).
- `app-page.png` — живая страница `/spa/inbox` (1440×760 @2x, 3 выбранные
  строки, как в мастере).

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

Данные мастера сняты MCP pen `Get` (resolveVariables).

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| Поле даты (VYItk) | $surface, stroke $border #E5E5DF, r 8, [7,10], Inter 12/normal $textPrimary | `rgb(255,255,255)`, `1px rgb(229,229,223)`, `8px`, `7px 10px … 31px` (слот иконки), `12px/400` Inter, `rgb(23,23,30)` |
| Иконка календаря (nukLQ) | 13×13 $textMuted #9B9BA1 | `13px×13px`, `rgb(155,155,161)` |
| Тире (xYDIz) | 12/normal $textMuted | `12px`, `rgb(155,155,161)` |
| Группа инструментов (AYWJR) | $surface, r 8, [7,10], stroke $border | `8px`, `7px 10px`, `rgb(255,255,255)`, `rgb(229,229,223)` |
| Метка «Направление:» (YCC9c) | 12/normal $textSecondary | `12px`, `rgb(102,102,110)` |
| Чекбокс checked (lpeaS) | 16×16, r 4, fill+stroke $accent #1FA36B, галка check 10×10 #FFFFFF | `16px×16px`, `4px`, `rgb(31,163,107)`/`rgb(31,163,107)`, svg check `size-2.5` `$surface` |
| Чекбокс unchecked (mtES4) | 16×16, r 4, fill $surface, stroke $border | `16px×16px`, `4px`, `rgb(255,255,255)`, `rgb(229,229,223)` |
| «Сбросить» (b0ZLfV, Ghost [6,10] 12) | text $textSecondary 12/500, паддинги [6,10] | `12px/500`, `rgb(102,102,110)`, `6px 10px` |
| Карточка таблицы (zJIJt) | r 12, stroke $border, fill $surface, clip | `12px`, `rgb(229,229,223)`, `rgb(255,255,255)`, `overflow: hidden` |
| Шапка таблицы (Lq3kr/HC) | fill $surface2 #EFEFEA, Inter 11/600 $textSecondary | `rgb(239,239,234)` (на thead), th `11px/600`, `rgb(102,102,110)` |
| Выбранная строка (Row:1) | fill $accentSofter #EFF9F4 | tr `rgb(239,249,244)` = #EFF9F4 |
| «покупка» (Se3d8) | $info #4A7FB5 | `rgb(74,127,181)` |
| «продажа» (mC6i6) | $risk #DFA23B | `rgb(223,162,59)` |
| Ячейка времени (wFOZv) | Inter 12/normal $textSecondary | `12px`, `rgb(102,102,110)` |
| Панель целей (P9U8QH) | 320, r 12, stroke $border, fill $surface, [14], gap 10 | `320px`, `12px`, `rgb(229,229,223)`, `rgb(255,255,255)`, `14px`, `10px` |
| Подпись панели (m9kwpl) | Inter 10/normal, letterSpacing 0.5, $textMuted | `10px/400`, `0.5px`, `rgb(155,155,161)` |
| Пилюля-цель (VZPxZ) | r 9, [9,11], fill $surface, stroke $border 1 | `9px`, `9px 11px`, `rgb(255,255,255)`, `1px rgb(229,229,223)` |
| Имя цели (Ps9xn) | Inter 12.5/500 $textPrimary | `12.5px/500`, `rgb(23,23,30)` |
| Мета цели (Z4qkX) | Inter 11/normal $textMuted | `11px`, `rgb(155,155,161)` |
| Стрелка цели (DH2hF) | arrow-right 14×14 $textMuted | `14px×14px`, `rgb(155,155,161)` |
| «Выбрано: N …» (o0uiPT) | Inter 12/600 $textPrimary | `12px/600`, `rgb(23,23,30)` |
| «… объём» (mq1QS) | Inter 11/normal $textMuted | `11px`, `rgb(155,155,161)` |
| Счётчик шапки (tLN45) | Inter 12.5/normal $textSecondary | `12.5px`, `rgb(102,102,110)` |
| «Создать …» (mT7xr, Ghost+бордер) | [7,10], 11.5, stroke $border | `7px 10px`, `11.5px`, `rgb(229,229,223)` |

Строк/выбора: 5 строк (`rowCount: 5`), выбрано 3 (`selectedRowCount: 3`)
— как Row:1/2/4 мастера.

## Решения по мастер-нодам (отклонения зафиксированы)

1. **Задача называет пару accentSoft/accentSofter — мастер Body #5
   использует только accentSofter.** Прямое чтение P9U8QH: подсветка
   drag-over (jMnKo) — fill $accentSofter #EFF9F4 + stroke $accent
   1.5 + стрелка $accentStrong; в fills Body #5 $accentSoft отсутствует
   вовсе ($accentSofter ×5, $accent ×3). Реализация следует мастеру:
   пилюли — accentSofter с каймой accent, DropHint — accentSofter.
2. **Инструменты — рамочное поле с чекбоксами, а не сводка «3 из 7» с
   шевроном.** Мастер показывает нераcкрытый дропдаун-инстанс AYWJR;
   строить дропдаун — функциональное изменение (Non-Goals change).
   Функциональный список чекбоксов сохранён и свёрнут в рамку поля
   мастера ([7,10], r 8, $surface/$border), шеврон опущен как ложный
   афорданс. Формат дат «07 сен 2026» мастера — рендер нативного
   input type=date (локаль браузера); кастомный виджет даты вне рамок
   задачи.
3. **Кнопка «Собрать из Входящих» остаётся в панели, а не в шапке.**
   Мастер (a7Mqd, Secondary [7,12] 12) держит её в строке титула;
   перемещение кнопки — раскладка вне §3.6:2–4. Кнопка тематизирована
   Secondary-геометрией мастера (12, [7,12]→h-auto px-3 py-[7px]).
4. **Состав колонок таблицы доменный.** Мастер: Время/Инструмент/
   Направление/Кол-во/Цена/Объём; приложение хранит execId/Сумму/
   Комиссию и подпись «Выбрать всё» (Non-Goals: состав данных на
   месте). Типографика и тона применены ко всем колонкам по паттернам
   мастера (данные — 12 $textSecondary, инструмент — 12.5/500).
5. **DropHint (JkO8D) — состояние перетаскивания.** В макете отрисован
   статически; на странице появляется только при активном drag
   (dragExecIds), текст — с доменным числительным (formatCount).
   Подсветка пилюли при drag-over и DropHint не покрыты jsdom-тестами
   (drag-события), проверены сверкой кода с мастером.
6. **«Сбросить» возвращает фильтры к окну по умолчанию** (месяц назад →
   сегодня, обе стороны, все инструменты) через очистку URL-параметров
   и штатный эффект дефолтного диапазона — Ghost-инстанс b0ZLfV мастера
   ([6,10], 12).

## Реализация

- `components/ui/input.tsx` (новый): примитив-поле по нодам VYItk/euvt0/
  AYWJR — $surface, кайма $border, r 8 (--radius-sm), [7,10], Inter 12,
  placeholder $textMuted; фокус-ринг ring.
- `components/ui/checkbox.tsx` (новый): дизайн-примитив по нодам CB
  (lpeaS/mtES4) — 16×16, r 4, unchecked $surface/$border, checked
  $accent с галкой Lucide check 10×10 $surface; нативный input сохраняет
  семантику, галка — абсолютный peer-слой.
- `pages/inbox-page.tsx`: строка фильтров по ZvdbR (даты DateField с
  Calendar 13 textMuted + нативный индикатор скрыт, инструменты в рамке,
  группа направления по qoUNa, «Сбросить»); счётчик шапки по tLN45;
  карточка таблицы r 12 + шапка $surface2 + разделители $divider +
  выбранные строки $accentSofter; направление $info/$risk; панель целей
  по P9U8QH: подпись 10/0.5px, пилюли r 9 [9,11] 12.5/500 + 11, стрелки
  arrow-right 14, подсветка accentSofter/accent 1.5, DropHint, блок
  выбранного («Выбрано: N …» 12/600 + объём 11), форма создания на
  Input + Ghost-кнопка с каймой по mT7xr; моб. пилюли тематизированы.

## Тесты

Швы (согласованы задачей): существующие сценарные тесты
`inbox-page.test.tsx` (роли чекбоксов/кнопок сохранены — правок селекторов
не потребовалось) + новые: примитивы фильтров (data-slot, геометрия,
checked-состояние), «Сбросить» возвращает скрытый фильтром инструмент,
тонирование info/risk + выбор строки (data-state selected,
accentSofter-класс, счётчик/объём), панель (подпись, пилюля r 9 [9,11],
имя 12.5/500, мета словами); новые тесты примитивов `input.test.tsx`
(геометрия мастера, ввод) и `checkbox.test.tsx` (16/4/бордер, галка
peer-checked, клик-переключение). `npm test` — 40 файлов / 289 тестов
зелёные; `npm run build` проходит. Временные скрипты (мок-API, CDP-съёмка,
профиль Chrome) удалены, серверы остановлены.

<!-- Скриншот-сверка выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/07-pages/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: openspec:ui/design-system#requirement-reusable-design-primitives -->
<!-- Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D2 -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D3 -->
