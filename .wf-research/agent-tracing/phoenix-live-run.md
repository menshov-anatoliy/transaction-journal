# Прогон #87: Arize Phoenix локально с OTLP-трейсом из .NET (Windows, Docker Desktop)

Дата прогона: 2026-10-23. Контейнер: `arizephoenix/phoenix:latest` = **arize-phoenix v20.25.0** (образ собран 2026-10-23, rev `ea058fd`).

Схема прогона: Phoenix на `:6006` (UI + OTLP HTTP) и `:4317` (OTLP gRPC); тестовое консольное .NET 9-приложение ([wf87-probe-Program.cs](wf87-probe-Program.cs)) шлёт 3 трейса формы решения [#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82): `chat_turn` с итерацией и тулом, `chat_turn` без тулов, `hint_pass` с фазами; атрибуты `gen_ai.*` + `tj.*`, полных текстов prompt/completion нет (metadata-only).

## (а) Вес запуска

- 1 контейнер; образ **1.43 GB** на диске.
- RAM в покое **~549 MiB**, CPU ~0.3% (лимит Docker на машине 2 GB — помещается, но ощутимо).
- SQLite внутри контейнера; volume для персистентности в прогоне не подключался.

## (б) Рендеринг токенов/модели/тулов

Авто-конвертация `gen_ai.*` → OpenInference (v20.25.0) подтверждена на живых спанах:

| gen_ai.* | Что получаем в Phoenix |
|---|---|
| `gen_ai.request.model` | `llm.model_name` (плюс `llm.provider`, `llm.system` из `gen_ai.system`) |
| `gen_ai.request.temperature` | `llm.invocation_parameters` (JSON) |
| `gen_ai.usage.input_tokens` / `output_tokens` | `llm.token_count.prompt` / `.completion`; `total` вычисляется (512+96=608) |
| `gen_ai.response.finish_reasons` | `llm.finish_reason` |
| `gen_ai.response.model` | `output.value` (JSON) |
| `gen_ai.tool.name` / `gen_ai.tool.call.id` | `tool.name` / `tool.id` |
| `gen_ai.tool.call.result` | `output.value` карточки тула |

- `tj.*`-атрибуты сохраняются как есть; событие `tj.tool.args` видно в Events спана (Events 1).
- SpanKind на приёме: спан с `gen_ai.system` → **LLM**; спан с `gen_ai.tool.name` → **Tool**; прочие (фазы `hint_pass`) → unknown.
- UI: карточка LLM-спана — latency, суммарные токены (608), оценка стоимости (**<$0.01** — Phoenix сам оценивает по model+tokens), Input = модель, Output = JSON. Карточка тула — «Tool: get_account_balance» + output JSON. Корневые `chat_turn`/`hint_pass` получают cumulative tokens детей (1 451 / 2 713 / 1 653) и cumulative cost.
- metadata-only карточки не опустошает: Input показывает модель, Output — JSON ответа модели; текстов сообщений нет — и не нужно.

### Критично для харнеса

Дочерний LLM-спан получает `kind=LLM` **только при наличии `gen_ai.system` на нём самом** (из usage-атрибутов не выводится). Без kind=LLM его токены не попадают в cumulative rollup корня: в первой партии прогона у корней было 0 токенов; после добавления `gen_ai.system` на `chat.completion` — 1 451 / 2 713 / 1 653. Инструментация M.E.AI `gen_ai.system` ставит всегда; ручные спаны харнеса, на которые вешаются usage-метрики, обязаны нести `gen_ai.system`.

## (в) Поиск по трейсам

- Поисковый бокс с filter-DSL на вкладках Spans/Traces; DSL проверен server-side: `name == 'chat_turn'` вернул ровно 4 `chat_turn`-спана.
- Сортируемые колонки таблицы корней: latency, cumulative tokens, cumulative cost; тайм-рейндж (7d по умолчанию).
- Сравнение трейсов по токенам/стоимости доступно сразу из таблицы, без открытия трейса.

## Расхождения с research #85

1. **OTLP gRPC `:4317` через Docker Desktop port-proxy на этой машине молча не доходит**: TCP-порт доступен, `ForceFlush=True`, трейсов 0 (проверено и `localhost`, и `127.0.0.1`). Рабочий транспорт — **OTLP/HTTP на `:6006/v1/traces`**. Research подавал gRPC и HTTP равнозначными; при внедрении в design `add-agent-tracing` фиксировать HTTP/protobuf (или дать настройку транспорта). Строка design.md:51 упоминает оба — противоречия нет, но выбор для dev-опции теперь ясен.
2. Авто-kind LLM у дочерних спанов требует `gen_ai.system` (см. выше) — в research не упоминалось.
3. Оценка стоимости Phoenix'ом — бонус сверх research.

## Прочие факты

- NU1902: `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0 имеет известную уязвимость среднего уровня (GHSA-4625-4j76-fww9) — при внедрении взять актуальную версию пакета.
- Образ Phoenix distroless: нет `sh`, отладка только через UI (`:6006`) и GraphQL (`/graphql`).
- URL проекта в UI требует Relay node id (`/projects/UHJvamVjdDox`); имя проекта в path не работает.
