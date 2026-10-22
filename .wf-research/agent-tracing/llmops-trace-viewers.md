# Бесплатные LLMOps-вьюеры трейсов для dev-time опции

Статус: исследовано, рекомендация выдана (wayfinder-research).
Происхождение: тикет [#85](https://github.com/menshov-anatoliy/transaction-journal/issues/85), карта [#79](https://github.com/menshov-anatoliy/transaction-journal/issues/79).
Ограничения: [ADR-0003](../../docs/adr/0003-stack-single-exe-dotnet-sqlite.md) — single-exe .NET 9 + SQLite, без обязательной внешней инфраструктуры в проде; внешний backend трейсов — только опция разработки. Трассировка уже решена в карте: спаны `ActivitySource` с атрибутами `gen_ai.*` (OTel semantic conventions), локальный ротируемый JSONL file-экспортёр ([#81](https://github.com/menshov-anatoliy/transaction-journal/issues/81), `research/otel-local-sink`). Полные тексты prompt/completion не пишем.
Все факты проверены по первоисточникам: официальные документации (langfuse.com, arize.com, signoz.io, jaegertracing.io, grafana.com, aspire.dev, opentelemetry.io), файлы LICENSE и конфиги из GitHub-репозиториев, GitHub API (звёзды/релизы сняты 2026-10-22). Ссылка — у каждого утверждения.

## 1. Рамка

- Вход во все кандидаты один: OTel-спаны из `ActivitySource` с `gen_ai.*`-атрибутами; OTel .NET SDK экспортирует OTLP поверх gRPC или HTTP/protobuf ([opentelemetry.io: .NET exporters](https://opentelemetry.io/docs/languages/net/exporters/)). Фирменные SDK LLMOps-систем (Python/JS) не рассматриваем — путь должен быть чистым OTLP.
- Semconv `gen_ai.*` живые и меняющиеся: они вынесены из основного репозитория в [open-telemetry/semantic-conventions-genai](https://github.com/open-telemetry/semantic-conventions-genai), страница в semconv-репозитории помечена «moved» ([opentelemetry.io](https://opentelemetry.io/docs/specs/semconv/gen-ai/)).
- Приёмник — опция разработки: docker на Windows допустим, но каждый лишний контейнер — минус; прод от него не зависит; артефакт истины уже есть в JSONL.
- Ценность приёмника не должна опираться на полные тексты prompt/completion — их мы не шлём; ищем рендеринг метаданных: модель, токены, задержки, тулы, ошибки.
- Платные SaaS исключены заранее; ищем self-hosted/OSS с готовым внешним UI.

## 2. Краткий ответ

**Рекомендация: два уровня.**

1. **Arize Phoenix** — если нужен именно LLM-вьюер: единственный одно-контейнерный приёмник (SQLite-хранилище по умолчанию), который на приёме автоматически конвертирует `gen_ai.*` в полноценную LLM-семантику UI (токены, модель, сообщение I/O, тулы) — релиз arize-phoenix 15.10.0+ от 2026-05-15. Ограничение: лицензия Elastic License 2.0 — source-available, не OSI; для внутреннего dev-использования свободна, запрещает только перепродажу как hosted-сервис.
2. **.NET Aspire Dashboard** (или Jaeger) — если достаточно «посмотреть операционные трейсы»: MIT/Apache-2.0, один контейнер, ноль конфигурации, OTLP gRPC+HTTP из коробки; токены/модель/тулы видны как атрибуты key-value, LLM-семантики нет.

**Langfuse v4** — самый богатый LLM-UI и честный маппинг `gen_ai.*`, core MIT; цена — 6 контейнеров (Postgres+ClickHouse+Redis+MinIO+web+worker) и OTLP без gRPC (только HTTP/JSON и HTTP/protobuf — для .NET не блокер).

## 3. Сводная таблица

| Кандидат | Лицензия | Приём OTLP | LLM-семантика из gen_ai.* | Вес локально (Windows) | Живость (2026-10-22) |
|---|---|---|---|---|---|
| Langfuse v4 | MIT core + EE (ee/); © ClickHouse, Inc. | HTTP/JSON + HTTP/protobuf; **gRPC нет**; `/api/public/otel` | Полный маппинг: `gen_ai.usage.*`→токены, `gen_ai.request.model`→модель, `gen_ai.tool.call.*`→тулы | 6 контейнеров (web, worker, Postgres, ClickHouse, Redis, MinIO) | ~36.2k ★, v4.68.0 (22.10), пуш сегодня |
| Arize Phoenix | **ELv2** (source-available) | gRPC :4317 + HTTP :6006/v1/traces | Авто-конвертация gen_ai.*→OpenInference c 15.10.0+: токены, модель, I/O, тулы | **1 контейнер**, SQLite + volume | ~12.0k ★, релизы 22.10, пуш сегодня |
| SigNoz | MIT core + EE (ee/) | gRPC/HTTP :4317/:4318 | Да: модуль LLM Observability, spanmapper нормализует OpenInference/OpenLLMetry→gen_ai.* | 5–6 контейнеров, ≥4 GB RAM; **Windows: только WSL2 c Docker Engine в WSL** (Docker Desktop не поддерживается) | ~32.4k ★, v0.147.0 (22.10) |
| OpenLIT | Apache-2.0 | gRPC/HTTP :4317/:4318 в контейнере UI | Дашборды LLM-вызовов/тулов/стоимости; разбор gen_ai есть в коде приёмника, в доках не задокументирован | 3 контейнера (ClickHouse, NATS, UI) | ~2.9k ★, пуш сегодня |
| Grafana Tempo (+Grafana) | **AGPL-3.0** | OTLP-приёмник в Tempo | Нет — generic спаны, Grafana-дашборды | ≥2 контейнера (tempo, grafana), объектное хранилище | ~5.5k ★, v3.1.1 (21.10) |
| Jaeger (all-in-one) | Apache-2.0 | gRPC/HTTP :4317/:4318 из коробки | Нет — generic спаны, атрибуты как теги | **1 контейнер**, in-memory (неперсистентно) | ~23.3k ★, v2.22.0 (06.10) |
| Aspire Dashboard | MIT | gRPC :4317 + HTTP :4318 (контейнер/CLI) | Нет — generic логи/трейсы/метрики, атрибуты как key-value | **1 контейнер** или CLI | ~6.4k ★, v13.6.2 (20.10) |
| Helicone | Apache-2.0 | Приёмника OTLP-спанов в репо не найдено | — | 6 сервисов (Web, Worker, Jawn, Supabase, ClickHouse, MinIO) | ~6.2k ★, последний релиз 21.08.2025, коммиты идут |

## 4. Факты по кандидатам

### 4.1 Langfuse v4

- Лицензия: «Content outside of the above mentioned directories… is available under the "MIT Expat" license», всё в `ee/`, `web/src/ee/`, `worker/src/ee/` — под EE-лицензией; правообладатель «Copyright (c) 2023-2026 ClickHouse, Inc.» ([LICENSE](https://github.com/langfuse/langfuse/blob/main/LICENSE)).
- Self-host = тот же стек, что у Langfuse Cloud: контейнеры Web и Worker + PostgreSQL + Redis/Valkey + ClickHouse + S3/Blob (MinIO) ([self-hosting: architecture](https://langfuse.com/self-hosting)); в официальном [docker-compose.yml](https://raw.githubusercontent.com/langfuse/langfuse/main/docker-compose.yml) — 6 сервисов (`langfuse-worker:4`, web, postgres, minio, redis, clickhouse).
- OTLP: приём на `/api/public/otel`, «Langfuse currently supports OTLP over HTTP with both HTTP/JSON and HTTP/protobuf. gRPC is not supported yet»; локальный OTLP с версии ≥ v3.22.0; для v4 нужен заголовок `x-langfuse-ingestion-version: 4`, иначе прямые OTel-данные появляются с задержкой до 10 минут ([docs: OpenTelemetry](https://langfuse.com/docs/opentelemetry/get-started)). Для .NET это работает: HttpProtobuf поддерживается OTel .NET ([opentelemetry.io](https://opentelemetry.io/docs/languages/net/exporters/)).
- Маппинг `gen_ai.*` в модель Langfuse задокументирован построчно: `gen_ai.usage.*` → `usage` (токены), `gen_ai.usage.cost` → cost, `gen_ai.request.model`/`gen_ai.response.model` → модель, `gen_ai.request.*` → параметры вызова, `gen_ai.tool.call.arguments`/`gen_ai.tool.call.result` → input/output наблюдения тул-спана, `gen_ai.prompt`/`gen_ai.completion` → input/output; тип наблюдения можно задать `langfuse.observation.type` (`tool`, `agent`, …); непомеченные OTel-атрибуты попадают в `metadata.attributes` и фильтрации не поддаются ([таблицы mapping](https://langfuse.com/docs/opentelemetry/get-started)).
- Аутентификация приёма — Basic из пары public/secret-ключей ([docs](https://langfuse.com/docs/opentelemetry/get-started)).
- Живость: ~36.2k звёзд, релиз v4.68.0 2026-10-22, пуш 2026-10-22 (GitHub API).
- Базовая ценность не требует полных текстов: токены/модель/параметры рендерятся из метаданных; тексты только усиливают карточку generation.

### 4.2 Arize Phoenix

- Лицензия: Elastic License 2.0 — запрет только на предоставление ПО третьим лицам как hosted/managed-сервис и на обход лицензионных ключей; локальное и внутреннее использование свободное, но это не OSI-лицензия ([LICENSE](https://raw.githubusercontent.com/Arize-ai/phoenix/main/LICENSE)).
- Запуск: один контейнер `docker run -p 6006:6006 -p 4317:4317 arizephoenix/phoenix:latest`; UI и OTLP HTTP на :6006, OTLP gRPC на :4317 ([get-started](https://arize.com/docs/phoenix/get-started)).
- Хранилище по умолчанию — локальный SQLite (`PHOENIX_WORKING_DIR` + volume); PostgreSQL ≥ 14 — опционально ([Docker self-hosting](https://arize.com/docs/phoenix/self-hosting/deployment-options/docker)).
- Ключевое для нас: с версии arize-phoenix 15.10.0+ (заметка от 2026-05-15) Phoenix «automatically converts OpenTelemetry GenAI semantic convention attributes (`gen_ai.*`) to OpenInference» на приёме: `gen_ai.usage.*` → `llm.token_count.*` (токены), `gen_ai.system`/`gen_ai.request.model` → `llm.provider`/`llm.model_name`, описания тулов → `llm.tools.*`, retrieval-документы → `retrieval.documents.*`; «No code changes required» — трейсы рендерятся с полным message I/O, тулами и токенами ([release note](https://github.com/Arize-ai/phoenix/blob/main/docs/phoenix/release-notes/05-2026/05-15-2026-otel-semconv-conversion.mdx)).
- Известная неполнота маппинга: `gen_ai.usage.cache_read.input_tokens`/`cache_write` Phoenix пока не маппит — считает их обычными prompt-токенами; сырые атрибуты остаются на спане ([cookbook: claude-managed-agents](https://github.com/Arize-ai/phoenix/blob/main/docs/phoenix/cookbook/tracing/claude-managed-agents-guide.mdx)). Для наших спанов (без кэш-токенов) не актуально.
- Phoenix построен на OTel/OpenInference, спаны принимает по OTLP без фирменного SDK ([docs](https://arize.com/docs/phoenix)); юнит-тест приёма подтверждает разбор `gen_ai.request.model`, `gen_ai.usage.input_tokens/output_tokens`, `gen_ai.input/output.messages` ([tests/unit/trace/test_otel.py](https://github.com/Arize-ai/phoenix/blob/main/tests/unit/trace/test_otel.py)).
- Живость: ~12.0k звёзд, релизы пачками 2026-10-22 (монорепо с тегами по пакетам), пуш 2026-10-22 (GitHub API).
- Полные тексты не нужны для базовой ценности: токены/модель/тулы рендерятся из атрибутов.

### 4.3 SigNoz

- Лицензия: MIT для всего вне `ee/` и `cmd/enterprise/`; enterprise-функции — под ee-лицензией ([LICENSE](https://github.com/SigNoz/signoz/blob/main/LICENSE)).
- OTLP-приём нативный (ports 4317/4318), LLM-спансы ложатся в общий Traces explorer: «Your application emits `gen_ai.*` spans and metrics through standard OpenTelemetry libraries, exports them over OTLP, and SigNoz stores and queries them next to the rest of your application telemetry» — токены, задержки, стоимость, тулы ([docs: LLM Observability](https://signoz.io/docs/llm-observability/)).
- Нормализация на приёме: модуль spanmapper содержит определения `gen_ai.llm`, `gen_ai.tool`, `gen_ai.agent`, которые сводят OpenInference (`llm.token_count.prompt`, `llm.model_name`), OpenLLMetry, `langfuse.*` и прочие конвенции к `gen_ai.*`-атрибутам ([gen_ai.llm.json](https://github.com/SigNoz/signoz/blob/main/pkg/modules/spanmapper/implspanmapper/fs/definitions/gen_ai.llm.json)).
- Вес: Docker-установка через Foundry поднимает как минимум ClickHouse, ClickHouse Keeper, Postgres, signoz-otel-collector и бэкенд SigNoz; требование — «At least 4GB of memory allocated to Docker» ([docs: install docker](https://signoz.io/docs/install/docker/)).
- Windows-нюанс (критично для нас): «On Windows, run everything inside a WSL 2 distribution with Docker Engine installed natively in WSL, **not Docker Desktop**. ClickHouse Keeper has been reported to crash in a restart loop with segmentation faults (exit code 139) under Docker Desktop's virtualization layer» ([docs: install docker](https://signoz.io/docs/install/docker/)).
- Живость: ~32.4k звёзд, v0.147.0 2026-10-22, пуш 2026-10-22 (GitHub API).

### 4.4 OpenLIT

- Лицензия Apache-2.0 ([LICENSE](https://raw.githubusercontent.com/openlit/openlit/main/LICENSE)).
- Self-host одним compose: `docker compose up -d` → UI на `http://127.0.0.1:3000`; compose-файл — ровно 3 контейнера: ClickHouse 24.4, NATS 2.10 и сам OpenLIT, в котором открыты OTLP gRPC :4317 и HTTP :4318 ([README](https://raw.githubusercontent.com/openlit/openlit/main/README.md), [docker-compose.yml](https://raw.githubusercontent.com/openlit/openlit/main/docker-compose.yml)).
- UI: LLM-вызовы, tool-вызовы, стоимость, дашборды по агентам и coding-агентам ([README](https://raw.githubusercontent.com/openlit/openlit/main/README.md)).
- Приём внешних OTel-спанов поддержан архитектурно (OTLP-приёмник в контейнере), а разбор `gen_ai.*` присутствует в коде приёмника и семконв-модулях (`src/otlp-receiver/…`, `sdk/go/semconv/genai.go`) — но документированного описания «что UI рисует из `gen_ai.*` внешнего .NET-экспортёра» я в доках не нашёл: документация описывает в первую очередь их собственные SDK ([поиск по репо](https://github.com/search?q=repo%3Aopenlit%2Fopenlit+gen_ai.usage&type=code)).
- Живость: ~2.9k звёзд, пуш 2026-10-22 (GitHub API); позиционирование сместилось к «agent harness engineering platform» ([README](https://raw.githubusercontent.com/openlit/openlit/main/README.md)).

### 4.5 Jaeger (all-in-one)

- Лицензия Apache-2.0 ([LICENSE](https://github.com/jaegertracing/jaeger/blob/main/LICENSE)).
- Один контейнер: `docker run … -p 16686:16686 -p 4317:4317 -p 4318:4318 … cr.jaegertracing.io/jaegertracing/jaeger:2.22.0`; collector+query в одном процессе, OTLP gRPC/HTTP включены, UI на :16686; хранилище — «transient in-memory storage» ([docs: getting started](https://www.jaegertracing.io/docs/latest/getting-started/)).
- Официальная рекомендация — слать данные OTel SDK'ами; фирменный SDK не нужен ([docs](https://www.jaegertracing.io/docs/latest/getting-started/)).
- UI универсальный: дерево спанов, длительности, ошибки, атрибуты как теги; никакой LLM-семантики (токены/модель — обычные атрибуты) ([docs](https://www.jaegertracing.io/docs/latest/getting-started/)).
- Для dev-time неперсистентность in-memory терпима: артефакт истины у нас в JSONL (#81).
- Живость: ~23.3k звёзд, v2.22.0 2026-10-06 (GitHub API).

### 4.6 .NET Aspire Dashboard (standalone)

- Лицензия MIT ([dotnet/aspire](https://github.com/dotnet/aspire/blob/main/LICENSE)).
- Standalone-режим именно для нашего случая: «Run the Aspire dashboard as a standalone OTLP viewer to inspect local application logs, distributed traces, and metrics without an AppHost»; UI :18888, OTLP gRPC :4317, OTLP HTTP :4318; старт через Aspire CLI (`npx`-пакет) или один контейнер `mcr.microsoft.com/aspire/dashboard:latest` ([aspire.dev](https://aspire.dev/dashboard/standalone/)).
- По умолчанию доступ по browser-токену (выводится в консоль), есть `--allow-anonymous` для локальной разработки ([aspire.dev](https://aspire.dev/dashboard/standalone/)).
- UI универсальный: structured logs / traces / metrics; LLM-семантики нет — `gen_ai.*` видны как атрибуты key-value ([aspire.dev](https://aspire.dev/dashboard/standalone/)).
- Живость: ~6.4k звёзд, v13.6.2 2026-10-20, пуш 2026-10-22 (GitHub API); образ переехал с `mcr.microsoft.com/dotnet/aspire-dashboard` на `mcr.microsoft.com/aspire/dashboard` ([aspire.dev](https://aspire.dev/dashboard/standalone/)).

### 4.7 Grafana Tempo (+ Grafana)

- Лицензия AGPL-3.0 ([LICENSE](https://raw.githubusercontent.com/grafana/tempo/main/LICENSE)).
- Tempo — только backend хранения/поиска трейсов; визуализация через Grafana как отдельный сервис; официальный гайд: клиент → (рекомендуется Alloy/OTel Collector) → Tempo → Grafana ([docs: getting started](https://grafana.com/docs/tempo/latest/getting-started/)).
- Минимальный локальный сетап — 2 контейнера (tempo + grafana) плюс конфиг хранилища (объектное хранилище, локальный fs для дев-режима); это уже конструктор, а не готовый вьюер ([docs](https://grafana.com/docs/tempo/latest/getting-started/)).
- UI generic: TraceQL/Traces Drilldown с RED-метриками; рендеринга `gen_ai.*`-токенов/моделей/тулов нет ([README](https://raw.githubusercontent.com/grafana/tempo/main/README.md)).
- Имеет смысл только если в хозяйстве уже стоит Grafana — у нас её нет.
- Живость: ~5.5k звёзд, v3.1.1 2026-10-21 (GitHub API).

### 4.8 Helicone

- Лицензия Apache-2.0, но модель интеграции другая: LLM-трафик направляется через AI Gateway (замена `baseURL` на прокси) либо логируется их SDK/OpenLLMetry; это шлюз, а не пассивный приёмник трейсов ([README](https://raw.githubusercontent.com/Helicone/helicone/main/README.md)).
- Self-host — 6 сервисов: Web (NextJS), Worker, Jawn, Supabase, ClickHouse, MinIO ([README](https://raw.githubusercontent.com/Helicone/helicone/main/README.md)).
- Поиск по репозиторию «otlp receiver» даёт 0 результатов — приёма OTLP-спанов, совместимого с нашим путём, в репо не видно ([поиск](https://github.com/search?q=repo%3AHelicone%2Fhelicone+otlp+receiver&type=code)).
- Для нас это ломает главный принцип: приложение не должно зависеть от приёмника и маршрутизировать через него трафик.

## 5. Вывод: рекомендация и trade-offs

Наш профиль: .NET 9 + OTel `ActivitySource` с `gen_ai.*`, Windows, docker допустим, тяжёлая инфраструктура не нужна, полных текстов промптов нет. Артефакт истины уже пишется в JSONL (#81), поэтому вьюер — чисто dev-удобство, и его вес важнее полноты функций.

**Фавориты:**

1. **Arize Phoenix** — лучший баланс «LLM-семантика / вес»: один контейнер с SQLite против 6 у Langfuse, при этом с 15.10.0 он сам конвертирует `gen_ai.*` в токены/модель/тулы в UI. Trade-offs: лицензия ELv2 (source-available; для внутреннего dev-использования свободна, но не OSI), Python-стек, кэш-токены пока не маппятся.
2. **.NET Aspire Dashboard** — «нулевая» опция разработки: MIT, один контейнер или CLI-процесс, родной для .NET OTLP-приём, ничего настраивать не надо. Trade-off: без LLM-семантики — токены/модель/тулы читаются как атрибуты key-value; годится как первый шаг, до него же рекомендован `research/otel-local-sink`.
3. **Langfuse v4** — если LLM-UI важнее веса: самый богатый вьюер (sessions → traces → generations/tools, токены, стоимости, типы наблюдений) и честный построчный маппинг `gen_ai.*`. Trade-offs: 6 контейнеров с Postgres/ClickHouse/Redis/MinIO для однопользовательского dev-просмотра; OTLP только по HTTP (нет gRPC — для .NET не блокер, но лишний нюанс с заголовком `x-langfuse-ingestion-version`).

Jaeger остаётся равнозначной замене Aspire на уровне «нулевой вес» (Apache-2.0, один контейнер), но in-memory-хранилище и общий UI не дают ему преимущества перед Aspire в .NET-стеке. SigNoz функционально подходит (модуль LLM Observability + нормализация в `gen_ai.*`), но 4+ GB RAM, ClickHouse-стек и требование WSL2 с Docker Engine внутри WSL (не Docker Desktop) делают его самым тяжёлым вариантом для Windows-рабочей станции. Практическая связка: включить OTLP-экспорт в Aspire Dashboard/Phoenix одной переменной окружения рядом с постоянным JSONL-экспортёром — прод по-прежнему ни от чего из этого не зависит.

## 6. Исключены и не подошли

**Исключены (платные SaaS, без разбора):** LangSmith (LangChain), W&B Weave (Weights & Biases), Braintrust, Arize AX — проприетарные SaaS-платформы, не self-hosted/OSS; Arize AX упоминается на странице Phoenix как «managed enterprise platform» ([docs](https://arize.com/docs/phoenix)).

**Не подошли:**

- **Helicone** — gateway/прокси-модель интеграции (замена `baseURL`, их SDK), приёмника OTLP-спанов в репо не найдено; self-host из 6 сервисов (§4.8).
- **Grafana LGTM/Tempo** — AGPL-3.0, минимум 2 сервиса + конфиги, UI без LLM-семантики; оправдан только при уже существующем Grafana-хозяйстве, которого у проекта нет (§4.7).
- **SigNoz** — не исключён, но отодвинут: функционально соответствует, однако самый тяжёлый локально и единственный с жёстким Windows-ограничением «только WSL2, не Docker Desktop» (§4.3).

## 7. Что не удалось верифицировать

- Ни один кандидат не запускался локально: всё проверено по документации, конфигам, коду и GitHub API, без живого прогона OTLP-экспорта из .NET.
- Отображение `gen_ai.*` в Aspire Dashboard и Jaeger подтверждено только как «generic key-value» из документации; точного описания рендеринга атрибутов в разрезе gen_ai в их доках нет.
- Для OpenLIT документированного маппинга `gen_ai.*` → UI для внешних (не их SDK) OTel-спанов не нашёл; поддержка следует из кода приёмника (`src/otlp-receiver`), а не из доков.
- Точная минимальная память/диск Phoenix и Langfuse на Windows-докере в доках не публикуются.

## 8. Источники

| # | Источник | Что подтверждает |
|---|---|---|
| 1 | [opentelemetry.io: .NET exporters](https://opentelemetry.io/docs/languages/net/exporters/) | OTel .NET экспортирует OTLP только gRPC и HTTP/protobuf |
| 2 | [opentelemetry.io: semconv gen-ai (moved)](https://opentelemetry.io/docs/specs/semconv/gen-ai/) | semconv перенесены в open-telemetry/semantic-conventions-genai |
| 3 | [Langfuse LICENSE](https://github.com/langfuse/langfuse/blob/main/LICENSE) | MIT core + EE-директории; © ClickHouse, Inc. |
| 4 | [Langfuse: self-hosting](https://langfuse.com/self-hosting) | архитектура: web+worker, Postgres, Redis, ClickHouse, S3/MinIO |
| 5 | [Langfuse docker-compose.yml](https://raw.githubusercontent.com/langfuse/langfuse/main/docker-compose.yml) | 6 сервисов, образы `langfuse:4` |
| 6 | [Langfuse: OpenTelemetry](https://langfuse.com/docs/opentelemetry/get-started) | `/api/public/otel`, HTTP-only (gRPC нет), ≥v3.22.0, заголовок ingestion-version, полные таблицы маппинга gen_ai.* |
| 7 | [Phoenix LICENSE](https://raw.githubusercontent.com/Arize-ai/phoenix/main/LICENSE) | Elastic License 2.0 |
| 8 | [Phoenix: get-started](https://arize.com/docs/phoenix/get-started) | 1 контейнер; UI+OTLP HTTP :6006, OTLP gRPC :4317 |
| 9 | [Phoenix: Docker self-hosting](https://arize.com/docs/phoenix/self-hosting/deployment-options/docker) | SQLite по умолчанию, Postgres ≥14 опционально |
| 10 | [Phoenix release note 15.10.0+](https://github.com/Arize-ai/phoenix/blob/main/docs/phoenix/release-notes/05-2026/05-15-2026-otel-semconv-conversion.mdx) | авто-конвертация gen_ai.*→OpenInference на приёме |
| 11 | [Phoenix cookbook: claude-managed-agents](https://github.com/Arize-ai/phoenix/blob/main/docs/phoenix/cookbook/tracing/claude-managed-agents-guide.mdx) | кэш-токены gen_ai.usage.cache_* пока не маппятся |
| 12 | [Phoenix test_otel.py](https://github.com/Arize-ai/phoenix/blob/main/tests/unit/trace/test_otel.py) | разбор gen_ai.request.model/usage.input_tokens/messages |
| 13 | [SigNoz LICENSE](https://github.com/SigNoz/signoz/blob/main/LICENSE) | MIT core + ee/ |
| 14 | [SigNoz: LLM Observability](https://signoz.io/docs/llm-observability/) | gen_ai.* по OTLP хранятся и рендерятся рядом с остальной телеметрией |
| 15 | [SigNoz gen_ai.llm.json](https://github.com/SigNoz/signoz/blob/main/pkg/modules/spanmapper/implspanmapper/fs/definitions/gen_ai.llm.json) | spanmapper: нормализация OpenInference/OpenLLMetry/langfuse.* → gen_ai.* |
| 16 | [SigNoz: install docker](https://signoz.io/docs/install/docker/) | Foundry; ≥4 GB RAM; Windows — только WSL2 c Docker Engine (ClickHouse Keeper segfault под Docker Desktop) |
| 17 | [OpenLIT LICENSE](https://raw.githubusercontent.com/openlit/openlit/main/LICENSE) | Apache-2.0 |
| 18 | [OpenLIT README](https://raw.githubusercontent.com/openlit/openlit/main/README.md) | self-host `docker compose up -d`, UI :3000, LLM/tool/cost дашборды |
| 19 | [OpenLIT docker-compose.yml](https://raw.githubusercontent.com/openlit/openlit/main/docker-compose.yml) | 3 контейнера; OTLP gRPC :4317 / HTTP :4318 |
| 20 | [OpenLIT: поиск gen_ai в репо](https://github.com/search?q=repo%3Aopenlit%2Fopenlit+gen_ai.usage&type=code) | разбор gen_ai в otlp-receiver и semconv-модулях (код, не доки) |
| 21 | [Jaeger: getting started](https://www.jaegertracing.io/docs/latest/getting-started/) | all-in-one 1 контейнер; OTLP :4317/:4318; UI :16686; in-memory |
| 22 | [Jaeger LICENSE](https://github.com/jaegertracing/jaeger/blob/main/LICENSE) | Apache-2.0 |
| 23 | [aspire.dev: standalone dashboard](https://aspire.dev/dashboard/standalone/) | standalone OTLP-viewer; :18888/:4317/:4318; образ MCR; browser-токен |
| 24 | [dotnet/aspire LICENSE](https://github.com/dotnet/aspire/blob/main/LICENSE) | MIT |
| 25 | [Tempo: getting started](https://grafana.com/docs/tempo/latest/getting-started/) | Tempo = backend, визуализация в Grafana, рекомендация collector'а |
| 26 | [Tempo LICENSE](https://raw.githubusercontent.com/grafana/tempo/main/LICENSE) | AGPL-3.0 |
| 27 | [Helicone README](https://raw.githubusercontent.com/Helicone/helicone/main/README.md) | gateway-модель интеграции; self-host: 6 сервисов |
| 28 | [GitHub API: repos/{langfuse, Arize-ai/phoenix, openlit/openlit, SigNoz/signoz, jaegertracing/jaeger, grafana/tempo, dotnet/aspire, Helicone/helicone}](https://api.github.com/repos/langfuse/langfuse) | звёзды, даты пуша, лицензии, последние релизы (снято 2026-10-22) |
