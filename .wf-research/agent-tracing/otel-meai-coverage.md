# Покрытие агентных LLM-вызовов официальной OpenTelemetry-инструментацией Microsoft.Extensions.AI

- **Дата:** 2026-10-22
- **Назначение:** research к wayfinder-билету #80 (карта #79) — решить, что берём из официальных OTel-пакетов MEAI, а что пишем своим ActivitySource.
- **Контекст харнеса:** .NET 9; конвейер `IChatClient` с `FunctionInvokingChatClient` (`MaximumIterationsPerRequest = 6`, через `AsBuilder().UseFunctionInvocation(...).Build()`); ответ стримится (`GetStreamingResponseAsync`); read-only инструменты через `ChatTools` (AIFunction); фоновый `HintAgentPass` — детерминированные фазы без LLM; sink — локальный ротируемый JSONL; полные тексты prompt/completion не пишем (только метаданные, компактные аргументы тулов, токены, задержки, model id).

> **Главный факт, опровергающий посылку билета:** отдельного пакета **`Microsoft.Extensions.AI.OpenTelemetry` на nuget.org НЕ СУЩЕСТВУЕТ** (страница пакета — 404, flat-container API — `BlobNotFound`, поиск NuGet — 0 результатов; проверено 2026-10-22). Вся OTel-инструментация chat-клиента входит в состав пакета **`Microsoft.Extensions.AI`** — уже в релизе v9.0.0 (в дереве исходников `src/Libraries/` отдельной библиотеки `Microsoft.Extensions.AI.OpenTelemetry` нет ни на теге `v9.0.0`, ни в `main`). Класс называется `OpenTelemetryChatClient` (неймспейс `Microsoft.Extensions.AI`, **не** `.OpenTelemetry`), подключается методом-расширением `UseOpenTelemetry()`.

---

## 1. Пакеты и версии

| Пакет | Stable | Preview | TFM (подходит net9.0?) | Дата stable | URL |
|---|---|---|---|---|---|
| `Microsoft.Extensions.AI` (включает всю OTel-инструментацию) | **10.11.0** (2026-10-14) | 9.5.0-preview.1.25262.9 (2025-05-13) — последняя preview в ленте | net10.0, **net9.0**, net8.0, .NETFramework 4.6.2, netstandard2.0 — **да** | 2026-10-14 | https://www.nuget.org/packages/Microsoft.Extensions.AI |
| `Microsoft.Extensions.AI.OpenTelemetry` | **не существует** | — | — | — | https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenTelemetry (404) |
| `OpenTelemetry.Extensions.Hosting` (нужен только для DI-подключения `AddOpenTelemetry` + экспортёров) | **1.20.1** (2026-10-20) | 1.20.1-rc.1 | net10.0, **net9.0**, net8.0, .NETFramework 4.6.2, netstandard2.0 — **да** | 2026-10-20 | https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting |

Примечания:

- Список версий `Microsoft.Extensions.AI`: от 9.0.0-preview.9 до **10.11.0**; последний prerelease — `9.5.0-preview.1.25262.9` (после линейки 9.5.0 все релизы стабильные; 10.0.0 — 2025-11-11). Источник: https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai/index.json, даты — https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.ai/10.11.0.json.
- Зависимости `Microsoft.Extensions.AI` 10.11.0 (nuspec, https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai/10.11.0/microsoft.extensions.ai.nuspec): `Microsoft.Extensions.AI.Abstractions` 10.11.0, `Microsoft.Extensions.Caching.Abstractions` 10.0.13, `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.13, `Microsoft.Extensions.Logging.Abstractions` 10.0.13, `System.Diagnostics.DiagnosticSource` 10.0.13 (на net8.0/net9.0), `System.Numerics.Tensors`, `System.Text.Json`, `System.Threading.Channels`. **Зависимости от OpenTelemetry SDK нет** — инструментация построена на `System.Diagnostics` (ActivitySource/Meter), OTel SDK нужен только на стороне сбора.
- `OpenTelemetry.Extensions.Hosting` 1.20.1 зависит от `OpenTelemetry` 1.20.1 + `Microsoft.Extensions.Hosting.Abstractions` (nuspec: https://api.nuget.org/v3-flatcontainer/opentelemetry.extensions.hosting/1.20.1/opentelemetry.extensions.hosting.nuspec).
- Тип **`ChatClientInstrumentationOptions` не существует** в текущем коде (поиск по dotnet/extensions — 0 совпадений). Опции — это свойства прямо на `OpenTelemetryChatClient`: `EnableSensitiveData` и `JsonSerializerOptions` (см. ниже). Microsoft Learn подтверждает состав класса: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.opentelemetrychatclient (страница показывает пакет v10.10.0 — доки слегка отстают от NuGet 10.11.0).

## 2. Подключение и конфигурация

Сигнатура (dotnet/extensions, `src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryChatClientBuilderExtensions.cs`):

```csharp
public static ChatClientBuilder UseOpenTelemetry(
    this ChatClientBuilder builder,
    ILoggerFactory? loggerFactory = null,
    string? sourceName = null,
    Action<OpenTelemetryChatClient>? configure = null)
```

Канонический пример из официального README (`src/Libraries/Microsoft.Extensions.AI.OpenAI/README.md`):

```csharp
// Configure telemetry
var sourceName = Guid.NewGuid().ToString();
var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
    .AddSource(sourceName)
    .AddConsoleExporter()
    .Build();

IChatClient client = new ChatClientBuilder(openaiClient)
    .UseDistributedCache(cache)
    .UseFunctionInvocation()
    .UseOpenTelemetry(sourceName: sourceName, configure: c => c.EnableSensitiveData = true)
    .Build();
```

**Позиция в конвейере (критично):** `UseOpenTelemetry` должен стоять **после** `UseFunctionInvocation`, т.е. слоем **внутренним** относительно FICC (ближе к провайдеру), как в примере выше. Причины (по исходникам):

1. OTel-обёртка ставит спан на каждый вызов внутреннего клиента — только тогда **каждая итерация агентного цикла получает свой спан `chat`**. Если поставить OTel снаружи FICC, спан будет один на весь запрос.
2. `FunctionInvokingChatClient` берёт ActivitySource у внутреннего клиента: `_activitySource = innerClient.GetService<ActivitySource>()` (`FunctionInvokingChatClient.cs:92`), а `OpenTelemetryChatClient.GetService` возвращает свой `_activitySource` (`OpenTelemetryChatClient.cs:130-131`). Без OTel «под» FICC спаны `orchestrate_tools` / `execute_tool` вообще не создаются.

Опции `OpenTelemetryChatClient` (всего две):

| Свойство | По умолчанию | Что делает |
|---|---|---|
| `EnableSensitiveData` | `false`; переопределяется env-переменной **`OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true`** (регистронезависимо) | Включает запись содержимого: `gen_ai.system_instructions`, `gen_ai.input.messages`, `gen_ai.output.messages`; в `gen_ai.tool.definitions` добавляет description/parameters тулов; на chat-спан — сырое `AdditionalProperties` запроса/ответа; на `execute_tool` — `gen_ai.tool.call.arguments` и `gen_ai.tool.call.result`. Реализация дефолта: `src/Libraries/Microsoft.Extensions.AI/TelemetryHelpers.cs` (`EnableSensitiveDataDefault`), XML-доки свойства — `OpenTelemetryChatClient.cs:115-127`. |
| `JsonSerializerOptions` | `AIJsonUtilities.DefaultOptions` | JSON-сериализация сообщений/аргументов в телеметрию. |

Opt-in флагов семантических конвенций типа `OTEL_SEMCONV_STABILITY_OPT_IN` в пакете **нет** (поиск по dotnet/extensions — 0 совпадений); единственный opt-in — `EnableSensitiveData` / env-переменная выше. Конвенции объявлены как experimental и «subject to change» (XML-доки класса: реализация GenAI semconv **v1.41**).

Для харнеса (JSONL-сink): регистрируем `ActivitySource` и `Meter` с именем по умолчанию **`Experimental.Microsoft.Extensions.AI`** (константа `OpenTelemetryConsts.DefaultSourceName`, `OpenTelemetryConsts.cs:9`) либо передаём свой `sourceName` — тогда `.AddSource(name)` / `.AddMeter(name)`.

## 3. Спаны и атрибуты

**ActivitySource/Meter:** имя по умолчанию `Experimental.Microsoft.Extensions.AI` (или переданный `sourceName`). Источник: `OpenTelemetryConsts.cs:8-9`, конструктор `OpenTelemetryChatClient.cs:54-91`.

### 3.1 Спан вызова модели — `chat {modelId}` (или `chat`), `ActivityKind.Client`

Создаётся на **каждый** вызов `GetResponseAsync`/`GetStreamingResponseAsync` внутреннего клиента, т.е. **на каждую итерацию агентного цикла** (`OpenTelemetryChatClient.cs:274-378` — `CreateAndConfigureActivity`; имя спана формируется на строках 281-284). Имя соответствует конвенции `{gen_ai.operation.name} {gen_ai.request.model}` (semconv client-inference, `docs/gen-ai/client-inference.md:58`).

Атрибуты запроса (ставятся при создании спана, `OpenTelemetryChatClient.cs:286-376`):

| Атрибут | Когда пишется |
|---|---|
| `gen_ai.operation.name` = `chat` | всегда |
| `gen_ai.request.model` | всегда (`options.ModelId` ?? дефолтный из метаданных клиента) |
| `gen_ai.provider.name` | всегда, если известен из `ChatClientMetadata.ProviderName` (замена старого `gen_ai.system`) |
| `gen_ai.request.stream` = `true` | только стриминг |
| `server.address`, `server.port` | если известен `ProviderUri` |
| `gen_ai.conversation.id` | если задан `options.ConversationId` |
| `gen_ai.request.frequency_penalty` / `presence_penalty` / `max_tokens` / `seed` / `stop_sequences` / `temperature` / `top_k` / `top_p` | если заданы в `ChatOptions` |
| `gen_ai.output.type` = `text` \| `json` | если задан `ResponseFormat` |
| `gen_ai.tool.definitions` | если есть `options.Tools`: JSON-массив определений тулов; name — всегда, description/parameters — только при `EnableSensitiveData` |
| сырое `options.AdditionalProperties` | только при `EnableSensitiveData` |

Атрибуты ответа (`TraceResponse`, `OpenTelemetryChatClient.cs:396-497`):

| Атрибут | Когда пишется |
|---|---|
| `gen_ai.response.finish_reasons` | если `FinishReason` задан; формат `["stop"]` (lowercase) |
| `gen_ai.response.id` | если задан `ResponseId` |
| `gen_ai.response.model` | если модель в ответе |
| `gen_ai.response.time_to_first_chunk` | только стриминг, секунды |
| `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens` | если есть `Usage` |
| `gen_ai.usage.cache_read.input_tokens` | если есть `CachedInputTokenCount` |
| `gen_ai.usage.reasoning.output_tokens` | если есть `ReasoningTokenCount` |
| `error.type` + `Status=Error` | при исключении (`OpenTelemetryLog.RecordOperationError`) |
| сырое `response.AdditionalProperties` | только при `EnableSensitiveData` |

Содержимое (только при `EnableSensitiveData`, `OpenTelemetryChatClient.cs:522-545`):

| Атрибут | Что содержит |
|---|---|
| `gen_ai.system_instructions` | сериализованный system prompt |
| `gen_ai.input.messages` | вся история сообщений запроса (включая `tool_call` / `tool_call_response` части с аргументами и результатами) |
| `gen_ai.output.messages` | сообщения ответа модели |

**Атрибутов `gen_ai.prompt` / `gen_ai.completion` в текущей реализации НЕТ** — содержимое пишется только как `gen_ai.input.messages` / `gen_ai.output.messages`; в актуальной спецификации semconv `gen_ai.prompt`/`gen_ai.completion` тоже отсутствуют (заменены на input/output.messages, статус Opt-In; см. `docs/gen-ai/client-inference.md:111-114`). Для харнеса это и требуется: мы содержимое всё равно не пишем (`EnableSensitiveData = false`).

### 3.2 Спан оркестрации тулов — `orchestrate_tools` (FICC, нестандартное имя)

`FunctionInvokingChatClient` создаёт **один** спан на весь агентный цикл (обе точки входа): `FunctionInvokingChatClient.cs:285` (GetResponseAsync) и `:445` (GetStreamingResponseAsync). Имя помечено в константах как «Non-standard» (`OpenTelemetryConsts.cs:34`). Спан не создаётся, если текущий спан уже `invoke_agent`/`invoke_workflow` (интеграция с агентными фреймворками, `FunctionInvocationHelpers.CurrentActivityIsInvokeAgent`).

Теги: только **суммарные** `gen_ai.usage.input_tokens` и `gen_ai.usage.output_tokens` за все итерации (`AddUsageTags`, `FunctionInvokingChatClient.cs:721-736`; вызов `:431` и `:704`). Номера итераций, потолка, задержек между итерациями — нет.

### 3.3 Спан исполнения тула — `execute_tool {functionName}` (FICC, `ActivityKind.Internal`)

На каждый вызов AIFunction, `src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationProcessor.cs:160-256` (создание спана — строки 165-175). Теги:

| Атрибут | Когда пишется |
|---|---|
| `gen_ai.operation.name` = `execute_tool` | всегда |
| `gen_ai.tool.type` = `function` | всегда |
| `gen_ai.tool.call.id` | всегда (CallId) |
| `gen_ai.tool.name` | всегда |
| `gen_ai.tool.description` | всегда (description AIFunction) |
| `gen_ai.tool.call.arguments` | только при `EnableSensitiveData` (JSON аргументов) |
| `gen_ai.tool.call.result` | только при `EnableSensitiveData` (JSON результата) |
| `error.type` + `Status=Error` | при исключении в туле |

Длительность спана = длительность исполнения тула. Признак sensitive «протекает» из OTel-обёртки через custom property `__EnableSensitiveData__` на спане или прямой опрос `InnerClient.GetService<OpenTelemetryChatClient>()?.EnableSensitiveData` (`FunctionInvokingChatClient.cs:96-104`).

## 4. Метрики

**Meter:** то же имя, что у ActivitySource — `Experimental.Microsoft.Extensions.AI` (или `sourceName`). Константы: `OpenTelemetryConsts.cs:38-73`; создание — конструктор `OpenTelemetryChatClient.cs:68-91`.

| Метрика | Тип/ед. | Теги | Что меряет |
|---|---|---|---|
| `gen_ai.client.token.usage` | histogram, `{token}`; бакеты 1…67 108 864 | `gen_ai.token.type`=`input`/`output`, `gen_ai.operation.name`, `gen_ai.request.model`, `gen_ai.provider.name`, `server.address`, `server.port`, `gen_ai.response.model` | записывается **дважды** на ответ: отдельно input и output токены (`OpenTelemetryChatClient.cs:417-434`) |
| `gen_ai.client.operation.duration` | histogram, `s`; бакеты 0.01…81.92 | `gen_ai.operation.name`, `gen_ai.request.model`, `gen_ai.provider.name`, `server.address`, `server.port`, `gen_ai.response.model`, `error.type` (при ошибке) | длительность вызова модели (для стрима — всего стрима) |
| `gen_ai.client.operation.time_to_first_chunk` | histogram, `s` | те же, что у operation.duration (без error.type) | только стриминг: время до первого update |
| `gen_ai.client.operation.time_per_output_chunk` | histogram, `s` | те же | только стриминг: интервалы между чанками |

Метрики пишутся только когда включены подписчики (`Histogram.Enabled`) — лишнего оверхеда без Meter-провайдера нет. Расхождение со свежей спецификацией: в актуальном semconv токеновые метрики названы `gen_ai.client.inference.usage.input_tokens` / `.output_tokens` (счётчики, `docs/gen-ai/gen-ai-token-metrics.md:13-19`), а пакет эмитит старый вариант `gen_ai.client.token.usage` — следите при обновлениях мажоров.

## 5. Стриминг (`GetStreamingResponseAsync`)

- **Один спан на весь стрим** одной итерации: создаётся до первого движения итератора, «закрывается» в `finally` после полного перечисления (или проброса ошибки) — `OpenTelemetryChatClient.cs:166-268` (try/finally: строки 208-265; `TraceResponse` в finally — строка 260). Т.е. длительность спана = полная длительность итерации стрима, включая простой между чанками.
- Отличия от non-streaming на спане: тег `gen_ai.request.stream=true` (строка 315-318), атрибут `gen_ai.response.time_to_first_chunk` (строка 471-474).
- После каждого `yield return` восстанавливается `Activity.Current` (обход бага dotnet/runtime#47802, строки 253-256).
- **Токены при стриминге:** OTel-обёртка аккумулирует все `ChatResponseUpdate` и в конце вызывает `trackedUpdates.ToChatResponse()` (строка 261); конвертер суммирует все `UsageContent` из апдейтов в `response.Usage` (`ChatResponseExtensions.cs:624-627`: `(response.Usage ??= new()).Add(usage.Details)`). Т.е. **usage берётся из UsageContent, который стримит провайдер** (обычно в финальном апдейте; для OpenAI-совместимых API — при включённом `stream_options.include_usage` на стороне провайдера). Если провайдер не стримит usage — на спане/в метриках токенов не будет. FICC в стриминге свои суммарные usage кладёт только на `orchestrate_tools`-спан (`FunctionInvokingChatClient.cs:553-563`, `:704`), синтетический UsageContent наружу не эмитит.
- Метрики стрима: `time_to_first_chunk` (первый чанк) и `time_per_output_chunk` (интервалы), см. §4.

## 6. Tool-вызовы и агентный цикл

- **Планирование тулов моделью** видно на уровне chat-спана: `gen_ai.tool.definitions` (список тулов запроса) на каждом спане итерации; `gen_ai.response.finish_reasons` = `["tool_calls"]`; при `EnableSensitiveData` — сами вызовы в `gen_ai.input.messages`/`gen_ai.output.messages` (части `tool_call` с аргументами). Без sensitive — аргументы запланированных вызовов в телеметрии **отсутствуют**.
- **Исполнение** — отдельный спан `execute_tool {name}` на каждый вызов AIFunction (§3.3), включая ошибки (`error.type`, статус Error). Это делается самим FICC через общий ActivitySource — отдельная обвязка тулов не нужна.
- **Каждая итерация цикла = отдельный спан `chat {model}`** (OTel-обёртка стоит внутри FICC). Всего chat-спанов на запрос: до `MaximumIterationsPerRequest + 1` — при достижении потолка FICC логирует событие и делает **финальную итерацию без тулов** (`PrepareOptionsForLastIteration`, `FunctionInvokingChatClient.cs:336-339` и `:528-531`).
- **Счётчика итераций НЕТ**: ни тега с номером итерации, ни общего числа итераций ни на одном спане. Число итераций выводимо только косвенно — количеством дочерних `chat`-спанов под `orchestrate_tools` (в JSONL-синке можно посчитать при экспорте).
- **Потолок `MaximumIterationsPerRequest`:** при достижении эмитится **только ILogger-событие** Debug-уровня `"Reached maximum iteration count of {MaximumIterationsPerRequest}. Stopping function invocation loop."` (`src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationLogger.cs:32-33`, вызов `FunctionInvokingChatClient.cs:338`/`:530`). В спаны/метрики/Activity-события это **не попадает**.
- Суммарные токены за весь цикл — только на `orchestrate_tools` (§3.2).

## 7. Что НЕ покрывается из потребностей харнеса (проверено по исходникам)

| Потребность | Статус | Подтверждение |
|---|---|---|
| (a) Потолок/счётчик итераций FICC в телеметрии | **Нет.** Только Debug-лог через ILogger; в Activity/метрики не эмитится. Число итераций выводимо лишь подсчётом chat-спанов. | `FunctionInvokingChatClient.cs:336-339`, `FunctionInvocationLogger.cs:32-33`; в `OpenTelemetryChatClient.cs` тега итерации нет (полный просмотр файла) |
| (b) Стрим-гвард (таймаут/бездействие стрима) | **Нет вообще.** Ни в OTel-обёртке, ни в FICC нет ни одного упоминания Timeout/Idle (0 совпадений grep по обоим файлам); единственный механизм отмены — внешний `CancellationToken`. | grep `Timeout|Idle` по `OpenTelemetryChatClient.cs` и `FunctionInvokingChatClient.cs` — 0 |
| (c) Фазовые задержки конвейера (между итерациями/фазами) | **Нет.** Есть только длительность каждого вызова модели (`gen_ai.client.operation.duration`), TTFC и интервалы чанков. Простой между итерациями, задержки между фазами конвейера и общее время оркестрации как отдельные значения не измеряются (длительность `orchestrate_tools` можно вычесть из суммы chat-спанов лишь постфактум). | `OpenTelemetryConsts.cs:38-73` (полный список метрик); `FunctionInvokingChatClient.cs:721-736` (AddUsageTags — только токены) |
| (d) Фоновый проход `HintAgentPass` (детерминированный, без LLM) | **Нет готового.** Инструментация привязана к `IChatClient` (и Realtime/Files/Speech-клиентам); для конвейера без LLM готового эмиттера в `Microsoft.Extensions.AI` нет. Зарезервированные константы `invoke_agent`/`invoke_workflow`/`execute_tool` существуют (`OpenTelemetryConsts.cs:31-35`), но публичного класса «activity source для workflow» в пакете нет — нужен свой ActivitySource (можно с теми же gen_ai-именами операций, как это делает AgentFramework через `invoke_agent`; см. `docs/gen-ai/gen-ai-agent-spans.md:200-206`). | поиск по dotnet/extensions; `OpenTelemetryConsts.cs:30-36` |

## 8. Итоговая таблица вердиктов

| Грань | Вердикт | Обоснование (1 строка) |
|---|---|---|
| Вызов модели (нестрим) | **из коробки** | спан `chat {model}` + полные метаданные/токены/finish_reasons без sensitive-флага |
| Стриминг | **из коробки** | один спан на стрим, `gen_ai.request.stream`, TTFC + per-chunk метрики; токены — при наличии UsageContent от провайдера |
| Tool-вызовы: планирование моделью | **из коробки** (аргументы — нет) | `gen_ai.tool.definitions` + `finish_reasons=tool_calls` на каждом chat-спане; аргументы вызовов — только в messages при `EnableSensitiveData`, который мы не включаем |
| Tool-вызовы: исполнение (AIFunction) | **из коробки** | спан `execute_tool {name}` с id/длительностью/ошибками; аргументы/результат — только при `EnableSensitiveData` |
| Счётчик итераций агентного цикла | **своим ActivitySource** | номера/числа итераций нигде не эмитится; только косвенно числом chat-спанов под `orchestrate_tools` |
| Потолок итераций (`MaximumIterationsPerRequest=6`) | **не трекаем** (из коробки) | только Debug-лог ILogger; в спаны/метрики не попадает — при желании ловить лог или дописать свой спан |
| Стрим-гвард | **не трекаем** | механизма нет ни в OTel-обёртке, ни в FICC — нужен свой таймаут-гвард вокруг итератора |
| Фазовые задержки конвейера | **своим ActivitySource** | меряются только вызовы модели и чанки; межфазные/межитерационные простои не измеряются |
| Фоновый проход подсказок (HintAgentPass) | **своим ActivitySource** | детерминированный конвейер без LLM официальной инструментацией не покрывается; можно эмитить `invoke_workflow`-подобные спаны своим source |

## 9. Таблица источников

| URL | Что подтверждает |
|---|---|
| https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai/index.json | полный список версий `Microsoft.Extensions.AI` (последняя stable 10.11.0, последняя preview 9.5.0-preview.1.25262.9) |
| https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai/10.11.0/microsoft.extensions.ai.nuspec | TFM (в т.ч. net9.0) и зависимости 10.11.0; отсутствие зависимости от OpenTelemetry SDK |
| https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.ai/10.11.0.json (и аналогичные для 9.5.0, 9.5.0-preview, 10.0.0) | даты публикации |
| https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenTelemetry (404) + https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.opentelemetry/index.json (BlobNotFound) + поиск NuGet (0 hits) | отдельного пакета не существует |
| https://api.nuget.org/v3-flatcontainer/opentelemetry.extensions.hosting/index.json, .../1.20.1/opentelemetry.extensions.hosting.nuspec, registration-leaf | версии/TFM/зависимости/дата OpenTelemetry.Extensions.Hosting |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryChatClient.cs | сигнатуры GetResponse/GetStreaming, ActivitySource+Meter, все атрибуты спана, `EnableSensitiveData`, стриминг-поведение (строки: 30-31, 54-91, 115-131, 135-268, 274-378, 396-497, 499-545) |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/OpenTelemetryConsts.cs | имя source `Experimental.Microsoft.Extensions.AI`, все имена атрибутов/метрик, названия операций (`chat`, `orchestrate_tools` Non-standard, `execute_tool`, `invoke_agent`, `invoke_workflow`), env-переменная opt-in |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryChatClientBuilderExtensions.cs | сигнатура `UseOpenTelemetry` |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs | спан `orchestrate_tools` (285, 445), ActivitySource из inner client (92), sensitive-проброс (96-104), цикл и потолок итераций (329-339, 520-531), суммарные usage-теги (431, 704, 721-736), стриминг-аккумуляция usage (553-563); отсутствие timeout/idle (grep 0) |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationProcessor.cs | спан `execute_tool {name}` на каждый вызов AIFunction и все его теги (160-256) |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/Common/FunctionInvocationLogger.cs | Debug-событие достижения потолка итераций (32-33) |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/TelemetryHelpers.cs | дефолт `EnableSensitiveData` из `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatResponseExtensions.cs | агрегация UsageContent из стрима в `response.Usage` (624-627) |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.OpenAI/README.md | канонический порядок `UseFunctionInvocation()` → `UseOpenTelemetry(...)` и подключение TracerProvider |
| https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/README.md | `UseOpenTelemetry` входит в пакет `Microsoft.Extensions.AI` |
| https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.opentelemetrychatclient | официальный API-референс класса: свойства `EnableSensitiveData`, `JsonSerializerOptions`; GenAI semconv v1.41; пакет Microsoft.Extensions.AI |
| https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai | обзор: `UseOpenTelemetry` как часть пакета `Microsoft.Extensions.AI` |
| https://opentelemetry.io/docs/specs/semconv/gen-ai/ | GenAI semconv переехали в отдельный репозиторий |
| https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/gen-ai/README.md | статус конвенций: **Development** (experimental) |
| https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/gen-ai/client-inference.md | имя спана `{operation} {model}`; `gen_ai.input.messages`/`gen_ai.output.messages` Opt-In; отсутствие `gen_ai.prompt`/`gen_ai.completion`; `gen_ai.provider.name` |
| https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/gen-ai/gen-ai-token-metrics.md | актуальные токен-метрики `gen_ai.client.inference.usage.*` (расходится с `gen_ai.client.token.usage` в пакете) |
| https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/gen-ai/gen-ai-agent-spans.md | конвенция `invoke_agent {gen_ai.agent.name}` для агентных спанов |

### Замечания о неполноте / не подтверждено источником

- Поведение конкретного LLM-провайдера харнеса (стримит ли он `UsageContent` в финальном апдейте) — зависит от провайдерского пакета (`Microsoft.Extensions.AI.OpenAI` и т.п.), в рамках этого research не проверялось; по коду MEAI токены стрима появятся только если провайдер прислал usage.
- `time_to_first_chunk` присутствует в текущем spec-черновике client-inference (упоминания в тексте), но в проверенном фрагменте не удалось подтвердить точное имя атрибута `gen_ai.response.time_to_first_chunk` как спецификационное — в пакете имя именно такое (`OpenTelemetryConsts.cs:117`); статус атрибута в spec — не подтверждено источником дословно.
- Версии Learn-док (v10.10.0) отстают от NuGet (10.11.0) — расхождение зафиксировано, некритично.
