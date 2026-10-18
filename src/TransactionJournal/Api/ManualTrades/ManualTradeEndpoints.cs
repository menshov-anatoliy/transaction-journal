namespace TransactionJournal.Api.ManualTrades;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using TransactionJournal.Application.ManualTrades;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;

/// <summary>
/// Эндпоинты ручных сделок: внесение во «Входящие» либо сразу в конструкцию,
/// правка атрибутов и удаление из любого места. Слой тонкий: валидация значений
/// и охрана неизменяемости биржевых записей принадлежат use-case сервису,
/// эндпоинт фиксирует JSON-контракт /api/v1 и переводит отказы домена
/// в состояния HTTP.
/// </summary>
// CRUD ручных сделок — отдельные эндпоинты рядом с «Входящими»; привязка,
// перенос и возврат остаются командами bind/move/return-to-inbox.
// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
// Traceability: change:manual-journal-management/design#d4
public static class ManualTradeEndpoints
{
	/// <summary>
	/// Подключает эндпоинты ручных сделок к версионированной группе API.
	/// </summary>
	public static RouteGroupBuilder MapManualTradeEndpoints(this RouteGroupBuilder api)
	{
		var group = api.MapGroup("/manual-trades").WithTags("Ручные сделки");

		// Внесение ручной сделки: constructionId опционален — null кладёт сделку
		// во «Входящие», заданное значение сразу привязывает её к конструкции.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
		group.MapPost("/", async (
			[FromBody] CreateManualTradeRequest request,
			[FromServices] IManualTradeService trades,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.Symbol))
			{
				return Results.Json(new ManualTradeErrorResponse("Инструмент сделки не задан."), statusCode: StatusCodes.Status400BadRequest);
			}

			try
			{
				var trade = await trades.CreateAsync(ToDraft(request), cancellationToken).ConfigureAwait(false);
				return Results.Json(ToResponse(trade));
			}
			catch (Exception exception) when (exception is ManualTradeValidationException or ArgumentException)
			{
				// Нарушенные правила формы — отказ запроса с человекочитаемой причиной.
				// Traceability: openspec:domain/manual-trades#requirement-manual-trade-validation
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (ConstructionNotFoundException exception)
			{
				return Results.Json(new ManualTradeErrorResponse($"Конструкция не найдена: {exception.ConstructionId}."), statusCode: StatusCodes.Status404NotFound);
			}
			catch (ManualSymbolResolutionException exception)
			{
				// Символ не разрешён справочником и биржей: сообщение сервиса
				// человекочитаемо и объясняет, что сделка не сохранена.
				// Traceability: change:manual-journal-management/design#d3
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
			}
		});

		// Правка атрибутов ручной сделки из любого места — из «Входящих» и из
		// конструкции; привязка правкой не меняется, производные данные
		// пересчитываются при ближайшем чтении.
		// Traceability: openspec:domain/manual-trades#scenario-manual-trade-edit-recalculates
		group.MapPut("/{id:long}", async (
			long id,
			[FromBody] EditManualTradeRequest request,
			[FromServices] IManualTradeService trades,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.Symbol))
			{
				return Results.Json(new ManualTradeErrorResponse("Инструмент сделки не задан."), statusCode: StatusCodes.Status400BadRequest);
			}

			try
			{
				var trade = await trades.EditAsync(id, ToDraft(request), cancellationToken).ConfigureAwait(false);
				return Results.Json(ToResponse(trade));
			}
			catch (Exception exception) when (exception is ManualTradeValidationException or ArgumentException)
			{
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (ManualTradeImmutableException exception)
			{
				// Попытка изменить биржевую запись — ошибка запроса по решению D4.
				// Traceability: openspec:domain/manual-trades#scenario-synced-trade-edit-rejected
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (ManualTradeNotFoundException exception)
			{
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status404NotFound);
			}
			catch (ManualSymbolResolutionException exception)
			{
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
			}
		});

		// Удаление ручной сделки из любого места: позиции и метрики конструкции
		// и «Входящих» пересчитываются при ближайшем чтении без удалённой сделки.
		// Traceability: openspec:domain/manual-trades#scenario-manual-trade-delete-anywhere
		group.MapDelete("/{id:long}", async (
			long id,
			[FromServices] IManualTradeService trades,
			CancellationToken cancellationToken) =>
		{
			try
			{
				await trades.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
				return Results.NoContent();
			}
			catch (ManualTradeImmutableException exception)
			{
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (ManualTradeNotFoundException exception)
			{
				return Results.Json(new ManualTradeErrorResponse(exception.Message), statusCode: StatusCodes.Status404NotFound);
			}
		});

		return api;
	}

	/// <summary>Собирает черновик сервиса из команды внесения.</summary>
	private static ManualTradeDraft ToDraft(CreateManualTradeRequest request) => new()
	{
		ExecutedAt = request.ExecutedAt,
		Symbol = request.Symbol,
		Side = request.Side,
		Quantity = request.Quantity,
		Price = request.Price,
		FeeUsdt = request.FeeUsdt ?? 0m,
		ConstructionId = request.ConstructionId,
	};

	/// <summary>Собирает черновик сервиса из команды правки; привязка черновика игнорируется сервисом.</summary>
	private static ManualTradeDraft ToDraft(EditManualTradeRequest request) => new()
	{
		ExecutedAt = request.ExecutedAt,
		Symbol = request.Symbol,
		Side = request.Side,
		Quantity = request.Quantity,
		Price = request.Price,
		FeeUsdt = request.FeeUsdt ?? 0m,
		ConstructionId = null,
	};

	/// <summary>Собирает ответ API из сохранённой ручной сделки.</summary>
	private static ManualTradeResponse ToResponse(ManualTrade trade) => new(
		trade.Id,
		trade.ExecutedAt,
		trade.Symbol,
		trade.Side,
		trade.Quantity,
		trade.Price,
		trade.FeeUsdt,
		trade.ConstructionId);
}

/// <summary>Команда внесения ручной сделки; constructionId опционален — null означает «Входящие».</summary>
/// <param name="ExecutedAt">Время исполнения сделки.</param>
/// <param name="Symbol">Инструмент сделки.</param>
/// <param name="Side">Сторона сделки: Buy или Sell.</param>
/// <param name="Quantity">Количество сделки: положительное число.</param>
/// <param name="Price">Цена сделки: положительное число.</param>
/// <param name="FeeUsdt">Комиссия в USDT; null — комиссии нет.</param>
/// <param name="ConstructionId">Конструкция для немедленной привязки; null — во «Входящие».</param>
public sealed record CreateManualTradeRequest(
	DateTimeOffset ExecutedAt,
	string Symbol,
	string Side,
	decimal Quantity,
	decimal Price,
	decimal? FeeUsdt,
	long? ConstructionId);

/// <summary>Команда правки ручной сделки; привязка правкой не меняется.</summary>
/// <param name="ExecutedAt">Новое время исполнения сделки.</param>
/// <param name="Symbol">Новый инструмент сделки.</param>
/// <param name="Side">Новая сторона сделки: Buy или Sell.</param>
/// <param name="Quantity">Новое количество: положительное число.</param>
/// <param name="Price">Новая цена: положительное число.</param>
/// <param name="FeeUsdt">Новая комиссия в USDT; null — комиссии нет.</param>
public sealed record EditManualTradeRequest(
	DateTimeOffset ExecutedAt,
	string Symbol,
	string Side,
	decimal Quantity,
	decimal Price,
	decimal? FeeUsdt);

/// <summary>Сохранённая ручная сделка в контракте API.</summary>
/// <param name="Id">Идентификатор ручной сделки.</param>
/// <param name="ExecutedAt">Время исполнения сделки.</param>
/// <param name="Symbol">Инструмент сделки.</param>
/// <param name="Side">Сторона сделки: Buy или Sell.</param>
/// <param name="Quantity">Количество сделки.</param>
/// <param name="Price">Цена сделки.</param>
/// <param name="FeeUsdt">Комиссия сделки в USDT.</param>
/// <param name="ConstructionId">Привязка к конструкции; null — сделка во «Входящих».</param>
public sealed record ManualTradeResponse(
	long Id,
	DateTimeOffset ExecutedAt,
	string Symbol,
	string Side,
	decimal Quantity,
	decimal Price,
	decimal FeeUsdt,
	long? ConstructionId);

/// <summary>Ошибка команды ручной сделки.</summary>
/// <param name="Error">Человекочитаемая причина ошибки.</param>
public sealed record ManualTradeErrorResponse(string Error);
