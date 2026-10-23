using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Одноразовый прогон #87: эмулирует форму трейсов харнеса (решение #82) —
// метаданные + метрики модели + компактные аргументы тулов, без полных текстов.

using var chats = new ActivitySource("TransactionJournal.Chats");
using var hints = new ActivitySource("TransactionJournal.Hints");

using var provider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("tj-probe", serviceVersion: "0.1.0"))
    .AddSource("TransactionJournal.Chats", "TransactionJournal.Hints")
    .AddOtlpExporter(o =>
    {
        // Прогон #87: рабочий транспорт — HTTP/protobuf на :6006/v1/traces (gRPC через docker-proxy не доходит).
        o.Protocol = OtlpExportProtocol.HttpProtobuf;
        o.Endpoint = new Uri("http://localhost:6006/v1/traces");
    })
    .Build();

// Трейс 1: chat_turn с итерацией и вызовом тула
using (var turn = chats.StartActivity("chat_turn"))
{
    turn?.SetTag("gen_ai.system", "openai");
    turn?.SetTag("gen_ai.request.model", "gpt-4o-mini");
    turn?.SetTag("gen_ai.request.temperature", 0.2);
    turn?.SetTag("tj.chat.id", "chat-0001");
    turn?.SetTag("tj.chat.message.id", "msg-0001");
    turn?.SetTag("tj.chat.iterations", 2);

    using (var call = chats.StartActivity("chat.completion"))
    {
        call?.SetTag("gen_ai.system", "openai");
        call?.SetTag("gen_ai.request.model", "gpt-4o-mini");
        call?.SetTag("gen_ai.response.model", "gpt-4o-mini-2024-07-18");
        call?.SetTag("gen_ai.response.finish_reasons", new[] { "tool_calls" });
        call?.SetTag("gen_ai.usage.input_tokens", 512);
        call?.SetTag("gen_ai.usage.output_tokens", 96);
        Thread.Sleep(180);
    }

    using (var tool = chats.StartActivity("execute_tool"))
    {
        tool?.SetTag("gen_ai.tool.name", "get_account_balance");
        tool?.SetTag("gen_ai.tool.call.id", "call_abc123");
        tool?.SetTag("gen_ai.tool.call.args", "{\"accountId\":\"ACC-001\"}");
        tool?.SetTag("gen_ai.tool.call.result", "{\"balance\":15430.25,\"currency\":\"RUB\"}");
        var tags = new ActivityTagsCollection { ["args.bytes"] = 31 };
        tool?.AddEvent(new ActivityEvent("tj.tool.args", DateTimeOffset.UtcNow, tags));
        Thread.Sleep(45);
    }

    using (var call2 = chats.StartActivity("chat.completion"))
    {
        call2?.SetTag("gen_ai.system", "openai");
        call2?.SetTag("gen_ai.request.model", "gpt-4o-mini");
        call2?.SetTag("gen_ai.response.model", "gpt-4o-mini-2024-07-18");
        call2?.SetTag("gen_ai.usage.input_tokens", 689);
        call2?.SetTag("gen_ai.usage.output_tokens", 154);
        Thread.Sleep(210);
    }
}

// Трейс 2: chat_turn без тулов, другая модель — для проверки поиска
using (var turn = chats.StartActivity("chat_turn"))
{
    turn?.SetTag("gen_ai.system", "openai");
    turn?.SetTag("gen_ai.request.model", "gpt-4o");
    turn?.SetTag("tj.chat.id", "chat-0002");
    turn?.SetTag("tj.chat.message.id", "msg-0002");
    turn?.SetTag("tj.chat.iterations", 1);

    using (var call = chats.StartActivity("chat.completion"))
    {
        call?.SetTag("gen_ai.system", "openai");
        call?.SetTag("gen_ai.request.model", "gpt-4o");
        call?.SetTag("gen_ai.usage.input_tokens", 2311);
        call?.SetTag("gen_ai.usage.output_tokens", 402);
        Thread.Sleep(340);
    }
}

// Трейс 3: hint_pass с дочерними спанами фаз
using (var pass = hints.StartActivity("hint_pass"))
{
    pass?.SetTag("gen_ai.request.model", "gpt-4o-mini");
    pass?.SetTag("tj.hint.request.id", "hint-req-0001");

    using (var collect = hints.StartActivity("hint_pass.collect_context"))
    {
        collect?.SetTag("tj.hint.context.documents", 7);
        Thread.Sleep(60);
    }

    using (var compose = hints.StartActivity("hint_pass.compose"))
    {
        compose?.SetTag("gen_ai.system", "openai");
        compose?.SetTag("gen_ai.request.model", "gpt-4o-mini");
        compose?.SetTag("gen_ai.usage.input_tokens", 1420);
        compose?.SetTag("gen_ai.usage.output_tokens", 233);
        Thread.Sleep(260);
    }
}

var flushed = provider.ForceFlush(10_000);
Console.WriteLine($"flushed={flushed}: 3 traces -> OTLP HTTP/protobuf localhost:6006/v1/traces");
