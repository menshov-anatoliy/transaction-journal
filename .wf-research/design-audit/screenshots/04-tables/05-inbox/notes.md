# Скриншот-сверка 4.2 — таблица Body #5 «Входящие»

Приёмка задачи 4.2 change `reconcile-frontend-with-design` по процессу
design.md D3/D6. Сырая HTML-таблица `inbox-page.tsx` переведена на общий
слой `ui/table.tsx` с плотностью `regular` (карта 4.1).

## Пара

- `design-frame.png` — нода «Непривязанные сделки» (zJIJt) внутри Body #5
  (TECU5) экрана «Экран/Входящие» (B1GoG1); Export 2× через MCP pen.
- `app-page.png` — `/spa/inbox` (1440×760, Vite 5173 + мок-API 5019,
  фикстуры: 5 непривязанных сделок + 2 конструкции-цели).

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

Данные мастера сняты MCP pen `Get` по нодам zJIJt (HeaderRow/HC:\*, Row:N/C\*).

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| data-density | — | `regular` |
| шапка HC:Время (TpqO5) | [8,10], Inter 11/600, $textSecondary #66666E | `padding: 8px 10px`, `11px/600` Inter, `rgb(102,102,110)` = #66666E |
| ячейка C1 времени (wFOZv) | [8,10], Inter 12/normal | `8px 10px`, `12px/400` Inter |
| инструмент C2 (MazqS) | Inter 12.5/500, $textPrimary #17171E | `12.5px/500` Inter, `rgb(23,23,30)` = #17171E |
| строк данных | Row:1–Row:5 | 5 строк (`tbody tr`) |

Computed-сверка снята CDP-скриптом против headless Chrome на живой
странице (браузер pen в моменте был недоступен, скриншот app-страницы —
тот же headless Chrome 1440×760).

## Реализация

`inbox-page.tsx`: `<table className="min-w-full text-sm">` (было
`inbox-page.tsx:316`, `px-2 py-2`, `bg-muted/40` на шапке) →
`<Table density="regular">` + `TableHeader/TableRow/TableHead` и
`TableBody/TableRow/TableCell`; инструмент —
`<span className="text-[12.5px] font-medium">` (как имя в
constructions-table). Колонки (Выбор, Время, execId, Инструмент,
Направление, Количество, Цена, Сумма, Комиссия), чекбоксы выбора,
drag&drop строк и обёртка `overflow-x-auto rounded border` сохранены.
Радиус 12 таблицы и цветовой микс ячеек мастера — доводка 7.x (как в
выводах 4.1).

## Тесты

`npm test` — 38 файлов / 264 теста зелёные; `npm run build` проходит.
Существующие тесты `inbox-page.test.tsx` завязаны на роли
(`getByRole("table")`, чекбоксы, тексты), а не на сырую разметку — правки
селекторов не потребовались.

<!-- Скриншот-сверка таблиц выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/04-tables/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D6 -->
