namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application.Ops;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки HTTP-контрактов раздела «Синхронизация»: настройки
/// подключения/бэкапа, журнал запусков и опасные команды обслуживания.
/// </summary>
[TestClass]
public sealed class SyncSettingsApiTests
{
	[TestMethod]
	[Description("Настройки синхронизации отдают подключение Bybit и флаг бэкапа")]
	// Блок `/sync-settings` читает маску ключа и переключатель бэкапа через
	// единый `/api/v1`, чтобы SPA не обращался к Blazor-сервисам напрямую.
	// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfSyncSettingsReturnConnectionAndBackupFlag()
	{
		await using var factory = new SectionApiFactory(services => services
			.ReplaceSettingsReadModel(new SettingsReadModel(new StubCredentialsProvider(
				new BybitCredentials("abcd1234wxyz", "secret"))))
			.ReplaceBackupPolicyStore(new StubBackupPolicyStore(enabled: false)));
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/v1/sync/settings");

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["bybit"]!["isConfigured"]!.GetValue<bool>(), Is.True);
		Assert.That(payload["bybit"]!["maskedApiKey"]!.GetValue<string>(), Is.EqualTo("abcd······wxyz"));
		Assert.That(payload["backupBeforeSyncEnabled"]!.GetValue<bool>(), Is.False);
	}

	[TestMethod]
	[Description("Журнал синхронизаций возвращает строки и заметки последнего запуска")]
	// Карта переноса требует журнал запусков и заметки пропущенных областей/
	// неразрешённых инструментов/непокрытых активов в разделе синхронизации.
	// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfSyncRunsReturnWarnings()
	{
		var warnings = new SyncRunWarnings
		{
			SkippedAreas = ["option:BTC"],
			UnresolvedInstruments = ["ETH-30OCT26-4500-C"],
			UncoveredBaseCoins = ["ETH"],
		};
		await using var factory = new SectionApiFactory(services => services
			.ReplaceSyncJournal(new StubSyncJournalReadModel(
				new SyncRunRow(
					Id: 12,
					StartedAt: new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero),
					FinishedAt: new DateTimeOffset(2026, 10, 8, 9, 1, 0, TimeSpan.Zero),
					Mode: SyncRunMode.Incremental,
					Status: SyncRunStatus.Succeeded,
					Error: null,
					NewExecutions: 4,
					NewDeliveries: 1,
					NewInstruments: 2,
					WarningsJson: warnings.ToJson()))));
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/v1/sync/runs?limit=10");

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		var run = payload!["runs"]!.AsArray().Single();
		Assert.That(run!["id"]!.GetValue<long>(), Is.EqualTo(12));
		Assert.That(run["skippedAreas"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EquivalentTo(new[] { "option:BTC" }));
		Assert.That(run["unresolvedInstruments"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EquivalentTo(new[] { "ETH-30OCT26-4500-C" }));
		Assert.That(run["uncoveredBaseCoins"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EquivalentTo(new[] { "ETH" }));
	}

	[TestMethod]
	[Description("Опасная зона запускает пересбор и сброс состояния категории")]
	// Опасная зона `/sync-settings` обязана уметь запускать полный пересбор и
	// сбрасывать состояние linear/option через HTTP-контракт `/api/v1`.
	// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfDangerZoneCommandsInvokeServices()
	{
		var assembly = new StubConstructionAssemblyService();
		var stateStore = new StubSyncStateStore();
		await using var factory = new SectionApiFactory(services => services
			.ReplaceAssemblyService(assembly)
			.ReplaceStateStore(stateStore));
		using var client = factory.CreateClient();

		var rebuildResponse = await client.PostAsync("/api/v1/sync/rebuild", content: null);
		var resetResponse = await client.PostAsJsonAsync("/api/v1/sync/reset-category", new { category = "option" });

		Assert.That(rebuildResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var rebuildPayload = await rebuildResponse.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(rebuildPayload!["constructionsCount"]!.GetValue<int>(), Is.EqualTo(3));
		Assert.That(assembly.RebuildCalls, Is.EqualTo(1));
		Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
		Assert.That(stateStore.ResetCategories, Is.EqualTo(new[] { "option" }));
	}

	[TestMethod]
	[Description("Сброс с неизвестной категорией отклоняется ошибкой 400")]
	// Контракт сброса принимает только linear/option: ошибочный ввод не должен
	// попасть в доменный стор состояния синхронизации.
	// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
	public async Task ThrowOnResetWithUnknownCategoryReturns400()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var response = await client.PostAsJsonAsync("/api/v1/sync/reset-category", new { category = "spot" });

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
	}

	[TestMethod]
	[Description("OpenAPI публикует эндпоинты раздела sync-settings")]
	// Контракты раздела документируются автоматически в OpenAPI и доступны для
	// типизированного клиента SPA.
	// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
	public async Task TryIfOpenApiListsSyncSettingsEndpoints()
	{
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		var document = await (await client.GetAsync("/openapi/v1.json")).Content.ReadFromJsonAsync<JsonNode>();
		var paths = document!["paths"]!.AsObject().Select(path => path.Key).ToArray();
		Assert.That(paths, Does.Contain("/api/v1/sync/settings"));
		Assert.That(paths, Does.Contain("/api/v1/sync/runs"));
		Assert.That(paths, Does.Contain("/api/v1/sync/rebuild"));
		Assert.That(paths, Does.Contain("/api/v1/sync/reset-category"));
	}
}

internal static class SyncSettingsApiServiceExtensions
{
	public static IServiceCollection ReplaceSettingsReadModel(this IServiceCollection services, SettingsReadModel readModel)
	{
		Replace(services, typeof(SettingsReadModel), readModel);
		return services;
	}

	public static IServiceCollection ReplaceBackupPolicyStore(this IServiceCollection services, IBackupPolicyStore store)
	{
		Replace(services, typeof(IBackupPolicyStore), store);
		return services;
	}

	public static IServiceCollection ReplaceSyncJournal(this IServiceCollection services, ISyncJournalReadModel readModel)
	{
		Replace(services, typeof(ISyncJournalReadModel), readModel);
		return services;
	}

	public static IServiceCollection ReplaceAssemblyService(this IServiceCollection services, IConstructionAssemblyService service)
	{
		Replace(services, typeof(IConstructionAssemblyService), service);
		return services;
	}

	public static IServiceCollection ReplaceStateStore(this IServiceCollection services, IExecutionSyncStateStore stateStore)
	{
		Replace(services, typeof(IExecutionSyncStateStore), stateStore);
		return services;
	}

	private static void Replace(IServiceCollection services, Type serviceType, object implementation)
	{
		foreach (var existing in services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(serviceType, implementation));
	}
}

internal sealed class StubCredentialsProvider(BybitCredentials credentials) : IBybitCredentialsProvider
{
	public BybitCredentials GetCredentials() => credentials;
}

internal sealed class StubBackupPolicyStore(bool enabled) : IBackupPolicyStore
{
	public Task<bool> IsBackupBeforeSyncEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(enabled);

	public Task SetBackupBeforeSyncEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class StubSyncJournalReadModel(params SyncRunRow[] rows) : ISyncJournalReadModel
{
	public Task<IReadOnlyList<SyncRunRow>> ReadAsync(int limit = 20, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<SyncRunRow>>(rows.Take(limit).ToArray());

	public Task<SyncRunRow?> ReadLastCompletedAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(rows.LastOrDefault(row => row.Status != SyncRunStatus.Running));
}

internal sealed class StubConstructionAssemblyService : IConstructionAssemblyService
{
	public int RebuildCalls { get; private set; }

	public Task<ConstructionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default)
	{
		RebuildCalls++;
		return Task.FromResult(new ConstructionRebuildResult { ConstructionsCount = 3, BoundCount = 9, TradesInInbox = 2 });
	}

	public Task<ConstructionRebuildResult> AssembleInboxAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(new ConstructionRebuildResult { ConstructionsCount = 0, BoundCount = 0, TradesInInbox = 0 });
}

internal sealed class StubSyncStateStore : IExecutionSyncStateStore
{
	public List<string> ResetCategories { get; } = [];

	public Task<SyncState?> FindAsync(string category, CancellationToken cancellationToken = default) =>
		Task.FromResult<SyncState?>(null);

	public Task SaveAsync(SyncState state, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task ResetAsync(string category, CancellationToken cancellationToken = default)
	{
		ResetCategories.Add(category);
		return Task.CompletedTask;
	}
}
