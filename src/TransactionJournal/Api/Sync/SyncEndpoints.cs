namespace TransactionJournal.Api.Sync;

using Microsoft.AspNetCore.Builder;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application.Sync;
using TransactionJournal.Domain.Data;

/// <summary>
/// Эндпоинт синхронизации раздела «Конструкции»: компактная кнопка шапки
/// запускает полный проход синхронизации с Bybit и возвращает итог закрытого
/// запуска. Подробности и журнал запусков остаются разделу «Синхронизация»;
/// слой тонкий — запуск выполняет доменный сервис синхронизации.
/// </summary>
public static class SyncEndpoints
{
	/// <summary>
	/// Подключает эндпоинт синхронизации к версионированной группе API.
	/// </summary>
	public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder api)
	{
		// Команда «Синхронизировать» шапки раздела: все команды SPA идут через
		// единый версионированный API одного хоста.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Кнопка компактная, с индикатором выполнения; состояние и подробный
		// результат закреплены за разделом «Синхронизация» — шапке достаточно
		// итога закрытого запуска.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		api.MapPost("/sync", async (IJournalSyncService syncService, CancellationToken cancellationToken) =>
		{
			JournalSyncResult result;
			try
			{
				result = await syncService.SyncAsync(cancellationToken);
			}
			catch (BybitApiException exception)
			{
				// Ошибка биржи закрывает запуск со статусом Failed: шапка
				// показывает сбой с причиной — молчаливое проглатывание
				// оставило бы владельца со старыми данными без объяснения.
				return Results.Json(new SyncFailedResponse(exception.Message), statusCode: StatusCodes.Status502BadGateway);
			}

			return Results.Json(new SyncRunResponse(
				SerializeMode(result.Run.Mode),
				SerializeStatus(result.Run.Status),
				result.Run.StartedAt,
				result.Run.FinishedAt,
				result.Run.NewExecutions,
				result.Run.NewDeliveries,
				result.Run.NewInstruments));
		}).WithTags("Синхронизация");

		return api;
	}

	/// <summary>Стабильная строка режима запуска в контракте API.</summary>
	private static string SerializeMode(SyncRunMode mode) => mode switch
	{
		SyncRunMode.Backfill => "backfill",
		SyncRunMode.Incremental => "incremental",
		_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
	};

	/// <summary>Стабильная строка статуса запуска в контракте API.</summary>
	private static string SerializeStatus(SyncRunStatus status) => status switch
	{
		SyncRunStatus.Running => "running",
		SyncRunStatus.Succeeded => "succeeded",
		SyncRunStatus.Failed => "failed",
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
	};
}

/// <summary>Итог запуска синхронизации для компактной кнопки шапки.</summary>
/// <param name="Mode">Режим запуска: backfill или incremental.</param>
/// <param name="Status">Статус закрытого запуска: succeeded или failed.</param>
/// <param name="StartedAt">Момент запуска.</param>
/// <param name="FinishedAt">Момент завершения запуска.</param>
/// <param name="NewExecutions">Новых записей исполнения за запуск.</param>
/// <param name="NewDeliveries">Новых delivery-записей за запуск.</param>
/// <param name="NewInstruments">Новых инструментов справочника за запуск.</param>
public sealed record SyncRunResponse(
	string Mode,
	string Status,
	DateTimeOffset StartedAt,
	DateTimeOffset? FinishedAt,
	int NewExecutions,
	int NewDeliveries,
	int NewInstruments);

/// <summary>Сбой синхронизации: запуск закрыт со статусом Failed, причина — в тексте.</summary>
/// <param name="Error">Человекочитаемая причина сбоя запуска.</param>
public sealed record SyncFailedResponse(string Error);
