namespace TransactionJournal.Api.Sync;

using Microsoft.AspNetCore.Builder;
using TransactionJournal.Application;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application.Ops;
using TransactionJournal.Application.Sync;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;

/// <summary>
/// Эндпоинты раздела «Синхронизация»: состояние подключения Bybit, политика
/// резервной копии, ручной запуск синхронизации, журнал запусков и опасные
/// команды обслуживания (полный пересбор и сброс состояния категории). Слой
/// фиксирует HTTP-контракты `/api/v1` и делегирует работу существующим
/// доменным сервисам без добавления собственной бизнес-логики.
/// </summary>
public static class SyncEndpoints
{
	/// <summary>
	/// Подключает эндпоинт синхронизации к версионированной группе API.
	/// </summary>
	public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder api)
	{
		var group = api.MapGroup("/sync").WithTags("Синхронизация");

		// Команда «Синхронизировать» шапки раздела: все команды SPA идут через
		// единый версионированный API одного хоста.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Кнопка компактная, с индикатором выполнения; состояние и подробный
		// результат закреплены за разделом «Синхронизация» — шапке достаточно
		// итога закрытого запуска.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapPost("/", async (IJournalSyncService syncService, CancellationToken cancellationToken) =>
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
				result.Run.NewInstruments,
				result.ProjectionError,
				SerializeReconciliationWarnings(result),
				CollectSkippedAreas(result),
				CollectUnresolvedInstruments(result),
				result.UncoveredBaseCoins));
		}).WithTags("Синхронизация");

		// Блок подключения и переключатель бэкапа читаются одним запросом:
		// либо маска ключа и описание аккаунта, либо подсказка для appsettings.
		// Контракт раздела публикуется через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
		// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
		group.MapGet("/settings", async (
			SettingsReadModel connection,
			IBackupPolicyStore backupPolicy,
			CancellationToken cancellationToken) =>
		{
			var connectionInfo = connection.GetConnection();
			string? backupPolicyError = null;
			var backupBeforeSyncEnabled = true;
			try
			{
				backupBeforeSyncEnabled = await backupPolicy.IsBackupBeforeSyncEnabledAsync(cancellationToken);
			}
			catch (Exception exception)
			{
				backupPolicyError = $"Переключатель копирования недоступен: {exception.Message}";
			}

			return Results.Json(new SyncSettingsResponse(
				new BybitConnectionResponse(
					IsConfigured: connectionInfo is not null,
					MaskedApiKey: connectionInfo?.MaskedApiKey,
					AccountDescription: connectionInfo?.AccountDescription,
					SecretStorage: connectionInfo?.SecretStorage,
					SetupHint: connectionInfo is null
						? $"Ключ не настроен: заполните {BybitCredentialsConfig.ApiKeyConfigKey} и {BybitCredentialsConfig.ApiSecretConfigKey} в файле {BybitCredentialsConfig.LocalFileName}."
						: null),
				backupBeforeSyncEnabled,
				backupPolicyError));
		});

		// Переключатель бэкапа хранится в базе AppSetting и переживает перезапуск:
		// запись ошибки не маскируется успехом, поэтому команда возвращает 503.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
		group.MapPut("/settings/backup-before-sync", async (
			SyncBackupPolicyRequest request,
			IBackupPolicyStore backupPolicy,
			CancellationToken cancellationToken) =>
		{
			try
			{
				await backupPolicy.SetBackupBeforeSyncEnabledAsync(request.Enabled, cancellationToken);
				return Results.NoContent();
			}
			catch (Exception exception)
			{
				return Results.Json(new SyncFailedResponse($"Переключатель не сохранён: {exception.Message}"), statusCode: StatusCodes.Status503ServiceUnavailable);
			}
		});

		// Табличный журнал запусков для раздела «Синхронизация»: режим, статус,
		// счётчики и заметки запуска из сохранённых предупреждений.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
		group.MapGet("/runs", async (
			ISyncJournalReadModel journal,
			int? limit,
			CancellationToken cancellationToken) =>
		{
			try
			{
				var rows = await journal.ReadAsync(limit ?? 20, cancellationToken);
				return Results.Json(new SyncRunsResponse(rows
					.Select(row => new SyncRunJournalEntryResponse(
						row.Id,
						row.StartedAt,
						row.FinishedAt,
						SerializeMode(row.Mode),
						SerializeStatus(row.Status),
						row.Error,
						row.NewExecutions,
						row.NewDeliveries,
						row.NewInstruments,
						row.Warnings.SkippedAreas,
						row.Warnings.UnresolvedInstruments,
						row.Warnings.UncoveredBaseCoins))
					.ToArray()));
			}
			catch (Exception exception)
			{
				return Results.Json(new SyncFailedResponse($"Журнал синхронизаций недоступен: {exception.Message}"), statusCode: StatusCodes.Status503ServiceUnavailable);
			}
		});

		// Опасная команда полного пересбора: обслуживание запускается только явной
		// кнопкой раздела «Синхронизация», данные пересбора считает доменный use-case.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
		group.MapPost("/rebuild", async (IConstructionAssemblyService assembly, CancellationToken cancellationToken) =>
		{
			try
			{
				var result = await assembly.RebuildAsync(cancellationToken);
				return Results.Json(new SyncRebuildResponse(result.ConstructionsCount, result.BoundCount, result.TradesInInbox));
			}
			catch (Exception exception)
			{
				return Results.Json(new SyncFailedResponse($"Пересбор не выполнен: {exception.Message}"), statusCode: StatusCodes.Status503ServiceUnavailable);
			}
		});

		// Сброс состояния категории возвращает категорию в режим первичного backfill:
		// очищаются только водяные знаки category, а сырые данные остаются.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
		group.MapPost("/reset-category", async (
			ResetSyncCategoryRequest request,
			IExecutionSyncStateStore stateStore,
			CancellationToken cancellationToken) =>
		{
			if (string.Equals(request.Category, "linear", StringComparison.Ordinal) == false
				&& string.Equals(request.Category, "option", StringComparison.Ordinal) == false)
			{
				return Results.Json(new SyncFailedResponse("Категория должна быть linear или option."), statusCode: StatusCodes.Status400BadRequest);
			}

			try
			{
				await stateStore.ResetAsync(request.Category, cancellationToken);
				return Results.NoContent();
			}
			catch (Exception exception)
			{
				return Results.Json(new SyncFailedResponse($"Сброс состояния не выполнен: {exception.Message}"), statusCode: StatusCodes.Status503ServiceUnavailable);
			}
		});

		return api;
	}

	/// <summary>Список предупреждений сверки последнего запуска синхронизации.</summary>
	private static IReadOnlyList<SyncReconciliationWarningResponse> SerializeReconciliationWarnings(JournalSyncResult result) =>
		result.Projection?.ReconciliationWarnings
			.Select(warning => new SyncReconciliationWarningResponse(
				warning.Symbol,
				warning.DeliveryTime,
				warning.Difference))
			.ToArray()
		?? [];

	/// <summary>Сводит перечень пропущенных областей исполнения и delivery по категориям.</summary>
	private static IReadOnlyList<string> CollectSkippedAreas(JournalSyncResult result)
	{
		var executionAreas = result.Executions
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.SelectMany(pair => pair.Value.SkippedAreas);
		var deliveryAreas = result.Deliveries
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.SelectMany(pair => pair.Value.SkippedAreas);
		return executionAreas.Concat(deliveryAreas).ToArray();
	}

	/// <summary>Сводит перечень неразрешённых инструментов синхронизации и проекции.</summary>
	private static IReadOnlyList<string> CollectUnresolvedInstruments(JournalSyncResult result)
	{
		var projectionSymbols = result.Projection?.UnresolvedInstruments ?? [];
		return result.UnresolvedInstruments
			.Concat(projectionSymbols)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(symbol => symbol, StringComparer.Ordinal)
			.ToArray();
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
	int NewInstruments,
	string? ProjectionError,
	IReadOnlyList<SyncReconciliationWarningResponse> ReconciliationWarnings,
	IReadOnlyList<string> SkippedAreas,
	IReadOnlyList<string> UnresolvedInstruments,
	IReadOnlyList<string> UncoveredBaseCoins);

/// <summary>Состояние подключения Bybit для блока раздела «Синхронизация».</summary>
/// <param name="IsConfigured">Признак настроенного ключа: true — показывается маска, false — инструкция appsettings.</param>
/// <param name="MaskedApiKey">Маска API-ключа, когда ключ настроен.</param>
/// <param name="AccountDescription">Описание аккаунта и прав ключа.</param>
/// <param name="SecretStorage">Место хранения секрета.</param>
/// <param name="SetupHint">Инструкция настройки ключа в appsettings при отсутствии ключа.</param>
public sealed record BybitConnectionResponse(
	bool IsConfigured,
	string? MaskedApiKey,
	string? AccountDescription,
	string? SecretStorage,
	string? SetupHint);

/// <summary>Снимок настроек раздела синхронизации.</summary>
/// <param name="Bybit">Состояние подключения Bybit для блока интерфейса.</param>
/// <param name="BackupBeforeSyncEnabled">Переключатель резервной копии перед синхронизацией.</param>
/// <param name="BackupPolicyError">Текст ошибки чтения политики; null, когда чтение успешно.</param>
public sealed record SyncSettingsResponse(
	BybitConnectionResponse Bybit,
	bool BackupBeforeSyncEnabled,
	string? BackupPolicyError);

/// <summary>Команда изменения переключателя резервной копии перед синхронизацией.</summary>
/// <param name="Enabled">Новое значение переключателя.</param>
public sealed record SyncBackupPolicyRequest(bool Enabled);

/// <summary>Табличный журнал запусков синхронизации новыми сверху.</summary>
/// <param name="Runs">Строки запусков с режимом, итогом, статусом и заметками.</param>
public sealed record SyncRunsResponse(IReadOnlyList<SyncRunJournalEntryResponse> Runs);

/// <summary>Строка журнала запусков синхронизации для таблицы SPA.</summary>
/// <param name="Id">Идентификатор запуска.</param>
/// <param name="StartedAt">Время старта запуска.</param>
/// <param name="FinishedAt">Время завершения запуска; null у выполняющегося.</param>
/// <param name="Mode">Режим запуска: backfill/incremental.</param>
/// <param name="Status">Статус запуска: running/succeeded/failed.</param>
/// <param name="Error">Текст ошибки прерванного запуска.</param>
/// <param name="NewExecutions">Новых записей исполнения.</param>
/// <param name="NewDeliveries">Новых delivery-записей.</param>
/// <param name="NewInstruments">Новых инструментов справочника.</param>
/// <param name="SkippedAreas">Пропущенные области запуска.</param>
/// <param name="UnresolvedInstruments">Неразрешённые инструменты запуска.</param>
/// <param name="UncoveredBaseCoins">Непокрытые базовые активы доски.</param>
public sealed record SyncRunJournalEntryResponse(
	long Id,
	DateTimeOffset StartedAt,
	DateTimeOffset? FinishedAt,
	string Mode,
	string Status,
	string? Error,
	int NewExecutions,
	int NewDeliveries,
	int NewInstruments,
	IReadOnlyList<string> SkippedAreas,
	IReadOnlyList<string> UnresolvedInstruments,
	IReadOnlyList<string> UncoveredBaseCoins);

/// <summary>Предупреждение сверки delivery расчёта с биржей.</summary>
/// <param name="Symbol">Символ инструмента экспирации.</param>
/// <param name="DeliveryTime">Момент расчёта экспирации.</param>
/// <param name="Difference">Величина расхождения с биржей.</param>
public sealed record SyncReconciliationWarningResponse(string Symbol, DateTimeOffset DeliveryTime, decimal Difference);

/// <summary>Итог полного пересбора конструкций из опасной зоны.</summary>
/// <param name="ConstructionsCount">Сколько конструкций собрано.</param>
/// <param name="BoundCount">Сколько сделок привязано.</param>
/// <param name="TradesInInbox">Сколько сделок осталось во «Входящих».</param>
public sealed record SyncRebuildResponse(int ConstructionsCount, int BoundCount, int TradesInInbox);

/// <summary>Запрос сброса состояния категории синхронизации.</summary>
/// <param name="Category">Категория для сброса: linear или option.</param>
public sealed record ResetSyncCategoryRequest(string Category);

/// <summary>Сбой синхронизации: запуск закрыт со статусом Failed, причина — в тексте.</summary>
/// <param name="Error">Человекочитаемая причина сбоя запуска.</param>
public sealed record SyncFailedResponse(string Error);
