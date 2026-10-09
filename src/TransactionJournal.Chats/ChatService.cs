namespace TransactionJournal.Chats;

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using TransactionJournal.Chats.Ports;
using ChatMessage = TransactionJournal.Chats.Ports.ChatMessage;

/// <summary>
/// Конвейер сообщения чата: сообщение владельца фиксируется первым —
/// без идентификатора диалога оно создаёт диалог, затем стримится ответ
/// ассистента, по завершении стрима ответ фиксируется в диалоге с as-of
/// момента фиксации и рыночным следом — какие инструменты вызывались и с
/// какими as-of их данные. Рендер чанков, троттлинг перерисовок и отмена —
/// ответственность UI, конвейер отдаёт поток обновлений модели как есть.
// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
/// </summary>
public sealed class ChatService
{
	private readonly IChatStore _store;

	private readonly IChatContextReader _contextReader;

	private readonly ChatAgent _agent;

	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт конвейер над хранилищем, читателем снимка контекста и агентным циклом.</summary>
	/// <param name="store">Порт хранения чатов конструкции.</param>
	/// <param name="contextReader">Порт снимка контекста конструкции.</param>
	/// <param name="agent">Агентный цикл чата.</param>
	/// <param name="timeProvider">Поставщик времени as-of сообщений; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Какая-либо обязательная зависимость не задана.</exception>
	public ChatService(
		IChatStore store,
		IChatContextReader contextReader,
		ChatAgent agent,
		TimeProvider? timeProvider = null)
	{
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
		_agent = agent ?? throw new ArgumentNullException(nameof(agent));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <summary>
	/// Фиксирует сообщение владельца и возвращает его с присвоенными ключами:
	/// сообщение без диалога создаёт новый диалог чата.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="dialogueId">Идентификатор диалога; null — создаётся новый диалог этим сообщением.</param>
	/// <param name="question">Вопрос владельца.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Сохранённое сообщение владельца.</returns>
	public Task<ChatMessage> AppendUserMessageAsync(
		long constructionId,
		long? dialogueId,
		string question,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(question);
		return _store.AppendMessageAsync(
			constructionId,
			dialogueId,
			new ChatMessageDraft
			{
				Role = ChatMessageRole.User,
				Text = question.Trim(),
				AsOf = _timeProvider.GetUtcNow(),
			},
			cancellationToken);
	}

	/// <summary>
	/// Стримит ответ ассистента на зафиксированное сообщение владельца: история
	/// диалога читается до вопроса, инструменты агентного цикла пишут рыночный
	/// след, по завершении стрима ответ фиксируется с as-of и следом. Отмена
	/// генерации оставляет диалог без ответа ассистента.
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="userMessage">Зафиксированное сообщение владельца, на которое отвечает ассистент.</param>
	/// <param name="cancellationToken">Токен отмены генерации.</param>
	/// <returns>Поток обновлений ответа: текстовые чанки и tool-вызовы.</returns>
	public async IAsyncEnumerable<ChatResponseUpdate> StreamAssistantAnswerAsync(
		long constructionId,
		ChatMessage userMessage,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(userMessage);
		var allMessages = await _store
			.ListMessagesAsync(constructionId, userMessage.DialogueId, cancellationToken)
			.ConfigureAwait(false);

		// История диалога — сообщения до вопроса: сам вопрос агент получает
		// отдельно вместе со снимком контекста.
		var history = allMessages
			.TakeWhile(message => message.Id != userMessage.Id)
			.ToList();
		var snapshot = await _contextReader
			.ReadAsync(constructionId, cancellationToken)
			.ConfigureAwait(false);

		var traceRecorder = new ChatMarketTraceRecorder();
		var answer = new StringBuilder();
		await foreach (var update in _agent
			.StreamAnswerAsync(snapshot, history, userMessage.Text, traceRecorder, cancellationToken)
			.ConfigureAwait(false))
		{
			answer.Append(update.Text);
			yield return update;
		}

		// Ответ фиксируется по завершении стрима: текст целиком, as-of момента
		// фиксации и рыночный след из накопителя агентного цикла.
		// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
		await _store.AppendMessageAsync(
			constructionId,
			userMessage.DialogueId,
			new ChatMessageDraft
			{
				Role = ChatMessageRole.Assistant,
				Text = answer.ToString(),
				AsOf = _timeProvider.GetUtcNow(),
				MarketTrace = traceRecorder.Build(),
			},
			cancellationToken).ConfigureAwait(false);
	}
}
