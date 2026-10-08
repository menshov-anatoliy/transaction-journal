# 02-primitives / design-components — примитивы задачи 2.2

Change `reconcile-frontend-with-design`, задача 2.2: `StatusChip`, `SourceChip`,
`Metric`, `NavItem` в `frontend/src/components/design/`.

## Пары сверки

| Файл | Источник | Что показывает |
| --- | --- | --- |
| `design-frame.png` | MCP pen `Export`, фрейм «Примитивы» `s0YfZ` (scale 2) | Мастер-ноды: Кнопки/*, Чип/Источник `fYadZ`, Чип/Статус `aa6cK`, Бейдж/Число `FQT4Y`, Метрика `jtDmV`, Нав-пункт `apcZH`, Нав-пункт/Активный `Vx9C2` |
| `app-components.png` | headless Chrome ×2, `http://localhost:5173/spa/design-stand.html` | Живой рендер примитивов из `components/design/` на токенах `index.css` |
| `design-cards.png` (доп.) | MCP pen `Export`, фрейм «Карточки» `sUDDX` (scale 2) | Контекст инстансов: Чип/Статус в 4 тонах, Метрика с переопределениями значения |

## Вывод сверки

Соответствие подтверждено по всем четырём примитивам:

- **StatusChip ↔ Чип/Статус (aa6cK)**: pill 999, паддинги [4,10], Inter
  11.5/500. Тоны из инстансов дизайн-нод: pos accentSoft/accentStrong
  («открыта» nrPzY/FoDLw), info infoSoft/info («Однозначное» U6mVZ),
  neutral surface2/textSecondary («закрыта» EIqx3), muted
  surface2/textMuted («архив» dAcLW). Варианты neg/risk для статусов в
  макете не существуют — в примитив не включены.
- **SourceChip ↔ Чип/Источник (fYadZ)**: pill 999, surface + бордер border,
  [5,10], иконка Lucide 12×12 accent (мастер check), подпись 12/normal
  textSecondary. Поддержаны инстансы: check («журнал», «корпус правил»),
  globe («рынок Bybit»), cpu+textSecondary («GLM-5.3»).
- **Metric ↔ Метрика (jtDmV)**: колонка gap 3, подпись 11/normal textMuted
  c трекингом 0.3, значение 15/600 textPrimary; переопределения инстансов
  M1–M9 (14/16, accentStrong) закрываются `valueClassName`.
- **NavItem ↔ Нав-пункт (apcZH) / Нав-пункт/Активный (Vx9C2)**: радиус 8,
  [8,10], gap 10, иконка 16×16, подпись 13/500 textSecondary; активный —
  surface + бордер border + accentStrong 13/600, доступно через
  `aria-current="page"`. Бейдж по мастер-ноде q35Tj9: pill 999, accent,
  [2,8], 11/600 (инстанс bKXol «Входящие 12»).

## Зафиксированное отклонение от формулировки аудита

Аудит §3.1 (п. 2) утверждал «активный пункт — accentSoft/accentStrong» на
основании агрегатов экстракта. Прямое чтение нод через MCP pen (`Get`) показывает:
мастер-нода Vx9C2 «Нав-пункт/Активный» — заливка **surface**, бордер **border**,
текст/иконка **accentStrong**; пара accentSoft/accentStrong в «Каркасе» g0z20
принадлежит логотипу топбара (srW2i/W6QRKp). Примитив реализован по ноде
(требование design.pen — единственный источник визуальных решений); скриншотная
пара это подтверждает. Перекраска бейджа «Входящие» на пару
accentSoft/accentStrong — решение задачи 3.1 (шов: `badgeClassName`).

## Параметры, извлечённые из дизайн-нод (источник — Get через MCP pen)

- Чип/Статус aa6cK: `cornerRadius 999, padding [4,10], gap 6, align center`,
  Label `$accentStrong 11.5/500`; инстансы перекраски см. выше.
- Чип/Источник fYadZ: `fill $surface, stroke $border, cornerRadius 999,
  padding [5,10], gap 6`; Icon `check 12×12 $accent`, Label
  `$textSecondary 12/normal`.
- Метрика jtDmV: `layout vertical, gap 3`; Caption `$textMuted 11/normal
  letterSpacing 0.3`; Value `$textPrimary 15/600`.
- Нав-пункт apcZH: `cornerRadius 8, gap 10, padding [8,10], align center`,
  Icon `layers 16×16 $textSecondary`, Label `$textSecondary 13/500`;
  Badge (отключён в мастере): `fill $accent, 999, [2,8], Value #FFF 11/600`.
- Нав-пункт/Активный Vx9C2: `fill $surface, stroke $border, 8, [8,10], 10`,
  Icon/Label `$accentStrong`, Label `13/600`.
