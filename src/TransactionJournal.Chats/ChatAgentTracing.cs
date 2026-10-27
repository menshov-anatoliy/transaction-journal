namespace TransactionJournal.Chats;

using System.Diagnostics;

/// <summary>
/// Единый источник операционных спанов хода чата: корневой chat_turn вокруг
/// StreamAssistantAnswerAsync. Встроенные спаны Microsoft.Extensions.AI
/// (chat {model}, orchestrate_tools, execute_tool {name}) используются как
/// есть из их собственного источника — собственных дубликатов этих спанов
/// здесь нет; собственные атрибуты используют только префикс tj.*.
// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
// Traceability: openspec:ops/agent-tracing#scenario-trace-meai-spans-verbatim
/// </summary>
internal static class ChatAgentTracing
{
	/// <summary>
	/// Источник спанов хода чата; имя — часть контракта трассировки, sink
	/// подписывается на него в composition root наряду с источником подсказок.
	// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
	/// </summary>
	public static readonly ActivitySource Source = new("TransactionJournal.Chats");

	/// <summary>
	/// Встроенный источник спанов Microsoft.Extensions.AI: UseOpenTelemetry
	/// в конвейере агента пишет именно в него, а OTel sink подписан на то же
	/// имя в composition root.
	/// </summary>
	public const string MeaiSourceName = "Experimental.Microsoft.Extensions.AI";

	/// <summary>Имя корневого спана хода чата — от вопроса владельца до фиксации ответа.</summary>
	public const string TurnSpanName = "chat_turn";

	/// <summary>Число запросов модели за ход — считаются запросы, дошедшие до клиента модели.</summary>
	public const string IterationsTag = "tj.chat.iterations";

	/// <summary>Идентификатор чата, в котором выполняется ход.</summary>
	public const string ChatIdTag = "tj.chat.id";

	/// <summary>Идентификатор вопроса владельца, на который отвечает ход.</summary>
	public const string MessageIdTag = "tj.chat.message.id";

	/// <summary>Признак отменённого владельцем хода; статус спана при этом остаётся не-ошибкой.</summary>
	public const string CancelledTag = "tj.chat.cancelled";
}
