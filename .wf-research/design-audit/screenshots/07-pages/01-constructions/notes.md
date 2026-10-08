# Скриншот-сверка 7.1 — титулы 21/600, шапка итога (oYD4G), кнопка разбора в тулбаре

Приёмка задачи 7.1 change `reconcile-frontend-with-design` по процессу
design.md D3: пара «макет/страница» для страницы «Конструкции» (Body #1) по
расхождениям §3.2 аудита: 1 (титул 21/600), 5 (шапка итога по фрейму «Итог»
oYD4G), 8 (кнопка разбора входящих Secondary в тулбаре).

Процесс: макет — экспорт нод `X2ic2p` (Body #1) и `oYD4G` («Итог») из
design.pen через MCP pen (`Export` png 2×) + `Get` по нодам
(`ZkeW5`/H1, `oYD4G`, `R840hE`/SyncBtn, `RV74d`/Кнопка/Secondary) для
авторитетных значений; страница — живой рендер `http://localhost:5173/spa/`
(Vite 5173 + мок-API 5019, фикстуры со значениями мастера: итог
+1284.5 USDT, реализов. +940.2 / нереализов. +344.3, 8 конструкций / 5
открыто, бейдж «Входящие» 12) в headless Chrome 1440×760 через CDP:
computed-стили и PNG. Браузер pen в моменте был недоступен («A capture is
already running») — как и в сверке 4.2, computed-снятие выполнено
CDP-скриптом против headless Chrome.

## Файлы пары

- `design-frame.png` — Body #1 «Конструкции» (X2ic2p) из design.pen (2×).
- `design-summary-oYD4G.png` — фрейм «Итог» (oYD4G) из design.pen (2×):
  авторитетный мастер шапки итога по тексту задачи 7.1.
- `app-page.png` — живая страница «Конструкции» (1440×760).

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

Данные мастера сняты MCP pen `Get`.

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| Титул H1 (ZkeW5) | Inter 21/600 $textPrimary #17171E, letterSpacing не задан | `21px/600` Inter, `rgb(23,23,30)` = #17171E, `letterSpacing: normal` |
| Блок итога (oYD4G) | vertical, padding [14,16], gap 3 | `display: flex` (column), `padding: 14px 16px`, `gap: 3px` |
| Значение итога (oYD4G/T) | Inter 18/600 $accentStrong #167853 | `18px/600` Inter, `rgb(22,120,83)` = #167853 |
| Подпись разбивки (oYD4G/P) | Inter 11/normal $textSecondary #66666E | `11px/400` Inter, `rgb(102,102,110)` = #66666E |
| Счётчики (oYD4G/C) | Inter 11/normal $textMuted #9B9BA1 | `11px/400` Inter, `rgb(155,155,161)` = #9B9BA1 |
| Кнопка «Разобрать входящие» (RV74d Secondary) | fill $surface #FFFFFF, stroke $border #E5E5DF, радиус 8, Label Inter 13/500 $textPrimary | `bg rgb(255,255,255)`, `border rgb(229,229,223)`, `border-radius: 8px`, `13px/500` Inter, `rgb(23,23,30)`, `href="/spa/inbox"` |
| Тулбар (TitleRow X2ic2p) | H1 + Spacer + кнопка в строке титула, gap 12 | тулбар `flex` c `ml-auto`-группой действий в строке титула, `gap-3` |

## Решения по мастер-нодам (отклонения зафиксированы)

1. **Шапка итога — по фрейму «Итог» (oYD4G), а не по TotalRow Body #1.**
   В Body #1 итог показан строкой TotalRow (20/600, выравнивание end) — ранняя
   компоновка; задача 7.1 явно называет oYD4G (18/600 accentStrong, [14,16])
   мастером шапки итога, что подтверждено прямым чтением ноды: вертикальный
   стек значение → подпись → счётчики, [14,16], gap 3. Отметка котировок
   свёрнута в подпись разбивки через «·» (паттерн строки P).
2. **Формулировки подписей сохранены доменными.** Мастер: «конструкций 8 ·
   открыто 5», страница: «8 конструкций (5 открыто)»; «котировки на 2026-10-07
   13:51» против «котировки 13:51». Аудит §3.2:5 требует типографику, цвета и
   паддинги, не формулировки; счётчики и дата остаются в ведении домена
   (formatCount/pluralForm/formatMoment).
3. **Кнопка «Синхронизировать» остана outline/sm.** §3.2:8 упоминает Primary
   для неё, но смена варианта не входит в задачу 7.1 (строго титулы, итог,
   кнопка разбора) — не исправлялось, остаётся для пер-страничной доводки.
4. **Вертикальный паддинг кнопки разбора 8px против 9px дизайн-профиля
   RV74d** (высота примитива `h-9` принята сверкой 2.1); геометрия примитива
   вне рамок 7.1.
5. **Тексты masters «Собрать из „Входящих“» (Body #5) в макете нет** —
   надпись «Разобрать входящие» взята из формулировки задачи 7.1/§3.2:8
   (аудит: «в коде кнопки разбора в шапке нет»).

## Реализация

- `index.css`: `@utility page-title` — Inter 21/600 (line-height 28, без
  tracking; кегль дизайн-токена вместо `text-2xl` 24px).
- Титулы переведены на `page-title`: constructions, inbox, hints, agent,
  sync-settings, construction-card (3 вхождения, включая flex-вариант с
  именем-статусом), error-page, `section-placeholder.tsx`.
- `constructions-page.tsx`: тулбар в строке титула (кнопка «Разобрать
  входящие» variant="secondary" как `<Link to="/inbox">` + «Синхронизировать»
  справа); `ConstructionsHeader` — вертикальный блок
  `data-slot="constructions-summary"` по oYD4G (значение 18/600
  `text-accent-strong`, подписи 11 `text-text-secondary`/`text-text-muted`,
  `px-4 py-3.5 gap-[3px]`), отметка котировок в подписи через «·»,
  статус-строки синхронизации сохранены.

## Тесты

Швы (согласованы задачей): existing page-тесты (титулы через
`findByRole("heading")`, поведение шапки/кнопок) + точечные новые проверки
классов дизайн-типографики (паттерн design-тестов репо, напр.
`status-chip.test.tsx`). `npm test` — 38 файлов / 274 теста зелёные;
`npm run build` проходит (`.page-title{font-size:21px;font-weight:600;
line-height:28px}` в сборке).

<!-- Скриншот-сверка выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/07-pages/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: openspec:ui/design-system#requirement-typography-matches-design-system -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D2 -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D3 -->
