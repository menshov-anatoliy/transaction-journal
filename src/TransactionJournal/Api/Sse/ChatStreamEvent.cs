namespace TransactionJournal.Api.Sse;

/// <summary>
/// Событие SSE-канала чата агента: имя события протокола и JSON-нагрузка.
/// Транспортный конверт доменно-нейтрален: генератор чата (change
/// add-agent-chat) порождает поток событий, а SSE-слой доставляет их SPA.
/// </summary>
public sealed record ChatStreamEvent(string Name, object? Data = null);

/// <summary>
/// Типы событий протокола SSE-канала чата: начало, токен, активность
/// инструмента, завершение, ошибка.
/// </summary>
public static class ChatStreamEvents
{
	/// <summary>Имя события начала: канал открыт, генерация пошла.</summary>
	public const string StartName = "start";

	/// <summary>Имя события токена: порция текста ответа.</summary>
	public const string TokenName = "token";

	/// <summary>Имя события завершения: ответ дописан целиком.</summary>
	public const string DoneName = "done";

	/// <summary>Имя события ошибки: генерация не удалась.</summary>
	public const string ErrorName = "error";

	/// <summary>Имя события активности инструмента: агентный цикл вызывает тул реестра.</summary>
	public const string ToolName = "tool";

	/// <summary>Фаза активности инструмента «начало»: вызов инструмента стартовал.</summary>
	public const string ToolStartPhase = "start";

	/// <summary>Фаза активности инструмента «конец»: вызов инструмента завершён.</summary>
	public const string ToolEndPhase = "end";

	/// <summary>Событие начала канала.</summary>
	public static ChatStreamEvent Start() => new(StartName);

	/// <summary>Событие токена ответа.</summary>
	public static ChatStreamEvent Token(string text) => new(TokenName, new ChatTokenPayload(text));

	/// <summary>Событие завершения ответа.</summary>
	public static ChatStreamEvent Done() => new(DoneName);

	/// <summary>Событие ошибки генерации.</summary>
	public static ChatStreamEvent Error(string message) => new(ErrorName, new ChatErrorPayload(message));

	/// <summary>Событие активности инструмента: имя инструмента и фаза вызова вокруг исполнения тула реестра.</summary>
	// Кадры события tool доставляются между началом и единственным
	// терминальным кадром: имя — из реестра read-only инструментов,
	// фаза — начало или конец вызова, деталь необязательна. Идентификатор
	// вызова (callId) сопоставляет пары «начало — конец»: одновременные
	// вызовы одного инструмента не гасят пилюли друг друга раньше времени.
	// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
	public static ChatStreamEvent Tool(string name, string phase, string? detail = null, string? callId = null) =>
		new(ToolName, new ChatToolPayload(name, phase, detail, callId));
}

/// <summary>Нагрузка события токена: порция текста ответа.</summary>
public sealed record ChatTokenPayload(string Text);

/// <summary>Нагрузка события ошибки: сообщение о сбое генерации.</summary>
public sealed record ChatErrorPayload(string Message);

/// <summary>Нагрузка события активности инструмента: имя инструмента, фаза вызова, опциональная деталь и опциональный идентификатор вызова.</summary>
public sealed record ChatToolPayload(string Name, string Phase, string? Detail = null, string? CallId = null);
