# 6.1 — Тематизация MD-редактора (Body «Стенд/MD-редактор (split)»)

Пара сверки: `design-frame.png` (Export 2× из design.pen, фреймы nBfEb/f1i0Gf/veYUd/H5zNRM)
и `app-page.png` (живой попап редактора комментария конструкции, headless Chrome 960×680,
мок-бэкенд 5019). Контекст экрана — `app-context.png`.

## Вывод

Соответствие мастеру достигнуто библиотечными средствами (`@uiw/react-md-editor`:
className-хук `.journal-md-editor` + CSS-переопределения `markdown-editor-theme.css`
на дизайн-токенах; футер — примитивы `ui/button` Secondary/Primary). Эскалация
на план Б ADR 0010 не потребовалась.

## Computed-сверка (значение-в-значение)

| Зона | Мастер | Приложение (computed) |
|---|---|---|
| Попап (LeiDq) | 960×680, r16, `$bg`, тень 0.15/16/48 | 960×680, 16px, #F3F3EF, rgba(23,23,30,.15) 16/48 |
| Заголовок (nBfEb) | Inter 14/600 `$textPrimary` | Inter 14/600 #17171E |
| Toolbar (f1i0Gf) | [10,18], низ divider, фон surface | 10px 18px, #EBEBE5, #FFFFFF |
| Кнопки тулбара | 30×30, r8, иконка 14, `$textSecondary`; hover surface2 | 30×30, 8px, svg 14, #66666E (8 кнопок) |
| Source (veYUd/wRRDM) | `$bg`, [16], JetBrains Mono 12/1.7 | #F3F3EF, 16px, JBM 12px/20.4px |
| Preview (veYUd/hrCte) | [16], surface, левая линия divider, Inter 13 | 16px, #FFFFFF, inset 1px #EBEBE5, Inter 13px |
| Цитата (j7wXc) | surface2, r6, лево 2 accentSoft, italic 12 | #EFEFEA, 6px, 2px #E2F4EB, italic 12px |
| Footer (H5zNRM) | [12,18], верх divider; hint 11.5 `$textMuted` | 12px 18px, #EBEBE5; 11.5px #9B9BA1 |
| Cancel / Save | Secondary / Primary(accent), r8, 12/500 | #FFFFFF+#E5E5DF / #1FA36B, 8px, 12px/500 |

Отклонений цвета/геометрии не зафиксировано; пиксельная выборка пар содержит
только палитру дизайн-токенов.

## Процесс

Обрыв прогона саб-агента 6.1 дочитан оркестратором: тесты/сборка зелёные,
сверка завершена по оставленному CDP-скрипту (headless Chrome + мок-API 5019,
порты свободны после остановки). Временные скрипты удалены.

Traceability: change:reconcile-frontend-with-design/design#D5
Traceability: openspec:ui/design-system#requirement-screenshot-acceptance
Traceability: openspec:ui/design-system#requirement-design-pen-single-source
