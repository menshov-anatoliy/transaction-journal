namespace TransactionJournal.Api.Constructions;

using Microsoft.AspNetCore.Builder;
using TransactionJournal.Application;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Hints.Display;

/// <summary>
/// Эндпоинты раздела «Конструкции» — обзор списка журнала: сводка итога
/// со счётчиками и строки таблицы с бейджами живых подсказок. Слой тонкий:
/// доменные read-модели уже вычисляют данные, эндпоинт фиксирует JSON-контракт
/// раздела и не добавляет собственных правил.
/// </summary>
public static class ConstructionsEndpoints
{
	/// <summary>
	/// Подключает эндпоинты раздела к версионированной группе API.
	/// </summary>
	public static RouteGroupBuilder MapConstructionsEndpoints(this RouteGroupBuilder api)
	{
		var group = api.MapGroup("/constructions").WithTags("Конструкции");

		// Обзор раздела одним запросом: сводка шапки и строки таблицы приходят
		// вместе, поэтому шапка и таблица не расходятся между перезагрузками.
		// Все данные и команды раздела идут через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Шапка с итогом и счётчиками, таблица со строками и бейджами живых
		// подсказок — состав раздела закреплён концепцией §3.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapGet("/", async (IConstructionListReadModel list, IHintDisplayReadModel hintDisplays, CancellationToken cancellationToken) =>
		{
			ConstructionListData data;
			try
			{
				data = await list.ReadAsync(cancellationToken);
			}
			catch (Exception exception) when (exception is TradeMaterializationException
				or ExpiryMaterializationException
				or InstrumentResolveException)
			{
				// Повреждённое сырьё не даёт собрать журнал: раздел отвечает
				// явным состоянием недоступности вместо пустого списка.
				return Results.Json(new ConstructionsUnavailableResponse(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			// Счётчики живых подсказок — вспомогательный слой строк: недоступность
			// хранилища подсказок не ломает обзор, бейджи просто не показываются.
			IReadOnlyDictionary<long, int> liveCounts;
			try
			{
				liveCounts = await hintDisplays.ReadLiveCountsByConstructionAsync(cancellationToken);
			}
			catch (Exception)
			{
				liveCounts = new Dictionary<long, int>();
			}

			var summary = new ConstructionsSummaryResponse(
				data.TotalPnL,
				data.RealizedPnL,
				data.UnrealizedPnL,
				data.MarksAsOf,
				data.HasMarkFailure,
				data.ConstructionCount,
				data.OpenCount);
			// Строки таблицы публикуют реальный риск со статусом состояния:
			// расчёт остаётся в аналитике, эндпоинт переносит величину
			// и состояние в JSON-контракт как есть.
			// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
			var rows = data.Items
				.Select(item => new ConstructionRowResponse(
					item.ConstructionId,
					item.Name,
					SerializeStatus(item.Status),
					item.AllocatedCapitalUsdt,
					item.RiskPercent,
					item.RiskUsdt,
					item.ProfitPercent,
					item.ProfitUsdt,
					item.RealizedPnL,
					item.UnrealizedPnL,
					item.AdjustmentsPnL,
					item.TotalPnL,
					item.TotalPnLPercent,
					item.MarkValue,
					item.CapitalUsagePercent,
					item.RealRiskUsdt,
					// Худший исход публикуется рядом с реальным риском: знаковая
					// величина со статусом состояния переиспользуется без пересчёта.
					// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
					item.WorstCaseUsdt,
					// Статус проходит рядом с величиной: конечный риск отличим от
					// неограниченного хвоста и неполных данных без догадок по null.
					// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
					RealRiskContract.SerializeStatus(item.RealRiskStatus, item.RealRiskUsdt, item.WorstCaseUsdt),
					item.OpenedAt,
					item.ClosedAt,
					liveCounts.GetValueOrDefault(item.ConstructionId),
					// Счётчик предупреждений — бейдж «⚠ N» строки: число проходит
					// из метрик аналитики без пересчёта и совпадает с перечнем
					// превью и карточки.
					// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
					item.RiskWarningsCount,
					// Колонки времени жизни строки списка: минимум суток до ближайшей
					// экспирации открытых опционных ног и нетто-распад со знаком;
					// null — конструкция без опционных ног или без тикеров.
					// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-list-lifetime-columns
					item.DaysToNearestExpiry,
					item.NetDailyDecayUsdt))
				.ToArray();
			return Results.Json(new ConstructionsOverviewResponse(summary, rows));
		});

		// Превью выделенной конструкции — read-only снимок правой области:
		// сводка метрик с плановыми границами индикатора, период и счётчики
		// записей; данные читаются той же read-моделью деталей, что и карточка.
		// Все данные раздела идут через единый версионированный API.
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		// Состав превью закреплён концепцией §3: метрики, полный индикатор,
		// период, счётчики позиций/сделок/корректировок.
		// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
		group.MapGet("/{constructionId:long}/preview", async (long constructionId, IConstructionDetailReadModel details, CancellationToken cancellationToken) =>
		{
			ConstructionDetailData data;
			try
			{
				data = await details.ReadAsync(constructionId, cancellationToken);
			}
			catch (ConstructionNotFoundException)
			{
				// Чужой или удалённый идентификатор — явный 404: SPA снимает
				// выделение вместо показа пустого превью.
				return Results.Json(
					new ConstructionPreviewNotFoundResponse(constructionId),
					statusCode: StatusCodes.Status404NotFound);
			}

			// Превью публикует реальный риск конструкции со статусом состояния:
			// индикатору нужна граница конечного риска, величина и состояние
			// проходят из метрик аналитики.
			// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
			return Results.Json(new ConstructionPreviewResponse(
				data.ConstructionId,
				data.Name,
				SerializeStatus(data.Status),
				data.AllocatedCapitalUsdt,
				data.RiskPercent,
				data.RiskUsdt,
				data.ProfitPercent,
				data.ProfitUsdt,
				data.Metrics.RealizedPnL,
				data.Metrics.UnrealizedPnL,
				data.Metrics.AdjustmentsPnL,
				data.Metrics.TotalPnL,
				data.Metrics.TotalPnLPercent,
				data.Metrics.MarkValue,
				data.Metrics.CapitalUsagePercent,
				data.Metrics.RealRiskUsdt,
				// Худший исход публикуется рядом с реальным риском: знаковая
				// величина границы индикатора со статусом состояния проходит
				// из метрик аналитики без пересчёта.
				// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
				data.Metrics.WorstCaseUsdt,
				// Статус проходит рядом с величиной: конечный риск отличим от
				// неограниченного хвоста и неполных данных без догадок по null.
				// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
				RealRiskContract.SerializeStatus(data.Metrics.RealRiskStatus, data.Metrics.RealRiskUsdt, data.Metrics.WorstCaseUsdt),
				data.Metrics.OpenedAt,
				data.Metrics.ClosedAt,
				data.MarksAsOf,
				data.HasMarkFailure,
				new ConstructionPreviewCountsResponse(
					data.Positions.Count,
					data.Positions.Count(position => position.IsOpen),
					data.Trades.Count,
					data.Adjustments.Count),
				// Счётчик предупреждений превью — тот же, что у бейджа строки:
				// число проходит из метрик аналитики без пересчёта.
				// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
				data.Metrics.RiskWarningsCount,
				// Полный перечень предупреждений краткой карточки: тексты идут
				// из тех же метрик, что и счётчик, — расхождение исключено;
				// без предупреждений перечень пуст.
				// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-messages
				(data.RiskWarnings ?? []).Select(warning => new ConstructionRiskWarningResponse(warning)).ToArray()));
		});

		return api;
	}

	/// <summary>Стабильная строка статуса конструкции в контракте API.</summary>
	private static string SerializeStatus(ConstructionStatus status) => status switch
	{
		ConstructionStatus.Open => "open",
		ConstructionStatus.Closed => "closed",
		ConstructionStatus.Archived => "archived",
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
	};
}

/// <summary>Обзор раздела «Конструкции»: сводка шапки и строки таблицы.</summary>
/// <param name="Summary">Сводка журнала — итог, котировки и счётчики шапки.</param>
/// <param name="Items">Строки таблицы конструкций без архивных.</param>
public sealed record ConstructionsOverviewResponse(
	ConstructionsSummaryResponse Summary,
	IReadOnlyList<ConstructionRowResponse> Items);

/// <summary>Сводка журнала для шапки раздела.</summary>
/// <param name="TotalPnL">Итог по журналу; null при неполном итоге из-за сбоя марок.</param>
/// <param name="RealizedPnL">Реализованный PnL журнала; виден всегда.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL журнала; null при сбое марок.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки; null при сбое или без открытых остатков.</param>
/// <param name="HasMarkFailure">Признак сбоя провайдера котировок.</param>
/// <param name="ConstructionCount">Число видимых конструкций списка.</param>
/// <param name="OpenCount">Число открытых конструкций среди видимых.</param>
public sealed record ConstructionsSummaryResponse(
	decimal? TotalPnL,
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	DateTimeOffset? MarksAsOf,
	bool HasMarkFailure,
	int ConstructionCount,
	int OpenCount);

/// <summary>Строка конструкции таблицы раздела с бейджем живых подсказок.</summary>
/// <param name="ConstructionId">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус: open, closed или archived.</param>
/// <param name="AllocatedCapitalUsdt">Выделенный капитал; null, когда не задан.</param>
/// <param name="RiskPercent">Риск в процентах от капитала; null без величины.</param>
/// <param name="RiskUsdt">Риск в USDT; null без величины.</param>
/// <param name="ProfitPercent">Профит в процентах от капитала; null без величины.</param>
/// <param name="ProfitUsdt">Профит в USDT; null без величины.</param>
/// <param name="RealizedPnL">Реализованный PnL конструкции.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL; null при сбое марок.</param>
/// <param name="AdjustmentsPnL">Сумма внешних корректировок PnL.</param>
/// <param name="TotalPnL">Итог конструкции; null при сбое марок.</param>
/// <param name="TotalPnLPercent">Итог в процентах от капитала; null без базы.</param>
/// <param name="MarkValue">Стоимость открытых позиций по маркам; null без остатков или при сбое.</param>
/// <param name="CapitalUsagePercent">Занятость капитала в процентах; null без базы.</param>
/// <param name="RealRiskUsdt">Реальный риск в USDT — наихудший результат открытых остатков на экспирации; null при неограниченном худшем случае или неполных данных.</param>
/// <param name="WorstCaseUsdt">Худший исход в USDT — знаковая величина: реализованный результат плюс неклэмпированный минимум платежа групп открытых остатков; null вместе с реальным риском в состояниях `unbounded`/`unavailable`.</param>
/// <param name="RealRiskStatus">Состояние реального риска: finite, unbounded или unavailable; число выдаётся только конечному риску.</param>
/// <param name="OpenedAt">Дата открытия; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия; null у открытой конструкции.</param>
/// <param name="LiveHintCount">Число живых подсказок конструкции для бейджа строки.</param>
/// <param name="RiskWarningsCount">Число предупреждений диагностики незакрытых рисков для бейджа «⚠ N» строки; ноль — предупреждений нет.</param>
/// <param name="DaysToNearestExpiry">Остаток полных суток до ближайшей экспирации открытых опционных ног; null без опционных ног.</param>
/// <param name="NetDailyDecayUsdt">Нетто-дневной распад открытых опционных ног со знаком, USDT/день; null без опционных ног или без тикеров.</param>
// Реальный риск входит в контракт строки списка со статусом состояния:
// SPA рисует насечку только конечного риска и различает неограниченный
// хвост и неполные данные без догадок по null.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: change:show-unbounded-finresult-risk/design#d1
// Худший исход входит в контракт рядом с реальным риском: насечке индикатора
// строки нужна знаковая величина со статусом состояния.
// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
// Счётчик предупреждений входит в контракт строки аддитивно: бейдж «⚠ N»
// строится по числу из метрик аналитики, ноль — бейдж скрыт.
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
// Колонки времени жизни входят в контракт строки аддитивно: null клиент
// рендерит прочерком, красный остаток суток при ≤ 14 выводится на клиенте.
// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-list-lifetime-columns
public sealed record ConstructionRowResponse(
	long ConstructionId,
	string Name,
	string Status,
	decimal? AllocatedCapitalUsdt,
	decimal? RiskPercent,
	decimal? RiskUsdt,
	decimal? ProfitPercent,
	decimal? ProfitUsdt,
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	decimal AdjustmentsPnL,
	decimal? TotalPnL,
	decimal? TotalPnLPercent,
	decimal? MarkValue,
	decimal? CapitalUsagePercent,
	decimal? RealRiskUsdt,
	decimal? WorstCaseUsdt,
	string RealRiskStatus,
	DateTimeOffset? OpenedAt,
	DateTimeOffset? ClosedAt,
	int LiveHintCount,
	int RiskWarningsCount,
	int? DaysToNearestExpiry,
	decimal? NetDailyDecayUsdt);

/// <summary>Состояние недоступности журнала: обзор не собран, причина — в тексте.</summary>
/// <param name="Error">Человекочитаемая причина недоступности.</param>
public sealed record ConstructionsUnavailableResponse(string Error);

/// <summary>Read-only превью выделенной конструкции правой области раздела.</summary>
/// <param name="ConstructionId">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус: open, closed или archived.</param>
/// <param name="AllocatedCapitalUsdt">Выделенный капитал; null, когда не задан.</param>
/// <param name="RiskPercent">Риск в процентах от капитала; null без величины.</param>
/// <param name="RiskUsdt">Плановый риск в USDT — граница индикатора; null без величины.</param>
/// <param name="ProfitPercent">Профит в процентах от капитала; null без величины.</param>
/// <param name="ProfitUsdt">Плановый профит в USDT — граница индикатора; null без величины.</param>
/// <param name="RealizedPnL">Реализованный PnL конструкции.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL; null при сбое марок.</param>
/// <param name="AdjustmentsPnL">Сумма внешних корректировок PnL.</param>
/// <param name="TotalPnL">Итог конструкции; null при сбое марок.</param>
/// <param name="TotalPnLPercent">Итог в процентах от капитала; null без базы.</param>
/// <param name="MarkValue">Стоимость открытых позиций по маркам; null без остатков или при сбое.</param>
/// <param name="CapitalUsagePercent">Занятость капитала в процентах; null без базы.</param>
/// <param name="RealRiskUsdt">Реальный риск в USDT — наихудший результат открытых остатков на экспирации; null при неограниченном худшем случае или неполных данных.</param>
/// <param name="WorstCaseUsdt">Худший исход в USDT — знаковая величина: реализованный результат плюс неклэмпированный минимум платежа групп открытых остатков; null вместе с реальным риском в состояниях `unbounded`/`unavailable`.</param>
/// <param name="RealRiskStatus">Состояние реального риска: finite, unbounded или unavailable; число выдаётся только конечному риску.</param>
/// <param name="OpenedAt">Дата открытия; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия; null у открытой конструкции.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки; null при сбое или без остатков.</param>
/// <param name="HasMarkFailure">Признак сбоя провайдера котировок.</param>
/// <param name="Counts">Счётчики записей конструкции для превью.</param>
/// <param name="RiskWarningsCount">Число предупреждений диагностики незакрытых рисков; ноль — предупреждений нет.</param>
/// <param name="RiskWarnings">Полный перечень текстов предупреждений краткой карточки; пустой список без предупреждений.</param>
// Реальный риск входит в контракт превью со статусом состояния: правая
// область строит полный индикатор финрезультата с насечкой только
// конечного риска.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: change:show-unbounded-finresult-risk/design#d1
// Худший исход входит в контракт превью рядом с реальным риском: граница
// индикатора ставится знаковой величиной худшего исхода.
// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
// Предупреждения входят в контракт превью аддитивно: счётчик и полный
// перечень текстов идут из одного вызова вычислителя с метриками.
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
public sealed record ConstructionPreviewResponse(
	long ConstructionId,
	string Name,
	string Status,
	decimal? AllocatedCapitalUsdt,
	decimal? RiskPercent,
	decimal? RiskUsdt,
	decimal? ProfitPercent,
	decimal? ProfitUsdt,
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	decimal AdjustmentsPnL,
	decimal? TotalPnL,
	decimal? TotalPnLPercent,
	decimal? MarkValue,
	decimal? CapitalUsagePercent,
	decimal? RealRiskUsdt,
	decimal? WorstCaseUsdt,
	string RealRiskStatus,
	DateTimeOffset? OpenedAt,
	DateTimeOffset? ClosedAt,
	DateTimeOffset? MarksAsOf,
	bool HasMarkFailure,
	ConstructionPreviewCountsResponse Counts,
	int RiskWarningsCount,
	IReadOnlyList<ConstructionRiskWarningResponse> RiskWarnings);

/// <summary>Предупреждение диагностики незакрытых рисков конструкции в контракте API.</summary>
/// <param name="Text">Готовый текст записи на русском языке: инструмент, число незакрытых контрактов и причина.</param>
// Запись публикации предупреждений — аддитивный элемент контрактов превью
// и карточки: текст вычисляется при чтении и в базе не хранится.
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-messages
public sealed record ConstructionRiskWarningResponse(string Text);

/// <summary>Счётчики записей конструкции в превью правой области.</summary>
/// <param name="Positions">Всего позиций конструкции.</param>
/// <param name="OpenPositions">Открытых позиций с ненулевым остатком.</param>
/// <param name="Trades">Сделок, привязанных к конструкции.</param>
/// <param name="Adjustments">Внешних корректировок PnL.</param>
public sealed record ConstructionPreviewCountsResponse(
	int Positions,
	int OpenPositions,
	int Trades,
	int Adjustments);

/// <summary>Ответ 404 превью: конструкции с идентификатором нет в журнале.</summary>
/// <param name="ConstructionId">Идентификатор, по которому конструкции не нашлось.</param>
public sealed record ConstructionPreviewNotFoundResponse(long ConstructionId);
