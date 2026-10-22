# Греп-сводка применения change `rename-mark-to-quote`

Дата применения: 2026-10-21. Инструмент проверок: PowerShell `Select-String` (.NET-regex, lookahead поддерживается).

## 1. Русский термин: `[Мм]арк(?!ер|иров)|[Мм]арок`

Области с нулём совпадений после применения: `src/` (0), `tests/` (0), `frontend/src` (0), `docs/` (0), `GLOSSARY.md` (0), `CONTEXT.md` (0), `openspec/changes/add-summary-channels` (0), `design.pen` (0 — глубокий скан текстов через MCP Pen, включая инстансы компонентов), `prototype/` (0 вне «маркер»).

Допустимые остатки:

| Область | Причина |
|---|---|
| `openspec/changes/rename-mark-to-quote/**` | Собственные артефакты change (proposal/design/tasks описывают переименование; заголовки MODIFIED-дельт хранятся исходными по design.md D5 — механика archive сверяет по тексту). |
| `openspec/specs/**` | Живые specs обновляются дельтами при sync/archive; переименование заголовков и Purpose-абзацев — прямая задача 4.4 после archive. |
| `rules/ac-37.yaml` | «лучше маркета» — вербатим про рыночную заявку (термин «маркет», не «марка»); замена исказила бы смысл цитаты. |
| `rules/README.md` | «не маркирует» — семейство «маркировать», исключено D1. |
| `src/TransactionJournal/App_Data/**` | Локальные пользовательские данные (gitignore); содержимое БД не переименовывается. |
| `.idea/**` | Локальные артефакты IDE (gitignore). |

## 2. Латинские идентификаторы: `\bMark` (с учётом регистра), `src/` + `tests/`

Итог: 96 совпадений, все в допустимых категориях:

| Токен | Кол-во | Категория |
|---|---|---|
| `Markdown` | 48 | Слово «маркировать»-семейства, не термин «марка». |
| `MarkPrice` | 26 | Wire-классы `BybitTicker`/`BybitExecution` и обращения к их членам (`.MarkPrice`), JSON-фикстуры тестов клиента Bybit, исторические миграции `2026*`. |
| `MarkedAt` | 13 | 11 — исторические миграции/Designer; 2 — `RenameColumn("MarkedAt"…)` в новой миграции (ссылки на старые имена колонок обязательны). |
| `MarkIv` | 7 | Wire-классы Bybit и обращения. |
| `Market` | 2 | «Рынок» (чат-источник), не марка цены. |

`frontend/src`: остатки `\bmark|Mark` — `marker`/`Marker` (маркер итога финрезультата), литерал `"manual-mark"` (значение `ClosingEntryKind`, сохранён бэкендом), маршруты `/last-mark` и `/close-marks` (сохранены бэкендом), `market` (чат-источник), npm-пакеты `markdown`/`remark-*`, иконка `Landmark` (lucide-react), Traceability-ссылки.

## 3. JSON-поле `markPrice`, `src/`

5 совпадений: 2 × `[JsonPropertyName("markPrice")]` (wire BybitTicker/BybitExecution) + 3 свидетельских упоминания в комментариях `BybitTicker` (сверка живого ответа биржи). Внутренний контракт полностью на `quotePrice`.

## 4. Сопутствующие решения применения

- Маршруты `/last-mark`, `/close-marks` и литерал `"manual-mark"` сохранены: карта design.md D3 меняет только JSON-поле `markPrice`→`quotePrice`; URL-контракт задачей не охвачен. Возможный follow-up вне этого change — переименование маршрутов синхронно SPA+API.
- Обновлены 7 якорей `doc:docs/research/bybit-api.md#3-публичные-марки-…` → `#3-публичные-котировки-…` в Traceability-строках кода: заголовок секции переименован, ссылки обязаны разрешаться. Формат строк `Traceability:` сохранён.
- Автоматически изменившиеся JSON-ключи (следствие переименования C#-членов по D2) синхронизированы на SPA: `quoteValue`, `hasQuoteFailure`, `quotesAsOf`, `manualAnnotationId`, `annotatedAt`, ответ last-mark `{ quote }`.

## 5. Отложено

- Задача 4.4 (живые specs `openspec/specs`): выполняется после `openspec archive rename-mark-to-quote` — дельты сверяются по исходным заголовкам, затем прямой sweep заголовков и Purpose-абзацев.
