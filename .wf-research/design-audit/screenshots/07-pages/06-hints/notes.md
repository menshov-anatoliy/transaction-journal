# Скриншот-сверка 7.4 — карточки подсказок, фильтры на примитивах, источники `$font 11`

Приёмка задачи 7.4 change `reconcile-frontend-with-design` по процессу
design.md D3: пара «макет/страница» для Body #6 «Подсказки» по расхождениям
§3.7 аудита: 1 (таблица → карточки «Карточка подсказки» 6×), 2 (фильтры —
нативные select/кнопка → сегмент-контрол и поля-примитивы мастера), 3
(источники `font-mono` → Inter `$font 11`).

Процесс: макет — нода `LmLr6` (Body #6) из design.pen через MCP pen:
`Get(nodeId, {depth})` по зонам (`FlZDv` Content, `uQt8l` Шапка, `ccdID`
Фильтры, `btUq8` Status, `wgKkr`/`XyeI5` поля, `SVwot` Список, `NvaCc`/
`KSuGN`/`suSq3` ряды, `H0y2H` «Карточка подсказки», `Kn5dY` Кнопка/Ghost)
для авторитетных значений + `Export` png 2×; страница — живой рендер
`http://localhost:5173/spa/hints` (Vite 5173 + мок-API 5019, фикстуры
повторяют мастера: 6 карточек с текстами Card:1–6, статусы применена ×4 /
отклонена ×2, total 24) в headless Chrome 1440×760 @2x через CDP:
computed-стили и PNG. Временные скрипты (мок-API, CDP-съёмка, профиль
Chrome) удалены, серверы остановлены.

## Файлы пары

- `design-frame.png` — Body #6 «Подсказки» (LmLr6) из design.pen (2×).
- `app-page.png` — живая страница `/spa/hints` (1440×760 @2x, 6 карточек
  с текстами мастера).

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

Данные мастера сняты MCP pen `Get` (resolveVariables: `$font` = Inter,
строковый токен; `$font 11` — Inter 11/normal, не моноширинный).

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| Титул (tpZfS) | $font 21/600 $textPrimary | `21px/600`, `rgb(23,23,30)` |
| Счётчик шапки (pdFeV) | $font 12.5/normal $textSecondary | `12.5px/400`, `rgb(102,102,110)` |
| Сегмент статуса (btUq8) | $surface, stroke $border, r 9, [3], gap 4 | `9px`, `rgb(229,229,223)`, `rgb(255,255,255)`, `3px`, `4px` |
| Активный таб (n6u52b/I5LF84) | fill $accentSoft, r 7, [6,12], 12/600 $accentStrong | `7px`, `6px 12px`, `rgb(226,244,235)` = #E2F4EB, `12px/600`, `rgb(22,120,83)` = #167853 |
| Таб без выбора (Lmdo9/Nfth8) | 12/normal $textSecondary | `12px/400`, `rgb(102,102,110)` |
| Поле группы/характера (wgKkr/XyeI5) | $surface, stroke $border, r 8, [7,10], Inter 12/normal $textPrimary | `8px`, `rgb(229,229,223)`, `rgb(255,255,255)`, `7px 31px 7px 10px` (слот шеврона), `12px/400`, `rgb(23,23,30)` |
| Шеврон (G8TZmI) | chevron-down 13×13 $textMuted | `13px×13px`, `rgb(155,155,161)` |
| Сетка списка (SVwot/Row:1–3) | ряды по 2, гэп 12 | `12px`, `558px 558px` (2 колонки) |
| Карточка (H0y2H) | $surface, stroke $border, r 12, [14], gap 8 | `12px`, `rgb(229,229,223)`, `rgb(255,255,255)`, `14px`, `8px` |
| Группа карточки (dcQU6) | $font 12.5/600 $textPrimary | `12.5px/600`, `rgb(23,23,30)` |
| Чип статуса (aa6cK) | pill 999, [4,10], 11.5/500 | `full`, `4px 10px`, `11.5px/500` |
| Чип «применена» | accentSoft/accentStrong (Card:1) | `rgb(226,244,235)` / `rgb(22,120,83)` |
| Чип «отклонена» | negSoft/neg (Card:2 rdODt) | `rgb(251,234,231)` = #FBEAE7 / `rgb(209,75,65)` = #D14B41 |
| Текст карточки (urdBT) | $font 12.5/normal $textSecondary, lineHeight 1.5 | `12.5px`, `18.75px` (=1.5), `rgb(102,102,110)` |
| Сноска/мета/источники (QEiI8) | $font 11/normal $textMuted | `11px/400`, `rgb(155,155,161)`, `font-family: Inter…` (не mono) |
| Шрифт документа | $font = Inter | `Inter, ui-sans-serif…` |

## Решения по мастер-нодам (отклонения зафиксированы)

1. **Состав табов и полей доменный.** Мастер показывает 3 таба статуса
   (Все/Применено/Отклонено) и одну пару полей; домен журнала — 4 статуса
   (живая/применена/отклонена/погашена), поэтому сегмент-контрол несёт
   5 табов геометрией мастера (Non-Goals: состав данных на месте). Тон
   «погашена» без мастер-ноды — muted (как терминальный «архив» dAcLW).
2. **«Сбросить фильтры» остаётся условным.** Мастер рисует Ghost-кнопку
   (ciwcY, [6,10], 12) безусловно; приложение показывает её только при
   активном фильтре — прежнее поведение сохранено (Non-Goals).
3. **«Показать ещё 18» (More) не переносится.** Пагинации в домене нет
   (DEFAULT_LIMIT 200); диапазон выборки показан счётчиком шапки в слоте
   Count мастера («журнал всех субъектов · показано 6 из 24»).
4. **Подписи статусов — доменные слова.** Мастер: «Применено»/«Отклонено»;
   приложение: «применена»/«отклонена» (таксономия журнала). Тона и
   геометрия — инстансов мастера.
5. **Состав карточки расширен доменом.** Мастер H0y2H: шапка (группа +
   чип), текст, сноска «v1 · показана впервые 07.10 13:12». Приложение
   сохраняет видимость остальных атрибутов записи (субъект со ссылкой на
   конструкцию, характер, as-of, факты, источники) типографикой сноски
   мастера — Inter 11/normal $textMuted; сноска повторяет формат мастера:
   «правило {ruleId} · показана впервые …».
6. **Источники — `$font 11`, не моно.** Файлы источников печатаются Inter
   11/normal (computed `font-family: Inter…`, кегль 11) — моно в
   дизайн-системе живёт только в MD-редакторе (JetBrains Mono, D5);
   тег-пилюля — surface2, кегль блока тот же 11.

## Реализация

- `components/design/status-chip.tsx`: тон neg (negSoft/neg) по инстансам
  Body #6 (rdODt/AutfO) — расширение, разрешённое комментарием примитива.
- `pages/hints-page.tsx`: шапка (титул page-title + счётчик 12.5
  pdFeV-слот); фильтры ccdID — сегмент-контрол статуса (btUq8: r9/[3]/
  gap 4 + табы r7 [6,12], активный accentSoft/accentStrong 12/600) на
  role=radiogroup/radio, поля группы/характера (wgKkr/XyeI5: $surface,
  r8, [7,10], Inter 12, шеврон 13 $textMuted, appearance-none) на
  нативных select, сброс — Ghost [6,10] 12 (ciwcY); сетка карточек
  (SVwot: gap 12, ряды по 2 → `grid gap-3 sm:grid-cols-2`); карточка
  H0y2H: r12/[14]/gap 8, шапка (группа 12.5/600 + StatusChip), текст
  12.5/1.5 $textSecondary, мета/факты/источники/сноска 11 $textMuted;
  мобильная таблица-ветка удалена (карточки адаптивны: 1 колонка).

## Тесты

Швы (согласованы задачей): сценарные тесты `hints-page.test.tsx`
обновлены под карточную разметку с сохранением сценариев (журнал+ссылка+
источники+счётчик; передача фильтров в API; пустой журнал vs пустой
фильтр) + новые проверки: геометрия карточки и сетки, тон чипа по
инстансам мастера (pos/neg/muted), источники Inter 11 без font-mono,
сегмент-контрол (r9/[3], активный таб accentSoft 12/600, aria-checked),
поля-дропдауны (appearance-none, [7,10], подписи «Группа: все»/«Характер:
все»), Ghost-сброс возвращает таб «Все». `status-chip.test.tsx`: тон neg.
`npm test` — 40 файлов / 296 тестов зелёные; `npm run build` проходит.

<!-- Скриншот-сверка выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/07-pages/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: openspec:ui/design-system#requirement-reusable-design-primitives -->
<!-- Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D2 -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D3 -->
