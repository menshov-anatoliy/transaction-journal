namespace TransactionJournal.Tests.Web.Api;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransactionJournal.Api.Agent;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Infrastructure.Chats;

// Клиент модели работает с сообщениями Microsoft.Extensions.AI; доменная
// запись истории чатов живёт в TransactionJournal.Chats.Ports.
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

/// <summary>
/// Интеграционные проверки API раздела «Агент»: история чатов, жизненный
/// цикл active/completed и стриминг ответов работают поверх доменного
/// конвейера change add-agent-chat (клиент модели подменяется скриптом),
/// рядом — read-only каталог корпуса правил. Проверяется HTTP-слой раздела 5.3.
/// </summary>
[TestClass]
public sealed class AgentSectionApiTests
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Description("Удаление активного или завершённого привязанного чата убирает историю и сохраняется после загрузки хранилища")]
	// Чат удаляется целиком из обоих списков, не затрагивая соседние чаты;
	// хранилище — единая SQLite-база конвейера change add-agent-chat.
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public async Task TryIfDeletingBoundChatRemovesPersistedHistory(bool completed)
	{
		// Arrange: изолированная SQLite-база чатов в каталоге тестового прогона.
		var directory = Path.Combine(AppContext.BaseDirectory, $"agent-delete-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var store = new ChatStore(Path.Combine(directory, "chats.db"));
			await using var factory = CreateChatHost(store, new RecordingContextReader(), new ScriptedModelClient("Ответ"));
			using var client = factory.CreateClient();
			var boundChatId = await CreateChatAsync(client, "Проверь конструкцию", "7", ["journal"]);
			var retainedChatId = await CreateChatAsync(client, "Другой чат", "7", ["journal"]);
			if (completed)
			{
				var completedResponse = await client.PostAsync($"/api/v1/chats/{boundChatId}/completion", null);
				Assert.That(completedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			}

			// Act: удаление привязанного чата по HTTP.
			var response = await client.DeleteAsync($"/api/v1/chats/{boundChatId}");

			// Assert: исчезли список, история и сохранённая запись; соседний
			// чат со своей историей не затронут.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That((await client.GetAsync($"/api/v1/chats/{boundChatId}/messages")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			foreach (var status in new[] { "active", "completed" })
			{
				var list = await client.GetFromJsonAsync<JsonArray>($"/api/v1/chats?status={status}");
				Assert.That(list!.Any(node => node!["id"]!.GetValue<string>() == boundChatId), Is.False);
			}

			var reloaded = new ChatStore(Path.Combine(directory, "chats.db"));
			Assert.That(await reloaded.FindChatAsync(long.Parse(boundChatId, CultureInfo.InvariantCulture)), Is.Null);
			Assert.That((await reloaded.ListMessagesAsync(long.Parse(boundChatId, CultureInfo.InvariantCulture))), Is.Empty);
			var retained = await reloaded.FindChatAsync(long.Parse(retainedChatId, CultureInfo.InvariantCulture));
			Assert.That(retained, Is.Not.Null);
			Assert.That(await reloaded.ListMessagesAsync(long.Parse(retainedChatId, CultureInfo.InvariantCulture)), Has.Count.EqualTo(1));
		}
		finally
		{
			// Пул подключений SQLite держит файл базы до очистки пула;
			// без неё удаление каталога падает блокировкой chats.db.
			Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	[DataRow("abc")]
	[DataRow("999999")]
	[Description("Удаление неизвестного или нечислового чата отвечает 404 без создания записи")]
	// Неизвестный идентификатор даёт явный отказ вместо ложного успешного удаления.
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	public async Task ThrowOnDeletingUnknownChatReturns404(string chatId)
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		// Act: запрос удаления неизвестного чата.
		var response = await client.DeleteAsync($"/api/v1/chats/{chatId}");

		// Assert: отказ содержит тот же идентификатор.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["chatId"]!.GetValue<string>(), Is.EqualTo(chatId));
	}

	[TestMethod]
	[Description("Созданный чат появляется в активных и скрывается в завершённых после completion")]
	// История раздела «Агент» ведётся списками активных и завершённых чатов:
	// ручное завершение прячет чат в completed, продолжение возвращает его в active.
	// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	public async Task TryIfChatLifecycleMovesBetweenActiveAndCompleted()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		var chatId = await CreateChatAsync(client, "Проверь лимиты риска на неделю", null, ["journal", "rules-corpus", "market"]);

		var activeBeforeCompletion = await (await client.GetAsync("/api/v1/chats?status=active")).Content.ReadFromJsonAsync<JsonArray>();
		Assert.That(activeBeforeCompletion!.Any(node => node!["id"]!.GetValue<string>() == chatId), Is.True);

		var completeResponse = await client.PostAsync($"/api/v1/chats/{chatId}/completion", null);
		Assert.That(completeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var completed = await completeResponse.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(completed!["status"]!.GetValue<string>(), Is.EqualTo("completed"));

		var activeAfterCompletion = await (await client.GetAsync("/api/v1/chats?status=active")).Content.ReadFromJsonAsync<JsonArray>();
		var completedAfterCompletion = await (await client.GetAsync("/api/v1/chats?status=completed")).Content.ReadFromJsonAsync<JsonArray>();
		Assert.That(activeAfterCompletion!.Any(node => node!["id"]!.GetValue<string>() == chatId), Is.False);
		Assert.That(completedAfterCompletion!.Any(node => node!["id"]!.GetValue<string>() == chatId), Is.True);

		var resumeResponse = await client.DeleteAsync($"/api/v1/chats/{chatId}/completion");
		Assert.That(resumeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		var activeAfterResume = await (await client.GetAsync("/api/v1/chats?status=active")).Content.ReadFromJsonAsync<JsonArray>();
		Assert.That(activeAfterResume!.Any(node => node!["id"]!.GetValue<string>() == chatId), Is.True);
	}

	[TestMethod]
	[Description("Чат, созданный без выбора модели, получает дефолт GLM-5.3")]
	// Дефолт модели задаётся подсекцией Llm:Chat:Model: владелец создаёт чат,
	// не выбирая модель, и чат работает на GLM-5.3 без правки кода.
	// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
	public async Task TryIfChatCreatedWithoutModel_DefaultModelGlmApplied()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var createPayload = new JsonObject
		{
			["text"] = "Проверь конструкцию без выбора модели",
			["params"] = new JsonObject
			{
				["constructionId"] = null,
				["sources"] = new JsonArray("journal", "rules-corpus", "market"),
			},
		};

		// Act: создание чата с пустым параметром модели.
		var createResponse = await client.PostAsJsonAsync("/api/v1/chats", createPayload);

		// Assert: чат создан на дефолтной модели GLM-5.3.
		Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var created = await createResponse.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(created!["params"]!["model"]!.GetValue<string>(), Is.EqualTo("glm-5.3"));
	}

	[TestMethod]
	[Description("Смена модели на лету обновляет параметр чата и сохраняет историю")]
	// Модель — параметр чата: смена выполняется действием владельца поверх
	// существующего чата, история сообщений остаётся как есть; неизвестный
	// идентификатор даёт 404 без изменений.
	// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
	public async Task TryIfModelChangedMidChat_ParamsUpdatedAndHistoryPreserved()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		var chatId = await CreateChatAsync(client, "Проверь лимиты риска", null, ["journal", "rules-corpus", "market"]);

		// Act: владелец меняет модель существующего чата.
		var changeResponse = await client.PutAsJsonAsync($"/api/v1/chats/{chatId}/model", new { model = "glm-5.2" });

		// Assert: параметр чата обновлён, история не тронута.
		Assert.That(changeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var changed = await changeResponse.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(changed!["params"]!["model"]!.GetValue<string>(), Is.EqualTo("glm-5.2"));
		var messages = await (await client.GetAsync($"/api/v1/chats/{chatId}/messages")).Content.ReadFromJsonAsync<JsonArray>();
		Assert.That(messages!, Has.Count.EqualTo(1));
		Assert.That(messages[0]!["text"]!.GetValue<string>(), Is.EqualTo("Проверь лимиты риска"));

		// Assert: смена модели неизвестного чата даёт 404.
		var unknownResponse = await client.PutAsJsonAsync($"/api/v1/chats/999999/model", new { model = "glm-5.2" });
		Assert.That(unknownResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[TestMethod]
	[Description("Смена модели на пустое значение отвечает 400 без изменения чата")]
	// Модель чата не бывает пустой: пустая строка отвергается валидацией,
	// прежний параметр сохраняется.
	// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
	public async Task ThrowOnChangingModelWithoutModelReturns400()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		var chatId = await CreateChatAsync(client, "Проверь ликвидность", null, ["market"]);

		// Act: смена модели на пустую строку.
		var response = await client.PutAsJsonAsync($"/api/v1/chats/{chatId}/model", new { model = "  " });

		// Assert: отказ 400, прежняя модель сохранена.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		var reloaded = await (await client.GetAsync("/api/v1/chats?status=active")).Content.ReadFromJsonAsync<JsonArray>();
		var chat = reloaded!.Single(node => node!["id"]!.GetValue<string>() == chatId);
		Assert.That(chat["params"]!["model"]!.GetValue<string>(), Is.EqualTo("glm-5.3"));
	}

	[TestMethod]
	[Description("Стриминг ответа конвейера доставляет токены, финальное сообщение и сохраняет ответ в истории")]
	// SSE-канал обслуживает настоящий ИИ-конвейер: токены уходят по мере
	// стрима, done несёт финальное сообщение со следом источников (журнал в
	// источниках — журнальная ссылка со своим as-of), ответ ассистента
	// фиксируется в истории чата.
	// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	public async Task TryIfStreamingChatAnswerDeliversTokensAndPersistsAssistantMessage()
	{
		// Arrange: изолированное хранилище и скриптовый клиент модели.
		var directory = Path.Combine(AppContext.BaseDirectory, $"agent-stream-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var store = new ChatStore(Path.Combine(directory, "chats.db"));
			var reader = new RecordingContextReader();
			var model = new ScriptedModelClient("Ответ ", "ассистента по рискам");
			await using var factory = CreateChatHost(store, reader, model);
			using var client = factory.CreateClient();
			var chatId = await CreateChatAsync(client, "Проверь риски конструкции", "7", ["journal", "rules-corpus"]);

			// Act: стриминг ответа на следующее сообщение владельца; вопрос
			// создания уже зафиксирован POST /chats, запрос в стриме — новое
			// сообщение, на которое отвечает конвейер.
			using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/chats/{chatId}/messages/stream")
			{
				Content = JsonContent.Create(new { text = "Уточни по лимитам конструкции", model = "glm-5.3" }),
			};
			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

			// Assert: канал открыт как SSE, токены собраны целиком, done несёт
			// финальное сообщение ассистента со следом источников.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/event-stream"));
			var raw = await response.Content.ReadAsStringAsync();
			var frames = ParseFrames(raw);
			Assert.That(frames[0].EventName, Is.EqualTo("start"));
			var tokens = string.Concat(frames.Where(frame => frame.EventName == "token").Select(frame => frame.Data!["text"]!.GetValue<string>()));
			Assert.That(tokens, Is.EqualTo("Ответ ассистента по рискам"));
			var done = frames.Single(frame => frame.EventName == "done");
			var message = done.Data!["message"]!;
			Assert.That(message["role"]!.GetValue<string>(), Is.EqualTo("assistant"));
			Assert.That(message["text"]!.GetValue<string>(), Is.EqualTo("Ответ ассистента по рискам"));
			var references = message["sourceTrace"]!["references"]!.AsArray();
			Assert.That(references.Any(node => node!["kind"]!.GetValue<string>() == "journal"), Is.True);

			// Assert: ответ ассистента зафиксирован в истории чата: вопрос
			// создания, вопрос стрима и ответ конвейера; снимок контекста
			// запрошен по привязке чата.
			var history = await (await client.GetAsync($"/api/v1/chats/{chatId}/messages")).Content.ReadFromJsonAsync<JsonArray>();
			Assert.That(history!, Has.Count.EqualTo(3));
			Assert.That(history[0]!["role"]!.GetValue<string>(), Is.EqualTo("user"));
			Assert.That(history[0]!["text"]!.GetValue<string>(), Is.EqualTo("Проверь риски конструкции"));
			Assert.That(history[1]!["role"]!.GetValue<string>(), Is.EqualTo("user"));
			Assert.That(history[1]!["text"]!.GetValue<string>(), Is.EqualTo("Уточни по лимитам конструкции"));
			Assert.That(history[2]!["role"]!.GetValue<string>(), Is.EqualTo("assistant"));
			Assert.That(history[2]!["text"]!.GetValue<string>(), Is.EqualTo("Ответ ассистента по рискам"));
			Assert.That(reader.RequestedConstructionIds, Is.EqualTo(new long?[] { 7L }));
		}
		finally
		{
			// Пул подключений SQLite держит файл базы до очистки пула;
			// без неё удаление каталога падает блокировкой chats.db.
			Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	[Description("Стриминг в неизвестный чат отвечает 404 JSON до открытия SSE-канала")]
	// Отказ маршрута не должен выглядеть как событие ошибки генерации:
	// соединение не открывается, клиент получает обычный JSON-отказ.
	// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
	public async Task ThrowOnStreamingUnknownChatReturns404Json()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		// Act: стриминг в чат, которого нет в хранилище.
		var response = await client.PostAsJsonAsync("/api/v1/chats/999999/messages/stream", new { text = "Проверь риски", model = "glm-5.3" });

		// Assert: обычный JSON-отказ с тем же идентификатором чата.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
		Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["chatId"]!.GetValue<string>(), Is.EqualTo("999999"));
	}

	[TestMethod]
	[Description("Пост-мортем закрытой конструкции: привязка доходит до конвейера и стрим отвечает")]
	// Закрытая конструкция не блокирует чат: конвейер получает привязку и
	// признак закрытости в снимке контекста и ведёт пост-мортем.
	// Traceability: openspec:chats/context#requirement-chat-context-postmortem-mode
	public async Task TryIfStreamingClosedConstructionKeepsPostmortemBinding()
	{
		// Arrange: изолированное хранилище, контекстный читатель с закрытой
		// конструкцией (закрытая конструкция №8 из стабильной заглушки).
		var directory = Path.Combine(AppContext.BaseDirectory, $"agent-closed-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			var store = new ChatStore(Path.Combine(directory, "chats.db"));
			var reader = new RecordingContextReader { IsConstructionClosed = true };
			var model = new ScriptedModelClient("Разбор ", "закрытой конструкции");
			await using var factory = CreateChatHost(store, reader, model);
			using var client = factory.CreateClient();
			var chatId = await CreateChatAsync(client, "Почему закрытая конструкция ушла в минус", "8", ["journal"]);

			// Act: стриминг пост-мортема по закрытой конструкции.
			using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/chats/{chatId}/messages/stream")
			{
				Content = JsonContent.Create(new { text = "Почему закрытая конструкция ушла в минус", model = "glm-5.3" }),
			};
			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

			// Assert: канал открыт, ответ доставлен, привязка дошла до
			// снимка контекста с признаком закрытости.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			var frames = ParseFrames(await response.Content.ReadAsStringAsync());
			var tokens = string.Concat(frames.Where(frame => frame.EventName == "token").Select(frame => frame.Data!["text"]!.GetValue<string>()));
			Assert.That(tokens, Is.EqualTo("Разбор закрытой конструкции"));
			Assert.That(frames.Any(frame => frame.EventName == "done"), Is.True);
			Assert.That(reader.RequestedConstructionIds, Is.EqualTo(new long?[] { 8L }));
		}
		finally
		{
			// Пул подключений SQLite держит файл базы до очистки пула;
			// без неё удаление каталога падает блокировкой chats.db.
			Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	[Description("Каталог правил фильтруется по характеру, чёткости и поисковой строке")]
	// Каталог корпуса правил read-only доступен в том же окне раздела «Агент»:
	// список поддерживает фильтры характера, чёткости, субъекта и поиск.
	// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
	// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
	public async Task TryIfRulesCatalogSupportsFiltersAndSearch()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/v1/rules?character=risk-mode&clarity=crisp&subject=construction&search=лимит");
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		var items = payload!["items"]!.AsArray();
		Assert.That(items.Count, Is.GreaterThan(0));
		Assert.That(items.All(node => node!["character"]!.GetValue<string>() == "risk-mode"), Is.True);
		Assert.That(items.All(node => node!["clarity"]!.GetValue<string>() == "crisp"), Is.True);
		Assert.That(items.All(node => node!["subject"]!.GetValue<string>() == "construction"), Is.True);
		Assert.That(items.Any(node => node!["title"]!.GetValue<string>().ToLowerInvariant().Contains("лимит")), Is.True);
	}

	[TestMethod]
	[Description("OpenAPI публикует контракты раздела Агент под /api/v1")]
	// Контракты раздела публикуются в OpenAPI автоматически и лежат под единым
	// версионированным префиксом API.
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfOpenApiContainsAgentRoutes()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var document = await (await client.GetAsync("/openapi/v1.json")).Content.ReadFromJsonAsync<JsonNode>();
		var paths = document!["paths"]!.AsObject().Select(path => path.Key).ToArray();
		Assert.That(paths, Does.Contain("/api/v1/chats"));
		Assert.That(paths, Does.Contain("/api/v1/chats/{chatId}/messages"));
		Assert.That(paths, Does.Contain("/api/v1/chats/{chatId}/messages/stream"));
		Assert.That(paths, Does.Contain("/api/v1/rules"));
	}

	/// <summary>Создаёт чат первым сообщением владельца и возвращает строковый идентификатор.</summary>
	private static async Task<string> CreateChatAsync(HttpClient client, string text, string? constructionId, string[] sources)
	{
		var sourcesNode = new JsonArray();
		foreach (var source in sources)
			sourcesNode.Add(source);
		var paramsNode = new JsonObject
		{
			["constructionId"] = constructionId,
			["sources"] = sourcesNode,
		};
		var payload = new JsonObject
		{
			["text"] = text,
			["params"] = paramsNode,
		};
		var response = await client.PostAsJsonAsync("/api/v1/chats", payload);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var created = await response.Content.ReadFromJsonAsync<JsonObject>();
		return created!["id"]!.GetValue<string>();
	}

	/// <summary>
	/// Хост с подменой доменных зависимостей чатов: изолированное SQLite-
	/// хранилище, контекстный читатель-рекордер и скриптовый клиент модели
	/// вместо keyed-клиента "chats" из регистраций хоста.
	/// </summary>
	private static SectionApiFactory CreateChatHost(IChatStore store, IChatContextReader reader, IChatClient model) =>
		new(services =>
		{
			services.RemoveAll<IChatStore>();
			services.AddSingleton(store);
			services.RemoveAll<IChatContextReader>();
			services.AddSingleton(reader);
			services.AddKeyedSingleton<IChatClient>(ChatAgent.ChatClientServiceKey, (_, _) => model);
		});

	/// <summary>Разбирает SSE-кадр ответа на имя события и JSON-нагрузку.</summary>
	private static List<(string EventName, JsonNode? Data)> ParseFrames(string raw)
	{
		var frames = new List<(string, JsonNode?)>();
		foreach (var frame in raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
		{
			string? eventName = null;
			JsonNode? data = null;
			foreach (var line in frame.Split('\n'))
			{
				if (line.StartsWith("event: ", StringComparison.Ordinal))
					eventName = line["event: ".Length..].Trim();
				else if (line.StartsWith("data: ", StringComparison.Ordinal))
					data = JsonNode.Parse(line["data: ".Length..]);
			}

			frames.Add((eventName!, data));
		}

		return frames;
	}

	/// <summary>
	/// Контекстный читатель-рекордер: сохраняет привязки и наборы источников
	/// запросов снимка, признак закрытости конструкции настраивается тестом.
	/// </summary>
	private sealed class RecordingContextReader : IChatContextReader
	{
		/// <summary>Привязки, по которым запрашивался снимок, в порядке обращений.</summary>
		public List<long?> RequestedConstructionIds { get; } = [];

		/// <summary>Наборы источников, по которым запрашивался снимок, в порядке обращений.</summary>
		public List<IReadOnlyList<ChatDataSource>> RequestedSources { get; } = [];

		/// <summary>Признак закрытости конструкции в отдаваемом снимке.</summary>
		public bool IsConstructionClosed { get; init; }

		public Task<ChatContextSnapshot> ReadAsync(
			long? constructionId,
			IReadOnlyList<ChatDataSource>? sources = null,
			CancellationToken cancellationToken = default)
		{
			RequestedConstructionIds.Add(constructionId);
			RequestedSources.Add(sources ?? ChatDataSourceCatalog.All);
			return Task.FromResult(new ChatContextSnapshot
			{
				Markdown = "# Снимок контекста",
				AsOf = DateTimeOffset.Parse("2026-10-09T10:00:00Z", CultureInfo.InvariantCulture),
				IsConstructionClosed = IsConstructionClosed,
			});
		}
	}

	/// <summary>
	/// Подмена клиента модели: на каждый запрос отдаёт кадры одного текста
	/// ответа стрим-чанками, не вызывая инструментов агентного цикла.
	/// </summary>
	private sealed class ScriptedModelClient : IChatClient
	{
		private readonly string[] _frames;

		public ScriptedModelClient(params string[] frames) => _frames = frames;

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Агентный цикл обязан использовать стриминг.");

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			foreach (var frame in _frames)
			{
				yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = { new TextContent(frame) } };
			}
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose()
		{
		}
	}
}
