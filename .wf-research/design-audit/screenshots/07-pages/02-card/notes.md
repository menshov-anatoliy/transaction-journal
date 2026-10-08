# Скриншот-сверка 7.2 — метрики через Metric, статус через StatusChip

Приёмка задачи 7.2 change `reconcile-frontend-with-design` по процессу
design.md D3: пара «макет/страница» для карточки конструкции (Body #2) по
расхождениям §3.3 аудита: 1 (Метрика 9× — dl-гриды kstrip/превью), 2
(Чип/Статус у имени вместо текстового span).

Процесс: макет — нода `yHC4d` (Body #2) из design.pen через MCP pen:
`Export` png 2× + `Get` по узлам (`Uw04r`/TitleRow, `X7CR1q`/Status,
`h69OG`/«Сводка метрик», M1–M9/`ZzdbC`…`e5auzF`) для авторитетных
значений; страница — живой рендер `http://localhost:5173/spa/constructions/7`
(Vite 5173 + мок-API 5019, фикстуры со значениями мастера: итог +385.3
USDT / 64% / 1240 / 78% / +240.2 / +145.1 / 3, котировки 07.10 13:51,
период с 12 авг, имя «Контртренд ETH под прикрытием путов») в headless
Chrome 1440×760 @2x через CDP: computed-стили и PNG. Браузер pen в моменте
был недоступен («A capture is already running») — как в сверках 4.2/7.1,
снятие выполнено CDP-скриптом против headless Chrome. Временные скрипты
(мок-API, CDP-скрипт, профиль Chrome) удалены, серверы остановлены.

## Файлы пары

- `design-frame.png` — Body #2 «Карточка конструкции» (yHC4d) из design.pen (2×).
- `app-page.png` — живая карточка `/spa/constructions/7` (1440×760 @2x).

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

Данные мастера сняты MCP pen `Get` (bounds — координаты нод внутри Body #2).

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| Строка титула (Uw04r/TitleRow) | Title 21/600 + Status, gap 10, align center | h1 21/600 + чип, `gap: 10px`, `align-items: center` |
| Чип статуса (X7CR1q) | 67×22, pill 999, [4,10], 11.5/500 accentSoft #E2F4EB / accentStrong #167853 | 67×22, `border-radius: full`, `padding: 4px 10px`, `11.5px/500`, `rgb(226,244,235)` / `rgb(22,120,83)` |
| Сводка метрик (h69OG) | fill $surface, stroke $border, r 12, [14,18], gap 20 | `bg rgb(255,255,255)`, `1px solid rgb(229,229,223)`, `border-radius: 12px`, `padding: 14px 18px`, `gap: 20px` |
| Число метрик | 9 инстансов jtDmV (M1–M9) | 9 × `[data-slot="metric"]` |
| Подпись (M*/Caption) | 11/normal $textMuted, letterSpacing 0.3, гэп колонки 3 | `11px/400`, `rgb(155,155,161)` = #9B9BA1, `0.3px`, `gap-[3px]` |
| Итог (M1/Value) | 16/600 $accentStrong #167853 | `16px/600`, `rgb(22,120,83)` = #167853 |
| Разбивка (M2–M9/Value) | 14/600 $textPrimary | `14px/600` (цвет — см. решение 2) |
| Индикатор/Полный | 1 инстанс | `[data-slot="fin-result-full"]` присутствует |

## Решения по мастер-нодам (отклонения зафиксированы)

1. **Интерлиньяж примитивов pinned к шрифтовому normal.** Мастер не задаёт
   lineHeight (Inter normal ≈ 1.21: чип 22px, метрика ~33px); без пина
   страница наследует 1.5 (чип 36px, сводка 74px). В `Metric` (подпись и
   значение) и `StatusChip` добавлен `leading-[normal]` — в merge Metric он
   идёт последним, иначе tailwind-merge снимает его при конфликте с
   text-размером из `valueClassName`. Высота сводки 67px против 63 мастера —
   остаточный рендер-шаг line-height, принят как допустимый.
2. **Цвет знака величин — доменная семантика, оставлена.** Мастер
   акцентирует только итог (M1 accentStrong), разбивка textPrimary; в
   приложении значения окрашиваются по знаку (`--fin-positive-strong` =
   accentStrong для положительных, `--fin-negative` для отрицательных) —
   устоявшееся правило финрезультата (те же toneClass в таблицах карточки и
   списке конструкций). Для положительных значений итог совпадает с
   мастером значение-в-значение.
3. **Формулировки подписей сохранены доменными** (паттерн решения 2 сверки
   7.1): «занято капитала, %»/«реализ. P&L» против «Занятость»/«Реализов.»
   мастера, капитал — в подписи «% капитала (6000)», формат чисел —
   доменный (`+385.3` против `+385,30`).
4. **Превью конструкции (N6abN) — состав показателей доменный.** Мастер
   «Карточка конструкции/Превью» держит 4 метрики (Капитал, Стоимость,
   Реализов., Нереализов.) + строку периода-счётчиков; приложение
   показывает 8 показателей с деградационными состояниями. Non-Goals change
   запрещают менять состав данных — dl-грид превью переведён на `Metric`
   (2 колонки, зазоры 12/10 мастера, значения 15/600 textPrimary), состав
   сохранён. Превью рендерится на Body #1 и в пару 7.2 не входит.
5. **Кнопки шапки и meta-строка мастера — вне рамки 7.2.** Ghost/Secondary
   кнопки TitleRow и строка «плановый риск 300 / плановый профит 900…» не
   входят в задачу (метрики + статус); кнопочный слой принят сверкой 2.x.

## Реализация

- `components/constructions/card/metric-strip.tsx`: dl-грид (9 dt/dd)
  заменён девятью `Metric` в surface-карточке мастера h69OG (радиус 12,
  кайма, [14,18], gap 20); итог — `text-[16px]`, разбивка — `text-[14px]`;
  деградационные состояния (сбой котировок/прочерки) сохранены.
- `components/constructions/construction-preview.tsx`: dl-грид (8 строк)
  заменён `Metric`-сеткой 2 колонки (gap 12/10 по N6abN), значения — базовая
  типографика примитива 15/600; помощники SignedValue/NullableValue
  сохранены (без bold-парафоса итога — в мастере превью его нет).
- `pages/construction-card-page.tsx`: текстовый span статуса заменён
  `StatusChip`; маппинг доменных статусов на тона мастера: open → pos
  (X7CR1q), closed → neutral (EIqx3), archived → muted (dAcLW); расширение
  tones примитива не потребовалось (neg/risk для статусов в макете нет).
- `components/design/metric.tsx`, `status-chip.tsx`: пин шрифтового
  интерлиньяжа `leading-[normal]` (решение 1).

## Тесты

Швы (согласованы задачей): existing-тесты карточки (`construction-card-page.test.tsx`:
шапка/метрики/деградации) + новые проверки — рендер 9 `Metric` и геометрия
карточки сводки, маппинг статусов на тона (it.each open/closed/archived);
`construction-preview.test.tsx` — 8 `Metric`, отсутствие `dl`, базовая
типографика значений, сетка мастера; точечные расширения design-тестов
примитивов (`metric.test.tsx`, `status-chip.test.tsx` — интерлиньяж).
`npm test` — 38 файлов / 280 тестов зелёные; `npm run build` проходит.

<!-- Скриншот-сверка выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/07-pages/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: openspec:ui/design-system#requirement-reusable-design-primitives -->
<!-- Traceability: openspec:ui/design-system#requirement-typography-matches-design-system -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D2 -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D3 -->
