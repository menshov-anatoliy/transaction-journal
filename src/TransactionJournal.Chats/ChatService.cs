namespace TransactionJournal.Chats;

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using TransactionJournal.Chats.Ports;
using ChatMessage = TransactionJournal.Chats.Ports.ChatMessage;

/// <summary>
/// Конвейер сообщения чата агента: сообщение владельца без идентификатора
/// чата создаёт новый чат с выбранными параметрами, затем стримится ответ
/// ИИ-помощника, по завершении стрима ответ фиксируется в том же чате с
/// as-of момента фиксации и следом источников — какие инструменты
/// вызывались и с какими as-of их данные. Агенту передаётся история только
/// собственного чата: истории соседних чатов ему не видны. Рендер чанков,
/// троттлинг перерисовок и отмена — ответственность UI, конвейер отдаёт
/// поток обновлений модели как есть. Жизненный цикл чата — только ручные
/// действия владельца: завершение, продолжение, удаление целиком;
/// автоматического завершения нет.
// Traceability: openspec:chats/history#requirement-chat-flat-full-history
// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
// Traceability: openspec:chats/history#requirement-chat-manual-completion-and-deletion
/// </summary>
public sealed class ChatService
{
	private readonly IChatStore _store;

	private readonly IChatContextReader _contextReader;

	private readonly ChatAgent _agent;

	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт конвейер над хранилищем чатов, читателем снимка контекста и агентным циклом.</summary>
	/// <param name="store">Порт хранения чатов.</param>
	/// <param name="contextReader">Порт снимка контекста чата.</param>
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
		/// Завершает чат вручную владельца: чат уходит из списка активных в
		/// список завершённых, история сохраняется; автоматического завершения
		/// нет — статус меняет только это действие.
		// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
		/// </summary>
		/// <param name="chatId">Идентификатор завершаемого чата.</param>
		/// <param name="cancellationToken">Токен отмены.</param>
		public Task CompleteAsync(long chatId, CancellationToken cancellationToken = default) =>
			_store.CompleteChatAsync(chatId, cancellationToken);

		/// <summary>
		/// Продолжает завершённый чат: он возвращается в список активных ещё до
		/// отправки нового сообщения.
		// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
		/// </summary>
		/// <param name="chatId">Идентификатор продолжаемого чата.</param>
		/// <param name="cancellationToken">Токен отмены.</param>
		public Task ResumeAsync(long chatId, CancellationToken cancellationToken = default) =>
			_store.ResumeChatAsync(chatId, cancellationToken);

		/// <summary>
		/// Удаляет чат целиком со всеми сообщениями: явное действие владельца,
		/// корзины нет — восстановление невозможно.
		// Traceability: openspec:chats/history#scenario-chat-hard-delete
		/// </summary>
		/// <param name="chatId">Идентификатор удаляемого чата.</param>
		/// <param name="cancellationToken">Токен отмены.</param>
		public Task DeleteAsync(long chatId, CancellationToken cancellationToken = default) =>
			_store.DeleteChatAsync(chatId, cancellationToken);

		/// <summary>Активные чаты в порядке создания.</summary>
		/// <param name="cancellationToken">Токен отмены.</param>
		public Task<IReadOnlyList<ChatRecord>> ListActiveAsync(CancellationToken cancellationToken = default) =>
			_store.ListActiveChatsAsync(cancellationToken);

		/// <summary>Завершённые владельцем чаты в порядке создания.</summary>
		/// <param name="cancellationToken">Токен отмены.</param>
		public Task<IReadOnlyList<ChatRecord>> ListCompletedAsync(CancellationToken cancellationToken = default) =>
			_store.ListCompletedChatsAsync(cancellationToken);

	/// <summary>
	/// Фиксирует сообщение владельца и возвращает его с присвоенными ключами:
	/// сообщение без чата создаёт новый чат с переданными параметрами —
	/// модель, привязка (или её отсутствие), источники.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	/// </summary>
	/// <param name="chatId">Идентификатор чата; null — создаётся новый чат этим сообщением.</param>
	/// <param name="start">Параметры создания нового чата; обязательны при новом чате, запрещены для существующего.</param>
	/// <param name="question">Вопрос владельца.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Сохранённое сообщение владельца.</returns>
	public Task<ChatMessage> AppendUserMessageAsync(
		long? chatId,
		ChatStartParameters? start,
		string question,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(question);
		return _store.AppendMessageAsync(
			chatId,
			start,
			new ChatMessageDraft
			{
				Role = ChatMessageRole.User,
				Text = question.Trim(),
				AsOf = _timeProvider.GetUtcNow(),
			},
			cancellationToken);
	}

	/// <summary>
	/// Стримит ответ ИИ-помощника на зафиксированное сообщение владельца:
	/// читается чат и его собственная история до вопроса, снимок контекста
	/// собирается по привязке чата — с конструкцией или портфельный уровень,
	/// инструменты агентного цикла пишут след источников, по завершении
	/// стрима ответ фиксируется с as-of и следом. Отмена генерации оставляет
	/// чат без ответа помощника.
	// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	/// </summary>
	/// <param name="chatId">Идентификатор чата, в котором оставлено сообщение владельца.</param>
	/// <param name="userMessage">Зафиксированное сообщение владельца, на которое отвечает помощник.</param>
	/// <param name="cancellationToken">Токен отмены генерации.</param>
	/// <returns>Поток обновлений ответа: текстовые чанки и tool-вызовы.</returns>
	public async IAsyncEnumerable<ChatResponseUpdate> StreamAssistantAnswerAsync(
		long chatId,
		ChatMessage userMessage,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(userMessage);

		// Сообщение-якорь обязано принадлежать отвечаемому чату: рассинхрон
		// пары (чат, сообщение) означал бы ответ на чужой вопрос с записью
		// ответа в данный чат — нарушение изоляции историй, а не сценарий
		// конвейера.
		// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
		if (userMessage.ChatId != chatId)
		{
			throw new ArgumentException(
				$"Сообщение {userMessage.Id} принадлежит чату {userMessage.ChatId}, а не чату {chatId}.",
				nameof(userMessage));
		}

		// Чат — носитель параметров: привязка выбирает ветку снимка контекста,
		// отсутствующий чат означает, что отвечать некуда.
		var chat = await _store
			.FindChatAsync(chatId, cancellationToken)
			.ConfigureAwait(false)
			?? throw new InvalidOperationException($"Чат {chatId} не найден.");

		var allMessages = await _store
			.ListMessagesAsync(chatId, cancellationToken)
			.ConfigureAwait(false);

		// История чата — сообщения до вопроса: сам вопрос агент получает
		// отдельно вместе со снимком контекста; сообщения соседних чатов
		// хранилище в выборку не включает.
		// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
		var history = allMessages
			.TakeWhile(message => message.Id != userMessage.Id)
			.ToList();
		var snapshot = await _contextReader
			.ReadAsync(chat.ConstructionId, cancellationToken)
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
		// фиксации и след источников из накопителя агентного цикла.
		// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
		await _store.AppendMessageAsync(
			chatId,
			null,
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
