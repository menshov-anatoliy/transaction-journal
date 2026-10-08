namespace TransactionJournal.Api.Inbox;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using TransactionJournal.Application;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Infrastructure.ReadModels;

/// <summary>
/// Эндпоинты раздела «Входящие»: чтение непривязанных сделок с набором целей,
/// счётчик бейджа навигации и команды разбора — массовая привязка, создание
/// конструкции из выбранного и инкрементальная сборка. Слой фиксирует
/// HTTP-контракт `/api/v1` и делегирует действия существующим use-case
/// сервисам без добавления доменных правил.
/// </summary>
public static class InboxEndpoints
{
	/// <summary>
	/// Подключает эндпоинты раздела «Входящие» к версионированной группе API.
	/// </summary>
	public static RouteGroupBuilder MapInboxEndpoints(this RouteGroupBuilder api)
	{
		var group = api.MapGroup("/inbox").WithTags("Входящие");

		// Экран «Входящие» получает непривязанные сделки и список целей одним
		// запросом через единый API хоста: фильтры и разбор работают без доступа
		// к Blazor-сервисам.
		// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
		// Карта переноса №10/№12/№17: разбор, сборка и бейдж строятся поверх
		// этого контракта раздела.
		// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
		// Транспортный инвариант SPA: все данные и команды идут через
		// версионированный `/api/v1` одного хоста.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		group.MapGet("/", async (
			[FromServices] InboxReadModel inbox,
			[FromServices] IConstructionListReadModel list,
			CancellationToken cancellationToken) =>
		{
			IReadOnlyList<MaterializedTrade> trades;
			ConstructionListData constructions;
			try
			{
				trades = await inbox.ListAsync(cancellationToken).ConfigureAwait(false);
				constructions = await list.ReadAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is TradeMaterializationException
				or ExpiryMaterializationException
				or InstrumentResolveException)
			{
				return Results.Json(new InboxErrorResponse(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			var items = trades.Select(trade => new InboxTradeResponse(
				trade.ExecId,
				trade.Symbol,
				trade.ExecutedAt,
				trade.Quantity > 0m,
				trade.Quantity,
				trade.Price,
				Math.Abs(trade.Quantity) * trade.Price,
				trade.Fee,
				trade.FeeCurrency))
				.ToArray();

			var targets = constructions.Items
				.Select(item => new InboxTargetConstructionResponse(
					item.ConstructionId,
					item.Name,
					SerializeStatus(item.Status),
					item.TotalPnL))
				.ToArray();

			return Results.Json(new InboxOverviewResponse(items, targets));
		});

		// Бейдж «Входящих» в левой навигации читает тот же счётчик непривязанных
		// сделок, что и экран, и обновляется отдельным лёгким запросом.
		// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
		// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
		group.MapGet("/count", async ([FromServices] IFrameReadModel frame, CancellationToken cancellationToken) =>
		{
			var count = await frame.CountInboxAsync(cancellationToken).ConfigureAwait(false);
			return Results.Json(new InboxCountResponse(count));
		});

		// Массовая привязка выбранных сделок к целевой конструкции: операция
		// атомарная на стороне use-case, HTTP-слой валидирует только форму ввода.
		// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
		group.MapPost("/bind", async (
			[FromBody] BindInboxTradesRequest request,
			[FromServices] ITradeBindingService bindings,
			CancellationToken cancellationToken) =>
		{
			if (request.ExecIds.Count == 0 || request.ExecIds.Any(string.IsNullOrWhiteSpace))
			{
				return Results.Json(new InboxErrorResponse("Список выбранных сделок не задан."), statusCode: StatusCodes.Status400BadRequest);
			}

			try
			{
				await bindings.BindBatchAsync(request.ConstructionId, request.ExecIds, cancellationToken).ConfigureAwait(false);
			}
			catch (ArgumentException)
			{
				return Results.Json(new InboxErrorResponse("Проверьте заполненные поля команды привязки."), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (ConstructionNotFoundException exception)
			{
				return Results.Json(new InboxErrorResponse($"Целевая конструкция не найдена: {exception.ConstructionId}."), statusCode: StatusCodes.Status404NotFound);
			}
			catch (TradeNotFoundException exception)
			{
				return Results.Json(new InboxErrorResponse($"Сделка {exception.ExecId} не найдена в журнале."), statusCode: StatusCodes.Status404NotFound);
			}

			return Results.NoContent();
		});

		// Создание конструкции из выбранных: сервис конструкций создаёт цель в
		// статусе «открыта», после чего сделки той же командой привязываются в неё.
		// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
		group.MapPost("/create-construction", async (
			[FromBody] CreateInboxConstructionRequest request,
			[FromServices] IConstructionService constructions,
			[FromServices] ITradeBindingService bindings,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.Name))
			{
				return Results.Json(new InboxErrorResponse("Имя конструкции не может быть пустым."), statusCode: StatusCodes.Status400BadRequest);
			}

			if (request.ExecIds.Count == 0 || request.ExecIds.Any(string.IsNullOrWhiteSpace))
			{
				return Results.Json(new InboxErrorResponse("Список выбранных сделок не задан."), statusCode: StatusCodes.Status400BadRequest);
			}

			try
			{
				var created = await constructions
					.CreateAsync(request.Name.Trim(), request.AllocatedCapitalUsdt, cancellationToken: cancellationToken)
					.ConfigureAwait(false);
				await bindings.BindBatchAsync(created.Id, request.ExecIds, cancellationToken).ConfigureAwait(false);
				return Results.Json(new CreatedInboxConstructionResponse(created.Id));
			}
			catch (ArgumentException)
			{
				return Results.Json(new InboxErrorResponse("Проверьте заполненные поля команды создания."), statusCode: StatusCodes.Status400BadRequest);
			}
		});

		// Инкрементальная сборка «Собрать из Входящих»: команда создаёт новые
		// конструкции только из непривязанных сделок и возвращает счётчики итога.
		// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
		// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
		group.MapPost("/assemble", async ([FromServices] IConstructionAssemblyService assembly, CancellationToken cancellationToken) =>
		{
			var result = await assembly.AssembleInboxAsync(cancellationToken).ConfigureAwait(false);
			return Results.Json(new AssembleInboxResponse(
				result.ConstructionsCount,
				result.BoundCount,
				result.TradesInInbox));
		});

		return api;
	}

	/// <summary>Стабильная строка статуса конструкции в контракте списка целей.</summary>
	private static string SerializeStatus(ConstructionStatus status) => status switch
	{
		ConstructionStatus.Open => "open",
		ConstructionStatus.Closed => "closed",
		ConstructionStatus.Archived => "archived",
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
	};
}

/// <summary>Снимок раздела «Входящие»: непривязанные сделки и список целей привязки.</summary>
/// <param name="Items">Непривязанные сделки «Входящих» новыми сверху.</param>
/// <param name="Targets">Список конструкций-целей для привязки.</param>
public sealed record InboxOverviewResponse(
	IReadOnlyList<InboxTradeResponse> Items,
	IReadOnlyList<InboxTargetConstructionResponse> Targets);

/// <summary>Строка непривязанной сделки для таблицы раздела.</summary>
/// <param name="ExecId">Ключ исполнения сделки на бирже.</param>
/// <param name="Symbol">Инструмент сделки.</param>
/// <param name="ExecutedAt">Время исполнения сделки.</param>
/// <param name="IsBuy">Признак направления: true — покупка, false — продажа.</param>
/// <param name="Quantity">Знаковое количество сделки.</param>
/// <param name="Price">Цена исполнения.</param>
/// <param name="AmountUsdt">Сумма исполнения в USDT без знака (|qty| × price).</param>
/// <param name="Fee">Комиссия со знаком биржевой записи.</param>
/// <param name="FeeCurrency">Валюта комиссии.</param>
public sealed record InboxTradeResponse(
	string ExecId,
	string Symbol,
	DateTimeOffset ExecutedAt,
	bool IsBuy,
	decimal Quantity,
	decimal Price,
	decimal AmountUsdt,
	decimal Fee,
	string? FeeCurrency);

/// <summary>Компактная цель привязки сделки в разделе «Входящие».</summary>
/// <param name="ConstructionId">Идентификатор конструкции-цели.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Статус конструкции: open/closed/archived.</param>
/// <param name="TotalPnL">Итог конструкции для компактного списка целей.</param>
public sealed record InboxTargetConstructionResponse(
	long ConstructionId,
	string Name,
	string Status,
	decimal? TotalPnL);

/// <summary>Ответ счётчика непривязанных сделок для бейджа навигации.</summary>
/// <param name="Count">Число сделок «Входящих».</param>
public sealed record InboxCountResponse(int Count);

/// <summary>Команда массовой привязки выбранных сделок к конструкции.</summary>
/// <param name="ConstructionId">Идентификатор целевой конструкции.</param>
/// <param name="ExecIds">Список ключей выбранных сделок.</param>
public sealed record BindInboxTradesRequest(long ConstructionId, IReadOnlyList<string> ExecIds);

/// <summary>Команда создания конструкции из выбранных сделок.</summary>
/// <param name="Name">Имя новой конструкции.</param>
/// <param name="AllocatedCapitalUsdt">Необязательный капитал новой конструкции.</param>
/// <param name="ExecIds">Список ключей сделок для привязки к новой конструкции.</param>
public sealed record CreateInboxConstructionRequest(
	string Name,
	decimal? AllocatedCapitalUsdt,
	IReadOnlyList<string> ExecIds);

/// <summary>Итог создания конструкции из «Входящих».</summary>
/// <param name="ConstructionId">Идентификатор созданной конструкции.</param>
public sealed record CreatedInboxConstructionResponse(long ConstructionId);

/// <summary>Итог инкрементальной сборки из «Входящих».</summary>
/// <param name="ConstructionsCount">Число созданных конструкций.</param>
/// <param name="BoundCount">Сколько сделок привязано командой.</param>
/// <param name="TradesInInbox">Сколько сделок осталось во «Входящих» после команды.</param>
public sealed record AssembleInboxResponse(int ConstructionsCount, int BoundCount, int TradesInInbox);

/// <summary>Ошибка команды или чтения раздела «Входящие».</summary>
/// <param name="Error">Человекочитаемая причина ошибки.</param>
public sealed record InboxErrorResponse(string Error);
