namespace TransactionJournal.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Каркас HTTP API нового SPA: единая версионированная JSON-точка входа
/// и публикуемое OpenAPI-описание. Состав эндпоинтов разделов подключается
/// поверх того же префикса позже задачами 5.x; доменные правила каркасу
/// не принадлежат.
/// </summary>
public static class ApiSkeleton
{
	/// <summary>Версия API в маршруте: сегмент всех эндпоинтов контракта.</summary>
	public const string ApiVersion = "v1";

	/// <summary>Префикс всех маршрутов API текущей версии.</summary>
	public const string VersionPrefix = "/api/" + ApiVersion;

	/// <summary>
	/// Регистрирует службы каркаса API, включая генератор OpenAPI-документа.
	/// </summary>
	public static IServiceCollection AddApiSkeleton(this IServiceCollection services)
	{
		// Документ OpenAPI строится из фактически подключённых эндпоинтов
		// и публикуется по маршруту /openapi/v1.json.
		services.AddOpenApi();
		return services;
	}

	/// <summary>
	/// Подключает каркас API к конвейеру хоста: группу версионированных
	/// маршрутов с мета-эндпоинтом и маршрут OpenAPI-документа. Возвращает
	/// группу, чтобы задачи разделов 5.x подключали свои эндпоинты к тому же
	/// префиксу версии.
	/// </summary>
	public static RouteGroupBuilder MapApiSkeleton(this WebApplication app)
	{
		// Единая точка входа JSON: все данные и команды SPA идут через один
		// API одного хоста, отдельные транспорты не заводятся.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Версия API присутствует в маршруте сегментом /api/v1 — контракт
		// эволюционирует без ломки выпущенного SPA.
		// Traceability: openspec:http-api/transport#scenario-api-version-in-route
		var api = app
			.MapGroup(VersionPrefix)
			.WithTags("Каркас API");

		// Мета-эндпоинт каркаса: SPA узнаёт версию контракта единого API.
		api.MapGet("/meta", () => new ApiMetaResponse(ApiVersion));

		// OpenAPI-документ текущей версии API доступен по известному маршруту
		// и отражает фактические эндпоинты — пригоден для генерации
		// типизированного клиента SPA и ревью контрактов.
		// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
		app.MapOpenApi();

		return api;
	}

	/// <summary>Метаданные API для SPA: версия текущего контракта.</summary>
	public sealed record ApiMetaResponse(string ApiVersion);
}
