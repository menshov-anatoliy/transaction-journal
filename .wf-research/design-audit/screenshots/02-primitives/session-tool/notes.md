# 02-primitives / session-tool — примитивы задачи 2.3

Change `reconcile-frontend-with-design`, задача 2.3: `SessionItem` и
`ToolStatus` в `frontend/src/components/design/`.

## Пары сверки

| Файл | Источник | Что показывает |
| --- | --- | --- |
| `design-frame.png` | MCP pen `Export` ×2: фрейм «Список сессий» `dy6On` (верх) + мастер «Tool-статус» `ix8ma` (низ), склейка на фоне `--bg` | SessionItem в состояниях «текущая» (S1) и обычных (S2–S4) в контексте списка; Tool-статус мастера |
| `app-components.png` | headless Edge ×2, `http://localhost:5173/spa/design-stand.html` | Живой рендер `SessionItem`/`ToolStatus` из `components/design/` на токенах `index.css` |

## Вывод сверки

Соответствие подтверждено по обоим примитивам — визуальная пара и проверка
computed-стилей живой страницы через CDP:

- **SessionItem ↔ Сессия/пункт (s9J3h)**: радиус 10 (`rounded-md` = 10px),
  паддинги [8,10] (`8px 10px`), зазор 3 (`gap 3px`); заголовок Inter
  12.5/600 textPrimary (`12.5px/600`, `#17171E`), время 11/normal textMuted
  (`#9B9BA1`) на краях строки (space-between, зазор 8), превью 11.5/normal
  textMuted одной строкой (textGrowth fixed-width → `truncate`).
- **Активное состояние** — по инстансам «Cur» (WizmS, экран «Агент · Чат
  активный» LYsBg): заливка `--surface-2` (`rgb(239,239,234)` = `#EFEFEA`)
  + бордер `--border` (`#E5E5DF`) 1px, доступно `aria-current="true"`.
- **ToolStatus ↔ Tool-статус (ix8ma)**: pill 999 (`3.35544e+07px` → pill),
  заливка surface2, паддинги [4,10] (`4px 10px`), зазор 7 (`gap 7px`),
  иконка Lucide loader 12×12 textMuted, подпись Inter 11.5/normal italic
  textSecondary. Инстансы dK1P3 («корпус правил…») и tMmi0 («рынок Bybit…»)
  меняют только текст.

## Состояния в макете

Формулировка задачи предполагала состояния «выполняется/готово/ошибка».
Прямое чтение дизайн-нод через MCP pen (`Get`) показывает: у Tool-статуса
в макете существует **единственное состояние «выполняется»** (иконка
loader, инстансы dK1P3 и tMmi0 различаются только текстом); состояний
«готово»/«ошибка» дизайн-ноды не содержат. По прецеденту StatusChip
(задача 2.2) отсутствующие в макете варианты в примитив не изобретаются —
расширение только с появлением дизайн-узла. Единственная надстройка над
статичным макетом — `animate-spin` на иконке: статичный кадр pen не
передаёт ход выполнения.

У SessionItem в макете два состояния: обычное (прозрачное) и «текущая
сессия». Инстансы расходятся в деталях: S1 (V1puZa, экран «Агент · Чаты»
PNErm) — заливка surface2 **без** обводки; Cur (WizmS, экран «Агент · Чат
активный» LYsBg) — surface2 **с** бордером border. Примитив реализует
полную форму Cur; вариант без бордера при необходимости снимается через
`className` на интеграции (5.2).

## Отклонения стенда (не примитива)

- В дизайн-кадр `dy6On` входит кнопка «+ Новый чат» (ref gruwZ) — на стенде
  не воспроизведена: кнопка — примитив задачи 2.1, вне объёма 2.3.
- Подписи-разделители «АКТИВНЫЕ»/«ЗАВЕРШЁННЫЕ» на стенде воспроизведены как
  контекст списка (это композиция экрана 7.5/5.2, не часть SessionItem).

## Параметры, извлечённые из дизайн-нод (источник — Get через MCP pen)

- Сессия/пункт s9J3h: `cornerRadius 10, layout vertical, gap 3,
  padding [8,10], width fill_container`; Top (V0eXN): `justify
  space_between, align center, gap 8`; Title (mPoUU) `$textPrimary
  12.5/600`; Time (SqhBW) `$textMuted 11/normal`; Preview (r6Idf)
  `$textMuted 11.5/normal, textGrowth fixed-width`.
- Инстансы s9J3h: Cur/WizmS `fill $surface2 + stroke $border`; S1/V1puZa
  `fill $surface2`; S2–S4 — без переопределений (тексты: «13:47»,
  «11:20», «вчера», «пн» — время произвольной строкой).
- Tool-статус ix8ma: `fill $surface2, cornerRadius 999, gap 7,
  padding [4,10], align center`; Icon (T9Zpza) `lucide loader 12×12
  $textMuted`; Label (PfMRJ) `$textSecondary 11.5/normal italic`.
