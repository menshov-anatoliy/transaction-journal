namespace TransactionJournal.Api.Hints;

using Microsoft.AspNetCore.Builder;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Эндпоинты подсказок раздела «Конструкции»: панель субъекта для правой
/// области, кнопка ручного запуска прохода агента и команды жизненного
/// цикла карточек («Применено»/«Отклонено», автопометка первого показа).
/// Слой тонкий: группировку и переходы уже выполняет read-модель отображения,
/// эндпоинт фиксирует JSON-контракт и не добавляет собственных правил.
/// </summary>
public static class HintsEndpoints
{
	/// <summary>
	/// Подключает эндпоинты подсказок к версионированной группе API.
	/// </summary>
	public static RouteGroupBuilder MapHintsEndpoints(this RouteGroupBuilder api)
	{
		var group = api.MapGroup("/hints").WithTags("Подсказки");

		// Панель подсказок субъекта правой области: группы справочника v1,
		// карточки полного состава и свёрнутая история — одним запросом.
		// Все данные раздела идут через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Состав панели закреплён концепцией §3: группы справочника v1,
		// карточки полного состава, «Применено»/«Отклонено», история.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapGet("/panel", async (string? subject, long? constructionId, IHintDisplayReadModel hintDisplays, CancellationToken cancellationToken) =>
		{
			HintPanelData panel;
			switch (subject)
			{
				case "journal":
					panel = await hintDisplays.ReadJournalPanelAsync(cancellationToken);
					break;
				case "construction" when constructionId is { } id:
					panel = await hintDisplays.ReadConstructionPanelAsync(id, cancellationToken);
					break;
				default:
					// Субъект панели обязателен: правая область явно выбирает
					// журнал или конструкцию, запрос без субъекта — ошибка контракта.
					return Results.Json(new HintsPanelBadRequestResponse(), statusCode: StatusCodes.Status400BadRequest);
			}

			return Results.Json(new HintPanelResponse(
				new HintSubjectResponse(
					SerializeSubject(panel.Subject.Kind),
					panel.Subject.ConstructionId),
				panel.LiveGroups
					.Select(section => new HintGroupResponse(
						new HintGroupDefinitionResponse(section.Group.Id, section.Group.Title),
						section.Hints.Select(SerializeHint).ToArray()))
					.ToArray(),
				panel.History.Select(SerializeHint).ToArray(),
				panel.LiveCount));
		});

		// Кнопка «Запустить проход подсказок»: ручной запуск прохода агента
		// тем же кодом, что и плановые проходы; исход с диагностикой приходит
		// ответом команды.
		// Все команды раздела идут через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Исходы прохода (включая corpus-invalid со списком проблем) закреплены
		// концепцией §3 правой области.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapPost("/pass", async (IHintPassRunner passRunner, CancellationToken cancellationToken) =>
		{
			var result = await passRunner.RunAsync(cancellationToken);
			return Results.Json(new HintPassResponse(
				SerializeOutcome(result.Outcome),
				result.AsOf,
				result.CreatedHints,
				result.ExpiredHints,
				result.Diagnostics,
				result.UnimplementedRuleIds));
		});

		// Команда «Применено»: человек переводит живую подсказку в applied;
		// факт перехода возвращается ответом — терминальная запись не переводится.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapPost("/{hintId:long}/apply", async (long hintId, IHintDisplayReadModel hintDisplays, CancellationToken cancellationToken) =>
		{
			var transitioned = await hintDisplays.ApplyAsync(hintId, cancellationToken);
			return Results.Json(new HintTransitionResponse(hintId, transitioned));
		});

		// Команда «Отклонено»: человек переводит живую подсказку в dismissed;
		// повтор подсказки в текущем окне дедупа подавлен.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapPost("/{hintId:long}/dismiss", async (long hintId, IHintDisplayReadModel hintDisplays, CancellationToken cancellationToken) =>
		{
			var transitioned = await hintDisplays.DismissAsync(hintId, cancellationToken);
			return Results.Json(new HintTransitionResponse(hintId, transitioned));
		});

		// Автопометка первого показа: момент ставит сервер, повторные показы
		// запись не меняют — команда идемпотентна для SPA.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapPost("/{hintId:long}/seen", async (long hintId, IHintDisplayReadModel hintDisplays, CancellationToken cancellationToken) =>
		{
			await hintDisplays.MarkSeenAsync(hintId, DateTimeOffset.Now, cancellationToken);
			return Results.NoContent();
		});

		// Раздел «Подсказки» читает общий журнал всех подсказок всех субъектов:
		// read-only список с фильтрами статус/группа/характер и ограничением
		// размера ответа для экранной пагинации.
		// Traceability: doc:.wf-research/ui-concept/concept.md#6-раздел-подсказки-маршрут-hints
		// Перенос №15 закрепляет отдельный маршрут журнала подсказок.
		// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
		// Контракт журнала SPA публикуется через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		group.MapGet(
			"/log",
			async (string? status, string? group, string? character, int? limit, int? offset, IHintDisplayReadModel hintDisplays, CancellationToken cancellationToken) =>
			{
				HintStatus? parsedStatus = null;
				if (status is not null)
				{
					if (TryParseStatus(status, out var value) == false)
					{
						return Results.Json(
							new HintsLogBadRequestResponse("Недопустимый статус фильтра. Допустимо: new, applied, dismissed, expired."),
							statusCode: StatusCodes.Status400BadRequest);
					}

					parsedStatus = value;
				}

				if (group is not null && HintSectionGroups.V1.Any(definition => definition.Id == group) == false)
				{
					return Results.Json(
						new HintsLogBadRequestResponse("Недопустимая группа фильтра. Используйте идентификатор группы справочника v1."),
						statusCode: StatusCodes.Status400BadRequest);
				}

				if (limit is <= 0)
				{
					return Results.Json(new HintsLogBadRequestResponse("limit должен быть положительным числом."), statusCode: StatusCodes.Status400BadRequest);
				}

				if (offset is < 0)
				{
					return Results.Json(new HintsLogBadRequestResponse("offset не может быть отрицательным."), statusCode: StatusCodes.Status400BadRequest);
				}

				const int defaultLimit = 200;
				const int maxLimit = 500;
				var effectiveLimit = Math.Min(limit ?? defaultLimit, maxLimit);
				var effectiveOffset = offset ?? 0;
				var filter = parsedStatus is null && group is null && character is null
					? null
					: new HintLogFilter
					{
						Status = parsedStatus,
						GroupId = group,
						Character = character,
					};

				var allItems = await hintDisplays.ReadLogAsync(filter, cancellationToken);
				var pageItems = allItems
					.Skip(effectiveOffset)
					.Take(effectiveLimit)
					.Select(SerializeLogItem)
					.ToArray();
				return Results.Json(new HintsLogResponse(pageItems, allItems.Count, effectiveLimit, effectiveOffset));
			});

		return api;
	}

	/// <summary>Карточка подсказки контракта API из записи хранилища.</summary>
	private static HintRecordResponse SerializeHint(HintRecord hint) => new(
		hint.Id,
		hint.RuleId,
		hint.Character,
		hint.Clarity,
		hint.Sources.Select(source => new HintSourceTagResponse(source.Tag, source.File, source.Quotes)).ToArray(),
		hint.Text,
		hint.Facts,
		hint.AsOf,
		SerializeStatus(hint.Status),
		hint.FirstSeenAt);

	/// <summary>Запись журнала подсказок в контракте API.</summary>
	private static HintLogRecordResponse SerializeLogItem(HintRecord hint) => new(
		hint.Id,
		hint.RuleId,
		new HintSubjectResponse(SerializeSubject(hint.Subject.Kind), hint.Subject.ConstructionId),
		new HintGroupDefinitionResponse(HintSectionGroups.Resolve(hint.Character).Id, HintSectionGroups.Resolve(hint.Character).Title),
		hint.Character,
		hint.Clarity,
		hint.Sources.Select(source => new HintSourceTagResponse(source.Tag, source.File, source.Quotes)).ToArray(),
		hint.Text,
		hint.Facts,
		hint.AsOf,
		SerializeStatus(hint.Status),
		hint.FirstSeenAt);

	/// <summary>Стабильная строка статуса записи в контракте API.</summary>
	private static string SerializeStatus(HintStatus status) => status switch
	{
		HintStatus.New => "new",
		HintStatus.Applied => "applied",
		HintStatus.Dismissed => "dismissed",
		HintStatus.Expired => "expired",
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
	};

	/// <summary>Стабильная строка вида субъекта в контракте API.</summary>
	private static string SerializeSubject(HintSubjectKind kind) => kind switch
	{
		HintSubjectKind.Journal => "journal",
		HintSubjectKind.Construction => "construction",
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};

	/// <summary>Стабильная строка исхода прохода в контракте API.</summary>
	private static string SerializeOutcome(HintPassOutcome outcome) => outcome switch
	{
		HintPassOutcome.Completed => "completed",
		HintPassOutcome.SkippedMarketUnavailable => "skipped-market-unavailable",
		HintPassOutcome.CorpusInvalid => "corpus-invalid",
		_ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
	};

	/// <summary>Разбирает статус фильтра журнала из строкового контракта API.</summary>
	private static bool TryParseStatus(string value, out HintStatus status)
	{
		status = value switch
		{
			"new" => HintStatus.New,
			"applied" => HintStatus.Applied,
			"dismissed" => HintStatus.Dismissed,
			"expired" => HintStatus.Expired,
			_ => default,
		};

		return value is "new" or "applied" or "dismissed" or "expired";
	}
}

/// <summary>Панель подсказок субъекта правой области раздела.</summary>
/// <param name="Subject">Субъект панели — журнал или конструкция.</param>
/// <param name="LiveGroups">Живые подсказки по группам справочника v1; пустые группы отсутствуют.</param>
/// <param name="History">Терминальная история субъекта для свёрнутого блока.</param>
/// <param name="LiveCount">Число живых подсказок субъекта.</param>
public sealed record HintPanelResponse(
	HintSubjectResponse Subject,
	IReadOnlyList<HintGroupResponse> LiveGroups,
	IReadOnlyList<HintRecordResponse> History,
	int LiveCount);

/// <summary>Субъект подсказки в контракте API.</summary>
/// <param name="Kind">Вид субъекта: journal или construction.</param>
/// <param name="ConstructionId">Идентификатор конструкции; null у субъекта «журнал».</param>
public sealed record HintSubjectResponse(string Kind, long? ConstructionId);

/// <summary>Группа живых подсказок панели с определением группы справочника.</summary>
/// <param name="Group">Определение группы: стабильный идентификатор и заголовок.</param>
/// <param name="Hints">Живые подсказки группы, свежие сверху.</param>
public sealed record HintGroupResponse(HintGroupDefinitionResponse Group, IReadOnlyList<HintRecordResponse> Hints);

/// <summary>Определение группы справочника v1 в контракте API.</summary>
/// <param name="Id">Стабильный идентификатор группы.</param>
/// <param name="Title">Заголовок группы в панели.</param>
public sealed record HintGroupDefinitionResponse(string Id, string Title);

/// <summary>Карточка подсказки полного состава в контракте API.</summary>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="RuleId">Идентификатор правила корпуса.</param>
/// <param name="Character">Характер действия правила.</param>
/// <param name="Clarity">Чёткость правила: crisp или fuzzy.</param>
/// <param name="Sources">Теги источников с цитатами-доказательствами.</param>
/// <param name="Text">Рендеренный текст подсказки.</param>
/// <param name="Facts">Факты триггера — пары ключ-значение.</param>
/// <param name="AsOf">Отметка as-of прохода генерации.</param>
/// <param name="Status">Статус жизненного цикла: new, applied, dismissed или expired.</param>
/// <param name="FirstSeenAt">Момент первого показа; null, пока не показывалась.</param>
public sealed record HintRecordResponse(
	long Id,
	string RuleId,
	string Character,
	string Clarity,
	IReadOnlyList<HintSourceTagResponse> Sources,
	string Text,
	IReadOnlyDictionary<string, string> Facts,
	DateTimeOffset AsOf,
	string Status,
	DateTimeOffset? FirstSeenAt);

/// <summary>Тег источника подсказки в контракте API.</summary>
/// <param name="Tag">Тег источника (например ПИ, ЛИЧ, ОК).</param>
/// <param name="File">Путь файла базы знаний.</param>
/// <param name="Quotes">Цитаты-доказательства источника.</param>
public sealed record HintSourceTagResponse(string Tag, string File, IReadOnlyList<string> Quotes);

/// <summary>Итог ручного прохода агента подсказок в контракте API.</summary>
/// <param name="Outcome">Исход: completed, skipped-market-unavailable или corpus-invalid.</param>
/// <param name="AsOf">Отметка as-of начала прохода.</param>
/// <param name="CreatedHints">Созданных записей за проход.</param>
/// <param name="ExpiredHints">Погашенных записей за проход.</param>
/// <param name="Diagnostics">Диагностика: причина пропуска или проблемы корпуса; null — диагностик нет.</param>
/// <param name="UnimplementedRuleIds">Идентификаторы активных правил без машинной реализации.</param>
public sealed record HintPassResponse(
	string Outcome,
	DateTimeOffset AsOf,
	int CreatedHints,
	int ExpiredHints,
	IReadOnlyList<string>? Diagnostics,
	IReadOnlyList<string> UnimplementedRuleIds);

/// <summary>Результат команды перевода подсказки («Применено»/«Отклонено»).</summary>
/// <param name="HintId">Идентификатор записи команды.</param>
/// <param name="Transitioned">Выполнен ли переход; false — записи нет или она терминальная.</param>
public sealed record HintTransitionResponse(long HintId, bool Transitioned);

/// <summary>Ответ 400 панели: субъект не задан или задан неверно.</summary>
public sealed record HintsPanelBadRequestResponse(string Error = "Субъект панели обязателен: subject=journal или subject=construction&constructionId=N");

/// <summary>Ответ общего журнала подсказок c фильтрами и пагинацией.</summary>
/// <param name="Items">Страница записей журнала.</param>
/// <param name="Total">Общее число записей после фильтрации до пагинации.</param>
/// <param name="Limit">Применённый лимит страницы.</param>
/// <param name="Offset">Применённое смещение страницы.</param>
public sealed record HintsLogResponse(
	IReadOnlyList<HintLogRecordResponse> Items,
	int Total,
	int Limit,
	int Offset);

/// <summary>Карточка записи общего журнала подсказок.</summary>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="RuleId">Идентификатор правила корпуса.</param>
/// <param name="Subject">Субъект подсказки: журнал или конструкция.</param>
/// <param name="Group">Группа справочника v1, вычисленная из характера.</param>
/// <param name="Character">Характер действия правила.</param>
/// <param name="Clarity">Чёткость правила: crisp или fuzzy.</param>
/// <param name="Sources">Теги источников с цитатами-доказательствами.</param>
/// <param name="Text">Рендеренный текст подсказки.</param>
/// <param name="Facts">Факты триггера — пары ключ-значение.</param>
/// <param name="AsOf">Отметка as-of прохода генерации.</param>
/// <param name="Status">Статус жизненного цикла записи.</param>
/// <param name="FirstSeenAt">Момент первого показа; null, пока не показывалась.</param>
public sealed record HintLogRecordResponse(
	long Id,
	string RuleId,
	HintSubjectResponse Subject,
	HintGroupDefinitionResponse Group,
	string Character,
	string Clarity,
	IReadOnlyList<HintSourceTagResponse> Sources,
	string Text,
	IReadOnlyDictionary<string, string> Facts,
	DateTimeOffset AsOf,
	string Status,
	DateTimeOffset? FirstSeenAt);

/// <summary>Ответ 400 журнала подсказок: неверный параметр фильтра или пагинации.</summary>
public sealed record HintsLogBadRequestResponse(string Error);
