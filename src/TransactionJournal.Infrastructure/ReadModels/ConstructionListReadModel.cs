using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application;

namespace TransactionJournal.Infrastructure.ReadModels;

/// <summary>
/// Реализация read-модели списка конструкций: метрики читаются у read-модели
/// журнала, заголовки — короткоживущим контекстом из хранилища; соединение
/// по идентификатору конструкции. Архивные конструкции скрыты из строк
/// и счётчика, но остаются в итоге журнала — итог считает аналитика по всем
/// конструкциям.
/// </summary>
public sealed class ConstructionListReadModel : IConstructionListReadModel
{
	private readonly DbContextOptions<JournalDbContext> _options;

	private readonly IJournalMetricsReadModel _metrics;

	/// <summary>Читатель последних маркеров истории результата: последний маркер каждой конструкции для строк списка.</summary>
	private readonly ConstructionResultMarkersReader _resultMarkers;

	/// <summary>Создаёт read-модель списка над опциями контекста журнала, read-моделью метрик и читателем последних маркеров истории результата; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="metrics">Read-модель метрик журнала аналитики.</param>
	/// <param name="resultMarkers">Читатель последних маркеров истории результата конструкций.</param>
	/// <exception cref="ArgumentNullException">Аргументы не заданы.</exception>
	public ConstructionListReadModel(
		DbContextOptions<JournalDbContext> options,
		IJournalMetricsReadModel metrics,
		ConstructionResultMarkersReader resultMarkers)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
		_resultMarkers = resultMarkers ?? throw new ArgumentNullException(nameof(resultMarkers));
	}

	/// <inheritdoc />
	public async Task<ConstructionListData> ReadAsync(CancellationToken cancellationToken = default)
	{
		// Метрики журнала выводятся аналитикой из текущих данных при каждом чтении —
		// список не кэширует их и собственных расчётов не добавляет.
		var metrics = await _metrics.ReadAsync(cancellationToken).ConfigureAwait(false);
		var metricsById = metrics.Constructions.ToDictionary(item => item.ConstructionId);
		// Последние маркеры истории результата читаются тем же чтением списка:
		// по одному последнему маркеру на конструкцию, без полных историй —
		// они остаются в карточке. Потоки позиций из того же чтения метрик
		// передаются читателю: итог последнего маркера сходится с карточкой.
		// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
		var markerTails = (await _resultMarkers.ReadAsync(metrics.PositionEntries, cancellationToken).ConfigureAwait(false))
			.ToDictionary(tail => tail.ConstructionId);

		using var db = new JournalDbContext(_options);
		var headers = await db.Constructions
			.AsNoTracking()
			.OrderBy(construction => construction.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// Архивные конструкции скрыты из активного списка: ни строк, ни места
		// в счётчике; их вклад остаётся только в итоге журнала.
		// Traceability: openspec:ui/screens#scenario-archived-not-in-list
		var items = new List<ConstructionListItem>();
		foreach (var header in headers)
		{
			if (header.Status == ConstructionStatus.Archived)
			{
				continue;
			}

			if (metricsById.TryGetValue(header.Id, out var item) == false)
			{
				// Конструкция исчезла из хранилища между чтениями метрик и заголовков —
				// строка пропускается, следующее чтение увидит согласованное состояние.
				continue;
			}

			// Величины риска и профита обеих единиц вычисляются при чтении чистым
			// конвертером от текущего капитала строки: вычисленная пара не хранится.
			// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
			var risk = ConstructionTargetConverter.Convert(header.RiskValue, header.RiskUnit, header.AllocatedCapitalUsdt);
			var profit = ConstructionTargetConverter.Convert(header.ProfitValue, header.ProfitUnit, header.AllocatedCapitalUsdt);

			// Последний маркер истории результата строки: накопленный
			// зафиксированный денежный итог — сумма вкладов операций последнего
			// выведенного маркера; перечень операций расшифровки публикуется
			// рядом. У конструкции без выведенных маркеров поле null, а не ноль.
			// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
			var lastResultMarker = markerTails.GetValueOrDefault(header.Id)?.LastMarker;

			// Реальный риск проходит из метрик аналитики без пересчёта: расчёт из
			// структуры ног и правила null (неограниченный случай, неразобранный
			// символ) остаются в аналитике, список переносит величину как есть.
			// Статус проходит рядом с величиной: ноль конечного риска не должен
			// потеряться, а null без причины — превратиться в догадку списка.
			// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
			// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
			// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
			items.Add(new ConstructionListItem(
				header.Id,
				header.Name,
				header.Status,
				// Незаданный капитал передаётся как есть: представление решает,
				// как показать его отсутствие.
				header.AllocatedCapitalUsdt,
				risk.Percent,
				risk.Usdt,
				profit.Percent,
				profit.Usdt,
				item.RealizedPnL,
				item.UnrealizedPnL,
				item.AdjustmentsPnL,
				item.TotalPnL,
				item.TotalPnLPercent,
				item.OpenedAt,
				item.ClosedAt,
				item.QuoteValue,
				item.CapitalUsagePercent,
				item.RealRiskUsdt,
				// Последний маркер истории результата проходит в строку готовой
				// сводкой «итог + время + перечень операций расшифровки»:
				// полный список маркеров остаётся в карточке.
				// Traceability: openspec:analytics/performance#requirement-result-markers-contract
				lastResultMarker,
				item.RealRiskStatus,
				// Счётчик предупреждений проходит из метрик без пересчёта: бейдж
				// строки и перечень карточки обязаны показывать один набор.
				// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
				item.RiskWarningsCount,
				// Колонки времени жизни проходят из саммари аналитики как есть:
				// минимум суток до ближайшей экспирации и нетто-распад со знаком;
				// без открытых опционных ног саммари отсутствует и строка несёт
				// прочерки, деградация без тикеров решена в аналитике.
				// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-list-lifetime-columns
				item.Lifetime?.Summary?.DaysToNearestExpiry,
				item.Lifetime?.Summary?.NetDailyDecayUsdt,
				// Суммарная внутренняя стоимость проходит из того же саммари рядом
				// с распадом и временной стоимостью без пересчёта: null — ног нет
				// или тикеры недоступны.
				// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
				item.Lifetime?.Summary?.TotalIntrinsicValueUsdt));
		}

		// Счётчик сводки описывает видимые конструкции: сколько в списке и сколько
		// из них открыты — числа сходятся со строками таблицы. Признак сбоя котировок
		// проходит из аналитики без пересчёта: ему принадлежит решение, была ли
		// недоступна оценка нереализованной части.
		// Список упорядочен по смыслу чтения, а не по ключу хранилища: сначала
		// открытые конструкции, затем закрытые; внутри группы строки с датой
		// закрытия идут от более новых к более старым, за ними — по дате
		// открытия от более новых к более старым. Архивные в список не попадают.
		// Traceability: openspec:ui/screens#scenario-list-sorted-by-status-and-dates
		var orderedItems = items
			.OrderBy(item => StatusRank(item.Status))
			.ThenByDescending(item => item.ClosedAt)
			.ThenByDescending(item => item.OpenedAt)
			.ThenBy(item => item.ConstructionId)
			.ToList();

		return new ConstructionListData(
			metrics.TotalPnL,
			// Разбивка проходит из метрик журнала без пересчёта — тем же жестом,
			// что итог, отметка котировок и признак сбоя.
			// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
			metrics.RealizedPnL,
			metrics.UnrealizedPnL,
			metrics.QuotesAsOf,
			metrics.HasQuoteFailure,
			orderedItems.Count,
			orderedItems.Count(item => item.Status == ConstructionStatus.Open),
			orderedItems);
	}

	/// <summary>Порядок группы статуса в списке: открытые выше закрытых.</summary>
	private static int StatusRank(ConstructionStatus status) => status switch
	{
		ConstructionStatus.Open => 0,
		ConstructionStatus.Closed => 1,
		_ => 2,
	};
}

