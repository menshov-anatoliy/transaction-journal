namespace TransactionJournal.Api.Constructions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using TransactionJournal.Application;
using TransactionJournal.Application.Analytics;
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
		})
		// Контракт снимка публикуется в OpenAPI типизированной схемой ответа:
		// аддитивные поля строки syntheticCloses становятся видны в документе
		// и проверяемы контрактным тестом, а потребители снимка не затронуты.
		// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
		// Traceability: change:add-synthetic-close-dispute-ui/design#d1
		.Produces<ConstructionCardResponse>();

			// Переименование конструкции: свободная правка имени, остальные данные
			// не затрагиваются; ручное имя фиксируется и не перезаписывается сборкой.
			// Все команды карточки идут через единый версионированный API.
			// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
			// Действия конструкции живут в шапке карточки — концепция §4 (паритет №5).
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPost("/{constructionId:long}/rename", async (
				long constructionId,
				RenameConstructionRequest request,
				IConstructionService constructions,
				CancellationToken cancellationToken) =>
			{
				if (string.IsNullOrWhiteSpace(request.Name))
				{
					// Пустое имя — ошибка заполнения поля, команда в домен не прошла.
					return Results.Json(new ConstructionCardErrorResponse("Имя конструкции не может быть пустым."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunConstructionCommand(
					() => constructions.RenameAsync(constructionId, request.Name.Trim(), cancellationToken),
					errorPrefix: "Переименование не выполнено");
			});

			// Смена ручного статуса: свободные переходы «открыта» ↔ «закрыта», архив
			// и возврат из архива, восстанавливающий статус «закрыта».
			// Все команды карточки идут через единый версионированный API.
			// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
			// Возврат из архива — действие шапки карточки (паритет №28): статус до
			// архивации журнал не хранит, восстановление отдаёт «закрыта».
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPost("/{constructionId:long}/status", async (
				long constructionId,
				ChangeConstructionStatusRequest request,
				IConstructionService constructions,
				CancellationToken cancellationToken) =>
			{
				var status = DeserializeStatus(request.Status);
				if (status is null)
				{
					return Results.Json(new ConstructionCardErrorResponse("Неизвестный статус конструкции."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunConstructionCommand(
					() => constructions.ChangeStatusAsync(constructionId, status.Value, cancellationToken),
					errorPrefix: "Смена статуса не выполнена");
			});

			// Выделенный капитал: значение в USDT; null убирает капитал у конструкции,
			// ноль легитимен — правка меняет только процентные величины.
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPost("/{constructionId:long}/capital", async (
				long constructionId,
				ChangeAllocatedCapitalRequest request,
				IConstructionService constructions,
				CancellationToken cancellationToken) =>
			{
				return await RunConstructionCommand(
					() => constructions.UpdateAllocatedCapitalAsync(constructionId, request.AllocatedCapitalUsdt, cancellationToken),
					errorPrefix: "Изменение капитала не выполнено");
			});

			// Риск плановой границы: значение и единица меняются парой; оба пустых
			// равносильны удалению параметра.
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPost("/{constructionId:long}/risk", async (
				long constructionId,
				ChangeTargetRequest request,
				IConstructionService constructions,
				CancellationToken cancellationToken) =>
			{
				var unit = DeserializeUnit(request.Unit);
				if (request.Value is null && unit is not null || request.Value is not null && unit is null)
				{
					// Пара «значение + единица» либо задана целиком, либо отсутствует.
					return Results.Json(new ConstructionCardErrorResponse("Значение и единица задаются парой."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunConstructionCommand(
					() => constructions.UpdateRiskAsync(constructionId, request.Value, unit, cancellationToken),
					errorPrefix: "Изменение риска не выполнено");
			});

			// Профит плановой границы: те же правила, что у риска, — пара
			// «значение + единица» либо целиком, либо удалена.
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPost("/{constructionId:long}/profit", async (
				long constructionId,
				ChangeTargetRequest request,
				IConstructionService constructions,
				CancellationToken cancellationToken) =>
			{
				var unit = DeserializeUnit(request.Unit);
				if (request.Value is null && unit is not null || request.Value is not null && unit is null)
				{
					return Results.Json(new ConstructionCardErrorResponse("Значение и единица задаются парой."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunConstructionCommand(
					() => constructions.UpdateProfitAsync(constructionId, request.Value, unit, cancellationToken),
					errorPrefix: "Изменение профита не выполнено");
			});

			// Удаление пустой конструкции с опциональной резервной копией: копия
			// предшествует команде домена, неудача копирования отменяет удаление.
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPost("/{constructionId:long}/delete", async (
				long constructionId,
				DeleteConstructionRequest request,
				IConstructionService constructions,
				Application.Ops.IJournalBackupService backups,
				CancellationToken cancellationToken) =>
			{
				if (request.MakeBackup)
				{
					try
					{
						await backups.CreateBackupAsync("delete-construction", cancellationToken);
					}
					catch (Exception exception) when (exception is IOException or InvalidOperationException)
					{
						// Копия не создана — удаление отменено, данные нетронуты.
						return Results.Json(
							new ConstructionCardErrorResponse($"Удаление отменено: резервная копия не создана — {exception.Message}"),
							statusCode: StatusCodes.Status503ServiceUnavailable);
					}
				}

				return await RunConstructionCommand(
					() => constructions.DeleteAsync(constructionId, cancellationToken),
					errorPrefix: "Удаление не выполнено");
			});

			// Последняя известная марка инструмента — предзаполнение формы пометки
		// закрытия: SPA спрашивает величину при открытии формы, пустая марка
		// оставляет цену домену.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		group.MapGet("/{constructionId:long}/positions/{symbol}/last-mark", async (
			long constructionId,
			string symbol,
			IInstrumentMarkSource markSource,
			CancellationToken cancellationToken) =>
		{
			try
			{
				var mark = await markSource.GetLastMarkAsync(symbol, cancellationToken);
				return Results.Json(new LastInstrumentMarkResponse(mark));
			}
			catch (Exception exception) when (exception is ArgumentException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Инструмент позиции не задан."), statusCode: StatusCodes.Status400BadRequest);
			}
		});

		// Постановка ручной пометки закрытия из строки открытой позиции:
		// домен не проверяет остаток — избыточная пометка станет
		// предупреждением при чтении. Количество необязательно: незаданное
		// закрывает весь остаток на момент пометки, заданное клэмпится
		// остатком производным слоем.
		// Ручные пометки идут командами через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		group.MapPost("/{constructionId:long}/close-marks", async (
			long constructionId,
			AddManualCloseMarkRequest request,
			IManualCloseMarkService marks,
			CancellationToken cancellationToken) =>
		{
			return await RunMarkCommand(
				() => marks.AddAsync(constructionId, request.Symbol, request.MarkedAt, request.Price, request.Quantity, cancellationToken),
				errorPrefix: "Пометка закрытия не поставлена");
		});

		// Правка ручной пометки из таблицы закрывающих записей: инструмент,
		// время, цена и количество правятся свободно, пустая цена возвращает
		// пометку к последней марке при чтении, пустое количество — к закрытию
		// всего остатка.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		api.MapPut("/close-marks/{markId:long}", async (
			long markId,
			EditManualCloseMarkRequest request,
			IManualCloseMarkService marks,
			CancellationToken cancellationToken) =>
		{
			return await RunMarkCommand(
				() => marks.EditAsync(markId, request.Symbol, request.MarkedAt, request.Price, request.Quantity, cancellationToken),
				errorPrefix: "Правка пометки не выполнена");
		});

		// Удаление ручной пометки — позиция возвращается в открытое состояние
		// ближайшим чтением.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		api.MapDelete("/close-marks/{markId:long}", async (
			long markId,
			IManualCloseMarkService marks,
			CancellationToken cancellationToken) =>
		{
			return await RunMarkCommand(
				() => marks.DeleteAsync(markId, cancellationToken),
				errorPrefix: "Удаление пометки не выполнено");
		});

		// Добавление внешней корректировки PnL: дата, источник «робот»/«ручная»,
		// знаковая сумма и необязательное описание — форма с пикером даты.
		// Корректировки идут командами через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		group.MapPost("/{constructionId:long}/adjustments", async (
			long constructionId,
			SaveAdjustmentRequest request,
			IPnLAdjustmentService adjustments,
			CancellationToken cancellationToken) =>
		{
			var source = DeserializeAdjustmentSource(request.Source);
			if (source is null)
			{
				return Results.Json(new ConstructionCardErrorResponse("Неизвестный источник корректировки."), statusCode: StatusCodes.Status400BadRequest);
			}

			return await RunAdjustmentCommand(
				() => adjustments.AddAsync(constructionId, request.Date, source.Value, request.AmountUsdt, NormalizeComment(request.Description), cancellationToken),
				errorPrefix: "Корректировка не добавлена");
		});

		// Правка корректировки из строки таблицы: все атрибуты правятся свободно.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		api.MapPut("/adjustments/{adjustmentId:long}", async (
			long adjustmentId,
			SaveAdjustmentRequest request,
			IPnLAdjustmentService adjustments,
			CancellationToken cancellationToken) =>
		{
			var source = DeserializeAdjustmentSource(request.Source);
			if (source is null)
			{
				return Results.Json(new ConstructionCardErrorResponse("Неизвестный источник корректировки."), statusCode: StatusCodes.Status400BadRequest);
			}

			return await RunAdjustmentCommand(
				() => adjustments.EditAsync(adjustmentId, request.Date, source.Value, request.AmountUsdt, NormalizeComment(request.Description), cancellationToken),
				errorPrefix: "Правка корректировки не выполнена");
		});

		// Удаление корректировки — результат пересчитывается ближайшим чтением.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		api.MapDelete("/adjustments/{adjustmentId:long}", async (
			long adjustmentId,
			IPnLAdjustmentService adjustments,
			CancellationToken cancellationToken) =>
		{
			return await RunAdjustmentCommand(
				() => adjustments.DeleteAsync(adjustmentId, cancellationToken),
				errorPrefix: "Удаление корректировки не выполнено");
		});

		// Оспаривание распознанной связки: спор по паре execId скрывает связку
		// из логического слоя до явного возврата — карточка показывает её
		// строкой с признаком спора.
		// Команды спора идут через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Спор — пользовательский путь требования оспаривания: связка
		// оспаривается из карточки и возвращается явным действием.
		// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-dispute
		group.MapPost("/{constructionId:long}/synthetic-closes/dispute", async (
			long constructionId,
			DisputeSyntheticCloseRequest request,
			ISyntheticCloseDisputeService disputes,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.LinearExecId) || string.IsNullOrWhiteSpace(request.CounterOptionExecId))
			{
				return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
			}

			return await RunDisputeCommand(
				() => disputes.DisputeAsync(constructionId, request.LinearExecId.Trim(), request.CounterOptionExecId.Trim(), cancellationToken),
				errorPrefix: "Оспаривание не выполнено");
		});

		// Возврат оспоренной связки: запись спора удаляется, и связка снова
		// входит в логический слой ближайшим чтением карточки.
		// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-dispute
		group.MapPost("/{constructionId:long}/synthetic-closes/revert", async (
			long constructionId,
			DisputeSyntheticCloseRequest request,
			ISyntheticCloseDisputeService disputes,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.LinearExecId) || string.IsNullOrWhiteSpace(request.CounterOptionExecId))
			{
				return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
			}

			return await RunDisputeCommand(
				() => disputes.RevertAsync(constructionId, request.LinearExecId.Trim(), request.CounterOptionExecId.Trim(), cancellationToken),
				errorPrefix: "Возврат связки не выполнен");
		});

		// Ручная синтетическая связка: создание из линейной сделки и встречного
		// опциона одной конструкции — валидация правил связки в use-case сервисе,
		// окно времени между сделками не проверяется. Ответ несёт идентификатор
		// связки — по нему UI адресует удаление.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		// Traceability: change:manual-journal-management/design#d5
		group.MapPost("/{constructionId:long}/synthetic-closes/manual-links", async (
			long constructionId,
			CreateSyntheticCloseManualLinkRequest request,
			ISyntheticCloseManualLinkService links,
			CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.LinearTradeId) || string.IsNullOrWhiteSpace(request.OptionTradeId))
			{
				return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
			}

			try
			{
				var link = await links.CreateAsync(
					constructionId,
					request.LinearTradeId.Trim(),
					request.OptionTradeId.Trim(),
					request.Quantity,
					cancellationToken).ConfigureAwait(false);
				return Results.Json(new SyntheticCloseManualLinkResponse(link.Id, link.LinearTradeId, link.OptionTradeId, link.Quantity));
			}
			catch (SyntheticCloseManualLinkValidationException exception)
			{
				// Нарушенные правила связки — отказ запроса с человекочитаемой причиной.
				// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
				return Results.Json(new ConstructionCardErrorResponse(exception.Message), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (ConstructionNotFoundException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Конструкция не найдена — возможно, удалена."), statusCode: StatusCodes.Status404NotFound);
			}
			catch (ArgumentException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
			}
		});

		// Удаление ручной связки: связка исчезает из логического слоя ближайшим
		// чтением карточки; распознанные связки действие не затрагивает.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		group.MapDelete("/{constructionId:long}/synthetic-closes/manual-links/{linkId:long}", async (
			long constructionId,
			long linkId,
			ISyntheticCloseManualLinkService links,
			CancellationToken cancellationToken) =>
		{
			try
			{
				await links.DeleteAsync(constructionId, linkId, cancellationToken).ConfigureAwait(false);
				return Results.NoContent();
			}
			catch (SyntheticCloseManualLinkNotFoundException exception)
			{
				// Повторное удаление несуществующей связки — ошибка состояния:
				// пользователь видит, какой связки нет.
				return Results.Json(new ConstructionCardErrorResponse(exception.Message), statusCode: StatusCodes.Status404NotFound);
			}
		});

		// Цели переноса сделки: активные конструкции без текущей — лёгкий
		// список для формы «Перенести…» без метрик.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		group.MapGet("/{constructionId:long}/move-targets", async (
			long constructionId,
			IConstructionService constructions,
			CancellationToken cancellationToken) =>
		{
			IReadOnlyList<Construction> active;
			try
			{
				active = await constructions.ListActiveAsync(cancellationToken);
			}
			catch (Exception exception)
			{
				return Results.Json(new ConstructionCardErrorResponse($"Список конструкций недоступен: {exception.Message}"), statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			var targets = active
				.Where(candidate => candidate.Id != constructionId)
				.Select(candidate => new ConstructionMoveTargetResponse(candidate.Id, candidate.Name))
				.ToArray();
			return Results.Json(new ConstructionMoveTargetsResponse(targets));
		});

		// Комментарий конструкции: MD-текст правится модальным split-редактором
			// карточки; null или пробелы снимают комментарий.
			// Комментарии всех трёх уровней идут командами через единый API.
			// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
			// Комментарии конструкции, позиции и сделки — модальный split-редактор
			// по месту отображения (паритет №6).
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPut("/{constructionId:long}/comment", async (
				long constructionId,
				SetCommentRequest request,
				ICommentService comments,
				CancellationToken cancellationToken) =>
			{
				return await RunCommentCommand(
					() => comments.SetConstructionCommentAsync(constructionId, NormalizeComment(request.Text), cancellationToken),
					errorPrefix: "Комментарий конструкции не сохранён");
			});

			// Комментарий позиции по ключу «конструкция × инструмент»: правка по
			// месту строки таблицы позиций, пробелы снимают текст.
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			group.MapPut("/{constructionId:long}/positions/{symbol}/comment", async (
				long constructionId,
				string symbol,
				SetCommentRequest request,
				ICommentService comments,
				CancellationToken cancellationToken) =>
			{
				if (string.IsNullOrWhiteSpace(symbol))
				{
					return Results.Json(new ConstructionCardErrorResponse("Инструмент позиции не задан."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunCommentCommand(
					() => comments.SetPositionCommentAsync(constructionId, symbol, NormalizeComment(request.Text), cancellationToken),
					errorPrefix: "Комментарий позиции не сохранён");
			});

			// Команды строк сделок: комментарий и действия принадлежности. Ключ
			// сделки — биржевой execId, общий для всех конструкций журнала.
			var trades = api.MapGroup("/trades").WithTags("Карточка конструкции");

			// Комментарий сделки: правка по месту строки таблицы сделок.
			// Комментарии всех трёх уровней идут командами через единый API.
			// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			trades.MapPut("/{execId}/comment", async (
				string execId,
				SetCommentRequest request,
				ICommentService comments,
				CancellationToken cancellationToken) =>
			{
				if (string.IsNullOrWhiteSpace(execId))
				{
					return Results.Json(new ConstructionCardErrorResponse("Ключ сделки не задан."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunCommentCommand(
					() => comments.SetTradeCommentAsync(execId, NormalizeComment(request.Text), cancellationToken),
					errorPrefix: "Комментарий сделки не сохранён");
			});

			// Возврат сделки во «Входящие»: привязка снимается, комментарий сделки
			// сохраняется; действие обратимо повторной привязкой.
			// Действия принадлежности сделок идут через единый версионированный API.
			// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			trades.MapPost("/{execId}/return-to-inbox", async (
				string execId,
				[FromServices] ITradeBindingService bindings,
				CancellationToken cancellationToken) =>
			{
				if (string.IsNullOrWhiteSpace(execId))
				{
					return Results.Json(new ConstructionCardErrorResponse("Ключ сделки не задан."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunBindingCommand(
					() => bindings.UnbindAsync(execId, cancellationToken),
					errorPrefix: "Возврат во «Входящие» не выполнен");
			});

			// Перенос сделки в целевую конструкцию: целевая привязка заменяет
			// прежнюю, производные пересчитываются при очередном чтении.
			// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
			trades.MapPost("/{execId}/move", async (
				string execId,
				MoveTradeRequest request,
				[FromServices] ITradeBindingService bindings,
				CancellationToken cancellationToken) =>
			{
				if (string.IsNullOrWhiteSpace(execId))
				{
					return Results.Json(new ConstructionCardErrorResponse("Ключ сделки не задан."), statusCode: StatusCodes.Status400BadRequest);
				}

				return await RunBindingCommand(
					() => bindings.BindAsync(request.ConstructionId, execId, cancellationToken),
					errorPrefix: "Перенос сделки не выполнен");
			});

			return api;
	}

	/// <summary>
	/// Выполняет команду привязки сделки с единой обработкой отказов: ошибка
	/// ключа или ввода — 400, несуществующая сделка — 404 с причиной,
	/// прочие сбои — 500 с текстом причины.
	/// </summary>
	private static async Task<IResult> RunBindingCommand(Func<Task> action, string errorPrefix)
	{
			try
			{
				await action();
			}
			catch (ArgumentException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (TradeNotFoundException exception)
			{
				// Сделка ключом не находится ни в биржевом сырье, ни в ручном —
				// владелец видит, какой ключ не найден, вместо серверного сбоя.
				return Results.Json(new ConstructionCardErrorResponse($"Сделка {exception.ExecId} не найдена в журнале."), statusCode: StatusCodes.Status404NotFound);
			}
			catch (Exception exception)
			{
				return Results.Json(new ConstructionCardErrorResponse($"{errorPrefix}: {exception.Message}"), statusCode: StatusCodes.Status500InternalServerError);
			}

			return Results.NoContent();
	}

	/// <summary>
	/// Выполняет команду комментария с единой обработкой отказов: ошибка
	/// ключа или ввода — 400, неизвестная запись — 404, прочие сбои — 500.
	/// </summary>
	private static async Task<IResult> RunCommentCommand(Func<Task> action, string errorPrefix)
	{
			try
			{
				await action();
			}
			catch (ConstructionNotFoundException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Конструкция не найдена — возможно, удалена."), statusCode: StatusCodes.Status404NotFound);
			}
			catch (ArgumentException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (Exception exception)
			{
				return Results.Json(new ConstructionCardErrorResponse($"{errorPrefix}: {exception.Message}"), statusCode: StatusCodes.Status500InternalServerError);
			}

			return Results.NoContent();
	}

	/// <summary>Пробельный текст комментария превращается в снятие комментария.</summary>
	private static string? NormalizeComment(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

	/// <summary>
	/// Выполняет команду ручной пометки с единой обработкой отказов: ошибка
	/// ввода — 400, прочие сбои — 500 с текстом причины.
	/// </summary>
	private static async Task<IResult> RunMarkCommand(Func<Task> action, string errorPrefix)
	{
		try
		{
			await action();
		}
		catch (ArgumentException)
		{
			return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
		}
		catch (Exception exception)
		{
			return Results.Json(new ConstructionCardErrorResponse($"{errorPrefix}: {exception.Message}"), statusCode: StatusCodes.Status500InternalServerError);
		}

		return Results.NoContent();
	}

	/// <summary>
	/// Выполняет команду конструкции с единой обработкой отказов: неизвестная
	/// конструкция — 404, отказ удаления — 409 с причиной, ошибка ввода — 400,
	/// прочие сбои — 500 с текстом причины.
	/// </summary>
	private static async Task<IResult> RunConstructionCommand(Func<Task> action, string errorPrefix)
	{
			try
			{
				await action();
			}
			catch (ConstructionNotFoundException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Конструкция не найдена — возможно, удалена."), statusCode: StatusCodes.Status404NotFound);
			}
			catch (ConstructionDeletionRefusedException exception)
			{
				// Отказ удаления объясняет причину: блокирующие записи видны владельцу.
				return Results.Json(new ConstructionCardErrorResponse(exception.Message), statusCode: StatusCodes.Status409Conflict);
			}
			catch (ArgumentException)
			{
				return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
			}
			catch (Exception exception)
			{
				return Results.Json(new ConstructionCardErrorResponse($"{errorPrefix}: {exception.Message}"), statusCode: StatusCodes.Status500InternalServerError);
			}

			return Results.NoContent();
	}

	/// <summary>Разбирает строку статуса; null — неизвестное значение.</summary>
	private static ConstructionStatus? DeserializeStatus(string? status) => status switch
	{
			"open" => ConstructionStatus.Open,
			"closed" => ConstructionStatus.Closed,
			"archived" => ConstructionStatus.Archived,
			_ => null,
	};

	/// <summary>Разбирает строку единицы границы; null — параметр не задан.</summary>
	private static TargetUnit? DeserializeUnit(string? unit) => unit switch
	{
			"percent" => TargetUnit.Percent,
			"usdt" => TargetUnit.Usdt,
			null => null,
			_ => null,
	};

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
		// Метрики карточки публикуют реальный риск со статусом состояния:
		// индикатору карточки нужна граница конечного риска, величина
		// и состояние проходят из метрик аналитики.
		// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
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
			data.Metrics.RealRiskUsdt,
			// Худший исход публикуется рядом с реальным риском: знаковая
			// величина границы индикатора карточки со статусом состояния
			// проходит из метрик аналитики без пересчёта.
			// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
			data.Metrics.WorstCaseUsdt,
			// Статус проходит рядом с величиной: конечный риск отличим от
			// неограниченного хвоста и неполных данных без догадок по null.
			// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
			RealRiskContract.SerializeStatus(data.Metrics.RealRiskStatus, data.Metrics.RealRiskUsdt, data.Metrics.WorstCaseUsdt),
			data.Metrics.OpenedAt,
			data.Metrics.ClosedAt,
			data.Metrics.Duration is { } duration ? (long?)Math.Round(duration.TotalSeconds) : null,
			// Саммари времени жизни проходит в метрики карточки из аналитики
			// без пересчёта: минимум суток до ближайшей экспирации, нетто-распад
			// и суммарная временная стоимость открытых опционных остатков;
			// null — открытых опционных ног нет или тикеры недоступны.
			// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
			data.Metrics.Lifetime?.Summary?.DaysToNearestExpiry,
			data.Metrics.Lifetime?.Summary?.NetDailyDecayUsdt,
			data.Metrics.Lifetime?.Summary?.TotalTimeValueUsdt,
			// Сводная величина «с учётом зафиксированного синтетикой» публикуется
			// в метриках карточки как есть из read-модели деталей: null без
			// действующих связок, инструментальные величины рядом не меняются.
			// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#requirement-synthetic-close-adjusted-total
			data.Metrics.SyntheticAdjustedTotalUsdt),
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
			position.PriceChangePercent,
			position.SyntheticResidual,
			position.OtherResidual,
			// Величины времени жизни открытой опционной ноги проходят в контракт
			// позиции как есть: сутки, распад и состав тултипа; null у закрытой
			// позиции, фьючерсной ноги и без тикеров клиент рендерит прочерком.
			// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
			position.DaysToExpiry,
			position.DailyDecayUsdt,
			position.IntrinsicValueUsdt,
			position.TimeValueUsdt,
			position.DecayDayAgoUsdt,
			// Признак закрытия синтетикой опционной ноги проходит в контракт
			// позиции как есть из read-модели: сумма действующих связок по символу
			// ноги и остаток после вычета; null без действующих связок —
			// отсутствие, а не ноль.
			// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-leg-annotation-values
			position.SyntheticClosedQuantity,
			position.SyntheticOpenQuantity)).ToArray(),
		data.Trades.Select(trade => new ConstructionCardTradeResponse(
			trade.ExecId,
			trade.Symbol,
			trade.ExecutedAt,
			trade.IsBuy,
			trade.Quantity,
			trade.Price,
			trade.AmountUsdt,
			trade.Fee,
			trade.Comment,
			// Источник и ключ ручной сделки пробрасываются аддитивно: таблица
			// различает записи бейджем источника, ручные адресует правкой
			// и удалением по идентификатору строки сырья.
			// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui
			trade.Source,
			trade.ManualTradeId)).ToArray(),
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
			adjustment.AmountUsdt)).ToArray(),
		// Пустой список вместо null в JSON: отсутствие связок — обычное состояние,
		// контракт отдаёт явную пустоту, а не отсутствие поля.
		// Traceability: change:synthetic-close-logical-layer/design#d5
		(data.SyntheticCloses ?? []).Select(close => new ConstructionCardSyntheticCloseResponse(
			close.ClosedLegSymbol,
			close.Quantity,
			close.LockedValueUsdt,
			close.Disputed,
			close.LinearExecId,
			close.CounterOptionExecId,
			// Идентификатор ручной связки пробрасывается аддитивно: UI удаляет
			// ручную связку по строке снимка, спора у ручной связки нет.
			// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
			close.ManualLinkId)).ToArray(),
		// Предупреждения диагностики входят в контракт карточки аддитивно:
		// тексты идут из тех же метрик, что и счётчик бейджа списка, —
		// расхождение перечня и счётчика исключено; пустой список вместо null.
		// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
		(data.RiskWarnings ?? []).Select(warning => new ConstructionRiskWarningResponse(warning)).ToArray());

	/// <summary>Разбирает строку источника корректировки; null — неизвестное значение.</summary>
	private static PnLAdjustmentSource? DeserializeAdjustmentSource(string? source) => source switch
	{
		"robot" => PnLAdjustmentSource.Robot,
		"manual" => PnLAdjustmentSource.Manual,
		_ => null,
	};

	/// <summary>
	/// Выполняет команду корректировки с единой обработкой отказов: ошибка
	/// ввода — 400, прочие сбои — 500 с текстом причины.
	/// </summary>
	private static async Task<IResult> RunAdjustmentCommand(Func<Task> action, string errorPrefix)
	{
		try
		{
			await action();
		}
		catch (ArgumentException)
		{
			return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
		}
		catch (Exception exception)
		{
			return Results.Json(new ConstructionCardErrorResponse($"{errorPrefix}: {exception.Message}"), statusCode: StatusCodes.Status500InternalServerError);
		}

		return Results.NoContent();
	}

	/// <summary>
	/// Выполняет команду спора связки с единой обработкой отказов: неизвестная
	/// конструкция — 404, возврат неоспоренной пары — 404 с причиной домена,
	/// ошибка ввода — 400, прочие сбои — 500 с текстом причины.
	/// </summary>
	private static async Task<IResult> RunDisputeCommand(Func<Task> action, string errorPrefix)
	{
		try
		{
			await action();
		}
		catch (ConstructionNotFoundException)
		{
			return Results.Json(new ConstructionCardErrorResponse("Конструкция не найдена — возможно, удалена."), statusCode: StatusCodes.Status404NotFound);
		}
		catch (SyntheticCloseDisputeNotFoundException exception)
		{
			// Повторный возврат неоспоренной связки — ошибка состояния:
			// пользователь видит, какой пары в спорах нет.
			return Results.Json(new ConstructionCardErrorResponse(exception.Message), statusCode: StatusCodes.Status404NotFound);
		}
		catch (SyntheticCloseDisputeNotApplicableException exception)
		{
			// Спор ручной связки правилами не предусмотрен: вместо no-op-записи
			// клиент получает отказ с объяснением, что ручная связка удаляется.
			// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
			return Results.Json(new ConstructionCardErrorResponse(exception.Message), statusCode: StatusCodes.Status400BadRequest);
		}
		catch (ArgumentException)
		{
			return Results.Json(new ConstructionCardErrorResponse("Действие не выполнено: проверьте заполненные поля."), statusCode: StatusCodes.Status400BadRequest);
		}
		catch (Exception exception)
		{
			return Results.Json(new ConstructionCardErrorResponse($"{errorPrefix}: {exception.Message}"), statusCode: StatusCodes.Status500InternalServerError);
		}

		return Results.NoContent();
	}

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
/// <param name="SyntheticCloses">Строки распознанных синтетических закрытий конструкции; пустой список без связок.</param>
/// <param name="RiskWarnings">Полный перечень предупреждений диагностики незакрытых рисков; пустой список без предупреждений.</param>
// Синтетические закрытия входят в контракт карточки аддитивно: новые поля
// добавлены в конец записей, существующие потребители не ломаются.
// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#requirement-detail-synthetic-close-line
// Traceability: change:synthetic-close-logical-layer/design#d5
// Предупреждения диагностики входят в контракт карточки аддитивно: секция
// «Предупреждения» над таблицами скрыта при пустом списке.
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
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
	IReadOnlyList<ConstructionCardAdjustmentResponse> Adjustments,
	IReadOnlyList<ConstructionCardSyntheticCloseResponse> SyntheticCloses,
	IReadOnlyList<ConstructionRiskWarningResponse> RiskWarnings);

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
/// <param name="RealRiskUsdt">Реальный риск в USDT — наихудший результат открытых остатков на экспирации; null при неограниченном худшем случае или неполных данных.</param>
/// <param name="WorstCaseUsdt">Худший исход в USDT — знаковая величина: реализованный результат плюс неклэмпированный минимум платежа групп открытых остатков; null вместе с реальным риском в состояниях `unbounded`/`unavailable`.</param>
/// <param name="RealRiskStatus">Состояние реального риска: finite, unbounded или unavailable; число выдаётся только конечному риску.</param>
/// <param name="OpenedAt">Дата открытия — время первой сделки; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия — момент обнуления последней позиции; null у открытой.</param>
/// <param name="DurationSeconds">Длительность конструкции в секундах; null без сделок.</param>
/// <param name="DaysToNearestExpiry">Остаток полных суток до ближайшей экспирации открытых опционных ног; null без опционных ног.</param>
/// <param name="NetDailyDecayUsdt">Нетто-дневной распад открытых опционных ног со знаком, USDT/день; null без опционных ног или без тикеров.</param>
/// <param name="TotalTimeValueUsdt">Суммарная временная стоимость открытых опционных остатков; null без опционных ног или без тикеров.</param>
/// <param name="SyntheticAdjustedTotalUsdt">Сводная величина «с учётом зафиксированного синтетикой» — реализованный результат плюс сумма локов действующих связок (оспоренные не входят, ручные входят наравне); null без действующих связок.</param>
// Реальный риск входит в контракт метрик карточки со статусом состояния:
// полный индикатор карточки строится с насечкой только конечного риска.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: change:show-unbounded-finresult-risk/design#d1
// Худший исход входит в контракт рядом с реальным риском: граница индикатора
// карточки ставится знаковой величиной худшего исхода.
// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
// Саммари времени жизни входит в контракт метрик аддитивно: metric-strip
// карточки строит сводку времени жизни, null рендерится прочерком.
// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
// Сводная величина синтетики входит в контракт метрик аддитивно: null без
// действующих связок отличим от нуля, список и таблица конструкций её не
// получают — место числа только в карточке.
// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#requirement-synthetic-close-adjusted-total
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
	decimal? RealRiskUsdt,
	decimal? WorstCaseUsdt,
	string RealRiskStatus,
	DateTimeOffset? OpenedAt,
	DateTimeOffset? ClosedAt,
	long? DurationSeconds,
	int? DaysToNearestExpiry,
	decimal? NetDailyDecayUsdt,
	decimal? TotalTimeValueUsdt,
	decimal? SyntheticAdjustedTotalUsdt);

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
/// <param name="SyntheticResidual">Часть остатка, уведённая неоспоренными синтетическими связками; null без неоспоренного синтетического содержимого.</param>
/// <param name="OtherResidual">Прочий остаток линейной ноги — остаток минус синтетическая часть; null вместе с синтетической частью.</param>
/// <param name="DaysToExpiry">Остаток полных суток до экспирации открытой опционной ноги; null у закрытой позиции и фьючерсной ноги.</param>
/// <param name="DailyDecayUsdt">Дневной распад ноги, USDT/день со знаком остатка; null без тикеров, у закрытой и фьючерсной ног.</param>
/// <param name="IntrinsicValueUsdt">Внутренняя стоимость ноги на чистый остаток; материал тултипа состава, null без индексной цены.</param>
/// <param name="TimeValueUsdt">Временная стоимость ноги на чистый остаток; материал тултипа состава, null без индексной цены или марки.</param>
/// <param name="DecayDayAgoUsdt">Распад ноги на снимке суточной давности; материал динамики «вчера → сегодня», null без снимка истории.</param>
/// <param name="SyntheticClosedQuantity">Сумма количеств действующих синтетических связок по символу ноги; null — действующих связок по символу нет.</param>
/// <param name="SyntheticOpenQuantity">Открытая после синтетического закрытия часть ноги — остаток минус закрытое синтетикой, без клэмпа; null вместе с закрытым.</param>
// Распад остатка линейной ноги входит в контракт позиции аддитивно: null
// при отсутствии неоспоренной синтетики отличим от нуля остатка.
// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#scenario-detail-linear-leg-splits-synthetic-and-scalps
// Traceability: change:synthetic-close-logical-layer/design#d5
// Признак закрытия синтетикой опционной ноги входит в контракт позиции
// аддитивно: пара величин публикуется при действующих связках по символу
// ноги, null — их отсутствие, а не ноль.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-leg-annotation-values
// Величины времени жизни входят в контракт позиции аддитивно: колонки «до
// экспирации» и «распад» с тултипом состава; null клиент рендерит прочерком.
// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
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
	decimal? PriceChangePercent,
	decimal? SyntheticResidual,
	decimal? OtherResidual,
	int? DaysToExpiry,
	decimal? DailyDecayUsdt,
	decimal? IntrinsicValueUsdt,
	decimal? TimeValueUsdt,
	decimal? DecayDayAgoUsdt,
	decimal? SyntheticClosedQuantity,
	decimal? SyntheticOpenQuantity);

/// <summary>Строка списка синтетических закрытий карточки.</summary>
/// <param name="ClosedLegSymbol">Символ закрываемой опционной ноги связки.</param>
/// <param name="Quantity">Количество связки — закрываемая часть ноги.</param>
/// <param name="LockedValueUsdt">Величина «зафиксировано синтетикой» — паритетный лок связки к экспирации в USDT.</param>
/// <param name="Disputed">Связка оспорена пользователем: её лок не входит в логический слой ноги.</param>
/// <param name="LinearExecId">Идентификатор линейной сделки связки — единый ключ «execId» либо «manual:{id}», первый ключ команд оспаривания и возврата.</param>
/// <param name="CounterOptionExecId">Идентификатор встречной опционной сделки связки — второй ключ команд оспаривания и возврата.</param>
/// <param name="ManualLinkId">Идентификатор строки ручной связки — ключ её удаления; null у распознанных связок.</param>
// Признак спора проходит рядом со связкой: строка остаётся видимой после
// оспаривания, логический слой спорной связкой не трогается.
// Пара execId добавлена в конец записи аддитивно: UI адресует связку
// в готовых эндпоинтах synthetic-closes/dispute|revert, прежние потребители
// ответа не ломаются.
// Идентификатор ручной связки идёт следом: удаление адресуется строкой
// снимка напрямую, клиентские сессионные реестры связок не нужны.
// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
// Traceability: change:add-synthetic-close-dispute-ui/design#d1
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
public sealed record ConstructionCardSyntheticCloseResponse(
	string ClosedLegSymbol,
	decimal Quantity,
	decimal LockedValueUsdt,
	bool Disputed,
	string LinearExecId,
	string CounterOptionExecId,
	long? ManualLinkId);

/// <summary>Строка таблицы сделок карточки; источники различаются полем source.</summary>
/// <param name="ExecId">Ключ исполнения биржевой сделки; у ручной сделки отсутствует.</param>
/// <param name="Symbol">Инструмент сделки.</param>
/// <param name="ExecutedAt">Время исполнения сделки.</param>
/// <param name="IsBuy">Признак направления: true — покупка, false — продажа.</param>
/// <param name="Quantity">Количество без знака.</param>
/// <param name="Price">Цена исполнения.</param>
/// <param name="AmountUsdt">Сумма сделки в USDT без знака.</param>
/// <param name="Fee">Комиссия со знаком исходной записи.</param>
/// <param name="Comment">Комментарий сделки; null — комментария нет.</param>
/// <param name="Source">Источник записи: exchange либо manual — признак бейджа источника.</param>
/// <param name="ManualTradeId">Идентификатор строки сырья ручной сделки для правки и удаления; null у биржевых.</param>
public sealed record ConstructionCardTradeResponse(
	string ExecId,
	string Symbol,
	DateTimeOffset ExecutedAt,
	bool IsBuy,
	decimal Quantity,
	decimal Price,
	decimal AmountUsdt,
	decimal Fee,
	string? Comment,
	string Source,
	long? ManualTradeId);

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

/// <summary>Ошибка команды карточки: причина, объясняющая отказ.</summary>
/// <param name="Error">Человекочитаемая причина отказа команды.</param>
public sealed record ConstructionCardErrorResponse(string Error);

/// <summary>Запрос переименования конструкции.</summary>
/// <param name="Name">Новое имя; пустое отклоняется как ошибка заполнения.</param>
public sealed record RenameConstructionRequest(string Name);

/// <summary>Запрос смены ручного статуса конструкции.</summary>
/// <param name="Status">Целевой статус: open, closed или archived.</param>
public sealed record ChangeConstructionStatusRequest(string Status);

/// <summary>Запрос правки выделенного капитала.</summary>
/// <param name="AllocatedCapitalUsdt">Капитал в USDT; null убирает капитал, ноль легитимен.</param>
public sealed record ChangeAllocatedCapitalRequest(decimal? AllocatedCapitalUsdt);

/// <summary>Запрос правки плановой границы (риск или профит).</summary>
/// <param name="Value">Значение границы; null вместе с единицей удаляет параметр.</param>
/// <param name="Unit">Единица ввода: percent или usdt; null вместе со значением.</param>
public sealed record ChangeTargetRequest(decimal? Value, string? Unit);

/// <summary>Запрос удаления пустой конструкции.</summary>
/// <param name="MakeBackup">Создать резервную копию базы перед удалением; включён по умолчанию.</param>
public sealed record DeleteConstructionRequest(bool MakeBackup = true);

/// <summary>Запрос сохранения комментария любого уровня.</summary>
/// <param name="Text">Текст комментария в Markdown; null или пробелы снимают комментарий.</param>
public sealed record SetCommentRequest(string? Text);

/// <summary>Последняя известная марка инструмента для предзаполнения формы пометки.</summary>
/// <param name="Mark">Марка инструмента; null — марка неизвестна, цену задаст домен при чтении.</param>
public sealed record LastInstrumentMarkResponse(decimal? Mark);

/// <summary>Запрос постановки ручной пометки закрытия.</summary>
/// <param name="Symbol">Инструмент закрываемой позиции.</param>
/// <param name="MarkedAt">Время пометки — место записи в хронологии.</param>
/// <param name="Price">Цена закрытия; null — последняя марка при чтении.</param>
/// <param name="Quantity">Количество закрываемой позиции; null — весь остаток на момент пометки.</param>
public sealed record AddManualCloseMarkRequest(string Symbol, DateTimeOffset MarkedAt, decimal? Price, decimal? Quantity);

/// <summary>Запрос правки ручной пометки закрытия.</summary>
/// <param name="Symbol">Инструмент закрываемой позиции.</param>
/// <param name="MarkedAt">Время пометки — место записи в хронологии.</param>
/// <param name="Price">Цена закрытия; null — последняя марка при чтении.</param>
/// <param name="Quantity">Количество закрываемой позиции; null — весь остаток на момент пометки.</param>
public sealed record EditManualCloseMarkRequest(string Symbol, DateTimeOffset MarkedAt, decimal? Price, decimal? Quantity);

/// <summary>Цель переноса сделки: активная конструкция без текущей.</summary>
/// <param name="ConstructionId">Идентификатор целевой конструкции.</param>
/// <param name="Name">Имя целевой конструкции.</param>
public sealed record ConstructionMoveTargetResponse(long ConstructionId, string Name);

/// <summary>Список целей переноса сделки из текущей конструкции.</summary>
/// <param name="Targets">Активные конструкции без текущей.</param>
public sealed record ConstructionMoveTargetsResponse(IReadOnlyList<ConstructionMoveTargetResponse> Targets);

/// <summary>Запрос переноса сделки в целевую конструкцию.</summary>
/// <param name="ConstructionId">Целевая конструкция — новая владелица сделки.</param>
public sealed record MoveTradeRequest(long ConstructionId);

/// <summary>Запрос добавления или правки внешней корректировки PnL.</summary>
/// <param name="Date">Дата корректировки.</param>
/// <param name="Source">Источник: robot или manual.</param>
/// <param name="AmountUsdt">Знаковая сумма корректировки в USDT.</param>
/// <param name="Description">Описание; null или пробелы — описания нет.</param>
public sealed record SaveAdjustmentRequest(
	DateTimeOffset Date,
	string Source,
	decimal AmountUsdt,
	string? Description);

/// <summary>Запрос оспаривания или возврата синтетической связки.</summary>
/// <param name="LinearExecId">Биржевой идентификатор линейного исполнения связки.</param>
/// <param name="CounterOptionExecId">Биржевой идентификатор встречного опционного исполнения связки.</param>
public sealed record DisputeSyntheticCloseRequest(string LinearExecId, string CounterOptionExecId);

/// <summary>Запрос создания ручной синтетической связки.</summary>
/// <param name="LinearTradeId">Идентификатор линейной сделки: execId биржевой либо ключ ручной.</param>
/// <param name="OptionTradeId">Идентификатор встречной опционной сделки — с теми же правилами.</param>
/// <param name="Quantity">Количество связки: не превышает количество ни одной из сделок.</param>
public sealed record CreateSyntheticCloseManualLinkRequest(string LinearTradeId, string OptionTradeId, decimal Quantity);

/// <summary>Сохранённая ручная синтетическая связка в контракте API.</summary>
/// <param name="Id">Идентификатор ручной связки — ключ удаления.</param>
/// <param name="LinearTradeId">Идентификатор линейной сделки связки.</param>
/// <param name="OptionTradeId">Идентификатор встречной опционной сделки связки.</param>
/// <param name="Quantity">Количество связки.</param>
public sealed record SyntheticCloseManualLinkResponse(long Id, string LinearTradeId, string OptionTradeId, decimal Quantity);
