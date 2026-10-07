namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки каркаса HTTP API нового SPA: единая версионированная
/// JSON-точка входа и публикуемое OpenAPI-описание. Фабрика поднимает реальный
/// хост журнала на временной базе SQLite в каталоге прогона тестов, поэтому
/// локальная база владельца (App_Data) не затрагивается.
/// </summary>
[TestClass]
public sealed class ApiSkeletonTests
{
	[TestMethod]
	[Description("Мета-эндпоинт отвечает JSON с версией API в маршруте")]
	// Все обращения SPA идут через единый API одного хоста, а версия API
	// присутствует прямо в маршруте — контракт эволюционирует без ломки
	// выпущенного SPA.
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	// Traceability: openspec:http-api/transport#scenario-api-version-in-route
	public async Task TryIfMetaEndpointServesVersionedJson()
	{
		// Arrange: реальный хост журнала на временной базе SQLite.
		await using var factory = new JournalApiFactory();

		// Act: обращение к мета-эндпоинту каркаса API.
		var response = await factory.CreateClient().GetAsync("/api/v1/meta");

		// Assert: успешный JSON-ответ с версией API.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
		Assert.That(payload, Is.Not.Null);
		Assert.That(payload!.GetValueOrDefault("apiVersion"), Is.EqualTo("v1"));
	}

	[TestMethod]
	[Description("OpenAPI-документ публикуется по известному маршруту и содержит фактические эндпоинты")]
	// Схема API публикуется автоматически: документ текущей версии доступен
	// по известному маршруту и отражает фактически подключённые эндпоинты,
	// поэтому пригоден для генерации типизированного клиента SPA.
	// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
	public async Task TryIfOpenApiDocumentPublishedWithActualEndpoints()
	{
		// Arrange: реальный хост журнала на временной базе SQLite.
		await using var factory = new JournalApiFactory();

		// Act: запрос OpenAPI-документа текущей версии API.
		var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

		// Assert: валидный документ OpenAPI 3.x с фактическим эндпоинтом каркаса.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var document = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
		Assert.That(document, Is.Not.Null);
		Assert.That(document!["openapi"]!.GetValue<string>().StartsWith("3.", StringComparison.Ordinal), Is.True);
		var paths = document["paths"]!.AsObject().Select(path => path.Key).ToArray();
		Assert.That(paths, Does.Contain("/api/v1/meta"));
		// Все публикуемые пути API лежат под версионированным префиксом:
		// маршрутов API без сегмента версии не существует.
		Assert.That(
			paths.Where(path => path.StartsWith("/api/", StringComparison.Ordinal)
				&& path.StartsWith("/api/v1/", StringComparison.Ordinal) == false),
			Is.Empty);
	}

	[TestMethod]
	[Description("Маршрут API без сегмента версии не существует")]
	// Версия API обязательна в маршруте: обращение к API-маршруту без
	// сегмента версии не находит эндпоинта — безверсионных маршрутов
	// в приложении не публикуется.
	// Traceability: openspec:http-api/transport#scenario-api-version-in-route
	public async Task ThrowOnRouteWithoutVersionSegmentReturns404()
	{
		// Arrange: реальный хост журнала на временной базе SQLite.
		await using var factory = new JournalApiFactory();

		// Act: обращение к API без сегмента версии.
		var response = await factory.CreateClient().GetAsync("/api/meta");

		// Assert: эндпоинт отсутствует.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[TestMethod]
	[Description("Корневая страница Blazor продолжает отвечать после подключения каркаса API")]
	// Регресс каркаса: до паритета переносов журнал работает одновременно
	// в Blazor-UI и новом SPA — существующие страницы не ломаются.
	public async Task TryIfBlazorRootPageStillServed()
	{
		// Arrange: реальный хост журнала на временной базе SQLite.
		await using var factory = new JournalApiFactory();

		// Act: запрос корневой страницы Blazor.
		var response = await factory.CreateClient().GetAsync("/");

		// Assert: страница отвечает HTML.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/html"));
	}
}

/// <summary>
/// Хост журнала для интеграционных проверок API: реальная точка входа Program
/// на временной базе SQLite в каталоге прогона тестов. Подмена строки
/// подключения направляет стартовые миграции в тестовую базу — локальная база
/// владельца (App_Data) остаётся нетронутой.
/// </summary>
internal sealed class JournalApiFactory : WebApplicationFactory<Program>
{
	private readonly string _databasePath = Path.Combine(
		Path.GetDirectoryName(typeof(ApiSkeletonTests).Assembly.Location)!,
		$"journal-api-tests-{Guid.NewGuid():N}.db");

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Development: без HSTS и производственного обработчика ошибок —
		// ответы API видны тестам как есть.
		builder.UseEnvironment("Development");
		// Временная база каталога прогона вместо App_Data журнала.
		builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
			new Dictionary<string, string?>
			{
				["ConnectionStrings:Journal"] = $"Data Source={_databasePath}",
			}));
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing == false)
			return;
		// Временная база и соседние WAL-файлы удаляются после прогона.
		foreach (var suffix in new[] { "", "-wal", "-shm" })
		{
			try
			{
				File.Delete(_databasePath + suffix);
			}
			catch (IOException)
			{
			}
		}
	}
}
