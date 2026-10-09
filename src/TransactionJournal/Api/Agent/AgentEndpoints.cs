namespace TransactionJournal.Api.Agent;

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using TransactionJournal.Api.Sse;
using TransactionJournal.Chats;
using TransactionJournal.Hints.Corpus;

/// <summary>
/// Эндпоинты раздела «Агент»: история чатов с жизненным циклом active/completed,
/// стриминг ответа в SSE и read-only каталог корпуса правил с фильтрами и поиском.
/// Доменный контур чатов из change add-agent-chat ещё развивается, поэтому
/// здесь реализован тонкий прикладной слой без новой доменной логики.
/// </summary>
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
		// Traceability: change:add-agent-chat/proposal#what-changes
		chats.MapGet("/", (string status, AgentChatStore store) =>
		{
			if (TryParseStatus(status, out var parsed) == false)
				return Results.Json(new AgentChatErrorResponse("Неизвестный статус списка чатов."), statusCode: StatusCodes.Status400BadRequest);
			return Results.Json(store.List(parsed).Select(ToChatDto).ToArray());
		});

		chats.MapPost("/", (CreateAgentChatRequest request, AgentChatStore store) =>
		{
			if (ValidateCreateRequest(request, out var error))
				return Results.Json(new AgentChatErrorResponse(error), statusCode: StatusCodes.Status400BadRequest);
			var created = store.Create(request.Text.Trim(), request.Params);
			return Results.Json(ToChatDto(created));
		});

		chats.MapGet("/{chatId}/messages", (string chatId, AgentChatStore store) =>
		{
			if (store.TryGet(chatId, out var chat) == false)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			return Results.Json(chat!.Messages.Select(ToMessageDto).ToArray());
		});

		chats.MapPost("/{chatId}/messages/stream", async (
			string chatId,
			ChatMessageStreamRequest request,
			AgentChatStore store,
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

			if (store.TryGet(chatId, out var chat) == false)
			{
				http.Response.StatusCode = StatusCodes.Status404NotFound;
				await http.Response.WriteAsJsonAsync(new AgentChatNotFoundResponse(chatId), cancellationToken);
				return;
			}

			var userMessage = store.AppendUserMessage(chatId, request.Text.Trim(), request.Model);
			var assistant = AgentAssistantReply.Compose(chat!, request.Text.Trim(), catalog, DateTimeOffset.UtcNow);

			// SSE-стрим задачи 4.1: start/token/done/error по протоколу API 2.1.
			// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
			// Тонкая деградация: пока доменный конвейер add-agent-chat не подключён,
			// раздел отвечает детерминированным read-only резюме по выбранным
			// источникам и сохраняет след источников.
			// Traceability: change:add-agent-chat/proposal#what-changes
			async IAsyncEnumerable<ChatStreamEvent> Events()
			{
				yield return ChatStreamEvents.Start();
				foreach (var token in AgentAssistantReply.Tokenize(assistant.Text))
				{
					yield return ChatStreamEvents.Token(token);
					await Task.Delay(6, cancellationToken);
				}

				var saved = store.AppendAssistantMessage(chatId, assistant.Text, assistant.Trace, request.Model, userMessage.AsOf);
				yield return new ChatStreamEvent(ChatStreamEvents.DoneName, new { message = ToMessageDto(saved) });
			}

			await ChatSseStream.WriteStreamAsync(http.Response, Events(), cancellationToken);
		});

		chats.MapPost("/{chatId}/completion", (string chatId, AgentChatStore store) =>
		{
			if (store.TrySetStatus(chatId, AgentChatStatus.Completed, out var chat) == false)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			return Results.Json(ToChatDto(chat!));
		});

		chats.MapDelete("/{chatId}/completion", (string chatId, AgentChatStore store) =>
		{
			if (store.TrySetStatus(chatId, AgentChatStatus.Active, out var chat) == false)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			return Results.Json(ToChatDto(chat!));
		});

		// Смена модели на лету: параметр чата обновляется самим действием
		// владельца, история остаётся как есть — последующие сообщения уходят
		// выбранной модели.
		// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
		chats.MapPut("/{chatId}/model", (string chatId, ChangeAgentChatModelRequest request, AgentChatStore store) =>
		{
			if (string.IsNullOrWhiteSpace(request.Model))
				return Results.Json(new AgentChatErrorResponse("Модель чата не задана."), statusCode: StatusCodes.Status400BadRequest);
			if (store.TryChangeModel(chatId, request.Model, out var chat) == false)
				return Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound);
			return Results.Json(ToChatDto(chat!));
		});

		// Удаление привязанного чата убирает всю историю, а не только его статус.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		chats.MapDelete("/{chatId}", (string chatId, AgentChatStore store) =>
			store.TryDelete(chatId)
				? Results.NoContent()
				: Results.Json(new AgentChatNotFoundResponse(chatId), statusCode: StatusCodes.Status404NotFound));

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

	private static AgentChatDto ToChatDto(AgentChat chat) => new(
		chat.Id,
		chat.Status == AgentChatStatus.Active ? "active" : "completed",
		chat.Params,
		chat.CreatedAt,
		chat.LastMessageAt);

	private static AgentChatMessageDto ToMessageDto(AgentChatMessage message) => new(
		message.Id,
		message.Role,
		message.Text,
		message.AsOf,
		message.SourceTrace);

	private static bool TryParseStatus(string raw, out AgentChatStatus status)
	{
		status = AgentChatStatus.Active;
		if (string.Equals(raw, "active", StringComparison.Ordinal))
		{
			status = AgentChatStatus.Active;
			return true;
		}

		if (string.Equals(raw, "completed", StringComparison.Ordinal))
		{
			status = AgentChatStatus.Completed;
			return true;
		}

		return false;
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

public enum AgentChatStatus
{
	Active,
	Completed,
}

public sealed record AgentChatMessage(
	string Id,
	string Role,
	string Text,
	DateTimeOffset AsOf,
	string Model,
	AgentSourceTrace? SourceTrace);

public sealed class AgentChat
{
	public required string Id { get; init; }

	public required AgentChatStatus Status { get; set; }

	public required AgentChatParams Params { get; set; }

	public required DateTimeOffset CreatedAt { get; init; }

	public required DateTimeOffset LastMessageAt { get; set; }

	public required List<AgentChatMessage> Messages { get; init; }
}

/// <summary>
/// Хранилище чатов раздела «Агент»: минимальная прикладная персистентность
/// в JSON-файле App_Data без доменного расширения.
/// </summary>
public sealed class AgentChatStore
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = false,
	};

	private readonly object _sync = new();

	private readonly string _storagePath;

	private readonly List<AgentChat> _chats;

	/// <summary>Дефолт модели чата из подсекции Llm:Chat:Model; без опций — встроенный GLM-5.3.</summary>
	private readonly string _defaultModel;

	public AgentChatStore(string dataDirectory, ChatModelOptions? chatModelOptions = null)
	{
		Directory.CreateDirectory(dataDirectory);
		_storagePath = Path.Combine(dataDirectory, "agent-chats.json");
		_chats = Load(_storagePath);
		_defaultModel = chatModelOptions?.Model ?? ChatModelOptions.DefaultModel;
	}

	public IReadOnlyList<AgentChat> List(AgentChatStatus status)
	{
		lock (_sync)
		{
			return _chats
				.Where(chat => chat.Status == status)
				.OrderByDescending(chat => chat.LastMessageAt)
				.ToArray();
		}
	}

	public bool TryGet(string chatId, out AgentChat? chat)
	{
		lock (_sync)
		{
			chat = _chats.FirstOrDefault(item => string.Equals(item.Id, chatId, StringComparison.Ordinal));
			return chat is not null;
		}
	}

	public AgentChat Create(string firstMessageText, AgentChatParams rawParams)
	{
		lock (_sync)
		{
			var now = DateTimeOffset.UtcNow;
			var normalized = NormalizeParams(rawParams);
			var chat = new AgentChat
			{
				Id = $"chat-{Guid.NewGuid():N}",
				Status = AgentChatStatus.Active,
				Params = normalized,
				CreatedAt = now,
				LastMessageAt = now,
				Messages =
				[
					new AgentChatMessage(
						Id: $"msg-{Guid.NewGuid():N}",
						Role: "user",
						Text: firstMessageText,
						AsOf: now,
						Model: normalized.Model,
						SourceTrace: null),
				],
			};
			_chats.Add(chat);
			Persist();
			return chat;
		}
	}

	public AgentChatMessage AppendUserMessage(string chatId, string text, string? model)
	{
		lock (_sync)
		{
			var chat = _chats.First(item => item.Id == chatId);
			if (chat.Status == AgentChatStatus.Completed)
				chat.Status = AgentChatStatus.Active;
			var now = DateTimeOffset.UtcNow;
			var resolvedModel = string.IsNullOrWhiteSpace(model) ? chat.Params.Model : model.Trim();
			var next = new AgentChatMessage($"msg-{Guid.NewGuid():N}", "user", text, now, resolvedModel, null);
			chat.Messages.Add(next);
			chat.LastMessageAt = now;
			chat.Params = chat.Params with { Model = resolvedModel };
			Persist();
			return next;
		}
	}

	public AgentChatMessage AppendAssistantMessage(string chatId, string text, AgentSourceTrace trace, string? model, DateTimeOffset asOf)
	{
		lock (_sync)
		{
			var chat = _chats.First(item => item.Id == chatId);
			var now = DateTimeOffset.UtcNow;
			var resolvedModel = string.IsNullOrWhiteSpace(model) ? chat.Params.Model : model.Trim();
			var next = new AgentChatMessage($"msg-{Guid.NewGuid():N}", "assistant", text, asOf, resolvedModel, trace);
			chat.Messages.Add(next);
			chat.LastMessageAt = now;
			chat.Params = chat.Params with { Model = resolvedModel };
			Persist();
			return next;
		}
	}

	public bool TrySetStatus(string chatId, AgentChatStatus status, out AgentChat? chat)
	{
		lock (_sync)
		{
			chat = _chats.FirstOrDefault(item => item.Id == chatId);
			if (chat is null)
				return false;
			chat.Status = status;
			Persist();
			return true;
		}
	}

	/// <summary>
	/// Смена модели существующего чата на лету: меняется только параметр чата,
	/// история сообщений не трогается — последующие сообщения уходят новой
	/// модели. Неизвестный идентификатор даёт отказ без изменений.
	// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
	/// </summary>
	public bool TryChangeModel(string chatId, string model, out AgentChat? chat)
	{
		lock (_sync)
		{
			chat = _chats.FirstOrDefault(item => item.Id == chatId);
			if (chat is null)
				return false;
			chat.Params = chat.Params with { Model = model.Trim() };
			Persist();
			return true;
		}
	}

	/// <summary>Удаляет чат вместе с сообщениями и сохраняет результат в существующем хранилище.</summary>
	// Удалённый чат не должен восстанавливаться при повторном чтении JSON.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public bool TryDelete(string chatId)
	{
		lock (_sync)
		{
			var index = _chats.FindIndex(chat => string.Equals(chat.Id, chatId, StringComparison.Ordinal));
			if (index < 0)
				return false;
			Persist(_chats.Where(chat => string.Equals(chat.Id, chatId, StringComparison.Ordinal) == false));
			_chats.RemoveAt(index);
			return true;
		}
	}

	private void Persist(IEnumerable<AgentChat>? chats = null) =>
		File.WriteAllText(_storagePath, JsonSerializer.Serialize(chats ?? _chats, JsonOptions));

	private static List<AgentChat> Load(string path)
	{
		if (File.Exists(path) == false)
			return [];
		try
		{
			return JsonSerializer.Deserialize<List<AgentChat>>(File.ReadAllText(path), JsonOptions) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	/// <summary>
	/// Параметры создания нормализуются: модель без выбора владельца получает
	/// дефолт из подсекции Llm:Chat:Model (GLM-5.3), источники — подмножество
	/// закрытого справочника, пустой выбор разворачивается во все три.
	// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
	/// </summary>
	private AgentChatParams NormalizeParams(AgentChatParams raw)
	{
		var model = string.IsNullOrWhiteSpace(raw.Model) ? _defaultModel : raw.Model.Trim();
		var constructionId = string.IsNullOrWhiteSpace(raw.ConstructionId) ? null : raw.ConstructionId!.Trim();
		var sources = raw.Sources
			.Select(source => source.Trim())
			.Where(source => source is "journal" or "rules-corpus" or "market")
			.Distinct(StringComparer.Ordinal)
			.ToImmutableArray();
		var normalizedSources = sources.Length == 0
			? ImmutableArray.Create("journal", "rules-corpus", "market")
			: sources;
		return new AgentChatParams(model, constructionId, normalizedSources);
	}
}

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

internal static class AgentAssistantReply
{
	private static readonly Regex RuleIdRegex = new(@"\bac-\d{2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public static (string Text, AgentSourceTrace Trace) Compose(
		AgentChat chat,
		string prompt,
		AgentRulesCatalog catalog,
		DateTimeOffset asOf)
	{
		var tools = new List<AgentSourceToolCall>();
		var references = new List<AgentSourceReference>();

		if (chat.Params.Sources.Contains("journal", StringComparer.Ordinal))
			tools.Add(new AgentSourceToolCall("read_journal_snapshot", chat.Params.ConstructionId ?? "portfolio", asOf, null));
		if (chat.Params.Sources.Contains("rules-corpus", StringComparer.Ordinal))
			tools.Add(new AgentSourceToolCall("read_rule_card", "search", asOf, null));
		if (chat.Params.Sources.Contains("market", StringComparer.Ordinal))
			tools.Add(new AgentSourceToolCall("get_market_snapshot", "bybit", asOf, true));

		if (chat.Params.Sources.Contains("rules-corpus", StringComparer.Ordinal))
		{
			var matchedRuleId = RuleIdRegex.Match(prompt);
			if (matchedRuleId.Success && catalog.TryRead(matchedRuleId.Value.ToLowerInvariant(), out var matchedRule))
			{
				references.Add(new AgentSourceReference("rule-card", matchedRule!.Id, matchedRule.Title, asOf));
			}
			else
			{
				var first = catalog.List(null, null, null, null).FirstOrDefault();
				if (first is not null)
					references.Add(new AgentSourceReference("rule-card", first.Id, first.Title, asOf));
			}
		}

		var scope = chat.Params.ConstructionId is null ? "портфельному уровню" : $"конструкции {chat.Params.ConstructionId}";
		var text = $"Контекст подготовлен по {scope}. " +
			$"Временный ответ 5.3: доменный конвейер add-agent-chat ещё подключается, поэтому сейчас я даю краткое резюме запроса «{prompt}» " +
			$"и фиксирую след источников. Уточните вопрос — ответ разверну шагами «если/то».";

		return (text, new AgentSourceTrace(tools, references));
	}

	public static IReadOnlyList<string> Tokenize(string text)
	{
		var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0)
			return [text];
		return words.Select(word => $"{word} ").ToArray();
	}
}
