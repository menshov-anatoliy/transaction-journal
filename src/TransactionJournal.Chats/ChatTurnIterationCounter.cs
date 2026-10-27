namespace TransactionJournal.Chats;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

/// <summary>
/// Аккумулятор одного хода чата: число запросов модели, дошедших до клиента
/// модели внутри конвейера ChatAgent. Ход — это один вызов
/// StreamAssistantAnswerAsync, поэтому счётчик создаётся на ход и читается
/// из ChatService при закрытии корневого спана chat_turn; отдельных событий
/// итераций трасса не создаёт — задержки читаются из длительностей
/// встроенных спанов chat {model}.
// Traceability: openspec:ops/agent-tracing#scenario-trace-chat-turn-root
/// </summary>
public sealed class ChatTurnIterationCounter
{
	private int _count;

	/// <summary>Число запросов модели, дошедших до клиента модели в этом ходе.</summary>
	public int Count => _count;

	/// <summary>Регистрирует один запрос модели; вызывается из внутреннего слоя конвейера.</summary>
	public void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>
/// Считающий декоратор клиента модели: самый внутренний слой конвейера
/// ChatAgent — каждый запрос агентного цикла, включая повторные итерации
/// FunctionInvokingChatClient, проходит через него ровно один раз.
/// Отдельных событий итераций не создаёт — только инкремент аккумулятора.
// Traceability: openspec:ops/agent-tracing#scenario-trace-chat-turn-root
/// </summary>
internal sealed class ModelRequestCountingChatClient(IChatClient inner, ChatTurnIterationCounter counter) : IChatClient
{
	public Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		counter.Increment();
		return inner.GetResponseAsync(messages, options, cancellationToken);
	}

	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		counter.Increment();
		await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
		{
			yield return update;
		}
	}

	public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);

	// Владельцем клиента модели конвейер не является: внутренний клиент
	// живёт дольше хода и освобождается своей точкой регистрации.
	public void Dispose()
	{
	}
}
