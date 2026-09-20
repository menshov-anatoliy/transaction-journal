# Research: C# MCP SDK — stdio-сервер в том же exe, что и веб-приложение

Контекст: `transaction-journal` — личный журнал торговли на Bybit (C#/.NET, EF Core + SQLite, Blazor Server). Планируется read-only MCP-слой поверх той же SQLite-базы: режим `--mcp` того же самого exe, транспорт stdio, чтобы ИИ-клиент (VS Code / GitHub Copilot, Claude Desktop, Cursor) сам спавнил процесс даже при выключенном веб-UI. Исследование выполнено для issue #3 в ветке `research/mcp-csharp-sdk`. Разделы 4–5 — размещение MCP-хоста в одном exe с Blazor и конкурентный доступ к SQLite — собраны по первоисточникам: спецификация MCP (modelcontextprotocol.io), исходники и issues репозитория csharp-sdk, sqlite.org и learn.microsoft.com.

Все факты собраны из первоисточников по состоянию на 2026-09-19: nuget.org (включая NuGet Registration API и nuspec), репозиторий [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk), docs.modelcontextprotocol.io / csharp.sdk.modelcontextprotocol.io, code.visualstudio.com, cursor.com/docs.

## 1. Официальный NuGet-пакет

### Идентификаторы пакетов

SDK распространяется как несколько пакетов ([README репозитория](https://github.com/modelcontextprotocol/csharp-sdk), [страница пакета](https://www.nuget.org/packages/ModelContextProtocol)):

| Пакет | Назначение |
|---|---|
| `ModelContextProtocol` | **Основной пакет** для клиента и stdio-сервера: hosting, DI, атрибутное обнаружение tools/prompts/resources. Ссылается на `ModelContextProtocol.Core`. Рекомендуемая стартовая точка для большинства проектов. |
| `ModelContextProtocol.Core` | Минимальный набор: только клиент и low-level server API, минимум зависимостей. |
| `ModelContextProtocol.AspNetCore` | HTTP-транспорты (Streamable HTTP/SSE) поверх ASP.NET Core. |
| `ModelContextProtocol.Extensions.Apps` / `ModelContextProtocol.Extensions.Tasks` | Расширения MCP Apps / Tasks (для нашей задачи не нужны). |

### Статус GA и версии

По данным [NuGet flat container API](https://api.nuget.org/v3-flatcontainer/modelcontextprotocol/index.json) и [NuGet Registration API](https://api.nuget.org/v3-registration5-gz-semver2/modelcontextprotocol/index.json) (даты публикации):

- **Последний стабильный релиз: `2.2.0`**, опубликован **2026-08-13** ([release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0)). Стабильная линия 2.x GA.
- **Последний preview/rc: `2.0.0-rc.2`** от 2026-07-28 — это последний prerelease-пакет в ленте вообще; prerelease-канала поверх 2.1.0/2.2.0 на 2026-09-19 нет (ищи новые превью на [странице версий](https://www.nuget.org/packages/ModelContextProtocol#versions-body)).
- История стабильной линии: `1.0.0` GA — 2026-02-25 (первый стабильный релиз после долгой эпохи 0.x-preview), затем 1.1.0 (2026-03-06), 1.2.0 (2026-03-27), 1.3.0 (2026-05-08), 1.4.0 (2026-06-04), 1.4.1 (2026-07-09); 2.0.0-preview.1 (2026-06-26) → 2.0.0-rc.1 (2026-07-25) → 2.0.0-rc.2 (2026-07-28) → **2.0.0 GA — 2026-07-28**, 2.1.0 (2026-08-05), 2.2.0 (2026-08-13).
- Темп релизов высокий: мажор 2.0.0 и два минорных релиза укладываются в ~2,5 недели. Для спеки это означает: фиксировать точную версию пакета в csproj и осознанно обновляться.

Статус «GA vs preview»: с `1.0.0` пакет стабильный; текущая основная линия — 2.x, выровненная со [спецификацией MCP 2026-07-28](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0) (с обратной совместимостью к 2025-11-25 и более ранним ревизиям протокола).

### Целевые фреймворки и зависимости (по [nuspec 2.2.0](https://api.nuget.org/v3-flatcontainer/modelcontextprotocol/2.2.0/modelcontextprotocol.nuspec))

Пакет `ModelContextProtocol` 2.2.0 таргетирует **net8.0, net9.0, net10.0 и netstandard2.0**.

Зависимости `ModelContextProtocol` 2.2.0 (одинаковы для всех TFM):

- `ModelContextProtocol.Core` — точная версия `[2.2.0]`;
- `Microsoft.Extensions.Caching.Abstractions` 10.0.10;
- `Microsoft.Extensions.Hosting.Abstractions` 10.0.10.

Обрати внимание: основной пакет ссылается только на **Hosting.Abstractions**. Полный `Microsoft.Extensions.Hosting` (нужный для `Host.CreateApplicationBuilder` / `RunAsync`) официальный quickstart предписывает добавлять отдельной командой `dotnet add package Microsoft.Extensions.Hosting` ([getting-started](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html)).

Зависимости [`ModelContextProtocol.Core`](https://api.nuget.org/v3-flatcontainer/modelcontextprotocol.core/2.2.0/modelcontextprotocol.core.nuspec) 2.2.0: `Microsoft.Extensions.AI.Abstractions` 10.8.3, `Microsoft.Extensions.Logging.Abstractions` 10.0.10, а для net8.0/netstandard2.0 дополнительно `System.IO.Pipelines` 10.0.10 и `System.Net.ServerSentEvents` 10.0.10 (на net9.0+ входят в состав фреймворка); на netstandard2.0 ещё `System.Text.Json`, `System.Collections.Immutable`, `System.Threading.Channels` и др.

Лицензия — Apache 2.0 ([README](https://github.com/modelcontextprotocol/csharp-sdk#license)).

## 2. Минимальный stdio-шаблон сервера

### Официальный quickstart из документации SDK (актуален для 2.2.0)

Источник: [Getting Started, csharp.sdk.modelcontextprotocol.io](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html) ([исходник в репо](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md)).

Установка:

```
dotnet new console
dotnet add package ModelContextProtocol
dotnet add package Microsoft.Extensions.Hosting
```

`Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}
```

Ключевые API билдера в текущей версии: `builder.Services.AddMcpServer()` → `.WithStdioServerTransport()` → `.WithToolsFromAssembly()` (все классы с `[McpServerToolType]` в текущей сборке) или `.WithTools<T>()` (явный тип). Аналогично работают `[McpServerPromptType]`/`[McpServerPrompt]` и `[McpServerResourceType]`/`[McpServerResource]`.

### Официальный quickstart на modelcontextprotocol.io — важное расхождение

Источник: [Build an MCP server, вкладка C#](https://modelcontextprotocol.io/quickstart/server) (редирект на `/docs/2026-07-28/develop/build-server`). Там даётся тот же weather-сервер, но с другими деталями хоста:

```csharp
var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
// ... регистрация сервисов ...
var app = builder.Build();
await app.RunAsync();
```

> Примечание из этой доки: используй `CreateEmptyApplicationBuilder` вместо `CreateDefaultBuilder`, чтобы сервер не писал лишнего в консоль — критично только для stdio-транспорта.

Ещё два отличия вкладки C# на modelcontextprotocol.io от SDK-доки: установка там показана с флагом `dotnet add package ModelContextProtocol --prerelease` (артефакт времён preview — сейчас пакет стабильный, флаг не нужен), а конфигурация логирования в stderr не показана вовсе. **Для нашей спеки беру гибрид из SDK-доки: `Host.CreateApplicationBuilder` + явный `LogToStandardErrorThreshold = LogLevel.Trace`.** Правило из обоих источников: **в stdio-сервере никогда нельзя писать в stdout** (`Console.WriteLine` и т.п. ломает JSON-RPC поток) — весь вывод только в stderr.

### Эталонный сэмпл из репозитория

[samples/QuickstartWeatherServer](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/QuickstartWeatherServer) — [Program.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/QuickstartWeatherServer/Program.cs) (Host.CreateApplicationBuilder + `WithTools<WeatherTools>()` + логирование в stderr + `HttpClient` в DI) и [Tools/WeatherTools.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/QuickstartWeatherServer/Tools/WeatherTools.cs) (пример tools с DI-параметром `HttpClient`). Также полезен [samples/FileBasedMcpServer/Program.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/FileBasedMcpServer/Program.cs) — минимальный вариант с file-scoped классом тула.

Смена API между версиями: в 1.x и 2.x серверный API билдера (`AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`, `[McpServerToolType]`, `[McpServerTool]`) не менялся — свёрено с [getting-started на теге v1.4.1](https://github.com/modelcontextprotocol/csharp-sdk/blob/v1.4.1/docs/concepts/getting-started.md). Основные breaking changes 2.0.0 касаются HTTP/OAuth/структурированных результатов ([release notes v2.0.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0)) и на stdio-tools почти не влияют (детали в разделе 3).

### Запуск и завершение

- Запускается сервер как обычный консольный процесс: `await builder.Build().RunAsync();` стартует Generic Host, который живёт, пока жив транспорт. stdio-транспорт предназначен именно для локальной интеграции, где «MCP-сервер работает как дочерний процесс клиента» ([Transports → stdio transport](https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html), [исходник](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)).
- Завершение инициирует клиент-владелец процесса: клиентская сторона `StdioClientTransport` имеет `ShutdownTimeout` — таймаут graceful shutdown, по умолчанию 5 секунд (таблица [StdioClientTransportOptions](https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html) в той же доке). Практический вывод: наш `--mcp`-процесс должен быстро и чисто останавливаться по завершению хоста, без фоновых потоков, мешающих шатдауну.

## 3. Определение tools

Источник: [Tools, csharp.sdk.modelcontextprotocol.io](https://csharp.sdk.modelcontextprotocol.io/concepts/tools/tools.html) ([исходник](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tools/tools.md)) — актуально для 2.2.0.

### Атрибуты и привязка параметров

Способы определения тула (по убыванию распространённости):

1. Атрибуты: метод с `[McpServerTool]` внутри класса с `[McpServerToolType]` — основной путь;
2. Фабрики `McpServerTool.Create*` из делегата / `MethodInfo` / `AIFunction`;
3. Наследование от `McpServerTool` / `DelegatingMcpServerTool`;
4. Кастомный `McpRequestHandler<,>` через `McpServerHandlers`;
5. Low-level `McpRequestFilter<,>`.

Привязка параметров метода:

- **Из MCP-запроса (аргументы тула)** десериализуются автоматически из JSON; документируются атрибутом `System.ComponentModel.[Description]` (он же — `description` в JSON-схеме).
- **Из DI / специальные типы**, разрешаются автоматически и НЕ попадают в схему тула: `McpServer`, `IProgress<ProgressNotificationValue>`, `ClaimsPrincipal` и **любой сервис, зарегистрированный в DI** (в сэмпле WeatherTools это `HttpClient`). Это ключевой механизм для нас: read-only доступ к SQLite регистрируем в DI и принимаем параметром метода.
- Поддержка отмены операции упоминается в разделе обработки ошибок: `OperationCanceledException` пробрасывается, «когда был триггерен cancellation token».

Свойства атрибута `[McpServerTool]` по [исходнику main-ветки](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol.Core/Server/McpServerToolAttribute.cs) (= 2.2.0): `Name`, `Title`, `UseStructuredContent`, `OutputSchemaType`, `IconSource`. `Name` позволяет задать имя тула, отличное от имени метода (см. [FileBasedMcpServer](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/FileBasedMcpServer/Program.cs): `[McpServerTool(Name = "echo")]`).

```csharp
[McpServerToolType]
public class MyTools
{
    [McpServerTool, Description("Echoes the input message back")]
    public static string Echo([Description("The message to echo")] string message)
        => $"Echo: {message}";
}
```

### Генерация JSON-схем из типов

Схемы параметров (JSON Schema 2020-12) генерируются автоматически из сигнатуры метода при применении `[McpServerTool]`. Маппинг из доки: `string` → `string`; `int`/`long` → `integer`; `float`/`double` → `number`; `bool` → `boolean`; **сложные типы → `object` с `properties`**. `[Description]` на параметрах заполняет `description`, значения по умолчанию (например, `int maxResults = 10`) попадают в схему как default. Для output-схемы можно явно задать `OutputSchemaType`.

### Возвращаемые типы (актуально для 2.2.0 — имена типов менялись!)

- `string` — автоматически оборачивается в `TextContentBlock`;
- одиночные блоки контента: `TextContentBlock`, `ImageContentBlock` (`ImageContentBlock.FromBytes(bytes, "image/png")`), `AudioContentBlock` (`FromBytes`), `EmbeddedResourceBlock` (с `TextResourceContents` / `BlobResourceContents`);
- несколько блоков: `IEnumerable<ContentBlock>`;
- `Microsoft.Extensions.AI.DataContent` — автоматически маппится в подходящий блок по MIME-типу;
- DTO/объекты: атрибут/опция `UseStructuredContent = true` объявляет output schema и сериализует возвращаемое значение в `CallToolResult.StructuredContent`;
- асинхронные тулы возвращают `Task<T>` (все сэмплы). Сэмпл QuickstartWeatherServer возвращает `Task<string>`.

**Смена API между версиями:** в текущей 2.x иерархия контента называется `ContentBlock`/`TextContentBlock`/`ImageContentBlock`/`AudioContentBlock`/`EmbeddedResourceBlock` (то же имя `TextContentBlock` уже в [доке 1.4.1](https://github.com/modelcontextprotocol/csharp-sdk/blob/v1.4.1/docs/concepts/getting-started.md) — клиенты разбирают результат как `result.Content.OfType<TextContentBlock>()`). Типы с историческими именами `TextContent`, `McpContent` и `CollectionResult` в актуальном SDK **отсутствуют** — поиск по коду репозитория (`CollectionResult repo:modelcontextprotocol/csharp-sdk`) даёт 0 совпадений; это имена ранних preview-версий SDK, встречающиеся в старых статьях. Не опирайся на них.

Breaking change 2.0.0 для structured content: туры с `UseStructuredContent = true` и НЕ-объектным возвращаемым типом теперь отдают значение напрямую (`structuredContent: 72`), а не оборачивают в `{ "result": 72 }`; клиент обязан читать значение по объявленной output-схеме ([release notes v2.0.0, п.5](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0)). Ещё связанное изменение: десериализация `Tool` без `inputSchema` теперь бросает `JsonException` (там же, п.6).

### Обработка ошибок: ожидаемые ошибки vs баги

Официальная модель ([Tools → Error handling](https://csharp.sdk.modelcontextprotocol.io/concepts/tools/tools.html)):

- **Ошибка тула ≠ ошибка протокола.** Ошибка выполнения отдаётся клиенту внутри `CallToolResult` с `IsError = true`, чтобы LLM увидел её и мог исправить вызов.
- **Автоматическая обработка исключений**: сервер сам ловит исключение из метода и возвращает `CallToolResult` c `IsError = true`, кроме двух случаев: `McpProtocolException` пробрасывается как JSON-RPC ошибка (не tool-результат); `OperationCanceledException` пробрасывается при сработавшем cancellation token.
- **Какое сообщение увидит клиент**: если исключение унаследовано от `McpException` (кроме `McpProtocolException`) — в текст ошибки попадёт его сообщение; любое другое исключение — **generic-сообщение** (в примере: `"An error occurred invoking 'divide'."`), чтобы не утекали внутренние детали.
- **Практическое правило для нас**: ожидаемые/бизнес-ошибки (не нашли сделку, неверный диапазон дат) — `throw new McpException("...")` с человекочитаемым сообщением; всё остальное — баги, SDK сам скроет детали. Пример `McpException` есть и в [WeatherTools.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/QuickstartWeatherServer/Tools/WeatherTools.cs) (`?? throw new McpException(...)`).
- **Протокольные ошибки** (невалидные параметры, неизвестный тул): `throw new McpProtocolException("Missing required input", McpErrorCode.InvalidParams);` → JSON-RPC error c кодом -32602. Для атрибутных тулов этот случай редок — невалидные аргументы обычно отсекаются схемой.
- **На клиенте** проверяется `CallToolResult.IsError`, текст берётся из `result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text`.

## 4. Размещение MCP-сервера в том же exe, что и Blazor Server

### Официального паттерна нет — есть два канонических сценария и практика сообщества

В документации C# SDK описаны ровно два сценария хостинга: stdio-сервер как **консольное приложение на `Host.CreateApplicationBuilder`** ([getting-started](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/getting-started.md)) и HTTP-сервер как **`WebApplication` + `ModelContextProtocol.AspNetCore` + `MapMcp()`** ([transports](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)). Официального паттерна «`--mcp` в одном exe с WebApplication» не существует — это практика сообщества, встречающаяся вплоть до инструментов Microsoft. Ближайшие обсуждения в SDK: [discussion #166 «Passing arguments to my console application»](https://github.com/modelcontextprotocol/csharp-sdk/discussions/166) (передача CLI-аргументов в MCP-сервер) и [issue #713](https://github.com/modelcontextprotocol/csharp-sdk/issues/713) (загрязнение stdout логами). Прямого обсуждения «ASP.NET Core + stdio в одном процессе» в issues/discussions SDK не найдено — фиксируем честно.

### Требования спецификации к stdio-транспорту

Из [спецификации stdio-транспорта 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio):

- сервер читает JSON-RPC из `stdin` и пишет в `stdout`, сообщения построчные (newline-delimited);
- «The server MUST NOT write anything to its `stdout` that is not a valid MCP message» — любой `Console.WriteLine`, баннер хостинга или EF-лог ломает протокол;
- «The server MAY write UTF-8 strings to its `stderr` for any logging purposes» — stderr официально предназначен для логов;
- завершение: клиент закрывает stdin, «Servers SHOULD exit promptly when their standard input is closed»; при таймауте процесс жёстко терминируется (Windows: `TerminateProcess`/Job Objects); неожиданно упавший сервер клиент перезапускает, in-flight запросы теряются.

### Раннее ветвление в Program.cs — реальные примеры

**Microsoft Azure.GeneratorAgent** ([McpServerHost.cs](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/tools/Azure.GeneratorAgent/src/Mcp/McpServerHost.cs), [Program.cs](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/tools/Azure.GeneratorAgent/src/Program.cs)) — тот же бинарник активирует stdio-MCP-режим флагом `--mcp-server`:

```csharp
// Activated when the binary is run with the --mcp-server flag.
public static async Task<int> RunAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);   // НЕ WebApplication

    // Logging at Warning+ to avoid polluting stdout (the MCP transport channel)
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(McpServerHost).Assembly);

    using var host = builder.Build();
    try { await host.RunAsync().ConfigureAwait(false); return 0; }
    catch (OperationCanceledException) { return 0; }
    catch (Exception) { return 1; }
}
```

**PaperTodo** ([McpBridge.cs](https://github.com/snownico0722/PaperTodo/blob/main/src/McpBridge.cs)) — WPF-приложение с `--mcp`-режимом того же exe: константа `--mcp`, проверка (`args.Any(a => string.Equals(a?.Trim(), "--mcp", StringComparison.OrdinalIgnoreCase))`) в самом начале точки входа ([App.xaml.cs `OnStartup`](https://github.com/snownico0722/PaperTodo/blob/main/App.xaml.cs)) до создания UI, `Host.CreateEmptyApplicationBuilder` (не добавляет дефолтные logging/config-провайдеры), `builder.Logging.ClearProviders()`, ошибки — в `Console.Error` с exit code 1. Архитектурная деталь: их MCP-процесс вторичный и ходит по pipe в основной работающий экземпляр; наш сценарий проще — MCP-процесс сам читает SQLite.

**Вывод для нашего Program.cs (Blazor Server):** проверять `args.Contains("--mcp")` первым делом, **до** `WebApplication.CreateBuilder(...)`, и уходить в отдельный минимальный generic host (`Host.CreateApplicationBuilder` или `CreateEmptyApplicationBuilder`). `WebApplication` в MCP-режиме не строить вообще: не нужны Kestrel и middleware, а lifetime-сообщения ASP.NET Core не попадут в stdout. Замечание: `WebApplication.CreateBuilder(args)` сам подключает CommandLine-конфиг-провайдер, и «лишний» флаг `--mcp` ошибки не вызвал бы, но раннее ветвление чище и дешевле.

### Конфликт WebApplication-хоста и stdio-консоли, гигиена вывода

- [Issue #713](https://github.com/modelcontextprotocol/csharp-sdk/issues/713): логи хостинга на stdout ломают клиентов (LM Studio, gemini cli, Claude Desktop). Воркараунды из issue: `Console.SetOut(Console.Error)` (грубое перенаправление всего stdout) и `builder.Logging.AddFilter(_ => false)` (полное отключение); мейнтейнер подтвердил официальный способ — логи в stderr.
- Основной инструмент: [`ConsoleLoggerOptions.LogToStandardErrorThreshold`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.console.consoleloggeroptions.logtostandarderrorthreshold) — «minimum level of messages that get written to `Console.Error`». Стратегии из реального кода:
  - `AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)` — весь console-лог в stderr (getting-started SDK, Azure.GeneratorAgent);
  - `builder.Logging.ClearProviders()` — вообще без console-лога, для диагностики файловый лог (PaperTodo);
  - Serilog `WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)` + файл — официальный сэмпл [TestServerWithHosting/Program.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/TestServerWithHosting/Program.cs);
  - safety-net `Console.SetOut(Console.Error)` до построения хоста — страховка от случайных прямых записей в stdout.
- **Ограничение «того же exe»:** [`StdioServerTransport`](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol.Core/Server/StdioServerTransport.cs) жёстко привязан к консоли процесса (конструктор берёт `Console.OpenStandardInput()` и `Console.OpenStandardOutput()`) — один процесс **не может одновременно** обслуживать stdio-MCP и веб-UI. Режимы должны быть взаимоисключающими по аргументу — наш сценарий «либо web, либо `--mcp`» этому как раз соответствует.

### Lifetime: закрытие stdin × Generic Host

SDK связывает EOF stdin с Generic Host автоматически — [`SingleSessionMcpServerHostedService`](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol/SingleSessionMcpServerHostedService.cs): `BackgroundService` ждёт `session.RunAsync(stoppingToken)` и в `finally` вызывает `IHostApplicationLifetime.StopApplication()`. Цепочка: клиент закрыл stdin → чтение `RunAsync` завершилось → `StopApplication()` → штатный graceful shutdown хоста → `await host.RunAsync()` возвращает управление → процесс завершается. Кастомный код обработки stdin не нужен. Со стороны клиента `StdioClientTransportOptions.ShutdownTimeout` — 5 секунд, потом форс-килл ([transports](https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html)): в MCP-режиме не держать фоновые сервисы, которые долго сопротивляются остановке. Деталь реализации: SDK оборачивает stdin в `CancellableStdinStream`, потому что `WindowsConsoleStream` не уважает `CancellationToken` ([исходник StdioServerTransport](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol.Core/Server/StdioServerTransport.cs)).

### Тот же exe vs отдельный exe

Официального сравнения нет. За тот же exe: один артефакт для деплоя/релиза; переиспользование доменной логики и конфигурации; прецеденты — Azure.GeneratorAgent (флаг у того же бинарника), PaperTodo (`--mcp` у GUI-приложения); спецификация MCP этого не запрещает — важны только поведение потоков и реакция на EOF. Против/ограничения: stdio-режим монополизирует stdout всего процесса, требует раннего ветвления до инициализации хоста/UI и «тихого» окружения; каждый клиент спавнит свой экземпляр (child process), то есть процессов может быть несколько. Альтернатива по транспорту: HTTP MCP через `ModelContextProtocol.AspNetCore` + `MapMcp()` (stateless Streamable HTTP) — клиенту не нужен спавн процесса, но веб-UI должен быть запущен; наше требование «клиент спавнит процесс даже при выключенном веб-UI» однозначно выбирает stdio + `--mcp`.

## 5. Доступ к SQLite из MCP-процесса параллельно с веб-приложением

### Режим WAL

Из [sqlite.org/wal.html](https://www.sqlite.org/wal.html): «WAL provides more concurrency as readers do not block writers and a writer does not block readers» — чтение и запись идут конкурентно. Включение `PRAGMA journal_mode=WAL;` — **персистентное свойство файла базы** (достаточно выполнить один раз; pragma возвращает `wal`). Ограничения WAL: все процессы обязаны быть на одном хосте (не работает поверх сетевых ФС — нужна shared memory); дополнительные файлы `-wal` и `-shm`; авточекпоинт при 1000 страницах и при закрытии последнего соединения; большие транзакции (>~100 МБ) неэффективны; `page_size` после перехода в WAL не меняется. Microsoft явно рекомендует WAL для конкурентности, и: «Write-ahead logging is enabled by default on databases created using Entity Framework Core» ([Async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)) — наша база, созданная EF Core, уже в WAL. Не смешивать `Cache=Shared` с WAL: «Mixing shared-cache mode and write-ahead logging is discouraged» ([Caution в connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings)).

### Connection string Microsoft.Data.Sqlite — ключевые слова

Из [Connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings):

| Ключевое слово | Значения / семантика |
|---|---|
| `Data Source` | путь к файлу БД; синонимы `DataSource` и `Filename`; `:memory:` — in-memory |
| `Mode` | **`ReadWriteCreate` (по умолчанию!)**, `ReadWrite`, `ReadOnly`, `Memory` |
| `Cache` | `Default` (по умолчанию), `Private`, `Shared` (с WAL не смешивать) |
| `Default Timeout` | таймаут команд в секундах, по умолчанию 30; синоним `Command Timeout` |
| `Pooling` | `True` (по умолчанию с 6.0) / `False` |
| `Foreign Keys` | шлёт `PRAGMA foreign_keys` при открытии |

Типизированная сборка строки — `SqliteConnectionStringBuilder` (заодно защита от injection).

### Достаточно ли `Mode=ReadOnly` — да, с оговорками

- В WAL читатель не блокируется писателем и наоборот; каждый читатель держит свой «end mark» — снапшот на момент старта read-транзакции, чтение консистентно. Официальный пример read-only строки: `Data Source=Reference.db;Mode=ReadOnly`.
- Нюанс прав: «It is not possible to open read-only WAL databases» в общем случае, но начиная с SQLite 3.22.0 (2018-01-22) read-only WAL-база открывается, **если `-shm`/`-wal` уже существуют или могут быть созданы** — процессу нужны права записи на `-shm` ([wal.html](https://www.sqlite.org/wal.html)). Для локального exe под одним пользователем Windows это не проблема; на read-only каталоге или network share подключение упадёт. Вариант `immutable=1` (URI filename) нам не подходит: веб-приложение пишет, а immutable обещает SQLite неизменность файла — читатель видел бы устаревшие данные.
- Бонус `Mode=ReadOnly`: fail-fast — при отсутствующем/битом пути будет понятная ошибка вместо молчаливого создания пустой базы (по умолчанию-то `ReadWriteCreate`). «Read-only» означает «нет SQL-записей», но запись в shared-memory `-shm` всё равно происходит.

### «database is locked», таймауты, ретраи

- Microsoft.Data.Sqlite при busy/locked **автоматически ретраит** до достижения CommandTimeout (по умолчанию 30 с; 0 — без лимита) ([Database errors](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors)). ADO.NET-объекты (`SqliteConnection`/`SqliteCommand`/`SqliteDataReader`) не потокобезопасны — на каждый вызов открывать новое соединение (pooling делает это дёшево).
- `PRAGMA busy_timeout` Microsoft.Data.Sqlite **не использует**: CommandTimeout реализован как per-command retry/timeout; ручной обход при желании — через `StateChange` + `PRAGMA busy_timeout = 5000` ([dotnet/efcore#28135](https://github.com/dotnet/efcore/issues/28135), мейнтейнер bricelam).
- `SQLITE_BUSY_SNAPSHOT` ([dotnet/efcore#29514](https://github.com/dotnet/efcore/issues/29514), [rescode](https://www.sqlite.org/rescode.html#busy_snapshot)): соединение, начавшее read-транзакцию и повышающее её до write после чужой записи, получает BUSY навсегда (таймаут не лечит). Нашему read-only MCP-процессу не грозит; касается веб-приложения — не держать долгие read-транзакции перед записью.

### EF Core: read-only строка, PRAGMA, миграции

```csharp
var cs = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadOnly,
    Pooling = true,
}.ToString();
optionsBuilder.UseSqlite(cs);
```

`journal_mode` достаточно выполнить один раз (свойство файла); per-connection PRAGMA (`foreign_keys`, `busy_timeout`) — на каждом открытом соединении; в EF удобно через `Database.GetDbConnection().StateChange` (как в [#28135](https://github.com/dotnet/efcore/issues/28135)). В MCP-режиме **не вызывать** `Migrate()`/`EnsureCreated()`: с EF9 миграции защищены лок-таблицей `__EFMigrationsLock` — второму процессу пришлось бы ждать бесконечно ([EF Core SQLite Limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)). Важно для инструментов: «SQLite doesn't support asynchronous I/O. Async ADO.NET methods will execute synchronously» ([async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)) — async-подпись тулов не даёт реального I/O-параллелизма, конкурентность обеспечивает именно WAL. Ограничения провайдера: `DateTimeOffset`/`decimal`/`TimeSpan`/`ulong` оцениваются на клиенте для сравнения/сортировки — учесть в отчётных запросах журнала.

### Сводка граблей двух процессов с одной базой

1. Без WAL писатель блокирует читателей и наоборот → `SQLITE_BUSY` с ретраями до 30 с; у нас WAL уже есть (базу создаёт EF Core).
2. Сетевой диск — WAL не работает вовсе (shared memory).
3. Права записи на `-wal`/`-shm` нужны даже read-only читателю.
4. `Cache=Shared` не смешивать с WAL.
5. Пока висит хоть одно соединение (в т.ч. pooled у MCP-процесса), финальный чекпоинт «по закрытию последнего соединения» не происходит — это объясняет «зависшие» `-wal`-файлы; pooled-соединения закроются при завершении MCP-процесса по EOF stdin.
6. `ReadWriteCreate` по умолчанию → в MCP-строке обязателен `Mode=ReadOnly` (и защита от случайной записи, и fail-fast).
7. Async в Microsoft.Data.Sqlite синхронный — не закладывать I/O-параллелизм на async-подпись.
8. Миграции — только в веб-приложении, никогда в MCP-режиме (`__EFMigrationsLock`).


## 6. Регистрация stdio MCP в ИИ-клиентах (Windows)

Во всех трёх клиентах сервер описывается одинаково по сути: имя → `command` + `args` (+ опционально `env`). Наш вариант: `command` — путь к exe журнала, `args` — `["--mcp"]`. В JSON на Windows пути пишутся с двойными бэкслешами (`\\`) или прямыми слешами (`/`) — примечание из [официального quickstart](https://modelcontextprotocol.io/quickstart/server).

### VS Code / GitHub Copilot

Источник: [Add and manage MCP servers](https://code.visualstudio.com/docs/copilot/customization/mcp-servers) (редирект на `/docs/agent-customization/mcp-servers`) и [MCP configuration reference → stdio](https://code.visualstudio.com/docs/agents/reference/mcp-configuration).

Файл `.vscode/mcp.json` (workspace) или user-profile `mcp.json` (команда **MCP: Open User Configuration** / **MCP: Add Server**). Поля stdio-сервера по reference: `type` (в таблице помечен Required, хотя минимальный пример в той же доке обходится без него), `command` (Required), `args`, `cwd`, `env`, `envFile` (макрос `${workspaceFolder}`, `${input:...}` для секретов):

```json
{
  "servers": {
    "transaction-journal": {
      "type": "stdio",
      "command": "d:\\Git\\menshov-anatoliy\\transaction-journal\\src\\TransactionJournal.exe",
      "args": ["--mcp"]
    }
  }
}
```

Нюансы: сервер запускается там, где сконфигурирован (user-профиль — локально); при первом старте VS Code требует подтвердить доверие серверу (MCP server trust); автозапуск при обращении в чат управляется настройкой `chat.mcp.autostart` (`newAndOutdated` по умолчанию); для конфигурации, переносимой на Agent Host, используется workspace `.mcp.json` или `~/.copilot/mcp-config.json`.

### Claude Desktop

Источник: [Build an MCP server → Testing your server with Claude for Desktop](https://modelcontextprotocol.io/quickstart/server) (вкладки с конфигом для Windows).

Файл `%APPDATA%\Claude\claude_desktop_config.json` (в PowerShell: `$env:AppData\Claude\claude_desktop_config.json`), ключ `mcpServers`:

```json
{
  "mcpServers": {
    "transaction-journal": {
      "command": "D:\\Git\\menshov-anatoliy\\transaction-journal\\src\\TransactionJournal.exe",
      "args": ["--mcp"]
    }
  }
}
```

После сохранения конфига нужно перезапустить Claude Desktop. Примечание из доки: возможно, потребуется полный путь к исполняемому файлу в `command` (аналог `where uv` для проверки).

### Cursor

Источник: [Model Context Protocol (MCP) — Cursor Docs](https://cursor.com/docs/mcp) (markdown-версия `https://cursor.com/docs/mcp.md`; прежний URL docs.cursor.com/en/context/mcp теперь редиректит).

Файл `.cursor/mcp.json` (проект) или `~/.cursor/mcp.json` (глобально). Таблица полей STDIO-сервера: `type` (Required, `"stdio"`), `command` (Required), `args`, `env` (поддерживает интерполяцию `${env:VAR}`), `envFile` (только для stdio):

```json
{
  "mcpServers": {
    "transaction-journal": {
      "type": "stdio",
      "command": "D:\\Git\\menshov-anatoliy\\transaction-journal\\src\\TransactionJournal.exe",
      "args": ["--mcp"]
    }
  }
}
```

Общее для всех клиентов: сервер — дочерний процесс клиента; клиент спавнит его сам, убивает при закрытии/таймауте. Наш веб-UI запускать для этого не нужно — достаточно самого exe с аргументом `--mcp`.

## Открытые вопросы/риски

По темам 1–3:

1. **Темп мажорных релизов SDK.** 2.0.0 вышел 2026-07-28, уже есть 2.1.0 и 2.2.0; v2.0.0 содержит [10 breaking changes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0) (HTTP/OAuth в основном, но затронут и structured content). Риск дрейфа API при обновлениях — фиксировать версию в csproj и читать release notes перед bump.
2. **Зависимости Microsoft.Extensions.* 10.x** (Caching.Abstractions 10.0.10, AI.Abstractions 10.8.3 и транзитивно Hosting.Abstractions 10.0.10) поверх нашего проекта — проверить отсутствие конфликтов версий с пакетами приложения (особенно если app сейчас на net8.0 с более старыми Extensions-пакетами). Сам пакет поддерживает net8.0/net9.0/net10.0 — TFM не проблема, риск только в unify версий Extensions.
3. **Два расходящихся официальных quickstart**: SDK-дока (`Host.CreateApplicationBuilder` + stderr-логирование) vs modelcontextprotocol.io (`Host.CreateEmptyApplicationBuilder(settings: null)`, без настройки логов, с устаревшим флагом `--prerelease`). Для комбинированного exe выбор хост-стратегии — тема разделов 4–5, но гигиена «stdout только для JSON-RPC» обязательна в любом варианте.
4. **Генерация схем для наших DTO**: в доке формально описаны только примитивы и «сложные типы → object с properties». Поведение enum, `DateTime`, nullable-параметров и naming policy (PascalCase vs camelCase в JSON) в официальной доке не специфицировано — проверить экспериментально на реальных DTO журнала (сделки, фильтры по датам).
5. **`CollectionResult`/`TextContent`/`McpContent` из старых статей отсутствуют в SDK 2.x** — при чтении внешних примеров игнорировать, ориентироваться только на `*ContentBlock` и `CallToolResult`.
6. **Возврат больших выборок**: `IEnumerable<ContentBlock>` сериализуется целиком в один tool-результат; для журнала сделок нужны пагинация/лимиты в контракте тулов (по умолчанию, аналогично `maxResults = 10` из примера схемы).

По темам 4–5:

7. **Нет официального паттерна `--mcp`**: решение опирается на практику сообщества и прецеденты — Microsoft Azure.GeneratorAgent (`--mcp-server` у того же бинарника), PaperTodo (`--mcp` у WPF-приложения). Следить за issues/discussions csharp-sdk на случай появления официального guidance по совмещению с ASP.NET Core.
8. **Несколько MCP-процессов одновременно**: каждый клиент (и каждое его окно/инстанс) спавнит свой экземпляр — несколько read-only читателей параллельно с пишущим веб-приложением. WAL это допускает, но в спеке стоит продумать лимиты выборок/соединений.
9. **Расположение файла БД**: WAL требует локального диска с правами записи на `-shm`; сетевые и read-only каталоги исключены. Поведение пула Microsoft.Data.Sqlite с `Mode=ReadOnly` при отсутствии файла (fail-fast) и `Foreign Keys=True` для read-only EF-запросов — прямых документированных утверждений нет, проверить при реализации.

По теме 6 (клиенты):

10. **VS Code: расхождение reference и примеров** — поле `type` в таблице stdio помечено Required, но минимальный пример из той же доки работает без него; пишем явно `"type": "stdio"` (валидно и для Cursor).
11. **Claude Desktop**: документация modelcontextprotocol.io не описывает поле `type` для stdio вовсе (только `command`/`args`/`env`) — для надёжности не добавлять лишних полей.
12. **Пути в конфигах** — абсолютные, с `\\` или `/`; при переустановке/перемещении exe конфиги трёх клиентов правятся вручную. Стоит предусмотреть `dotnet publish` в стабильный путь и/или документировать настройку.
13. **Доверие/безопасность**: VS Code требует подтверждения trust при первом запуске и предупреждает, что локальные MCP-серверы исполняют произвольный код; `env` в конфигах — не для секретов (VS Code рекомендует `${input:...}`). Для read-only журнала это некритично, но учесть в документации режима `--mcp`.

## Выводы для спеки MCP

1. **Пакет**: `ModelContextProtocol` **2.2.0** (стабильный GA, 2026-08-13) + отдельной ссылкой `Microsoft.Extensions.Hosting`. TFMs (net8.0/net9.0/net10.0) покрывают наш стек. `ModelContextProtocol.AspNetCore` и extension-пакеты не нужны — транспорт у нас stdio.
2. **Каркас режима `--mcp`**: generic host, `builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` (или `.WithTools<T>()` для явного списка), `await builder.Build().RunAsync();`. Сервер — дочерний процесс ИИ-клиента, запускается конфигом клиента без участия веб-UI; завершение — по инициативе клиента (грейсфул-шатдаун хоста, у клиента таймаут 5 c по умолчанию).
3. **Гигиена вывода**: категорически ничего не писать в stdout (только JSON-RPC); все логи — в stderr через `LogToStandardErrorThreshold = LogLevel.Trace`. Это требование протокола из официальной доки.
4. **Тулы**: статические классы `[McpServerToolType]` + методы `[McpServerTool]` c `[Description]` на методе и параметрах; имя тула при необходимости — `Name = "..."`. Read-only доступ к SQLite — через DI-параметр метода (любой зарегистрированный сервис разрешается автоматически и не попадает в схему).
5. **Контракты тулов**: возврат `Task<string>` (текст для LLM) как базовый вариант; для машинно-читаемых выборок — DTO c `UseStructuredContent = true` (помнить: не-объектные значения отдаются как есть, без обёртки `result`). Большие выборки — только с пагинацией/лимитами.
6. **Ошибки**: ожидаемые ошибки — `throw new McpException("человекочитаемое сообщение")` → клиент получит `IsError = true` с этим текстом; баги — обычные исключения, SDK вернёт generic-сообщение и не утечёт детали; протокольные нарушения — `McpProtocolException`.
7. **Регистрация у клиентов**: три коротких JSON-конфига (VS Code `.vscode/mcp.json` → `servers`; Claude Desktop `%APPDATA%\Claude\claude_desktop_config.json` → `mcpServers`; Cursor `.cursor/mcp.json` → `mcpServers`), везде `command` = путь к exe, `args` = `["--mcp"]`. Схемы конфигов стабильны и задокументированы первоисточниками.
8. **Ветвление режима**: проверку `args.Contains("--mcp")` делать первым делом в точке входа, до `WebApplication.CreateBuilder`; в MCP-ветке — отдельный минимальный generic host (`Host.CreateApplicationBuilder`/`CreateEmptyApplicationBuilder`), `WebApplication` не строить вовсе. Режимы web и `--mcp` взаимоисключающие: `StdioServerTransport` монополизирует stdout процесса.
9. **Вывод и lifetime MCP-режима**: логи — только в stderr (`AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)`, либо `ClearProviders()` + файловый лог; страховка `Console.SetOut(Console.Error)`). Завершение по EOF stdin SDK делает сам (`SingleSessionMcpServerHostedService` → `StopApplication()`), у клиента таймаут 5 с — без фоновых сервисов, сопротивляющихся остановке.
10. **SQLite в MCP-режиме**: база EF Core уже в WAL → read-only читатель работает параллельно пишущему веб-приложению; connection string через `SqliteConnectionStringBuilder { DataSource, Mode = SqliteOpenMode.ReadOnly, Pooling = true }`, без `Cache=Shared`; миграции/`EnsureCreated` — только в веб-режиме; busy-ретраи встроены (`Default Timeout`), `SQLITE_BUSY_SNAPSHOT` read-only-тулу не грозит.
11. **Async-инструменты**: Microsoft.Data.Sqlite выполняет async синхронно — async в туллах ради отзывчивости хоста, а не I/O-параллелизма; консистентность выборки даёт снапшот read-транзакции; крупные выборки пагинировать (см. п. 5).
