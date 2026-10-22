# Локальный file-based sink трейсов для single-exe .NET

Статус: исследовано, рекомендация выдана (wayfinder-research).
Происхождение: тикет [#81](https://github.com/menshov-anatoliy/transaction-journal/issues/81), карта [#79](https://github.com/menshov-anatoliy/transaction-journal/issues/79).
Ограничение: [ADR-0003](../../docs/adr/0003-stack-single-exe-dotnet-sqlite.md) — single-exe .NET 9 + SQLite, без обязательной внешней инфраструктуры.
Все факты проверены по первоисточникам: официальная документация OpenTelemetry .NET (opentelemetry.io, extending-the-sdk в репозитории), Microsoft Learn (System.Diagnostics), исходники open-telemetry/opentelemetry-dotnet и dotnet/runtime, страницы NuGet. Ссылка — у каждого утверждения.

## 1. Рамка исследования

- Трассировка всегда включена, сэмплирование ничего не отбрасывает; трафик низкий (однопользовательский журнал сделок).
- Sink — ротируемый по размеру JSONL-файл рядом с `App_Data` (по образцу существующих `App_Data/backups`, `App_Data/chats.db`).
- Данные — метаданные спана: имя, длительность, атрибуты, статус/ошибки; полные тексты prompt/completion не пишутся.
- Dev-time просмотр в Aspire Dashboard/Jaeger — опция, не обязанность.
- Кодовая база сегодня не содержит ни `ActivitySource`, ни OpenTelemetry, ни Serilog (проверено поиском по `*.cs`/`*.csproj`) — выбор делается с нуля.

## 2. Краткий ответ

Поддерживаемого готового file-экспортёра у OpenTelemetry .NET нет: официальный SDK поставляет только OTLP (gRPC и HTTP/protobuf), Console, InMemory, Prometheus и устаревший Zipkin; запрос на официальный file-экспортёр открыт с 2024 года и заблокирован незакрытым вопросом спецификации. Любой локальный file-sink — это свой код; вопрос лишь в фундаменте.

**Рекомендация: OpenTelemetry SDK (`OpenTelemetry.Extensions.Hosting`) + собственный `BaseExporter<Activity>`, пишущий JSONL в `App_Data/traces/`, поверх штатного батч-процессора (`BatchActivityExportProcessor`); ротация по размеру — своя, внутри экспортёра (десятки строк); dev-time OTLP в Aspire Dashboard — тем же провайдером по переменной окружения.** Обоснование и отклонённые альтернативы — §5–6.

## 3. Факты по первоисточникам

### 3.1. Готовые экспортёры OTel .NET: локальной file-цели не существует

- Официальный список экспортёров — OTLP, Console, Jaeger (через OTLP), Prometheus (метрики), Zipkin; для OTLP документация прямо ограничивает выбор двумя протоколами: «HTTP/protobuf» и «gRPC» ([opentelemetry.io: exporters](https://opentelemetry.io/docs/languages/net/exporters/)). File-варианта на странице нет.
- В исходниках `OtlpExportProtocol` — только `Grpc = 0` и `HttpProtobuf = 1`: ни HTTP/JSON, ни файла ([OtlpExportProtocol.cs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExportProtocol.cs)).
- Встроенные trace-экспортёры SDK: Console, InMemory, OpenTelemetryProtocol, Zipkin (Deprecated) — [extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md).
- Запрос на официальный file-экспортёр — issue [#5920](https://github.com/open-telemetry/opentelemetry-dotnet/issues/5920) «[feature request] Official file exporter»: открыт с 2024-10-23, reopened, метки `enhancement` + `needs-spec-change`, не назначен. Позиция мейнтейнеров: в ядро не возьмут («stdout redirection is sufficient»), возможен contrib-компонент, которым никто не занимается. Ранее [#5231](https://github.com/open-telemetry/opentelemetry-dotnet/issues/5231) (2024-01, закрыт): «File exporter is not implemented in OTel .NET. You can use Otel Collector and have it export to file».
- В [opentelemetry-dotnet-contrib](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/issues?q=file+exporter) file-экспортёра тоже нет.
- На уровне спецификации: спецификация «OTLP File Exporter» существует, но в статусе Development (placeholder) и описывает только JSONL-сериализацию; обязанность SDK реализовывать её — открытый вопрос [spec#3817](https://github.com/open-telemetry/opentelemetry-specification/issues/3817), где прямо отмечено, что файловый экспорт «requires a variety of additional un-specified configuration options including the name of the file, whether a file rotation strategy is used», т. е. ротация в спецификации не определена. Спецификация полезна нам как готовый формат строки JSONL: [file-exporter spec](https://opentelemetry.io/docs/specs/otel/protocol/file-exporter/).
- OTel Collector имеет [fileexporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/exporter/fileexporter) с ротацией (`max_megabytes`, `max_backups`, `max_days`), но требует запущенный отдельный процесс коллектора — прямое нарушение ADR-0003. Отклонён.

**Вывод: поддерживаемого OTLP/file-экспортёра без внешнего endpoint не существует.** Собственная запись в файл — неизбежна.

### 3.2. Community-пакеты: единственный кандидат — альфа, пишущая в stdout

Поиск по официальному API nuget.org («opentelemetry file exporter») даёт одно релевантное имя: [`Essential.OpenTelemetry.Exporter.OtlpFile`](https://www.nuget.org/packages/Essential.OpenTelemetry.Exporter.OtlpFile), v0.1.0, опубликован 2026-02-28, 226 скачиваний, лицензия LGPL-3.0, автор sgryphon. README пакета прямо говорит: «This alpha version only supports console output. Future versions will include configuration for specific output files, along with rotation options» ([README](https://github.com/sgryphon/essential-opentelemetry/blob/main/src/Essential.OpenTelemetry.Exporter.OtlpFile/README.md)) — предполагаемое использование `dotnet run > logs.jsonl`. Репозиторий не активен после публикации. Для оконного single-exe приложения непригоден сегодня; производственной альтернативы официальным пакетам нет.

### 3.3. Собственный ActivityListener без SDK

- API: [`System.Diagnostics.ActivityListener`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitylistener) — колбэки `ActivityStarted`/`ActivityStopped` (`Action<Activity>`), предикат `ShouldListenTo`, делегат `Sample`. Всё в составе платформы (`System.Diagnostics.DiagnosticSource`), без внешних пакетов.
- **Колбэки выполняются синхронно в потоке, вызывающем `Activity.Stop()`**: в исходниках рантайма `Stop()` выставляет `Duration` и тут же вызывает `Source.NotifyActivityStop(this)` ([Activity.cs, dotnet/runtime](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/Activity.cs)). То же для старта. Значит, блокирующий I/O в колбэке встанет в горло потоку приложения — записывать можно только через фоновую очередь.
- К моменту `ActivityStopped` спан финализирован (`IsStopped = true`), доступны: `Duration`, `StartTimeUtc`, `Status`/`StatusDescription`, `TagObjects` (полный типизированный набор атрибутов; `Tags` возвращает только строковые), `Events`, `Links`, `TraceId`/`SpanId`/`ParentSpanId`, `Source`, `Kind`, `DisplayName` — [Activity, .NET 9 API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activity?view=net-9.0).
- «Всегда включено» = `Sample` возвращает `ActivitySamplingResult.AllDataAndRecorded` — [ActivitySamplingResult](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitysamplingresult).
- Документ .NET про wildcards (`distributed-tracing-collection-wildcards`) выведен из эксплуатации (404); его преемник — [distributed-tracing-configuration](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-configuration), но он описывает .NET 11 `Microsoft.Extensions.Diagnostics.Tracing`. Для .NET 9 «wildcard» — это просто собственный предикат в `ShouldListenTo` (`source.Name.StartsWith("TransactionJournal.")`).

### 3.4. OTel SDK: собственный Processor/Exporter

- Канонический способ — класс `MyExporter : BaseExporter<Activity>` с `Export(in Batch<Activity>)`; все правила задокументированы в [extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md): не бросать исключений, не мутировать activities, читать атрибуты из `TagObjects` (не `Tags`; для перфоманса — `EnumerateTagObjects`), оборачивать работу в `SuppressInstrumentationScope.Begin()` от live-loop, `Resource` через `ParentProvider.GetResource()`, статус — `Activity.Status` с фолбэком на теги `otel.status_code`/`otel.status_description`. Это официально поддерживаемая точка расширения, а не хак.
- Конвейер: процессор `BaseProcessor<Activity>` получает `OnStart`/`OnEnd`; к экспортёру его подключают обёртки `SimpleActivityExportProcessor` (синхронно) или `BatchActivityExportProcessor` (фоново) — пример в [extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md) (`AddProcessor(new SimpleActivityExportProcessor(new MyExporter(...)))`).
- **Simple vs Batch**: `SimpleExportProcessor` берёт лок и вызывает `Export` в потоке, завершившем спан, на каждый элемент ([SimpleExportProcessor.cs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/SimpleExportProcessor.cs)); `BatchExportProcessor` кладёт спан в ограниченную `CircularBuffer` (spin-ограничённый `TryAdd`), а выделенный фоновый поток сбрасывает пачки ([BatchExportProcessor.cs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/BatchExportProcessor.cs)). При переполнении очереди спан отбрасывается со счётчиком `DroppedCount`, который логируется при shutdown (`OpenTelemetrySdkEventSource.DroppedExportProcessorItems`).
- Дефолты батч-процессора (из исходников/опций): очередь `MaxQueueSize = 2048`, интервал `ScheduledDelayMilliseconds = 5000`, пачка `MaxExportBatchSize = 512`, таймаут экспортёра `ExporterTimeoutMilliseconds = 30000` — [BatchExportProcessorOptions.cs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/BatchExportProcessorOptions.cs).
- `BaseProcessor.OnEnd` документирован как «called synchronously on the thread which ended the telemetry object», а к `OnStart`/`OnEnd` требование: «should not block or take long time, since they will be called on critical code path» (XML-доки исходников, подтверждается [extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md)).
- `BaseExporter` даёт `ForceFlush(timeout)`/`Shutdown(timeout)` — их стоит реализовать, чтобы при остановке хоста хвост спанов доспамывался в файл ([BaseExporter.cs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/BaseExporter.cs)).
- **Цена SDK против голого ActivityListener**: SDK сам реализован поверх `ActivityListener`/DiagnosticSource, модель данных идентична (`System.Diagnostics.Activity`). Публикация SDK поверх listener'а добавляет: готовый фоновый батчинг с ограниченной очередью и подсчётом потерь, flush/shutdown-протокол, Resource, самплирование, фильтрацию по `AddSource` и — важнее всего — законный путь к OTLP для dev-time просмотра. Опубликованных цифр накладных расходов нет ни там, ни там; в репозитории есть BenchmarkDotNet-проекты ([test/Benchmarks/Trace](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/test/Benchmarks/Trace)) для локального промера. Для нашего трафика (спаны на границах запросов/тулов, не на токенах) разница не значима.

### 3.5. Мосты к логгингу

- Официальный мост только односторонний: ILogger → OpenTelemetry для логов ([opentelemetry.io: logs](https://opentelemetry.io/docs/languages/net/logs/)); официального Activity→ILogger моста нет ни в документации, ни в репозитории.
- Неофициальный, но зрелый мост — [SerilogTracing](https://github.com/serilog-tracing/serilog-tracing) (пакет `SerilogTracing` 2.4.0, 2026-05-08, ~3,3 млн скачиваний, мейнтейнеры Serilog/Datalust): «starts an activity listener in the background that will write spans from the framework and third-party libraries through your Serilog pipeline». Перехватывает спаны `ActivitySource`, каждый завершённый спан становится `LogEvent` (своё отображение полей: TraceId/SpanId/ParentSpanId/SpanKind/Level=статус и т. д.), дефолт сэмплирования `Sample.AllTraces()` — записывать всё, зависимости от OTel SDK нет.
- Файловый sink Serilog умеет ротацию по размеру из коробки: [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file) (7.0.0, 2025-04-28, ~1,24 млрд скачиваний): `fileSizeLimitBytes` (дефолт 1 GB), `rollOnFileSizeLimit: true` (`log.txt`, `log_001.txt`, …), `retainedFileCountLimit` (дефолт 31), `buffered` + `flushToDiskInterval`, хуки `FileLifecycleHooks`. JSONL даёт `CompactJsonFormatter` из [Serilog.Formatting.Compact](https://www.nuget.org/packages/Serilog.Formatting.Compact) (3.0.0) — один компактный JSON-объект на строку.
- Но: приложение не использует Serilog вообще; втягивание стека Serilog (Serilog + sink + formatter + SerilogTracing) ради одного sink трейсов — четвёртый-пятый новый пакет и собственная схема спана (Serilog LogEvent, не OTel-форма). Если бы Serilog уже был в стеке — это был бы сильный кандидат.

### 3.6. Ротация по размеру: кто даёт из первых рук

| Источник | Ротация по размеру | Статус |
|---|---|---|
| OTel .NET SDK | нет (file-экспортёра нет вообще, §3.1) | — |
| Serilog.Sinks.File | да: `fileSizeLimitBytes` + `rollOnFileSizeLimit` + `retainedFileCountLimit` | первый-party, зрелый ([README](https://github.com/serilog/serilog-sinks-file)) |
| Своя логика в экспортёре | да: экспорт идёт на одном фоновом потоке батч-процессора — проверка размера файла, ролловер и чистка старых файлов тривиальны (~30–40 строк) | свой код |
| OTel Collector fileexporter | да (`max_megabytes`/`max_backups`/`max_days`) | отклонён: внешний процесс ([ADR-0003](../../docs/adr/0003-stack-single-exe-dotnet-sqlite.md)) |

### 3.7. Latency: синхронный listener vs async batch, стриминг чата

- Все синхронные пути — `ActivityListener.ActivityStopped`, `BaseProcessor.OnEnd`, `SimpleExportProcessor.Export` — выполняются в потоке, завершившем спан ([Activity.cs, dotnet/runtime](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/Activity.cs); [SimpleExportProcessor.cs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/SimpleExportProcessor.cs)); официальное требование — не блокировать и не работать долго ([extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md)). Блокирующая запись файла здесь недопустима.
- Безопасный путь — `BatchExportProcessor`: `OnEnd` только кладёт спан в ограниченную очередь (быстрая операция), весь файловый I/O и ротация — на выделенном фоновом потоке; сброс раз в 5 с или пачкой 512, переполнение — дроп со счётчиком (§3.4). У голого listener'а эквивалент придётся собрать самому: `Channel<T>` + фоновый консюмер + флаш при shutdown.
- Опубликованных микробенчмарков стоимости enqueue нет; промерить можно проектами из репозитория ([test/Benchmarks/Trace](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/test/Benchmarks/Trace)). Для стриминга (ADR-0009: ответ идёт `IAsyncEnumerable<ChatResponseUpdate>`, перерисовки троттлятся ~150 мс, максимум ~6 тул-вызовов на сообщение) спаны создаются на границах «сообщение → спан, тул-вызов → дочерний спан», а не на токенах: `Activity.Stop()` выполняется один раз на операцию, и его цена в потоковой петле — один enqueue в очередь батчера. Токенный поток кодом трассировки не пересекается; главное правило — не создавать спан на чанке текста и не писать в файл ни из чего, кроме фонового экспортёра.

## 4. Сравнение вариантов

| Вариант | Поддержка/поддерживаемость | Ротация по размеру | JSONL | Асинхронность | Вес зависимостей |
|---|---|---|---|---|---|
| **(a) OTel SDK + свой `BaseExporter<Activity>` → JSONL** | официальный паттерн расширения ([extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md)); пакеты 1.20.1 (2026-10-20) в активной разработке | своя в экспортёре (просто: экспорт на одном фоновом потоке) | да (свой сериализатор; форма — по [file-exporter spec](https://opentelemetry.io/docs/specs/otel/protocol/file-exporter/) или своя) | штатный `BatchActivityExportProcessor`: фоновый поток, очередь 2048/5 с/512, дропы считаются | `OpenTelemetry.Extensions.Hosting` (+ transitively `OpenTelemetry`); OTLP-пакет — только для dev-time опции |
| **(b) Голый `ActivityListener` + фоновый писатель** | BCL, вечно зелёный API ([Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitylistener)) | своя | да (свой сериализатор) | колбэк только enqueu'ит; консюмер свой (`Channel<T>`, flush при shutdown, счётчик дропов — всё сами) | ноль пакетов |
| **(c) OTel SDK + `AddOtlpExporter` → Aspire/Jaeger** | стабильный экспортёр; требует запущенный endpoint | n/a (файла нет) | n/a | батчер SDK | требует внешний процесс/dashboard — только dev-time опция, прод-требованию не отвечает |
| **(d) Community `Essential.OpenTelemetry.Exporter.OtlpFile`** | альфа 0.1.0, 2026-02-28, 226 скачиваний, stdout-only, репозиторий спит ([NuGet](https://www.nuget.org/packages/Essential.OpenTelemetry.Exporter.OtlpFile)) | заявлена «в будущих версиях» | да (OTLP JSON) | процессоры OTel | LGPL-3.0; непроизводственная |
| **(e) SerilogTracing + Serilog.Sinks.File (+ CompactJsonFormatter)** | SerilogTracing 2.4.0 (2026-05-08), Serilog.Sinks.File 7.0.0 (2025-04-28) — зрелые, поддерживаются | **да, из коробки** (`fileSizeLimitBytes`/`rollOnFileSizeLimit`/`retainedFileCountLimit`) | да (`CompactJsonFormatter`) | пайплайн Serilog, `buffered` + `flushToDiskInterval` | 4+ пакета Serilog-стека в приложение, где Serilog нет; схема спана — Serilog LogEvent, не OTel-форма |
| **(f) OTel Collector fileexporter** | активно поддерживается, но alpha | да | да (newline-delimited OTLP JSON) | коллектор | отклонён: внешний процесс, нарушает [ADR-0003](../../docs/adr/0003-stack-single-exe-dotnet-sqlite.md) |

Проверенные версии пакетов ([nuget.org](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting)): `OpenTelemetry` / `OpenTelemetry.Exporter.OpenTelemetryProtocol` / `OpenTelemetry.Extensions.Hosting` — 1.20.1 (2026-10-20); `SerilogTracing` 2.4.0 (2026-05-08); `Serilog.Sinks.File` 7.0.0 (2025-04-28); `Serilog.Formatting.Compact` 3.0.0 (2024-06-09); `Essential.OpenTelemetry.Exporter.OtlpFile` 0.1.0 (2026-02-28).

## 5. Рекомендация

**Вариант (a): OpenTelemetry SDK + собственный JSONL-экспортёр поверх штатного батч-процессора.**

Почему не альтернативы:

- **(d)** — альфа без файлового вывода; **(f)** — внешний процесс против ADR-0003; **(c)** сам по себе не пишет файл.
- **(b)** экономит один NuGet-пакет, но заставляет навсегда содержать собственную копию того, что SDK уже делает: ограниченная очередь, фоновый поток, дропы со счётчиком, `ForceFlush`/`Shutdown`. Выигрыш только «не тянуть пакет», при этом теряется задокументированная точка расширения и путь к OTLP.
- **(e)** — честная альтернатива с бесплатной ротацией, но втягивает стек Serilog в приложение, которое его не использует, и фиксирует Serilog-схему спана вместо OTel-формы. Отклонён именно из-за несоответствия текущему стеку.

Что получается в нашем стеке (эскиз для будущего тикета имплементации):

```csharp
// composition root; пакеты: OpenTelemetry.Extensions.Hosting
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource("TransactionJournal.Consultations", "TransactionJournal.Hints") // свои ActivitySource
        .AddProcessor(new BatchActivityExportProcessor(         // фоновый батчинг: очередь/дропы/флаш
            new JsonlTraceExporter(Path.Combine(appData, "traces")),
            new BatchExportActivityProcessorOptions())));       // дефолты 2048/5s/512 — достаточны
```

- `JsonlTraceExporter : BaseExporter<Activity>`: сериализация одной строкой JSON на спан (`DisplayName`, `Source.Name`, `Kind`, `TraceId`/`SpanId`/`ParentSpanId`, `StartTimeUtc`, `Duration`, `Status`/`StatusDescription`, атрибуты из `EnumerateTagObjects` с фильтром-запретом текстов prompt/completion). Правила — [extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md).
- Ротация внутри экспортёра: файл `App_Data/traces/traces.jsonl`; перед строкой проверяется счётчик байт, при превышении лимита — флаш, закрыть, переименовать в `traces-{yyyyMMdd-HHmmss}.jsonl`, открыть новый, лишние старые удалить (хранить N файлов). Экспорт идёт на выделенном потоке батчера; на `ForceFlush`/`Shutdown` — сброс буфера (обнять локом на случай параллельного flush).
- Имена классов обёрток — `SimpleActivityExportProcessor`/`BatchActivityExportProcessor` по extending-the-sdk на момент 1.20.1; в версиях до 1.10 батч-обёртка называлась `BatchExportActivityProcessor`.

Dev-time (опция, не обязанность): рядом, тем же `TracerProvider`, включать `AddOtlpExporter()` только при заданной переменной окружения (например `OTEL_EXPORTER_OTLP_ENDPOINT`) и смотреть в standalone [Aspire Dashboard](https://aspire.dev/dashboard/standalone/) (UI `:18888`, OTLP gRPC `:4317` / HTTP `:4318`) или Jaeger с нативным OTLP ([exporters doc](https://opentelemetry.io/docs/languages/net/exporters/)). Прод-поведение от наличия dashboard не зависит: файловый экспортёр работает всегда.

## 6. Влияние на стриминг чата — фиксация билета

- Синхронный listener/`SimpleProcessor` выполняется в потоке остановки спана — файловый I/O там запрещён; официальное требование «не блокировать критический путь» — [extending-the-sdk](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md). Асинхронный батч оставляет потоковой петле цену одного enqueue в ограниченную очередь ([BatchExportProcessor.cs](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/BatchExportProcessor.cs)).
- Правило гранулярности для чата ([ADR-0009](../../docs/adr/0009-consultation-chat-architecture.md)): спан на сообщение и на тул-вызов (потолок ~6 итераций), не на токен/чанк; тогда трассировка не лежит на пути потока токенов вовсе.
- Потери при всплеске: ограниченная очередь (2048), дропы считаются и логируются при shutdown — для однопользовательского журнала дропов не ожидается.

## 7. Что не удалось верифицировать

- Опубликованные цифры накладных расходов SDK vs голый listener — нигде официально не публикуются; только локальный прогон [test/Benchmarks/Trace](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/test/Benchmarks/Trace).
- Точная дата публикации ядра `OpenTelemetry` 1.20.1 не сверялась (OTLP/Hosting 1.20.1 подтверждены 2026-10-20).
- Полнота поиска community-пакетов ограничена запросом «opentelemetry file exporter» по официальному API nuget.org; пакетов с заметным принятием не найдено.

## 8. Источники

| # | Источник | Что подтверждает |
|---|---|---|
| 1 | [opentelemetry.io: .NET exporters](https://opentelemetry.io/docs/languages/net/exporters/) | список экспортёров; OTLP = gRPC и HTTP/protobuf только |
| 2 | [OtlpExportProtocol.cs (main)](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/OtlpExportProtocol.cs) | нет HTTP/JSON и file-протокола |
| 3 | [opentelemetry-dotnet#5920](https://github.com/open-telemetry/opentelemetry-dotnet/issues/5920) | официальный file-экспортёр: открыт, needs-spec-change, в ядро не возьмут |
| 4 | [opentelemetry-dotnet#5231](https://github.com/open-telemetry/opentelemetry-dotnet/issues/5231) | file-экспортёр не реализован; предложен Collector |
| 5 | [opentelemetry-dotnet-contrib, поиск file exporter](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/issues?q=file+exporter) | contrib-компонента нет |
| 6 | [OTLP File Exporter spec (Development)](https://opentelemetry.io/docs/specs/otel/protocol/file-exporter/) | JSONL-формат файлового экспорта |
| 7 | [spec issue #3817](https://github.com/open-telemetry/opentelemetry-specification/issues/3817) | обязательность для SDK не решена; ротация не специфицирована |
| 8 | [Essential.OpenTelemetry.Exporter.OtlpFile (NuGet)](https://www.nuget.org/packages/Essential.OpenTelemetry.Exporter.OtlpFile), [README](https://github.com/sgryphon/essential-opentelemetry/blob/main/src/Essential.OpenTelemetry.Exporter.OtlpFile/README.md) | единственный community file-экспортёр: альфа, stdout-only |
| 9 | [ActivityListener — Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitylistener) | API listener'а |
| 10 | [Activity.cs (dotnet/runtime, main)](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Diagnostics.DiagnosticSource/src/System/Diagnostics/Activity.cs) | `Stop()` зовёт колбэки синхронно в потоке остановки |
| 11 | [Activity (.NET 9) — Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activity?view=net-9.0) | доступные поля спана на остановке |
| 12 | [ActivitySamplingResult — Learn](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitysamplingresult) | always-on = `AllDataAndRecorded` |
| 13 | [extending-the-sdk (README)](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md) | правила своего экспортёра/процессора; «не блокировать критический путь»; Simple/Batch-обёртки |
| 14 | [SimpleExportProcessor.cs (main)](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/SimpleExportProcessor.cs) | Simple = inline-экспорт под локом |
| 15 | [BatchExportProcessor.cs (main)](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/BatchExportProcessor.cs) | фоновый поток, CircularBuffer, дропы со счётчиком |
| 16 | [BatchExportProcessorOptions.cs (main)](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/BatchExportProcessorOptions.cs) | дефолты 2048 / 5000 мс / 512 / 30000 мс |
| 17 | [BaseExporter.cs (main)](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry/BaseExporter.cs) | `ForceFlush`/`Shutdown` |
| 18 | [test/Benchmarks/Trace](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/test/Benchmarks/Trace) | бенчмарки для самостоятельного промера |
| 19 | [opentelemetry.io: .NET logs](https://opentelemetry.io/docs/languages/net/logs/) | мост ILogger→OTel односторонний; Activity→ILogger моста нет |
| 20 | [serilog-tracing](https://github.com/serilog-tracing/serilog-tracing) | перехват ActivitySource-спанов в пайплайн Serilog |
| 21 | [serilog-sinks-file](https://github.com/serilog/serilog-sinks-file) | ротация по размеру из коробки |
| 22 | [Serilog.Formatting.Compact (NuGet)](https://www.nuget.org/packages/Serilog.Formatting.Compact) | JSONL через CompactJsonFormatter |
| 23 | [collector-contrib fileexporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/exporter/fileexporter) | ротация у коллектора; требует внешний процесс — отклонён |
| 24 | [Aspire Dashboard standalone](https://aspire.dev/dashboard/standalone/) | dev-time endpoint'ы :18888 / :4317 / :4318 |
| 25 | [NuGet: OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting) | версия 1.20.1 (2026-10-20) |
| 26 | [Learn: distributed-tracing-configuration](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-configuration) | преемник выведенного wildcard-дока (.NET 11); для .NET 9 — свой предикат `ShouldListenTo` |
