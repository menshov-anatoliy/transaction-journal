using Microsoft.EntityFrameworkCore;
using TransactionJournal.Analytics;
using TransactionJournal.Data;

namespace TransactionJournal.Components.Pages;

/// <summary>
/// Строка конструкции для таблицы экрана «Конструкции»: заголовок конструкции
/// (имя и ручной статус), соединённый с метриками аналитики, — ровно те столбцы,
/// что выводит список, без производных величин деталей.
/// </summary>
/// <param name="ConstructionId">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус конструкции.</param>
/// <param name="AllocatedCapitalUsdt">Выделенный капитал конструкции в USDT; null, когда капитал не задан.</param>
/// <param name="RiskPercent">Риск в процентах от капитала: введённые проценты либо вычисленные из введённых USDT; null, когда величины нет.</param>
/// <param name="RiskUsdt">Риск в USDT: введённые USDT либо вычисленные из введённых процентов; null, когда величины нет.</param>
/// <param name="ProfitPercent">Профит в процентах от капитала: введённые проценты либо вычисленные из введённых USDT; null, когда величины нет.</param>
/// <param name="ProfitUsdt">Профит в USDT: введённые USDT либо вычисленные из введённых процентов; null, когда величины нет.</param>
/// <param name="RealizedPnL">Реализованный PnL конструкции.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL; null при недоступной оценке марок.</param>
/// <param name="AdjustmentsPnL">Сумма внешних корректировок PnL конструкции.</param>
/// <param name="TotalPnL">Итог конструкции; null при недоступной оценке марок.</param>
/// <param name="TotalPnLPercent">Итог в процентах от капитала; null без базы процентов или при недоступном итоге.</param>
/// <param name="OpenedAt">Дата открытия — время первой сделки; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия — момент обнуления последней позиции; null у открытой конструкции.</param>
// Капитал передаётся незаданным как есть: прочерк вместо значения — решение
// представления, подмена нулём вводила бы ложную базу процентов.
// Traceability: openspec:ui/screens#scenario-list-no-capital-percent-dash
// Величины риска и профита выводятся обеими единицами чистым конвертером:
// введённая единица первоисточника, незаполненная вычисляется от капитала.
// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
public sealed record ConstructionListItem(
	long ConstructionId,
	string Name,
	ConstructionStatus Status,
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
	DateTimeOffset? OpenedAt,
	DateTimeOffset? ClosedAt);

/// <summary>
/// Данные экрана «Конструкции»: сводка журнала — итог с разбивкой на
/// реализованный и нереализованный PnL, отметка времени марок и счётчик
/// конструкций с числом открытых, — и строки таблицы конструкций.
/// Счётчик описывает видимые (неархивные) конструкции: скрытые из списка
/// в счётчике не числятся.
/// </summary>
/// <param name="TotalPnL">Итог по журналу; null, пока сбой марок оставляет его неполным.</param>
/// <param name="RealizedPnL">Реализованный PnL журнала — сумма реализованных частей всех конструкций, включая архивные; сбой марок его не затрагивает.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL журнала; null при сбое марок хотя бы одной конструкции.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки; null при сбое марок или без открытых остатков.</param>
/// <param name="HasMarkFailure">Признак сбоя марок: провайдер не оценил хотя бы один открытый остаток журнала.</param>
/// <param name="ConstructionCount">Число видимых конструкций списка.</param>
/// <param name="OpenCount">Число конструкций со статусом «открыта» среди видимых.</param>
/// <param name="Items">Строки таблицы конструкций, упорядоченные по статусу и датам.</param>
// Разбивка итога переносится из метрик аналитики как есть: правила агрегации
// и null-деградации принадлежат аналитике, модель списка их не повторяет.
// Traceability: openspec:analytics/performance#requirement-journal-pnl-aggregates
public sealed record ConstructionListData(
	decimal? TotalPnL,
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	DateTimeOffset? MarksAsOf,
	bool HasMarkFailure,
	int ConstructionCount,
	int OpenCount,
	IReadOnlyList<ConstructionListItem> Items);

/// <summary>
/// Read-модель экрана «Конструкции»: соединяет метрики аналитики журнала
/// с именами и ручными статусами конструкций и скрывает архивные из списка.
/// Каркас метрик принадлежит аналитике — модель тонкая: читает готовые метрики,
/// не считает ничего сама и ничего не мутирует.
/// </summary>
// Источник смысла: сводка журнала и таблица конструкций определены требованием экрана.
// Traceability: openspec:ui/screens#requirement-construction-list-screen
public interface IConstructionListReadModel
{
	/// <summary>
	/// Читает данные экрана «Конструкции» из текущего состояния журнала:
	/// сводку журнала и строки таблицы конструкций без архивных.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="Materialization.TradeMaterializationException">Сырая запись исполнения повреждена или конфликтует по execId.</exception>
	/// <exception cref="Materialization.ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует по ключу.</exception>
	/// <exception cref="Materialization.InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	Task<ConstructionListData> ReadAsync(CancellationToken cancellationToken = default);
}

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

	/// <summary>Создаёт read-модель списка над опциями контекста журнала и read-моделью метрик; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="metrics">Read-модель метрик журнала аналитики.</param>
	/// <exception cref="ArgumentNullException">Аргументы не заданы.</exception>
	public ConstructionListReadModel(DbContextOptions<JournalDbContext> options, IJournalMetricsReadModel metrics)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
	}

	/// <inheritdoc />
	public async Task<ConstructionListData> ReadAsync(CancellationToken cancellationToken = default)
	{
		// Метрики журнала выводятся аналитикой из текущих данных при каждом чтении —
		// список не кэширует их и собственных расчётов не добавляет.
		var metrics = await _metrics.ReadAsync(cancellationToken).ConfigureAwait(false);
		var metricsById = metrics.Constructions.ToDictionary(item => item.ConstructionId);

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
				item.ClosedAt));
		}

		// Счётчик сводки описывает видимые конструкции: сколько в списке и сколько
		// из них открыты — числа сходятся со строками таблицы. Признак сбоя марок
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
			// что итог, отметка марок и признак сбоя.
			// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
			metrics.RealizedPnL,
			metrics.UnrealizedPnL,
			metrics.MarksAsOf,
			metrics.HasMarkFailure,
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
