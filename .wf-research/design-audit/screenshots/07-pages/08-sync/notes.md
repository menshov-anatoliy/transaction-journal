# Скриншот-сверка 7.7 — опасная зона neg/negSoft, предупреждения risk/riskSoft, Danger-кнопки, журнал 6/12·11.5

Приёмка задачи 7.7 change `reconcile-frontend-with-design` по процессу
design.md D3: пара «макет/страница» для Body #8 «Синхронизация» по
расхождениям §3.9 аудита: 1 (опасная зона — шадсновский `--destructive` →
пара `$neg #D14B41`/`$negSoft #FBEAE7` + risk-токены предупреждений),
2 (Кнопка/Danger 2× — плотная заливка `$neg`/белый текст), 3 (журнал
запусков — плотность [6,12]·11.5), 4 (секции [16,18], радиусы 9–12).

Процесс: макет — экран `uxrZO` «Экран/Синхронизация» (Body `qA7CW`) из
design.pen через MCP pen: `Get(nodeId, {depth})` по зонам (`Uf04R`
Подключение, `YmlIB` Синхронизация, `D91td` Журнал, `tv63s` Warn,
`UOt6s` Опасная зона, `EbnzH` шапка зоны, `Z0KQV`/`cEu9l` ряды,
`jmklC`/`EK3Ob` Danger-инстансы, `fzke1` LlmNote, `JRitT` базовая
«Кнопка/Danger», `YMfP7` инстанс Delete) для авторитетных значений +
`Export` png 2×; страница — живой рендер `http://localhost:5173/spa/sync-settings`
(Vite 5173 + мок-API 5019, фикстуры повторяют мастера: ключ
sk-••••••••••••7f2a, 4 запуска включая прерванный «сбой котировок
(timeout Bybit)», заметка XRPUSDT) в headless Chrome 1440×1000 @2x:
PNG + computed-стили через CDP. Временные скрипты (мок-API, CDP-зонд,
профиль Chrome) удалены, серверы остановлены.

## Файлы пары

- `design-frame.png` — «Экран/Синхронизация» (uxrZO) из design.pen (2×).
- `app-page.png` — живая страница `/spa/sync-settings` (1440×1000 @2x).

## Вывод: computed-стили живой страницы = мастер (значение-в-значение)

| Зона | Мастер (нода) | Страница (computed) |
|---|---|---|
| Опасная зона (UOt6s) | fill $negSoft, stroke $neg, r12, [16,18], гэп 10 | `rgb(251,234,231)` = #FBEAE7, `rgb(209,75,65)` = #D14B41, `12px`, `16px 18px` |
| Шапка зоны (EbnzH) | OctagonAlert 16 $neg + Inter 13.5/600 $neg | svg 16 + `13.5px/600`, `rgb(209,75,65)` |
| Ряды зоны (Z0KQV/cEu9l) | N 12.5/600 $textPrimary, D 11.5/1.45 $textSecondary, гэп 14 | `text-[12.5px] font-semibold`, `text-[11.5px] leading-[1.45] text-text-secondary`, `gap-3.5` |
| Кнопка зоны (jmklC/EK3Ob) | «Кнопка/Danger» + заливка $neg, текст #FFFFFF, [8,14], кегль 12 | `rgb(209,75,65)`, `rgb(255,255,255)`, `padding-left 14px`, `12px` |
| Предупреждение (tv63s) | fill $riskSoft, r9, [8,12], TriangleAlert 14, Inter 11.5/1.4 $risk | `rgb(249,239,217)` = #F9EFD9, `9px`, `8px 12px`, `11.5px`, `rgb(223,162,59)` = #DFA23B |
| Журнал (D91td) | stroke $divider r10; шапка $surface2; клетки [6,12] 11.5 $textSecondary | `border-divider rounded-[10px]`; шапка `rgb(239,239,234)` = #EFEFEA; клетки `6px 12px`, `11.5px`, `rgb(102,102,110)` = #66666E; `data-density="dense"` |
| Прерванный запуск (Row:4 C4) | 11.5 $neg | `rgb(209,75,65)` |
| Секции (Uf04R/YmlIB) | $surface/$border, r12, [16,18] | `bg-surface border px-[18px] py-4 rounded-lg` (=12px) |
| Пилюля «подключено» (oi3fT) | accentSoft r999 [3,8] + accentStrong 10.5 | `rgb(226,244,235)` = #E2F4EB, `rgb(22,120,83)` = #167853, pill, `10.5px` |
| Титул (afQvR) | $font 21/600 $textPrimary | `21px/600` |
| Шрифт документа | $font = Inter | `Inter, ui-sans-serif…` |

## Решения по мастер-нодам (отклонения зафиксированы)

1. **Danger в зоне — плотный, базовый примитив — мягкий.** В design.pen
   базовая «Кнопка/Danger» (JRitT) мягкая — negSoft/neg (её использует
   инстанс Delete YMfP7 попапа удаления карточки конструкции), а инстансы
   зоны jmklC/EK3Ob переопределяют заливку на $neg с белым текстом.
   Поэтому `ui/button` получил отдельный вариант `destructive-solid`
   (bg-neg + text-destructive-foreground = surface), а `destructive`
   остался мягким для `construction-card-page.tsx` (греп использований:
   2 кнопки удаления + тексты ошибок на токене `--destructive` = neg).
2. **Сброс категории — две кнопки действия.** Мастер рисует один ряд
   «Сброс состояния категорий linear/option» с кнопкой «Сбросить»; команда
   API `/sync/reset-category` требует конкретную категорию, поэтому ряд
   мастера несёт две кнопки («Сбросить linear», «Сбросить option») — состав
   данных не меняется (Non-Goals).
3. **Колонки журнала доменные.** Мастер: Время/Режим/Сделок/Котировок/Итог;
   домен журнала — Время/Режим/Результат/Статус (Non-Goals: состав данных
   на месте); геометрия [6,12]·11.5 и тона — мастера.
4. **Переключатель бэкапа остаётся нативным чекбоксом.** Мастер рисует
   pill-свитч (Bmbwe, accent r999); компонента-переключателя в UI-ките нет,
   §3.9 его не флаговал — перенос зафиксирован вне задачи 7.7.
5. **Постоянная сводка LastT/LastD не переносится.** Мастер держит в
   карточке «Результат запуска …» + строку «последний запуск: успешно · …
   · 6,4 с»; домен не хранит длительность запуска, а итоги закрытого
   запуска показываются блоком `SyncRunResultNote` после ручного прогона
   (слоты LastT 12/600 / LastD 12 $textSecondary мастера) — данные строки
   живут журналом и заметками запуска.
6. **Подтверждение пересбора — доменное состояние.** В мастере нет
   подтверждающего блока; прежний inline-блок сохранён (кайма neg/50,
   кнопка подтверждения — `destructive-solid`, «Отмена» — Secondary).

## Реализация

- `components/ui/button.tsx`: вариант `destructive-solid` —
  bg-neg/text-destructive-foreground (hover neg/90) для инстансов зоны
  Body #8; `destructive` (negSoft/neg) не изменён.
- `pages/sync-settings-page.tsx`: контент [20,28,24,28] гэп 14; секции
  $surface/$border r12 [16,18]; Подключение — заголовок 13.5/600 + пилюля
  «подключено» (accentSoft r999 [3,8] accentStrong 10.5), ряды Ключ API/
  Аккаунт/Хранение секрета 12 (ключ $textMuted, значение $textPrimary);
  Синхронизация — RunBtn Primary [8,14] 12.5, подпись переключателя 12
  $textSecondary «резервная копия перед синхронизацией», журнал внутри
  секции (r10/$divider, шапка $surface2 11/600, клетки [6,12] 11.5
  $textSecondary, прерванный запуск $neg); `WarnBlock` (tv63s: riskSoft
  r9 [8,12] TriangleAlert 14 + 11.5/1.4 risk) для предупреждений сверки и
  заметок запуска; Опасная зона (UOt6s: negSoft/neg r12 [16,18], шапка
  OctagonAlert 16 + 13.5/600 neg, ряды N 12.5/600 + D 11.5/1.45,
  действия destructive-solid [8,14] 12, сноска LlmNote 11 $textMuted).

## Тесты

Швы (согласованы задачей): сценарные тесты `sync-settings-page.test.tsx`
сохранены (подключение+переключатель+заметки; инструкция appsettings;
ручной синк/пересбор/сброс — имена кнопок «Сбросить linear/option» по
ряду мастера) + новые проверки: зона neg/negSoft r12 [16,18] с иконкой и
заголовком 13.5/600 neg, плотные Danger-кнопки (bg-neg +
text-destructive-foreground), сноска LLM 11 $textMuted, Warn-блоки
riskSoft/risk r9 [8,12] 11.5 с иконкой (заметки + сверка после запуска),
журнал data-density=dense с шапкой surface2 и прерванной строкой $neg,
секции surface/border [16,18] с пилюлей «подключено» и рядами 12.
`button.test.tsx`: вариант `destructive-solid` в таблице маппинга +
раздельность мягкого/плотного Danger по инстансам мастера. `npm test` —
40 файлов / 310 тестов зелёные; `npm run build` проходит.

<!-- Скриншот-сверка выполняется процессом design.md D3 (MCP pen против
     живой страницы), пары сохраняются в screenshots/07-pages/. -->
<!-- Traceability: openspec:ui/design-system#requirement-screenshot-acceptance -->
<!-- Traceability: openspec:ui/design-system#requirement-design-pen-single-source -->
<!-- Traceability: openspec:ui/design-system#requirement-reusable-design-primitives -->
<!-- Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D2 -->
<!-- Traceability: change:reconcile-frontend-with-design/design#D3 -->
