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
/// <param name="AllocatedCapitalUsdt">Выделенный капитал конструкции в USDT.</param>
/// <param name="RealizedPnL">Реализованный PnL конструкции.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL; null при недоступной оценке марок.</param>
/// <param name="AdjustmentsPnL">Сумма внешних корректировок PnL конструкции.</param>
/// <param name="TotalPnL">Итог конструкции; null при недоступной оценке марок.</param>
/// <param name="TotalPnLPercent">Итог в процентах от капитала; null при нулевом капитале или недоступном итоге.</param>
/// <param name="OpenedAt">Дата открытия — время первой сделки; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия — момент обнуления последней позиции; null у открытой конструкции.</param>
public sealed record ConstructionListItem(
	long ConstructionId,
	string Name,
	ConstructionStatus Status,
	decimal AllocatedCapitalUsdt,
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	decimal AdjustmentsPnL,
	decimal? TotalPnL,
	decimal? TotalPnLPercent,
	DateTimeOffset? OpenedAt,
	DateTimeOffset? ClosedAt);

/// <summary>
/// Данные экрана «Конструкции»: сводка журнала — итог, отметка времени марок
/// и счётчик конструкций с числом открытых, — и строки таблицы конструкций.
/// Счётчик описывает видимые (неархивные) конструкции: скрытые из списка
/// в счётчике не числятся.
/// </summary>
/// <param name="TotalPnL">Итог по журналу; null, пока сбой марок оставляет его неполным.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки; null при сбое марок или без открытых остатков.</param>
/// <param name="ConstructionCount">Число видимых конструкций списка.</param>
/// <param name="OpenCount">Число конструкций со статусом «открыта» среди видимых.</param>
/// <param name="Items">Строки таблицы конструкций, упорядоченные по идентификатору.</param>
public sealed record ConstructionListData(
	decimal? TotalPnL,
	DateTimeOffset? MarksAsOf,
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

			items.Add(new ConstructionListItem(
				header.Id,
				header.Name,
				header.Status,
				header.AllocatedCapitalUsdt,
				item.RealizedPnL,
				item.UnrealizedPnL,
				item.AdjustmentsPnL,
				item.TotalPnL,
				item.TotalPnLPercent,
				item.OpenedAt,
				item.ClosedAt));
		}

		// Счётчик сводки описывает видимые конструкции: сколько в списке и сколько
		// из них открыты — числа сходятся со строками таблицы.
		return new ConstructionListData(
			metrics.TotalPnL,
			metrics.MarksAsOf,
			items.Count,
			items.Count(item => item.Status == ConstructionStatus.Open),
			items);
	}
}
