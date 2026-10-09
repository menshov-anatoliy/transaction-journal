namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransactionJournal.Api.Agent;

/// <summary>
/// Интеграционные проверки API раздела «Агент»: контракты истории чатов,
/// жизненного цикла active/completed и read-only каталога корпуса правил.
/// Проверяется только HTTP-слой раздела 5.3.
/// </summary>
[TestClass]
public sealed class AgentSectionApiTests
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Description("Удаление активного или завершённого привязанного чата убирает историю и сохраняется после загрузки хранилища")]
	// Чат удаляется целиком из обоих списков, не затрагивая соседние чаты.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public async Task TryIfDeletingBoundChatRemovesPersistedHistory(bool completed)
	{
		// Arrange: изолированный JSON в каталоге тестового проекта.
		var dataDirectory = Path.Combine(AppContext.BaseDirectory, $"agent-delete-{Guid.NewGuid():N}");
		try
		{
			var store = new AgentChatStore(dataDirectory);
			var parameters = new AgentChatParams("GLM-5.3", "7", ["journal"]);
			var chat = store.Create("Проверь конструкцию", parameters);
			var retained = store.Create("Другой чат", parameters);
			if (completed)
				store.TrySetStatus(chat.Id, AgentChatStatus.Completed, out _);
			await using var factory = new SectionApiFactory(services =>
			{
				services.RemoveAll<AgentChatStore>();
				services.AddSingleton(store);
			});
			using var client = factory.CreateClient();

			// Act: удаление по HTTP.
			var response = await client.DeleteAsync($"/api/v1/chats/{chat.Id}");

			// Assert: исчезли список, история и сохранённая запись.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That((await client.GetAsync($"/api/v1/chats/{chat.Id}/messages")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			foreach (var status in new[] { "active", "completed" })
			{
				var list = await client.GetFromJsonAsync<JsonArray>($"/api/v1/chats?status={status}");
				Assert.That(list!.Any(node => node!["id"]!.GetValue<string>() == chat.Id), Is.False);
			}
			var reloaded = new AgentChatStore(dataDirectory);
			Assert.That(reloaded.TryGet(chat.Id, out _), Is.False);
			Assert.That(reloaded.TryGet(retained.Id, out var preserved), Is.True);
			Assert.That(preserved!.Messages, Has.Count.EqualTo(1));
		}
		finally
		{
			if (Directory.Exists(dataDirectory))
				Directory.Delete(dataDirectory, recursive: true);
		}
	}

	[TestMethod]
	[Description("Удаление неизвестного чата отвечает 404 без создания пустой записи")]
	// Неизвестный идентификатор даёт явный отказ вместо ложного успешного удаления.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public async Task ThrowOnDeletingUnknownChatReturns404()
	{
		// Arrange: неизвестный идентификатор не совпадёт с существующими чатами.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		var chatId = $"missing-{Guid.NewGuid():N}";

		// Act: запрос удаления.
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
	// Traceability: doc:.wf-research/ui-concept/concept.md#7-раздел-агент-маршрут-agent
	// Traceability: change:add-agent-chat/proposal#what-changes
	public async Task TryIfChatLifecycleMovesBetweenActiveAndCompleted()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var createPayload = new JsonObject
		{
			["text"] = "Проверь лимиты риска на неделю",
			["params"] = new JsonObject
			{
				["model"] = "GLM-5.3",
				["constructionId"] = null,
				["sources"] = new JsonArray("journal", "rules-corpus", "market"),
			},
		};

		var createResponse = await client.PostAsJsonAsync("/api/v1/chats", createPayload);
		Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var created = await createResponse.Content.ReadFromJsonAsync<JsonNode>();
		var chatId = created!["id"]!.GetValue<string>();

		var activeBeforeCompletion = await (await client.GetAsync("/api/v1/chats?status=active")).Content.ReadFromJsonAsync<JsonArray>();
		Assert.That(activeBeforeCompletion!.Any(node => node!["id"]!.GetValue<string>() == chatId), Is.True);

		var completeResponse = await client.PostAsync($"/api/v1/chats/{chatId}/completion", null);
		Assert.That(completeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

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
		var created = await (await client.PostAsJsonAsync("/api/v1/chats", new JsonObject
		{
			["text"] = "Проверь лимиты риска",
			["params"] = new JsonObject
			{
				["model"] = "glm-5.3",
				["constructionId"] = null,
				["sources"] = new JsonArray("journal", "rules-corpus", "market"),
			},
		})).Content.ReadFromJsonAsync<JsonNode>();
		var chatId = created!["id"]!.GetValue<string>();

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
		var unknownResponse = await client.PutAsJsonAsync($"/api/v1/chats/missing-{Guid.NewGuid():N}/model", new { model = "glm-5.2" });
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
		var created = await (await client.PostAsJsonAsync("/api/v1/chats", new JsonObject
		{
			["text"] = "Проверь ликвидность",
			["params"] = new JsonObject
			{
				["model"] = "glm-5.3",
				["constructionId"] = null,
				["sources"] = new JsonArray("market"),
			},
		})).Content.ReadFromJsonAsync<JsonNode>();
		var chatId = created!["id"]!.GetValue<string>();

		// Act: смена модели на пустую строку.
		var response = await client.PutAsJsonAsync($"/api/v1/chats/{chatId}/model", new { model = "  " });

		// Assert: отказ 400, прежняя модель сохранена.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		var reloaded = await (await client.GetAsync($"/api/v1/chats?status=active")).Content.ReadFromJsonAsync<JsonArray>();
		var chat = reloaded!.Single(node => node!["id"]!.GetValue<string>() == chatId);
		Assert.That(chat["params"]!["model"]!.GetValue<string>(), Is.EqualTo("glm-5.3"));
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
		Assert.That(paths, Does.Contain("/api/v1/rules"));
	}
}
