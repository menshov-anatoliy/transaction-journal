namespace TransactionJournal.Api.Sse;

/// <summary>
/// Событие SSE-канала чата агента: имя события протокола и JSON-нагрузка.
/// Транспортный конверт доменно-нейтрален: генератор чата (change
/// add-agent-chat) порождает поток событий, а SSE-слой доставляет их SPA.
/// </summary>
public sealed record ChatStreamEvent(string Name, object? Data = null);

/// <summary>
/// Типы событий протокола SSE-канала чата: начало, токен, завершение, ошибка.
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

	/// <summary>Событие начала канала.</summary>
	public static ChatStreamEvent Start() => new(StartName);

	/// <summary>Событие токена ответа.</summary>
	public static ChatStreamEvent Token(string text) => new(TokenName, new ChatTokenPayload(text));

	/// <summary>Событие завершения ответа.</summary>
	public static ChatStreamEvent Done() => new(DoneName);

	/// <summary>Событие ошибки генерации.</summary>
	public static ChatStreamEvent Error(string message) => new(ErrorName, new ChatErrorPayload(message));
}

/// <summary>Нагрузка события токена: порция текста ответа.</summary>
public sealed record ChatTokenPayload(string Text);

/// <summary>Нагрузка события ошибки: сообщение о сбое генерации.</summary>
public sealed record ChatErrorPayload(string Message);
