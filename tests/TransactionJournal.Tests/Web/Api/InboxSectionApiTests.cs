namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки HTTP-контрактов раздела «Входящие»: чтение
/// непривязанных сделок, бейдж-счётчик, массовая привязка, создание
/// конструкции из выбранного и инкрементальная сборка.
/// </summary>
[TestClass]
public sealed class InboxSectionApiTests
{
	[TestMethod]
	[Description("Снимок входящих отдаёт список сделок и целей привязки")]
	// Раздел «Входящие» читает сделки и цели через единый /api/v1.
	// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfInboxOverviewServesTradesAndTargets()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/v1/inbox");

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["items"], Is.Not.Null);
		Assert.That(payload["targets"], Is.Not.Null);
	}

	[TestMethod]
	[Description("Бейдж входящих читает счётчик непривязанных сделок")]
	// Бейдж левой навигации читает отдельный лёгкий счётчик «Входящих».
	// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
	public async Task TryIfInboxCountServesBadgeCounter()
	{
		await using var factory = new SectionApiFactory(services => services.ReplaceFrameReadModel(new StubFrameReadModel(4)));
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/v1/inbox/count");

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["count"]!.GetValue<int>(), Is.EqualTo(4));
	}

	[TestMethod]
	[Description("Массовая привязка вызывает сервис привязки выбранных сделок")]
	// Кнопочная и drag&drop привязка используют одну команду bind.
	// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
	public async Task TryIfBindCommandUsesTradeBindingService()
	{
		var bindings = new RecordingTradeBindingService();
		await using var factory = new SectionApiFactory(services => services.ReplaceTradeBindingService(bindings));
		using var client = factory.CreateClient();

		var response = await client.PostAsJsonAsync("/api/v1/inbox/bind", new { constructionId = 7L, execIds = new[] { "exec-1", "exec-2" } });

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
		Assert.That(bindings.Moved, Is.EqualTo(new[] { (7L, "exec-1"), (7L, "exec-2") }));
	}

	[TestMethod]
	[Description("Создание конструкции из выбранных создаёт цель и привязывает сделки")]
	// Команда «Создать конструкцию из выбранного» использует сервисы
	// конструкции и привязки через единый HTTP-контракт.
	// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
	public async Task TryIfCreateConstructionCommandCreatesAndBinds()
	{
		var constructions = new RecordingInboxConstructionService();
		var bindings = new RecordingTradeBindingService();
		await using var factory = new SectionApiFactory(services => services
			.ReplaceConstructionService(constructions)
			.ReplaceTradeBindingService(bindings));
		using var client = factory.CreateClient();

		var response = await client.PostAsJsonAsync("/api/v1/inbox/create-construction", new
		{
			name = "Новая конструкция",
			allocatedCapitalUsdt = 3000m,
			execIds = new[] { "exec-1" },
		});

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["constructionId"]!.GetValue<long>(), Is.EqualTo(77L));
		Assert.That(constructions.CreatedNames, Is.EqualTo(new[] { "Новая конструкция" }));
		Assert.That(bindings.Moved, Is.EqualTo(new[] { (77L, "exec-1") }));
	}

	[TestMethod]
	[Description("Собрать из Входящих запускает инкрементальную сборку")]
	// Кнопка «Собрать из Входящих» запускает безопасную инкрементальную сборку.
	// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
	public async Task TryIfAssembleCommandRunsIncrementalAssembly()
	{
		var assembly = new StubConstructionAssemblyService();
		await using var factory = new SectionApiFactory(services => services.ReplaceAssemblyService(assembly));
		using var client = factory.CreateClient();

		var response = await client.PostAsync("/api/v1/inbox/assemble", content: null);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["constructionsCount"]!.GetValue<int>(), Is.EqualTo(0));
		Assert.That(payload["boundCount"]!.GetValue<int>(), Is.EqualTo(0));
		Assert.That(payload["tradesInInbox"]!.GetValue<int>(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("OpenAPI публикует эндпоинты раздела входящих")]
	// Контракт раздела должен быть отражён в OpenAPI.
	// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
	public async Task TryIfOpenApiListsInboxEndpoints()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var document = await (await client.GetAsync("/openapi/v1.json")).Content.ReadFromJsonAsync<JsonNode>();
		var paths = document!["paths"]!.AsObject().Select(path => path.Key).ToArray();
		Assert.That(paths, Does.Contain("/api/v1/inbox"));
		Assert.That(paths, Does.Contain("/api/v1/inbox/count"));
		Assert.That(paths, Does.Contain("/api/v1/inbox/bind"));
		Assert.That(paths, Does.Contain("/api/v1/inbox/create-construction"));
		Assert.That(paths, Does.Contain("/api/v1/inbox/assemble"));
	}
}

internal sealed class StubFrameReadModel(int count) : IFrameReadModel
{
	public Task<int> CountInboxAsync(CancellationToken cancellationToken = default) => Task.FromResult(count);

	public Task<ConstructionHeader?> FindConstructionHeaderAsync(long constructionId, CancellationToken cancellationToken = default) =>
		Task.FromResult<ConstructionHeader?>(null);
}

internal sealed class RecordingInboxConstructionService : IConstructionService
{
	public IReadOnlyList<string> CreatedNames => _createdNames;

	private readonly List<string> _createdNames = [];

	public Task<Construction> CreateAsync(string name, decimal? allocatedCapitalUsdt, string? comment = null, CancellationToken cancellationToken = default)
	{
		_createdNames.Add(name);
		return Task.FromResult(new Construction
		{
			Id = 77L,
			Name = name,
			AllocatedCapitalUsdt = allocatedCapitalUsdt,
			Status = ConstructionStatus.Open,
			Comment = comment,
		});
	}

	public Task RenameAsync(long constructionId, string newName, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task UpdateAllocatedCapitalAsync(long constructionId, decimal? allocatedCapitalUsdt, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task UpdateRiskAsync(long constructionId, decimal? value, TargetUnit? unit, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task UpdateProfitAsync(long constructionId, decimal? value, TargetUnit? unit, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task ChangeStatusAsync(long constructionId, ConstructionStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task ArchiveAsync(long constructionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task DeleteAsync(long constructionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<IReadOnlyList<Construction>> ListActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Construction>>([]);

	public Task<IReadOnlyList<Construction>> ListAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Construction>>([]);
}

internal static class InboxApiServiceExtensions
{
	public static IServiceCollection ReplaceFrameReadModel(this IServiceCollection services, IFrameReadModel model)
	{
		foreach (var existing in services.Where(service => service.ServiceType == typeof(IFrameReadModel)).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(model));
		return services;
	}
}
