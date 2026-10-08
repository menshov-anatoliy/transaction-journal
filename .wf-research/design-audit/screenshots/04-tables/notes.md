# Скриншот-сверка 4.1 — плотности таблиц Body #1 / #2 / #8

Приёмка задачи 4.1 change `reconcile-frontend-with-design` по процессу
design.md D3/D6. Общий слой таблиц получил варианты плотности, приложения —
`constructions-table` (comfortable), 4 таблицы карточки (compact), журнал
зусков синхронизации (dense). `inbox-page` не трогался (задача 4.2, регуляр
8/10 для него уже в слое).

## Карта плотностей, извлечённая из мастера (MCP pen `Get`, дизайн-ноды)

Паддинги мастера — `[вертикаль, горизонт]`.

| Плотность | Экран/нода | Заголовок HC* | Ячейка C* |
|---|---|---|---|
| comfortable | Body #1 «Таблица конструкций» (pKJ6k) | [8,10], Inter 11/600, $textSecondary | [9,10], Inter 12/normal; имя 12.5/500 $textPrimary |
| regular | Body #5 «Непривязанные сделки» (применит 4.2) | [8,10], 11/600 $textSecondary | [8,10], 12/normal; инструмент 12.5/500 |
| compact | Body #2 «Позиции»/«Сделки»/«Закрывающие записи»/«Корректировки PnL» | [7,12], 11/600 $textSecondary | [7,12], 12/normal; первая колонка 12/500 $textPrimary |
| dense | Body #8 «Журнал запусков» (D91td) | [6,12], 11/600 $textSecondary | [6,12], 11.5/normal |

Заголовки всех таблиц мастера едины: Inter 11/600 на `$textSecondary`
(#66666E). Аудит §4.2/§7.7 подтверждал поэкранность [9,10]×54 / [7,12]×51 /
[8,10]×42 / [6,12]×25; прямое чтение нод уточнило, что [8,10] в Body #1 —
это шапка, а ячейки Body #1 — [9,10] (заголовок уже ячейки).

## Реализация слоя

`ui/table.tsx`: `Table density?: TableDensity` (контекст + `data-density`),
экспортируемые маппинги `tableDensityCellClasses` / `tableDensityHeadClasses`
(шов юнит-тестов); без `density` — прежняя shadcn-геометрия (h-10/px-2/p-2),
поэтому `hints-page` не изменился. Кегли 12/11.5 — из требования типографики.

## Файлы пар

- `01-constructions/design-frame.png` — нода «Таблица конструкций» (pKJ6k,
  Export 2×); `app-page.png` — `/spa/` (1440×760, Vite + мок-API 5019).
- `02-card/design-frame.png` — секции «Позиции»/«Сделки»/«Закрывающие
  записи»/«Корректировки PnL» (B4HVo/tfdoR/GoQSG/kaNSW, Export 2×, склейка
  вертикально с зазором 24 на фоне $bg); `app-page.png` —
  `/spa/constructions/7` (1440×2100).
- `08-sync-log/design-frame.png` — нода «Журнал запусков» (D91td, Export 2×;
  фон прозрачный, рендерится тёмным — значения сверялись по ноде и computed);
  `app-page.png` — `/spa/sync-settings` (1440×1000).

Данные страниц — фикстуры юнит-тестов страниц (constructions/card/sync),
мок-API поднимался на 5019 (прокси Vite), бэкенд не поднимался.

## Вывод: computed-стилы живых страниц = мастер (значение-в-значение)

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| #1 шапка | [8,10], Inter 11/600 $textSecondary | `padding: 8px 10px`, `11px/600` Inter, `rgb(102,102,110)` = #66666E |
| #1 ячейка | [9,10], Inter 12/normal | `9px 10px`, `12px/400` Inter |
| #1 имя | 12.5/500 $textPrimary | `12.5px/500`, `rgb(23,23,30)` = #17171E |
| #2 шапка ×4 | [7,12], 11/600 $textSecondary | `7px 12px`, `11px/600`, #66666E — все 4 таблицы |
| #2 ячейка ×4 | [7,12], 12/normal | `7px 12px`, `12px/400` |
| #2 первая колонка | 12/500 $textPrimary | `12px/500`, #17171E |
| #8 шапка | [6,12], 11/600 $textSecondary | `6px 12px`, `11px/600`, #66666E |
| #8 ячейка | [6,12], 11.5/normal | `6px 12px`, `11.5px/400` |
| Плотности | — | `data-density`: comfortable / compact×4 / dense |

Пиксельная санити-выборка пар: общие цвета дизайн-системы на обеих
сторонах — фон $bg (243,243,239), поверхность #FFFFFF, разделитель
$divider/#E5E5DF, акцент #1FA36B, profitZone #B9E7CC, $textSecondary
#66666E.

## Расхождения вне зоны 4.1 — не исправлялись

- Цвет ячеек по колонкам ($textPrimary/$textSecondary-микс мастера) и чипы
  статусов — доводка 7.1/7.2; в 4.1 перенесены только плотности и кегли.
- Радиус таблиц 12 и секции карточки — §3.3.3, доводка 7.2.
- 13 колонок таблицы конструкций против 10 в макете — паритет данных (§3.2),
  состав колонок не входит в задачу плотности.
- `text-sm`-остаток (заголовки секций, формы) — доводка 7.x.

## Тесты

`npm test` — 38 файлов / 264 теста зелёные; `npm run build` проходит. Новый
шов `src/components/ui/table.test.tsx` — 14 тестов: маппинг плотность→классы
(шапка/ячейка), применение к рендеру, `data-density`, легаси-геометрия без
плотности.
<!-- Скриншот-сверка таблиц выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/04-tables/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D6 -->
