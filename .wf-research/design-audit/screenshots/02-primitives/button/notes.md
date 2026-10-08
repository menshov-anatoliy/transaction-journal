# Скриншот-сверка 2.1 — Кнопка/{Primary,Secondary,Ghost,Danger}

Change: `reconcile-frontend-with-design`, таск 2.1. Дата: 2026-10-08.

## Пара evidence

- `design-frame.png` — экспорт фрейма «Кнопки» (YZQBQ) из design.pen (`Export`, MCP pen).
- `app-buttons.png` — стенд живой страницы (Vite dev, инъекция CDP + подтверждение
  computed-стилями; дублирующий снимок через headless Edge по тому же классовому
  набору).

## Извлечено из s0YfZ (узлы «Кнопки», resolveVariables)

| Нода | Заливка | Текст | Бордер | Паддинг | Радиус | Шрифт |
|---|---|---|---|---|---|---|
| Кнопка/Primary (gruwZ) | `$accent` #1FA36B | #FFFFFF | — | [9,16] | 8 | Inter 13/500 |
| Кнопка/Secondary (RV74d) | `$surface` #FFFFFF | `$textPrimary` #17171E | `$border` #E5E5DF | [9,16] | 8 | Inter 13/500 |
| Кнопка/Ghost (Kn5dY) | нет | `$textSecondary` #66666E | — | [9,12] | 8 | Inter 13/500 |
| Кнопка/Danger (JRitT) | `$negSoft` #FBEAE7 | `$neg` #D14B41 | — | [9,16] | 8 | Inter 13/500 |

## Легло в `ui/button.tsx`

| Вариант shadcn | Дизайн-нода | Классы |
|---|---|---|
| `default` | Primary | `bg-primary text-primary-foreground` |
| `outline` | Secondary | `border bg-card text-foreground` |
| `secondary` | Secondary (прямое имя дизайн-системы, вид = outline) | там же |
| `ghost` | Ghost | `text-text-secondary` + compound `px-3` |
| `destructive` | Danger | `bg-neg-soft text-neg` |

Общее: `rounded-sm` (шаг 8 лестницы `--radius`; сам `--radius` = 12 — базовый радиус
карточек), `text-[13px] font-medium`, высота `h-9` (36px ≈ 9+16+9 макета), тени
убраны. Hover-состояний в макете нет — выведены из соседних токенов: Primary →
`accentStrong` (#167853), Secondary/Ghost → `surface2` (#EFEFEA), Danger →
`neg/15`.

## Вывод сверки

Сошлось. Computed-стилы живых кнопок совпали с дизайн-нодами значение-в-значение:
радиус 8px, шрифт Inter 13/500, высота 36px, паддинги 16/16 (ghost 12), цвета
rgb(31,163,107)=#1FA36B, rgb(251,234,231)=#FBEAE7, rgb(209,75,65)=#D14B41,
rgb(23,23,30)=#17171E, rgb(102,102,110)=#66666E, бордер rgb(229,229,223)=#E5E5DF;
пиксельная выборка app-buttons.png содержит те же доминирующие цвета.
Отличие от макета: подписи-названия вариантов на стенде (служебные), а также
выведенные hover-состояния — в design.pen не заданы, зафиксированы в коде и
комментарии button.tsx.

## Тесты

`npm test` (vitest run, frontend/) — 31 файл, 220 тестов, все зелёные; `npm run
build` проходит. Новый шов: `src/components/ui/button.test.tsx` — маппинг
вариант→токен-классы, геометрия 8/13/500, compound px-3 ghost, клик/disabled.
