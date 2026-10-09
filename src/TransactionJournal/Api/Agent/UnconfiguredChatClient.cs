namespace TransactionJournal.Api.Agent;

using Microsoft.Extensions.AI;

/// <summary>
/// Заглушка клиента модели на случай незастроенного API-ключа: резолв
/// зависимости не падает, история и служебные операции чатов доступны,
/// а обращение к модели завершается явной ошибкой, которая по SSE-каналу
/// доставляется событием ошибки.
/// </summary>
internal sealed class UnconfiguredChatClient : IChatClient
{
	private const string ErrorMessage =
		"API-ключ модели не настроен: задайте Llm:ApiKey в appsettings или переменной окружения.";

	/// <inheritdoc />
	public Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
		Task.FromException<ChatResponse>(new InvalidOperationException(ErrorMessage));

	/// <inheritdoc />
	public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException(ErrorMessage);

	/// <inheritdoc />
	public object? GetService(Type serviceType, object? serviceKey = null) => null;

	/// <inheritdoc />
	public void Dispose()
	{
		// Внешних ресурсов заглушка не держит — освобождать нечего.
	}
}
