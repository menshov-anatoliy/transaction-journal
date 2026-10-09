# Скриншот-сверка 7.5 — форма нового чата на примитивах, источники SourceChip

Приёмка задачи 7.5 change `reconcile-frontend-with-design` по процессу
design.md D3: пара «макет/страница» для Body #3 «Агент → Чаты» по
расхождению §3.4:2 аудита (форма нового чата — нативные `select`/`input`/
`textarea` c `rounded-md border px-2 py-1.5` → форма на примитивах-полях
мастера; источники — чекбокс-филдсет → чипы «Чип/Источник»).

Процесс: макет — нода `PNErm` (Body #3) из design.pen через MCP pen:
`Get(nodeId, {depth})` по зонам (`dy6On` Список сессий, `x7B9P1` →
`Z14sH` «Композер/Агент», `ZCrdN` Params, `bCLlQ` BindChip, `aF5vM`
ModelChip, `IIFw0` Sources, `fYadZ` «Чип/Источник», `w4QMx` AddSrc,
`dkwDP` Placeholder) для авторитетных значений + `Export` png 2×;
страница — живой рендер `http://localhost:5173/spa/agent` (Vite 5173 +
мок-API 5019, фикстуры повторяют мастера: 2 активные + 2 завершённые
сессии, обмен сообщений со следом источников) в headless Chrome
1440×760 @2x через CDP: computed-стили и PNG. Временные скрипты
(мок-API, CDP-съёмка, профиль Chrome) удалены, серверы остановлены.

## Файлы пары

- `design-frame.png` — Body #3 «Агент → Чаты» (PNErm) из design.pen (2×).
- `app-page.png` — живая страница `/spa/agent`, вкладка «Чаты»
  (1440×760 @2x, форма с выбранными по умолчанию источниками).

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

Данные мастера сняты MCP pen `Get` (resolveVariables: `$surface2` =
#EFEFEA, `$infoSoft` = #E9F1F8, `$info` = #4A7FB5, `$accent` = #1FA36B,
`$textMuted` = #9B9BA1, `$textSecondary` = #66666E, `$font` = Inter).

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| Поле модели (aF5vM) | $surface2 #EFEFEA, r 8, [5,10], 12/500 $textPrimary | `rgb(239,239,234)`, `8px`, `5px 32px 5px 10px` (слот шеврона), `12px/500`, `rgb(23,23,30)` |
| Шеврон модели (z6IqjT) | chevron-down 12×12 $textMuted | `12px×12px`, `rgb(155,155,161)` |
| Поле конструкции (bCLlQ) | $infoSoft #E9F1F8, r 999, [5,10], 12/normal $textSecondary | `rgb(233,241,248)`, `full` (999), `5px 10px 5px 28px` (слот link-иконки), `12px/400`, `rgb(102,102,110)` |
| Иконка привязки (lcKMT) | link 12×12 $info #4A7FB5 | `12px×12px`, `rgb(74,127,181)` |
| Чип «журнал» (fYadZ/R8Z6z6) | $surface #FFFFFF, stroke $border, r 999, [5,10], check 12 $accent, 12/normal $textSecondary | `rgb(255,255,255)`, `full`, `5px 10px`, `12px/400`, `rgb(102,102,110)`; иконка `12px×12px`, `rgb(31,163,107)` = #1FA36B |
| Чип «рынок Bybit» (xB2ZS) | globe 12×12 $accent, подпись инстанса «рынок Bybit» | `aria-pressed="true"`, `data-selected="true"`, иконка globe тем же классом accent |
| Первое сообщение (dkwDP) | 13.5/normal $textMuted, текст «Спросите агента о конструкции, правилах или рынке…» | `13.5px/400`, `rgb(23,23,30)`; плейсхолдер — текст мастера |
| Шрифт полей | $font = Inter | `Inter, ui-sans-serif…` |

## Решения по мастер-нодам (отклонения зафиксированы)

1. **Композиция формы доменная.** Мастер Body #3 рисует форму нового
   чата композером `Z14sH` в центральной колонке (карточка $surface,
   r 14, [14]: плейсхолдер + params-строка + строка чипов + Send 36×36).
   Приложение сохраняет панель «Новый чат» слева (Non-Goals: состав
   экрана на месте), перенося в неё контуры мастера: поле модели — чип
   aF5vM, поле конструкции — чип bCLlQ, сообщение — 13.5/normal с
   плейсхолдером dkwDP, источники — чипы IIFw0. Композер отправки в
   центре живёт в ленте чата (задача 5.1, отдельный мастер Body #4).
2. **Состояние выбора чипов.** В мастере все инстансы чипов композера
   «выбраны» (иконка accent); состояния снятия выбора в design.pen нет.
   Выбранный чип = вид мастера fYadZ; невыбранный приглушается до
   textMuted и иконкой, и подписью (по здравому смыслу), признак —
   `aria-pressed`/`data-selected`. Клик по чипу переключает выбор.
3. **AddSrc (w4QMx) не переносится.** «+» добавления источника в
   мастере открывает произвольный набор; доменный справочник источников
   закрыт — ровно три категории (change add-agent-chat), расширение
   только через домен.
4. **Подписи чипов — инстансы мастера:** «журнал», «корпус правил»,
   «рынок Bybit» (строчная первая буква), у рынка — иконка globe;
   пояснение источника (`hint`) — в подсказке `title` чипа.
5. **Кнопка «Создать чат»** — примитив Кнопка/Primary (r 8, [9,16],
   13/500) из задачи 2.1; Send 36×36 мастера в панели не дублируется —
   отправка первого сообщения в приложении осталась кнопкой создания
   (поведение сохранено, Non-Goals).

## Реализация

- `components/design/source-chip.tsx`: пропс `selected` (по умолчанию
  true — вид мастера, использования 5.2 не затронуты), невыбранный чип
  приглушён до textMuted (иконка и подпись), атрибут `data-selected`.
- `pages/agent-page.tsx` (вкладка «Чаты»): модель — нативный select в
  облике чипа aF5vM (surface2, r 8, [5,10], 12/500, appearance-none,
  шеврон 12 $textMuted абсолютным слоем); конструкция — input в облике
  чипа bCLlQ (infoSoft, pill 999, link 12 $info); источники — группа
  role="group" с чипами SourceChip (check/globe по инстансам мастера,
  gap 6) как toggle-кнопки `aria-pressed`; первое сообщение — textarea
  13.5/normal с плейсхолдером мастера; подписи полей — 12.5/600
  $textPrimary (типографика заголовков блоков Body #3).

## Тесты

Швы (согласованы задачей): сценарные тесты `agent-page.test.tsx`
обновлены с сохранением поведения (создание чата первым сообщением —
теперь с явным payload выбранных источников; общая картина вкладки) +
новые проверки: оформление полей по дизайн-нодам композера (модель
surface2/8/[5,10]/12-500 + шеврон, конструкция infoSoft/999/link,
сообщение 13.5 + плейсхолдер мастера), переключение источников чипами
(aria-pressed/data-selected, отсутствие чекбоксов), создание чата со
снятым «рынок Bybit» → `sources: ["journal","rules-corpus"]`.
`source-chip.test.tsx`: состояние selected по умолчанию и приглушение
невыбранного. `npm test` — 40 файлов / 300 тестов зелёные;
`npm run build` проходит.

<!-- Скриншот-сверка выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/07-pages/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: openspec:ui/design-system#requirement-reusable-design-primitives -->
<!-- Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D2 -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D3 -->
