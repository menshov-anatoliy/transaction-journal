namespace TransactionJournal.Api.Agent;

using System.Globalization;
using TransactionJournal.Api.Sse;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Hints.Corpus;

/// <summary>
/// Эндпоинты раздела «Агент»: чаты работают поверх доменного конвейера
/// TransactionJournal.Chats (change add-agent-chat) — единая SQLite-база
/// чатов, агентный цикл над keyed IChatClient модели, след источников
/// ответа; HTTP-слой хранит прежний контракт SPA (строковые идентификаторы,
/// SSE start/token/done/error) и отображает доменные записи в DTO. Рядом —
/// read-only каталог корпуса правил с фильтрами и поиском.
/// </summary>
// Traceability: openspec:chats/history#requirement-chat-flat-full-history
// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
public static class AgentEndpoints
{
	/// <summary>Подключает эндпоинты раздела «Агент» к версионированной группе API.</summary>
	public static RouteGroupBuilder MapAgentEndpoints(this RouteGroupBuilder api)
	{
		var chats = api.MapGroup("/chats").WithTags("Агент: чаты");

		// История чатов раздела «Агент» идёт через единый API одного хоста.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Списки active/completed и ручные продолжить/завершить закреплены
		// концепцией раздела «Агент».
		// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
		chats.MapGet("/", async (
			string status,
			ChatService pipeline,
			IChatStore store,
			AgentRulesCatalog catalog,
			CancellationToken cancellationToken) =>
		{
			if (TryParseStatus(status, out var parsed) == false)
				return Results.Json(new AgentChatErrorResponse("Неизвестный статус списка чатов."), statusCode: StatusCodes.Status400BadRequest);

			// Списки берутся у конвейера; момент последнего сообщения чата —
			// as-of его последнего сообщения: отдельного поля хранилище
			// не ведёт, а список чатов мал, и лишнее чтение истории дёшево.
			var records = parsed == ChatStatus.Active
				? await pipeline.ListActiveAsync(cancellationToken)
				: await pipeline.ListCompletedAsync(cancellationToken);
			var items = new List<AgentChatDto>(records.Count);
			foreach (var record in records)
			{
				var messages = await store.ListMessagesAsync(record.Id, cancellationToken);
				items.Add(ToChatDto(record, messages));
			}

			return Results.Json(items);
		});

		// Чат создаётся первым сообщением владельца вместе с параметрами:
		// модель без выбора получает дефолт подсекции Llm:Chat, привязка к
		// конструкции и набор источников фиксируются при создании.
		// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
		// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
		chats.MapPost("/", async (
			CreateAgentChatRequest request,
			ChatService pipeline,
			IChatStore store,
			ChatModelOptions modelOptions,
			CancellationToken cancellationToken) =>
		{
			if (ValidateCreateRequest(request, out var error))
				return Results.Json(new AgentChatErrorResponse(error), statusCode: StatusCodes.Status400BadRequest);
			if (TryParseConstructionId(request.Params.ConstructionId, out var constructionId, out var constructionError) == false)
				return Results.Json(new AgentChatErrorResponse(constructionError), statusCode: StatusCodes.Status400BadRequest);

			var start = new ChatStartParameters
			{
				Model = string.IsNullOrWhiteSpace(request.Params.Model) ? modelOptions.Model : request.Params.Model.Trim(),
				ConstructionId = constructionId,
				Sources = ParseSources(request.Params.Sources),
			};
			var userMessage = await pipeline.AppendUserMessageAsync(null, start, request.Text.Trim(), cancellationToken);
			var chat = await store.FindChatAsync(userMessage.ChatId, cancellationToken);
			return Results.Json(ToChatDto(chat!, [userMessage]));
		});

		// Полная история одного чата: истории соседних чатов в выборку не
		// попадают, след источников ответов ассистента переносится в DTO.
		// Traceability: openspec:chats/history#requirement-chat-message-composition
		chats.MapGet("/{chatId}/messages", async (
			string chatId,
			IChatStore store,
			AgentRulesCatalog catalog,
			CancellationToken cancellationToken) =>
		{
			if (TryParseChatId(chatId, out var id) == false)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			if (await store.FindChatAsync(id, cancellationToken) is null)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			var messages = await store.ListMessagesAsync(id, cancellationToken);
			return Results.Json(messages.Select(message => ToMessageDto(message, catalog)).ToArray());
		});

		// Стриминг ответа настоящего ИИ-конвейера: сообщение владельца
		// фиксируется до генерации, токены уходят в SSE по мере стрима,
		// конвейер сам фиксирует ответ ассистента со следом источников по
		// завершении; done несёт финальное сообщение с тем же следом.
		// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
		// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
		chats.MapPost("/{chatId}/messages/stream", async (
			string chatId,
			ChatMessageStreamRequest request,
			ChatService pipeline,
			IChatStore store,
			AgentRulesCatalog catalog,
			HttpContext http,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.Text))
			{
				http.Response.StatusCode = StatusCodes.Status400BadRequest;
				await http.Response.WriteAsJsonAsync(new AgentChatErrorResponse("Текст сообщения не задан."), cancellationToken);
				return;
			}

			if (TryParseChatId(chatId, out var id) == false || await store.FindChatAsync(id, cancellationToken) is not { } chat)
			{
				http.Response.StatusCode = StatusCodes.Status404NotFound;
				await http.Response.WriteAsJsonAsync(new AgentChatNotFoundResponse(chatId), cancellationToken);
				return;
			}

			// Модель в теле запроса — текущий выбор владельца: отличие от
			// параметра чата применяет смену на лету тем же действием, без
			// отдельного вызова PUT /model; история не переписывается.
			// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
			var requestedModel = request.Model?.Trim();
			if (string.IsNullOrWhiteSpace(requestedModel) == false &&
				string.Equals(chat.Model, requestedModel, StringComparison.Ordinal) == false)
			{
				await pipeline.ChangeModelAsync(id, requestedModel, cancellationToken);
			}

			var userMessage = await pipeline.AppendUserMessageAsync(id, null, request.Text.Trim(), cancellationToken);

			// Сбой генерации (включая незастроенный ключ модели) доставляется
			// транспортным событием ошибки того же соединения: история чата
			// с зафиксированным вопросом владельца сохраняется.
			// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
			async IAsyncEnumerable<ChatStreamEvent> Events()
			{
				await foreach (var update in pipeline.StreamAssistantAnswerAsync(id, userMessage, cancellationToken))
				{
					if (string.IsNullOrEmpty(update.Text) == false)
						yield return ChatStreamEvents.Token(update.Text);
				}

				// Ответ ассистента уже зафиксирован конвейером: финальное
				// сообщение перечитывается из истории как последнее.
				var messages = await store.ListMessagesAsync(id, cancellationToken);
				var assistant = messages[^1];
				yield return new ChatStreamEvent(ChatStreamEvents.DoneName, new { message = ToMessageDto(assistant, catalog) });
			}

			await ChatSseStream.WriteStreamAsync(http.Response, Events(), cancellationToken);
		});

		// Ручное завершение чата владельцем: активный чат уходит в завершённые,
		// история сохраняется; автоматики завершения нет.
		// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
		chats.MapPost("/{chatId}/completion", async (
			string chatId,
			ChatService pipeline,
			IChatStore store,
			AgentRulesCatalog catalog,
			CancellationToken cancellationToken) =>
		{
			if (TryParseChatId(chatId, out var id) == false || await store.FindChatAsync(id, cancellationToken) is null)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			await pipeline.CompleteAsync(id, cancellationToken);
			var chat = await store.FindChatAsync(id, cancellationToken);
			var messages = await store.ListMessagesAsync(id, cancellationToken);
			return Results.Json(ToChatDto(chat!, messages));
		});

		// Продолжение завершённого чата: возвращается в активные ещё до
		// отправки нового сообщения.
		// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
		chats.MapDelete("/{chatId}/completion", async (
			string chatId,
			ChatService pipeline,
			IChatStore store,
			AgentRulesCatalog catalog,
			CancellationToken cancellationToken) =>
		{
			if (TryParseChatId(chatId, out var id) == false || await store.FindChatAsync(id, cancellationToken) is null)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			await pipeline.ResumeAsync(id, cancellationToken);
			var chat = await store.FindChatAsync(id, cancellationToken);
			var messages = await store.ListMessagesAsync(id, cancellationToken);
			return Results.Json(ToChatDto(chat!, messages));
		});

		// Смена модели на лету: параметр чата обновляется самим действием
		// владельца, история остаётся как есть — последующие сообщения уходят
		// выбранной модели.
		// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
		chats.MapPut("/{chatId}/model", async (
			string chatId,
			ChangeAgentChatModelRequest request,
			ChatService pipeline,
			IChatStore store,
			AgentRulesCatalog catalog,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.Model))
				return Results.Json(new AgentChatErrorResponse("Модель чата не задана."), statusCode: StatusCodes.Status400BadRequest);
			if (TryParseChatId(chatId, out var id) == false || await store.FindChatAsync(id, cancellationToken) is null)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			var chat = await pipeline.ChangeModelAsync(id, request.Model.Trim(), cancellationToken);
			var messages = await store.ListMessagesAsync(id, cancellationToken);
			return Results.Json(ToChatDto(chat, messages));
		});

		// Удаление привязанного чата убирает всю историю, а не только его статус.
		// Traceability: openspec:chats/history#scenario-chat-hard-delete
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		chats.MapDelete("/{chatId}", async (
			string chatId,
			ChatService pipeline,
			IChatStore store,
			CancellationToken cancellationToken) =>
		{
			if (TryParseChatId(chatId, out var id) == false || await store.FindChatAsync(id, cancellationToken) is null)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			await pipeline.DeleteAsync(id, cancellationToken);
			return Results.NoContent();
		});

		var rules = api.MapGroup("/rules").WithTags("Агент: корпус правил");

		// Каталог корпуса правил в разделе «Агент»: read-only фильтры и поиск
		// поверх канонических YAML-файлов rules/.
		// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
		// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
		rules.MapGet("/", (string? character, string? clarity, string? subject, string? search, AgentRulesCatalog catalog) =>
		{
			var items = catalog.List(character, clarity, subject, search).Select(rule => new AgentRuleListItemResponse(
				rule.Id,
				rule.Title,
				rule.Character,
				rule.Clarity,
				rule.Subject))
				.ToArray();
			return Results.Json(new AgentRulesListResponse(items, items.Length));
		});

		rules.MapGet("/{ruleId}", (string ruleId, AgentRulesCatalog catalog) =>
		{
			if (catalog.TryRead(ruleId, out var card) == false)
				return Results.Json(new AgentRuleNotFoundResponse(ruleId), statusCode: StatusCodes.Status404NotFound);

			return Results.Json(new AgentRuleCardResponse(
				card!.Id,
				card.Title,
				card.Character,
				card.Clarity,
				card.Subject,
				card.Status,
				card.Scope,
				card.Technique,
				card.TriggerDescription,
				card.ActionDescription,
				card.Thresholds.Select(threshold => new AgentRuleThresholdResponse(threshold.Name, threshold.Value, threshold.Unit)).ToArray(),
				card.Sources.Select(source => new AgentRuleSourceResponse(source.Tag, source.File, source.Quotes)).ToArray()));
		});

		return api;
	}

	private static bool ValidateCreateRequest(CreateAgentChatRequest request, out string error)
	{
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(request.Text))
		{
			error = "Текст первого сообщения обязателен.";
			return true;
		}

		// Модель при создании необязательна: чат без выбора модели получает
		// дефолт из подсекции Llm:Chat (GLM-5.3), проверяется только текст и
		// непустой набор источников.
		// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
		if (request.Params.Sources.Count == 0)
		{
			error = "Выберите хотя бы один источник данных.";
			return true;
		}

		return false;
	}

	private static AgentChatDto ToChatDto(ChatRecord chat, IReadOnlyList<ChatMessage> messages)
	{
		// Момент последнего сообщения — as-of последнего сообщения истории;
		// у только что созданного чата это его первое сообщение владельца.
		var lastMessageAt = messages.Count == 0 ? chat.CreatedAt : messages[^1].AsOf;
		return new AgentChatDto(
			chat.Id.ToString(CultureInfo.InvariantCulture),
			chat.Status == ChatStatus.Active ? "active" : "completed",
			new AgentChatParams(
				chat.Model,
				chat.ConstructionId?.ToString(CultureInfo.InvariantCulture),
				[.. chat.Sources.Select(ToWireSource)]),
			chat.CreatedAt,
			lastMessageAt);
	}

	private static AgentChatMessageDto ToMessageDto(ChatMessage message, AgentRulesCatalog catalog)
	{
		if (message.SourceTrace is not { } trace)
			return new AgentChatMessageDto(
				message.Id.ToString(CultureInfo.InvariantCulture),
				ToWireRole(message.Role),
				message.Text,
				message.AsOf,
				null);

		// Вызовы инструментов переносятся как есть: as-of данных инструмента
		// домен выражает, пометку деградации — нет, поле остаётся пустым.
		var toolCalls = trace.Invocations
			.Select(invocation => new AgentSourceToolCall(invocation.ToolName, invocation.Arguments, invocation.DataAsOf, null))
			.ToArray();

		// Прочитанные карточки правил — ссылки «правило»: заголовок берётся из
		// каталога корпуса, as-of ссылки — момент фиксации ответа ассистента.
		var references = new List<AgentSourceReference>(trace.RuleCards.Count + 1);
		foreach (var cardId in trace.RuleCards)
		{
			catalog.TryRead(cardId, out var card);
			references.Add(new AgentSourceReference("rule-card", cardId, card?.Title ?? cardId, message.AsOf));
		}

		// As-of данных журнала в снимке — ссылка «журнал»: происхождение фактов
		// ответа проверяемо постфактум.
		if (trace.JournalAsOf is { } journalAsOf)
			references.Add(new AgentSourceReference("journal", "journal", "Журнал сделок", journalAsOf));

		return new AgentChatMessageDto(
			message.Id.ToString(CultureInfo.InvariantCulture),
			ToWireRole(message.Role),
			message.Text,
			message.AsOf,
			new AgentSourceTrace(toolCalls, references));
	}

	private static string ToWireRole(ChatMessageRole role) => role switch
	{
		ChatMessageRole.User => "user",
		ChatMessageRole.Assistant => "assistant",
		_ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
	};

	private static string ToWireSource(ChatDataSource source) => source switch
	{
		ChatDataSource.Journal => "journal",
		ChatDataSource.RulesCorpus => "rules-corpus",
		ChatDataSource.BybitMarket => "market",
		_ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
	};

	/// <summary>
	/// Набор источников провода отображается в закрытый справочник домена:
	/// неизвестные категории отбрасываются, пустой выбор разворачивается в
	/// полный справочник — дефолт чата, все три категории.
	/// </summary>
	// Traceability: openspec:chats/sources#scenario-sources-three-categories
	private static IReadOnlyList<ChatDataSource> ParseSources(IEnumerable<string>? raw)
	{
		var parsed = new List<ChatDataSource>();
		foreach (var item in raw ?? [])
		{
			switch (item.Trim())
			{
				case "journal":
					parsed.Add(ChatDataSource.Journal);
					break;
				case "rules-corpus":
					parsed.Add(ChatDataSource.RulesCorpus);
					break;
				case "market":
					parsed.Add(ChatDataSource.BybitMarket);
					break;
			}
		}

		// Пустой выбор нормализуется полным справочником источников.
		// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
		return parsed.Count == 0 ? ChatDataSourceCatalog.All : parsed;
	}

	private static bool TryParseStatus(string raw, out ChatStatus status)
	{
		status = ChatStatus.Active;
		if (string.Equals(raw, "active", StringComparison.Ordinal))
		{
			status = ChatStatus.Active;
			return true;
		}

		if (string.Equals(raw, "completed", StringComparison.Ordinal))
		{
			status = ChatStatus.Completed;
			return true;
		}

		return false;
	}

	/// <summary>Идентификатор чата в проводе — строка десятичного ключа домена.</summary>
	private static bool TryParseChatId(string raw, out long chatId) =>
		long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out chatId);

	/// <summary>Привязка к конструкции в проводе опциональна; непустое значение обязано быть ключом журнала.</summary>
	private static bool TryParseConstructionId(string? raw, out long? constructionId, out string error)
	{
		constructionId = null;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(raw))
			return true;
		if (long.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) == false)
		{
			error = "Идентификатор конструкции должен быть числом.";
			return false;
		}

		constructionId = parsed;
		return true;
	}
}

public sealed record CreateAgentChatRequest(string Text, AgentChatParams Params);

public sealed record ChangeAgentChatModelRequest(string Model);

public sealed record ChatMessageStreamRequest(string Text, string? Model);

public sealed record AgentChatDto(
	string Id,
	string Status,
	AgentChatParams Params,
	DateTimeOffset CreatedAt,
	DateTimeOffset LastMessageAt);

public sealed record AgentChatMessageDto(
	string Id,
	string Role,
	string Text,
	DateTimeOffset AsOf,
	AgentSourceTrace? SourceTrace);

public sealed record AgentChatNotFoundResponse(string ChatId);

public sealed record AgentChatErrorResponse(string Error);

public sealed record AgentRuleListItemResponse(
	string Id,
	string Title,
	string Character,
	string Clarity,
	string Subject);

public sealed record AgentRulesListResponse(
	IReadOnlyList<AgentRuleListItemResponse> Items,
	int Total);

public sealed record AgentRuleThresholdResponse(string Name, string Value, string Unit);

public sealed record AgentRuleSourceResponse(string Tag, string File, IReadOnlyList<string> Quotes);

public sealed record AgentRuleCardResponse(
	string Id,
	string Title,
	string Character,
	string Clarity,
	string Subject,
	string Status,
	string Scope,
	string? Technique,
	string? TriggerDescription,
	string? ActionDescription,
	IReadOnlyList<AgentRuleThresholdResponse> Thresholds,
	IReadOnlyList<AgentRuleSourceResponse> Sources);

public sealed record AgentRuleNotFoundResponse(string RuleId);

public sealed record AgentChatParams(string Model, string? ConstructionId, IReadOnlyList<string> Sources);

public sealed record AgentSourceTrace(
	IReadOnlyList<AgentSourceToolCall> ToolCalls,
	IReadOnlyList<AgentSourceReference> References);

public sealed record AgentSourceToolCall(string Tool, string Argument, DateTimeOffset? AsOf, bool? Degraded);

public sealed record AgentSourceReference(string Kind, string Id, string Title, DateTimeOffset AsOf);

public sealed class AgentRulesCatalog
{
	private readonly RulesCorpusLoader _loader;

	public AgentRulesCatalog(RulesCorpusLoader loader)
	{
		_loader = loader;
	}

	public IReadOnlyList<AgentRuleCard> List(string? character, string? clarity, string? subject, string? search)
	{
		var cards = LoadCards();
		if (string.IsNullOrWhiteSpace(character) == false)
			cards = cards.Where(card => string.Equals(card.Character, character, StringComparison.Ordinal));
		if (string.IsNullOrWhiteSpace(clarity) == false)
			cards = cards.Where(card => string.Equals(card.Clarity, clarity, StringComparison.Ordinal));
		if (string.IsNullOrWhiteSpace(subject) == false)
			cards = cards.Where(card => string.Equals(card.Subject, subject, StringComparison.Ordinal));
		if (string.IsNullOrWhiteSpace(search) == false)
		{
			var query = search.Trim();
			cards = cards.Where(card =>
				card.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| card.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| (card.TriggerDescription?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
				|| (card.ActionDescription?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
		}

		return cards.OrderBy(card => card.Id, StringComparer.Ordinal).ToArray();
	}

	public bool TryRead(string ruleId, out AgentRuleCard? card)
	{
		card = LoadCards().FirstOrDefault(candidate => string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
		return card is not null;
	}

	private IEnumerable<AgentRuleCard> LoadCards()
	{
		var snapshot = _loader.Load();
		return snapshot.ExecutableCards
			.Concat(snapshot.UnimplementedCards)
			.Concat(snapshot.RetiredCards)
			.Select(ToCard);
	}

	private static AgentRuleCard ToCard(RuleCard card) => new(
		card.Id,
		card.Title,
		card.Character,
		card.Clarity == RuleClarity.Crisp ? "crisp" : "fuzzy",
		card.Scope == RuleScope.OpenConstructions ? "construction" : "portfolio",
		card.Status == RuleCardStatus.Active ? "active" : "retired",
		card.Scope == RuleScope.OpenConstructions ? "open-constructions" : "portfolio",
		card.Technique,
		card.TriggerDescription,
		card.ActionDescription,
		card.Thresholds,
		card.Sources);
}

public sealed record AgentRuleCard(
	string Id,
	string Title,
	string Character,
	string Clarity,
	string Subject,
	string Status,
	string Scope,
	string? Technique,
	string? TriggerDescription,
	string? ActionDescription,
	IReadOnlyList<RuleThreshold> Thresholds,
	IReadOnlyList<RuleSource> Sources);
