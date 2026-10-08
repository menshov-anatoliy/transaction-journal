namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки API раздела «Агент»: контракты истории чатов,
/// жизненного цикла active/completed и read-only каталога корпуса правил.
/// Проверяется только HTTP-слой раздела 5.3.
/// </summary>
[TestClass]
public sealed class AgentSectionApiTests
{
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

