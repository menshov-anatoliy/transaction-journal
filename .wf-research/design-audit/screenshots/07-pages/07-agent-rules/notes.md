# Скриншот-сверка 7.6 — карточки правил с пилюлями-атрибутами, краткий попап

Приёмка задачи 7.6 change `reconcile-frontend-with-design` по процессу
design.md D3: пара «макет/страница» для Body #7 «Агент → Правила» по
расхождениям §3.8:1 (строки списка li/p-2 + текстовая RulePopupCard →
«Карточка правила/Полная» с пилюлями-атрибутами r999 и секциями
accentSoft) и §3.5:7 (попап правила без дизайн-оформления → «Попап
правила/Краткий»).

Процесс: макет — нода `R3kdzS` (Body #7) и `Lpcap` («Попап правила/
Краткий») из design.pen через MCP pen: `Get(nodeId, {depth})` по зонам
(`SAYaX` Заголовок и табы, `z5Fqgz`/`VBxP8`/`k42Hl` табы, `zYZRw` Cnt,
`Q7Ur0` Поиск, `viuZI` Фильтры, `euAq5` группа, `Ib62d`/`DwR5J` чипы,
`cRRlh` Каталог, `Zes7z` «Карточка правила/Полная», `HLLTo`/`V3si6K`/
`GjcwT`/`p4Ot2` её секции, `aa6cK` «Чип/Статус», `Lpcap`/`oYr9W`/
`rlwtL`/`lidNU` попап) для авторитетных значений + `Export` png 2×;
страница — живой рендер `http://localhost:5173/spa/agent` (Vite 5173 +
мок-API 5019, фикстуры повторяют инстансы R:1–R:4 мастера: 4 карточки с
текстами мастера, чат со ссылкой на карточку правила в следе) в
headless Chrome 1440×760 @2x через CDP: клик таба «Правила» и клик по
заголовку первой карточки (правая панель), ховер ссылки правила для
попапа; computed-стили и PNG. Временные скрипты (мок-API, CDP-съёмка,
профили Chrome) удалены, серверы остановлены.

## Файлы пары

- `design-frame.png` — Body #7 «Агент → Правила» (R3kdzS) из design.pen (2×).
- `design-popup-Lpcap.png` — «Попап правила/Краткий» (Lpcap) из design.pen (2×).
- `app-page.png` — живая страница `/spa/agent`, вкладка «Правила»
  (1440×760 @2x: табы, счётчик, поиск, фильтры-чипы, каталог 2×2,
  правая панель с открытой карточкой).
- `app-popup.png` — живая страница, вкладка «Чаты»: попап при наведении
  на ссылку карточки правила в следе источников.

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

Данные мастера сняты MCP pen `Get` (resolveVariables: `$accentSoft` =
#E2F4EB, `$accentStrong` = #167853, `$risk` = #DFA23B, `$riskSoft` =
#F9EFD9, `$info` = #4A7FB5, `$infoSoft` = #E9F1F8, `$surface2` =
#EFEFEA, `$border` = #E5E5DF, `$textMuted` = #9B9BA1, `$textSecondary`
= #66666E, `$font` = Inter).

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| Активный таб «Правила» (k42Hl) | fill $accentSoft, r 7, [6,14], 12.5/600 $accentStrong | `rgb(226,244,235)` = #E2F4EB, `7px`, `6px 14px`, `12.5px/600`, `rgb(22,120,83)` = #167853 |
| Таб «Чаты» без выбора (VBxP8) | 12.5/normal $textSecondary | `12.5px/400`, `rgb(102,102,110)`, без заливки |
| Сегмент табов (z5Fqgz) | $surface, stroke $border, r 9, [3], gap 4 | `bg-card` #FFFFFF, кайма $border, `9px`, `p-[3px]`, `gap-1` |
| Счётчик корпуса (zYZRw) | $font 12/normal $textMuted | `12px/400`, `rgb(155,155,161)` |
| Поиск (Q7Ur0) | $surface, stroke $border, r 10, [10,14], gap 10 | `10px`, `rgb(255,255,255)`, `10px 14px`, `10px`; плейсхолдер мастера |
| Иконка поиска (pLY2z) | search 15×15 $textMuted | `15px×15px`, `rgb(155,155,161)` |
| Выбранный чип «Все» (Ib62d) | fill/stroke $accentSoft, r 999, [4,10], 11.5/600 $accentStrong | `rgb(226,244,235)` fill+border, pill (full), `4px 10px`, `11.5px/600`, `rgb(22,120,83)` |
| Невыбранный чип (DwR5J) | $surface, stroke $border, r 999, [4,10], 11.5/normal $textSecondary | `rgb(255,255,255)`, `rgb(229,229,223)`, pill, `4px 10px`, `11.5px/400`, `rgb(102,102,110)` |
| Каталог (cRRlh) | ряды по 2, гэп 12 | `12px`, 2 колонки grid |
| Карточка (Zes7z) | $surface, stroke $border, r 12, [18], gap 12 | `12px`, `rgb(255,255,255)`, кайма `rgb(229,229,223)`, `18px`, `12px` |
| Заголовок карточки (o1dx4I) | $font 14.5/600 $textPrimary | `14.5px/600`, `rgb(23,23,30)` |
| Id правила (NLBwE) | $font 11/normal $textMuted | `11px/400`, `rgb(155,155,161)` |
| Пилюля характера (nrPzY «Мягкое») | $riskSoft/$risk, pill 999, [4,10], 11.5/500 | `rgb(249,239,217)` = #F9EFD9 / `rgb(223,162,59)` = #DFA23B, pill, `4px 10px`, `11.5px/500` |
| Пилюля чёткости (U6mVZ «Однозначное») | $infoSoft/$info | `rgb(233,241,248)` / `rgb(74,127,181)` |
| Пилюля субъекта (u9Vm0h) | $surface2/$textSecondary | `rgb(239,239,234)` / `rgb(102,102,110)` |
| Тело карточки (GjcwT) | $font 13/normal $textSecondary, lineHeight 1.55 | `13px`, `20.15px` (=1.55), `rgb(102,102,110)` |
| Ссылка футера (qerdC) | $font 12/normal $accentStrong | `12px/400`, `rgb(22,120,83)` |
| Иконка футера (xCz4P) | external-link 12×12 $accentStrong | `12px×12px`, `rgb(22,120,83)` |
| Попап (Lpcap) | w 280, r 10, [12], gap 7, тень 0 8 24 #17171E20 | `280px`, `10px`, `12px`, `7px`, `rgba(23,23,30,0.125) 0px 8px 24px` (= #17171E20) |
| Заголовок попапа (oYr9W) | $font 12.5/600 $textPrimary | `12.5px/600`, `rgb(23,23,30)` |
| Мета попапа (rlwtL) | $font 11/normal $textMuted | `11px/400`, `rgb(155,155,161)` |
| Суть попапа (lidNU) | $font 12/normal $textSecondary, lineHeight 1.45 | `12px`, `17.4px` (=1.45), `rgb(102,102,110)` |
| Шрифт документа | $font = Inter | `Inter, ui-sans-serif…` |

## Решения по мастер-нодам (отклонения зафиксированы)

1. **«Секции accentSoft» = 4 заливки мастера, не секции карточки.**
   `Get` по R3kdzS показывает: `$accentSoft ×4` в Body #7 — активный
   таб k42Hl и три выбранных чипа фильтров Ib62d/HcPeA/Sh1JT «C:Все»;
   сами карточки несут пары riskSoft/infoSoft/surface2/negSoft. Перенесены
   и таб, и выбранные чипы (аудит §3.8 приписывал accentSoft карточкам —
   по агрегатам без разбора нод).
2. **Таксономия атрибутов доменная.** Мастер-макет рисует упрощённый
   корпус («Мягкое/Жёсткое», «Однозначное/Формальное», «журнал/
   стратегия»); реальный корпус — 10 характеров справочника, crisp/fuzzy,
   construction/portfolio. Пилюли несут доменные подписи (характер —
   слова того же справочника, что и подсказки; crisp/fuzzy → «Однозначное/
   Формальное» мастера; construction/portfolio → «конструкция/журнал»),
   тона — пар мастера: характер $riskSoft/$risk (доминантный инстанс
   nrPzY «Мягкое»; пара $negSoft/$neg «Жёсткое» не имеет аналога в
   таксономии корпуса), чёткость $infoSoft/$info (U6mVZ), субъект
   surface2/$textSecondary (u9Vm0h).
3. **Фильтры — чипы, но с доменным набором.** Группа «Характер» мастера
   (Все/Мягкое/Жёсткое) в приложении разворачивается в 10 чипов
   справочника; поведение фильтрации (одиночный выбор в группе, «Все»
   снимает) и запросы к API сохранены (Non-Goals: данные на месте).
4. **Тело карточки каталога — полной карточкой API.** Список каталога
   несёт только атрибуты, а тело GjcwT мастера — часть вида карточки:
   карточки каталога догружают описание через GET /rules/{id} (кэш
   react-query, только на вкладке правил); в каталоге — краткое
   содержание (действие/триггер), полные секции (триггер, действие,
   пороги, источники) — в правой панели типографикой мастера.
5. **Правая панель сохранена.** Мастер Body #7 не рисует панель —
   каталог во всю ширину с карточками 520px. Приложение сохраняет
   прежнее поведение «клик по заголовку → полная карточка» (Non-Goals:
   поведение сохранено), поэтому каталог занимает левую колонку при
   панеле 26rem (карточки ~340px вместо 520px). Панель рендерит ту же
   «Карточку правила/Полную» с полными секциями.
6. **Попап плавает над лентой.** Инстанс IBheJ Body #4 — absolute
   (x 560, y 150) поверх ленты; приложение рендерит попап
   absolute у правого верхнего края области ленты (якорение к конкретной
   ссылке — эскалация поведения, не дизайна). Наведение/уход/клик по
   ссылке правила сохранены.
7. **Счётчик корпуса.** Мастер «корпус · 64 правила · только чтение»;
   приложение считает от total API с русской формой множественного
   числа (в фикстурах — 4 правила мастера).

## Реализация

- `components/design/status-chip.tsx`: тон risk (riskSoft/risk) по
  инстансу Body #7 nrPzY «Мягкое» — расширение, разрешённое комментарием
  примитива («только с появлением дизайн-узла»).
- `pages/agent-page.tsx`: шапка мастера (титул 21/600 + сегмент табов
  z5Fqgz c активным accentSoft r7 [6,14] 12.5/600 + счётчик Cnt 12
  $textMuted); вкладка «Правила»: поиск Q7Ur0 (r10, [10,14], search 15
  $textMuted, плейсхолдер 13), фильтры viuZI (метка 11.5 $textMuted +
  пилюли 999 [4,10] 11.5: выбранный accentSoft/accentStrong 600,
  невыбранный $surface/$border $textSecondary), каталог cRRlh
  (`grid gap-3 sm:grid-cols-2`, scroll 34rem) карточками Zes7z (r12,
  [18], gap 12; заголовок 14.5/600 + id 11 $textMuted; пилюли-атрибуты
  StatusChip risk/info/neutral; тело 13/1.55 $textSecondary; футер
  external-link 12 + ссылка 12 $accentStrong); правая панель — полная
  карточка с секциями; `RuleBriefPopup` Lpcap (w 280, r 10, [12], gap 7,
  тень #17171E20 0 8 24; 12.5/600 / 11 $textMuted / 12/1.45
  $textSecondary) плавает в области ленты чата.

## Тесты

Швы (согласованы задачей): сценарные тесты `agent-page.test.tsx`
обновлены и дополнены: фильтры каталога — кликами по чипам-пилюлям
(запрос в API с character/search сохранён); новые проверки — табы
мастера (активный accentSoft/accentStrong r7, неактивный
$textSecondary), счётчик «корпус · N …», поиск Q7Ur0 (13, плейсхолдер,
иконка 15 $textMuted), выбранные «Все»/невыбранные чипы фильтров
(pill/accentSoft/surface), карточка Zes7z (r12, [18], gap 12, id 11
$textMuted, пилюли risk/info/neutral, тело 13/1.55 из GET /rules/{id},
футер-ссылка accentStrong 12, клик по заголовку открывает панель),
краткий попап Lpcap при ховере ссылки правила в следе источников
(w 280, r 10, тень, 12.5/600, мета 11 $textMuted, суть 12/1.45).
`status-chip.test.tsx`: тон risk. `npm test` — 40 файлов / 304 теста
зелёные; `npm run build` проходит.

<!-- Скриншот-сверка выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/07-pages/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: openspec:ui/design-system#requirement-reusable-design-primitives -->
<!-- Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D2 -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D3 -->
