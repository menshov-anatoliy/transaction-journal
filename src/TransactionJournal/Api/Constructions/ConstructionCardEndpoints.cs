namespace TransactionJournal.Api.Constructions;

using Microsoft.AspNetCore.Builder;
using TransactionJournal.Application;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;

/// <summary>
/// Эндпоинты карточки конструкции: полный снимок экрана одним запросом
/// и команды действий над конструкцией, её записями и комментариями.
/// Слой тонкий: правила принадлежат доменным сервисам, эндпоинт фиксирует
/// JSON-контракт карточки и переводит отказы домена в состояния HTTP.
/// </summary>
public static class ConstructionCardEndpoints
{
	/// <summary>
	/// Подключает эндпоинты карточки к версионированной группе API.
	/// </summary>
	public static RouteGroupBuilder MapConstructionCardEndpoints(this RouteGroupBuilder api)
	{
		var group = api.MapGroup("/constructions").WithTags("Карточка конструкции");

		// Снимок карточки одним запросом: шапка с параметрами и комментарием,
		// метрики с периодом и длительностью, четыре таблицы записей
		// и предупреждения об избыточных закрывающих записях.
		// Все данные карточки идут через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Карточка — полная информация и всё управление: состав снимка
		// закреплён концепцией §4 (паритет №3–№9).
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		group.MapGet("/{constructionId:long}", async (long constructionId, IConstructionDetailReadModel details, CancellationToken cancellationToken) =>
		{
			ConstructionDetailData data;
			try
			{
				data = await details.ReadAsync(constructionId, cancellationToken);
			}
			catch (ConstructionNotFoundException)
			{
				// Чужой или удалённый идентификатор — явный 404: карточка
				// показывает состояние «не найдена» вместо пустых таблиц.
				return Results.Json(
					new ConstructionCardNotFoundResponse(constructionId),
					statusCode: StatusCodes.Status404NotFound);
			}
			catch (Exception exception) when (exception is TradeMaterializationException
				or ExpiryMaterializationException
				or InstrumentResolveException)
			{
				// Повреждённое сырьё не даёт собрать карточку: явное состояние
				// недоступности вместо пустого экрана.
				return Results.Json(new ConstructionCardUnavailableResponse(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			return Results.Json(ToCardResponse(data));
		});

		return api;
	}

	/// <summary>Перевод доменного снимка деталей в контракт карточки.</summary>
	private static ConstructionCardResponse ToCardResponse(ConstructionDetailData data) => new(
		data.ConstructionId,
		data.Name,
		SerializeStatus(data.Status),
		data.AllocatedCapitalUsdt,
		data.RiskPercent,
		data.RiskUsdt,
		SerializeUnit(data.RiskUnit),
		data.ProfitPercent,
		data.ProfitUsdt,
		SerializeUnit(data.ProfitUnit),
		data.Comment,
		data.HasOpenResidual,
		data.HasMarkFailure,
		data.MarksAsOf,
		new ConstructionCardMetricsResponse(
			data.Metrics.RealizedPnL,
			data.Metrics.UnrealizedPnL,
			data.Metrics.AdjustmentsPnL,
			data.Metrics.TotalPnL,
			data.Metrics.TotalPnLPercent,
			data.Metrics.RealizedPnLPercent,
			data.Metrics.UnrealizedPnLPercent,
			data.Metrics.AdjustmentsPnLPercent,
			data.Metrics.MarkValue,
			data.Metrics.CapitalUsagePercent,
			data.Metrics.OpenedAt,
			data.Metrics.ClosedAt,
			data.Metrics.Duration is { } duration ? (long?)Math.Round(duration.TotalSeconds) : null),
		data.Positions.Select(position => new ConstructionCardPositionResponse(
			position.Symbol,
			position.Residual,
			position.AverageEntryPrice,
			position.AverageClosePrice,
			position.RealizedPnL,
			position.RealizedPnLPercent,
			position.UnrealizedPnL,
			position.UnrealizedPnLPercent,
			position.TotalPnL,
			position.TotalPnLPercent,
			position.AccumulatedFees,
			position.OpenedAt,
			position.ClosedAt,
			position.IsOpen,
			position.Comment,
			position.MarkValue,
			position.PriceChangePercent)).ToArray(),
		data.Trades.Select(trade => new ConstructionCardTradeResponse(
			trade.ExecId,
			trade.Symbol,
			trade.ExecutedAt,
			trade.IsBuy,
			trade.Quantity,
			trade.Price,
			trade.AmountUsdt,
			trade.Fee,
			trade.Comment)).ToArray(),
		data.ClosingEntries.Select(entry => new ConstructionCardClosingEntryResponse(
			entry.ClosedAt,
			SerializeClosingKind(entry.Kind),
			entry.Symbol,
			entry.Quantity,
			entry.Price,
			entry.AmountUsdt,
			entry.ManualMarkId)).ToArray(),
		data.ClosingWarnings.Select(warning => new ConstructionCardClosingWarningResponse(
			SerializeClosingKind(warning.Kind),
			warning.Symbol,
			warning.ClosedAt)).ToArray(),
		data.Adjustments.Select(adjustment => new ConstructionCardAdjustmentResponse(
			adjustment.AdjustmentId,
			adjustment.Date,
			adjustment.Description,
			SerializeAdjustmentSource(adjustment.Source),
			adjustment.AmountUsdt)).ToArray());

	/// <summary>Стабильная строка статуса конструкции в контракте API.</summary>
	private static string SerializeStatus(ConstructionStatus status) => status switch
	{
		ConstructionStatus.Open => "open",
		ConstructionStatus.Closed => "closed",
		ConstructionStatus.Archived => "archived",
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
	};

	/// <summary>Стабильная строка единицы плановой границы: null — параметр не задан.</summary>
	private static string? SerializeUnit(TargetUnit? unit) => unit switch
	{
		TargetUnit.Percent => "percent",
		TargetUnit.Usdt => "usdt",
		null => null,
		_ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
	};

	/// <summary>Стабильная строка вида закрывающей записи в контракте API.</summary>
	private static string SerializeClosingKind(PositionClosingKind kind) => kind switch
	{
		PositionClosingKind.Delivery => "delivery",
		PositionClosingKind.OtmExpiry => "otm-expiry",
		PositionClosingKind.ManualMark => "manual-mark",
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};

	/// <summary>Стабильная строка источника корректировки в контракте API.</summary>
	private static string SerializeAdjustmentSource(PnLAdjustmentSource source) => source switch
	{
		PnLAdjustmentSource.Robot => "robot",
		PnLAdjustmentSource.Manual => "manual",
		_ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
	};
}

/// <summary>Полный снимок карточки конструкции.</summary>
/// <param name="ConstructionId">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус: open, closed или archived.</param>
/// <param name="AllocatedCapitalUsdt">Выделенный капитал; null, когда не задан.</param>
/// <param name="RiskPercent">Риск в процентах от капитала; null без величины.</param>
/// <param name="RiskUsdt">Риск в USDT; null без величины.</param>
/// <param name="RiskUnit">Единица ввода риска — первоисточник; null, когда риск не задан.</param>
/// <param name="ProfitPercent">Профит в процентах от капитала; null без величины.</param>
/// <param name="ProfitUsdt">Профит в USDT; null без величины.</param>
/// <param name="ProfitUnit">Единица ввода профита — первоисточник; null, когда профит не задан.</param>
/// <param name="Comment">Комментарий конструкции в Markdown; null — комментария нет.</param>
/// <param name="HasOpenResidual">У конструкции есть открытый остаток.</param>
/// <param name="HasMarkFailure">Сбой котировок оставил нереализованную оценку непостроенной.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки; null при сбое или без остатков.</param>
/// <param name="Metrics">Метрики: итог с разбивкой, стоимость, занятость и период.</param>
/// <param name="Positions">Строки таблицы позиций.</param>
/// <param name="Trades">Строки таблицы сделок.</param>
/// <param name="ClosingEntries">Строки таблицы закрывающих записей.</param>
/// <param name="ClosingWarnings">Предупреждения об избыточных закрывающих записях.</param>
/// <param name="Adjustments">Строки таблицы корректировок PnL.</param>
public sealed record ConstructionCardResponse(
	long ConstructionId,
	string Name,
	string Status,
	decimal? AllocatedCapitalUsdt,
	decimal? RiskPercent,
	decimal? RiskUsdt,
	string? RiskUnit,
	decimal? ProfitPercent,
	decimal? ProfitUsdt,
	string? ProfitUnit,
	string? Comment,
	bool HasOpenResidual,
	bool HasMarkFailure,
	DateTimeOffset? MarksAsOf,
	ConstructionCardMetricsResponse Metrics,
	IReadOnlyList<ConstructionCardPositionResponse> Positions,
	IReadOnlyList<ConstructionCardTradeResponse> Trades,
	IReadOnlyList<ConstructionCardClosingEntryResponse> ClosingEntries,
	IReadOnlyList<ConstructionCardClosingWarningResponse> ClosingWarnings,
	IReadOnlyList<ConstructionCardAdjustmentResponse> Adjustments);

/// <summary>Метрики карточки: сводка kstrip с периодом и длительностью.</summary>
/// <param name="RealizedPnL">Реализованный PnL конструкции.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL; null при сбое марок.</param>
/// <param name="AdjustmentsPnL">Сумма внешних корректировок PnL.</param>
/// <param name="TotalPnL">Итог конструкции; null при сбое марок.</param>
/// <param name="TotalPnLPercent">Итог в процентах от капитала; null без базы.</param>
/// <param name="RealizedPnLPercent">Реализованный PnL в процентах; null без базы.</param>
/// <param name="UnrealizedPnLPercent">Нереализованный PnL в процентах; null без базы или при сбое.</param>
/// <param name="AdjustmentsPnLPercent">Корректировки в процентах; null без базы.</param>
/// <param name="MarkValue">Стоимость открытых остатков по маркам; null без остатков или при сбое.</param>
/// <param name="CapitalUsagePercent">Занятость капитала в процентах; null без базы.</param>
/// <param name="OpenedAt">Дата открытия — время первой сделки; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия — момент обнуления последней позиции; null у открытой.</param>
/// <param name="DurationSeconds">Длительность конструкции в секундах; null без сделок.</param>
public sealed record ConstructionCardMetricsResponse(
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	decimal AdjustmentsPnL,
	decimal? TotalPnL,
	decimal? TotalPnLPercent,
	decimal? RealizedPnLPercent,
	decimal? UnrealizedPnLPercent,
	decimal? AdjustmentsPnLPercent,
	decimal? MarkValue,
	decimal? CapitalUsagePercent,
	DateTimeOffset? OpenedAt,
	DateTimeOffset? ClosedAt,
	long? DurationSeconds);

/// <summary>Строка таблицы позиций карточки.</summary>
/// <param name="Symbol">Инструмент позиции.</param>
/// <param name="Residual">Чистый остаток со знаком; ноль — закрыта.</param>
/// <param name="AverageEntryPrice">Средняя цена входа; null без открывающих частей.</param>
/// <param name="AverageClosePrice">Средняя цена закрытия; null без закрывающих частей.</param>
/// <param name="RealizedPnL">Реализованный PnL позиции.</param>
/// <param name="RealizedPnLPercent">Реализованный PnL процентом от капитала; null без базы.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL; null при сбое марок, у закрытой — null.</param>
/// <param name="UnrealizedPnLPercent">Нереализованный PnL процентом; null вместе с ним.</param>
/// <param name="TotalPnL">Общий PnL позиции; null при сбое марок у открытой.</param>
/// <param name="TotalPnLPercent">Общий PnL процентом; null без базы.</param>
/// <param name="AccumulatedFees">Накопленные комиссии со знаком.</param>
/// <param name="OpenedAt">Время открытия — первая запись позиции.</param>
/// <param name="ClosedAt">Время закрытия; null, пока открыта.</param>
/// <param name="IsOpen">Позиция открыта — остаток не нулевой.</param>
/// <param name="Comment">Комментарий позиции в Markdown; null — комментария нет.</param>
/// <param name="MarkValue">Стоимость остатка по марке; null у закрытой и при сбое марок.</param>
/// <param name="PriceChangePercent">Изменение цены остатка в процентах; null у закрытой и при сбое.</param>
public sealed record ConstructionCardPositionResponse(
	string Symbol,
	decimal Residual,
	decimal? AverageEntryPrice,
	decimal? AverageClosePrice,
	decimal RealizedPnL,
	decimal? RealizedPnLPercent,
	decimal? UnrealizedPnL,
	decimal? UnrealizedPnLPercent,
	decimal? TotalPnL,
	decimal? TotalPnLPercent,
	decimal AccumulatedFees,
	DateTimeOffset OpenedAt,
	DateTimeOffset? ClosedAt,
	bool IsOpen,
	string? Comment,
	decimal? MarkValue,
	decimal? PriceChangePercent);

/// <summary>Строка таблицы сделок карточки.</summary>
/// <param name="ExecId">Биржевой идентификатор исполнения — ключ сделки.</param>
/// <param name="Symbol">Инструмент сделки.</param>
/// <param name="ExecutedAt">Время исполнения сделки.</param>
/// <param name="IsBuy">true — покупка, false — продажа.</param>
/// <param name="Quantity">Количество без знака.</param>
/// <param name="Price">Цена исполнения.</param>
/// <param name="AmountUsdt">Сумма сделки в USDT.</param>
/// <param name="Fee">Комиссия со знаком: положительная уплачена, отрицательная — rebate.</param>
/// <param name="Comment">Комментарий сделки в Markdown; null — комментария нет.</param>
public sealed record ConstructionCardTradeResponse(
	string ExecId,
	string Symbol,
	DateTimeOffset ExecutedAt,
	bool IsBuy,
	decimal Quantity,
	decimal Price,
	decimal AmountUsdt,
	decimal Fee,
	string? Comment);

/// <summary>Строка таблицы закрывающих записей карточки.</summary>
/// <param name="ClosedAt">Момент закрытия: биржевая запись или время пометки.</param>
/// <param name="Kind">Вид записи: delivery, otm-expiry или manual-mark.</param>
/// <param name="Symbol">Инструмент закрываемой позиции.</param>
/// <param name="Quantity">Знаковое количество, обнулившее остаток.</param>
/// <param name="Price">Эффективная цена закрытия; null при неизвестной марке.</param>
/// <param name="AmountUsdt">Сумма закрытия со знаком; null при неизвестной цене.</param>
/// <param name="ManualMarkId">Ключ ручной пометки для правки и удаления; null у биржевых записей.</param>
public sealed record ConstructionCardClosingEntryResponse(
	DateTimeOffset ClosedAt,
	string Kind,
	string Symbol,
	decimal Quantity,
	decimal? Price,
	decimal? AmountUsdt,
	long? ManualMarkId);

/// <summary>Предупреждение об избыточной закрывающей записи.</summary>
/// <param name="Kind">Вид избыточной записи: delivery, otm-expiry или manual-mark.</param>
/// <param name="Symbol">Инструмент избыточной записи.</param>
/// <param name="ClosedAt">Момент, на который запись претендовала на закрытие.</param>
public sealed record ConstructionCardClosingWarningResponse(
	string Kind,
	string Symbol,
	DateTimeOffset ClosedAt);

/// <summary>Строка таблицы корректировок PnL карточки.</summary>
/// <param name="AdjustmentId">Идентификатор корректировки.</param>
/// <param name="Date">Дата корректировки.</param>
/// <param name="Description">Описание; null — описания нет.</param>
/// <param name="Source">Источник: robot или manual.</param>
/// <param name="AmountUsdt">Знаковая сумма корректировки в USDT.</param>
public sealed record ConstructionCardAdjustmentResponse(
	long AdjustmentId,
	DateTimeOffset Date,
	string? Description,
	string Source,
	decimal AmountUsdt);

/// <summary>Ответ 404 карточки: конструкции с идентификатором нет в журнале.</summary>
/// <param name="ConstructionId">Идентификатор, по которому конструкции не нашлось.</param>
public sealed record ConstructionCardNotFoundResponse(long ConstructionId);

/// <summary>Состояние недоступности карточки: журнал не прочитан, причина — в тексте.</summary>
/// <param name="Error">Человекочитаемая причина недоступности.</param>
public sealed record ConstructionCardUnavailableResponse(string Error);
