using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Analytics;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Materialization;

namespace TransactionJournal.Components.Pages;

/// <summary>
/// Строка таблицы позиций деталей конструкции: итоговая картина позиции
/// в терминах её записей — вход, выход, результат, общий P&L,
/// комиссии, времена — с комментарием позиции; ровно те столбцы, что выводит
/// экран, без служебных величин оценки открытого остатка.
/// </summary>
/// <param name="Symbol">Инструмент позиции.</param>
/// <param name="Residual">Чистый остаток: положителен для длинной, отрицателен для короткой; ноль — закрыта.</param>
/// <param name="AverageEntryPrice">Средняя цена входа — количество-взвешенная цена всех открывающих частей FIFO-потока; null, если открывающих частей нет.</param>
/// <param name="AverageClosePrice">Средняя цена закрытия — количество-взвешенная цена всех закрывающих частей потока; null, если закрывающих частей нет.</param>
/// <param name="RealizedPnL">Реализованный PnL — FIFO-результат встречных частей минус комиссии записей; виден всегда, в том числе при сбое марок.</param>
/// <param name="RealizedPnLPercent">Реализованный P&L процентом от выделенного капитала конструкции; null при нулевом капитале.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL открытой позиции — оценка марками на момент запроса; null при сбое марок, у закрытой позиции нереализованной части нет.</param>
/// <param name="UnrealizedPnLPercent">Нереализованный P&L процентом от выделенного капитала конструкции; null вместе с нереализованным P&L или при нулевом капитале.</param>
/// <param name="TotalPnL">Общий PnL позиции: реализованный плюс нереализованная оценка; null у открытой позиции при сбое марок.</param>
/// <param name="TotalPnLPercent">Общий P&L процентом от выделенного капитала конструкции; null вместе с общим P&L или при нулевом капитале.</param>
/// <param name="AccumulatedFees">Накопленные комиссии записей позиции: уплаченные складываются, rebate снижает сумму.</param>
/// <param name="OpenedAt">Время открытия — время первой записи позиции.</param>
/// <param name="ClosedAt">Время закрытия — момент обнуления остатка; null, пока позиция открыта.</param>
/// <param name="IsOpen">Позиция открыта, пока остаток не нулевой.</param>
/// <param name="Comment">Комментарий позиции по ключу «конструкция × инструмент»; null — комментария нет.</param>
// Строка показывает картину позиции её записями: вход, выход,
// раздельные части реализованного и нереализованного результата
// с процентом от капитала, общий P&L с процентом, комиссии и времена.
// Traceability: openspec:ui/screens#scenario-detail-position-row-entry-close-total
// Раздельные части выводятся колонками «Реализ. P&L» и «Нереализ. P&L»:
// реализованная часть видна всегда, нереализованная деградирует вместе с марками.
// Traceability: openspec:ui/screens#scenario-detail-position-pnl-parts
public sealed record ConstructionPositionRow(
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
	string? Comment);

/// <summary>
/// Строка таблицы сделок деталей конструкции: сделка с атрибутами биржевой
/// записи и комментарием пользователя, привязанная к этой конструкции.
/// </summary>
/// <param name="ExecId">Биржевой идентификатор исполнения — ключ сделки.</param>
/// <param name="Symbol">Инструмент сделки.</param>
/// <param name="ExecutedAt">Время исполнения сделки.</param>
/// <param name="IsBuy">true — покупка, false — продажа.</param>
/// <param name="Quantity">Количество без знака; направление несёт отдельное поле.</param>
/// <param name="Price">Цена исполнения из биржевой записи.</param>
/// <param name="AmountUsdt">Сумма сделки — количество, умноженное на цену, в USDT.</param>
/// <param name="Fee">Комиссия со знаком биржевой записи: положительная уплачена, отрицательная — rebate.</param>
/// <param name="Comment">Комментарий сделки; null — комментария нет.</param>
public sealed record ConstructionTradeRow(
	string ExecId,
	string Symbol,
	DateTimeOffset ExecutedAt,
	bool IsBuy,
	decimal Quantity,
	decimal Price,
	decimal AmountUsdt,
	decimal Fee,
	string? Comment);

/// <summary>
/// Строка таблицы закрывающих записей деталей конструкции: запись единого
/// потока закрывающих записей — delivery, экспирация OTM или ручная пометка —
/// с количеством, эффективной ценой и суммой закрытия.
/// </summary>
/// <param name="ClosedAt">Момент закрытия: время биржевой записи либо время ручной пометки.</param>
/// <param name="Kind">Вид записи: delivery, экспирация OTM или ручная пометка.</param>
/// <param name="Symbol">Инструмент закрываемой позиции.</param>
/// <param name="Quantity">Знаковое количество, обнулившее остаток на момент применения.</param>
/// <param name="Price">Эффективная цена закрытия; null — марка инструмента неизвестна.</param>
/// <param name="AmountUsdt">Сумма закрытия — денежный поток записи со знаком; null при неизвестной цене.</param>
/// <param name="ManualMarkId">Идентификатор ручной пометки для правки и удаления из таблицы; null у биржевых записей.</param>
public sealed record ConstructionClosingEntryRow(
	DateTimeOffset ClosedAt,
	PositionClosingKind Kind,
	string Symbol,
	decimal Quantity,
	decimal? Price,
	decimal? AmountUsdt,
	long? ManualMarkId = null);

/// <summary>
/// Строка таблицы внешних корректировок PnL деталей конструкции.
/// </summary>
/// <param name="AdjustmentId">Идентификатор корректировки — ключ будущих правок и удаления.</param>
/// <param name="Date">Дата корректировки.</param>
/// <param name="Description">Описание корректировки; null — описания нет.</param>
/// <param name="Source">Источник корректировки: «робот» или «ручная».</param>
/// <param name="AmountUsdt">Знаковая сумма корректировки в USDT.</param>
public sealed record ConstructionAdjustmentRow(
	long AdjustmentId,
	DateTimeOffset Date,
	string? Description,
	PnLAdjustmentSource Source,
	decimal AmountUsdt);

/// <summary>
/// Данные экрана деталей конструкции: заголовок с комментарием, сводка метрик
/// с периодом и отметкой марок и четыре таблицы записей — позиции, сделки,
/// закрывающие записи и внешние корректировки PnL. Пустая таблица передаётся
/// пустым списком — экран показывает явное сообщение об отсутствии записей.
/// </summary>
/// <param name="ConstructionId">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус конструкции.</param>
/// <param name="AllocatedCapitalUsdt">Выделенный капитал конструкции в USDT.</param>
/// <param name="Comment">Комментарий конструкции; null — комментария нет.</param>
/// <param name="Metrics">Метрики конструкции: итог, разбивка, проценты, период и длительность.</param>
/// <param name="HasOpenResidual">У конструкции есть открытый остаток — марки нужны её нереализованной оценке.</param>
/// <param name="HasMarkFailure">Сбой марок оставил нереализованную оценку конструкции непостроенной.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки из аналитики журнала.</param>
/// <param name="Positions">Строки таблицы позиций, упорядоченные по инструменту.</param>
/// <param name="Trades">Строки таблицы сделок в хронологическом порядке.</param>
/// <param name="ClosingEntries">Строки таблицы закрывающих записей в хронологическом порядке.</param>
/// <param name="ClosingWarnings">Предупреждения об избыточных закрывающих записях конструкции.</param>
/// <param name="Adjustments">Строки таблицы корректировок, упорядоченные по дате.</param>
public sealed record ConstructionDetailData(
	long ConstructionId,
	string Name,
	ConstructionStatus Status,
	decimal AllocatedCapitalUsdt,
	string? Comment,
	ConstructionMetrics Metrics,
	bool HasOpenResidual,
	bool HasMarkFailure,
	DateTimeOffset? MarksAsOf,
	IReadOnlyList<ConstructionPositionRow> Positions,
	IReadOnlyList<ConstructionTradeRow> Trades,
	IReadOnlyList<ConstructionClosingEntryRow> ClosingEntries,
	IReadOnlyList<RedundantClosingEntryWarning> ClosingWarnings,
	IReadOnlyList<ConstructionAdjustmentRow> Adjustments);

/// <summary>
/// Read-модель экрана деталей конструкции: соединяет метрики аналитики, поток
/// закрывающих записей read-модели позиций и пользовательские записи хранилища
/// в один снимок экрана. Модель тонкая: читает готовые проекции, не считает
/// метрики сама и ничего не мутирует — изменение данных выполняют сервисы домена,
/// экран перечитывает снимок целиком.
/// </summary>
// Источник смысла: сводка и таблицы записей деталей определены требованием экрана.
// Traceability: openspec:ui/screens#requirement-construction-detail-screen
public interface IConstructionDetailReadModel
{
	/// <summary>
	/// Читает данные экрана деталей конструкции из текущего состояния журнала:
	/// заголовок с комментарием, метрики с периодом и таблицы записей.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	/// <exception cref="Materialization.TradeMaterializationException">Сырая запись исполнения повреждена или конфликтует по execId.</exception>
	/// <exception cref="Materialization.ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует по ключу.</exception>
	/// <exception cref="Materialization.InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	Task<ConstructionDetailData> ReadAsync(long constructionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Реализация read-модели деталей конструкции: метрики и позиции читаются у
/// read-модели метрик журнала, закрывающие записи — у read-модели позиций,
/// заголовок, сделки с комментариями и корректировки — короткоживущим контекстом
/// из хранилища. Чтение ничего не пишет в базу и безопасно в длительных сессиях
/// Blazor Server.
/// </summary>
public sealed class ConstructionDetailReadModel : IConstructionDetailReadModel
{
	/// <summary>Префикс ключа источника ручной пометки в едином потоке закрывающих записей.</summary>
	private const string ManualMarkKeyPrefix = "manual:";

	private readonly DbContextOptions<JournalDbContext> _options;

	private readonly IJournalMetricsReadModel _metrics;

	private readonly PositionReadModel _positions;

	/// <summary>Создаёт read-модель деталей над опциями контекста журнала, read-моделью метрик и read-моделью позиций; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="metrics">Read-модель метрик журнала аналитики.</param>
	/// <param name="positions">Read-модель позиций домена — источник единого потока закрывающих записей.</param>
	/// <exception cref="ArgumentNullException">Аргументы не заданы.</exception>
	public ConstructionDetailReadModel(
		DbContextOptions<JournalDbContext> options,
		IJournalMetricsReadModel metrics,
		PositionReadModel positions)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
		_positions = positions ?? throw new ArgumentNullException(nameof(positions));
	}

	/// <inheritdoc />
	public async Task<ConstructionDetailData> ReadAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		// Метрики журнала выводятся аналитикой из текущих данных при каждом чтении —
		// детали не кэшируют их и собственных расчётов не добавляют.
		var metrics = await _metrics.ReadAsync(cancellationToken).ConfigureAwait(false);
		var constructionMetrics = metrics.Constructions
			.FirstOrDefault(item => item.ConstructionId == constructionId);

		using var db = new JournalDbContext(_options);
		var construction = await db.Constructions
			.AsNoTracking()
			.FirstOrDefaultAsync(entity => entity.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (construction == null || constructionMetrics == null)
		{
			// Конструкция исчезла между чтениями или её нет вовсе — детали нечего
			// показывать, экран сообщит об отсутствии конструкции.
			throw new ConstructionNotFoundException(constructionId);
		}

		// Промежуточная совместимость до перехода read-модели на nullable-капитал:
		// незаданный капитал ведёт себя как нулевой — проценты позиций не строятся.
		var positions = await ReadPositionsAsync(db, constructionId, construction.AllocatedCapitalUsdt ?? 0m, metrics, cancellationToken).ConfigureAwait(false);
		var trades = await ReadTradesAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var closing = await ReadClosingEntriesAsync(constructionId, cancellationToken).ConfigureAwait(false);
		var adjustments = await ReadAdjustmentsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);

		// Открытый остаток требует марок своей нереализованной оценке: без него
		// марки не нужны, с ним null нереализованной части означает сбой марок.
		// Сводка сопровождает нереализованные величины отметкой времени марок.
		// Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period
		var hasOpenResidual = positions.Any(position => position.IsOpen);
		return new ConstructionDetailData(
			construction.Id,
			construction.Name,
			construction.Status,
			// Промежуточная совместимость до nullable-капитала в DTO: незаданный
			// капитал передаётся нулём и скрытие процентов приходит задачей 5.5.
			construction.AllocatedCapitalUsdt ?? 0m,
			construction.Comment,
			constructionMetrics,
			hasOpenResidual,
			hasOpenResidual && constructionMetrics.UnrealizedPnL is null,
			metrics.MarksAsOf,
			positions,
			trades,
			closing.Rows,
			closing.Warnings,
			adjustments);
	}

	#region Чтение таблиц

	/// <summary>
	/// Строки таблицы позиций: метрики позиций конструкции из аналитики
	/// с комментариями позиций по ключу «конструкция × инструмент»; процент
	/// общего P&L считается здесь, потому что метрики позиции капиталом
	/// конструкции не владеют.
	/// </summary>
	private static async Task<List<ConstructionPositionRow>> ReadPositionsAsync(
		JournalDbContext db,
		long constructionId,
		decimal allocatedCapitalUsdt,
		JournalMetrics metrics,
		CancellationToken cancellationToken)
	{
		var commentsBySymbol = (await db.PositionComments
				.AsNoTracking()
				.Where(comment => comment.ConstructionId == constructionId)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false))
			.ToDictionary(comment => comment.Symbol, comment => comment.Text);

		// Проценты величин P&L — от текущего выделенного капитала, как в метриках
		// конструкции: нулевой капитал базы не образует, процент остаётся null
		// вместе со своей величиной.
		decimal? PercentOfCapital(decimal? pnl) => pnl == null || allocatedCapitalUsdt == 0m
			? null
			: pnl.Value / allocatedCapitalUsdt * 100m;

		return metrics.Positions
			.Where(position => position.ConstructionId == constructionId)
			.OrderBy(position => position.Symbol, StringComparer.Ordinal)
			.Select(position => new ConstructionPositionRow(
				position.Symbol,
				position.Residual,
				position.AverageEntryPrice,
				position.AverageClosePrice,
				position.RealizedPnL,
				PercentOfCapital(position.RealizedPnL),
				position.UnrealizedPnL,
				PercentOfCapital(position.UnrealizedPnL),
				position.TotalPnL,
				PercentOfCapital(position.TotalPnL),
				position.AccumulatedFees,
				position.OpenedAt,
				position.ClosedAt,
				position.Residual != 0m,
				commentsBySymbol.GetValueOrDefault(position.Symbol)))
			.ToList();
	}

	/// <summary>
	/// Строки таблицы сделок: сделки материализуются проекцией синхронизации
	/// из того же сырья, что питает «Входящие» и метрики, затем фильтруются
	/// по действующей привязке к конструкции; комментарии читаются из
	/// пользовательских данных сделок.
	/// </summary>
	private static async Task<List<ConstructionTradeRow>> ReadTradesAsync(
		JournalDbContext db,
		long constructionId,
		CancellationToken cancellationToken)
	{
		var bound = await db.TradeUserdata
			.AsNoTracking()
			.Where(userdata => userdata.ConstructionId == constructionId)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var commentsByExecId = bound
			.Where(userdata => userdata.Comment != null)
			.ToDictionary(userdata => userdata.ExecId, userdata => userdata.Comment);
		var boundExecIds = bound.Select(userdata => userdata.ExecId).ToHashSet(StringComparer.Ordinal);

		var rawExecutions = await db.RawExecutions
			.OrderBy(execution => execution.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var rawInstruments = await db.RawInstruments
			.OrderBy(instrument => instrument.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		var materializer = new TradeMaterializer(new InstrumentResolver(new InstrumentCatalog(rawInstruments)));
		return materializer.Materialize(rawExecutions).Trades
			.Where(trade => boundExecIds.Contains(trade.ExecId))
			.OrderBy(trade => trade.ExecutedAt)
			.ThenBy(trade => trade.ExecId, StringComparer.Ordinal)
			.Select(trade => new ConstructionTradeRow(
				trade.ExecId,
				trade.Symbol,
				trade.ExecutedAt,
				trade.Quantity > 0m,
				Math.Abs(trade.Quantity),
				trade.Price,
				Math.Abs(trade.Quantity) * trade.Price,
				trade.Fee,
				commentsByExecId.GetValueOrDefault(trade.ExecId)))
			.ToList();
	}

	/// <summary>
	/// Строки таблицы закрывающих записей: единый поток закрывающих записей
	/// read-модели позиций — delivery, экспирации OTM и ручные пометки —
	/// с суммой закрытия как денежным потоком записи; ручные пометки несут
	/// идентификатор для правки и удаления из таблицы. Предупреждения об
	/// избыточных записях того же чтения передаются экрану.
	/// </summary>
	// Сумма закрытия — денежный поток знакового количества по эффективной цене:
	// продажа остатка приносит средства, выкуп короткого — тратит; неизвестная
	// цена оставляет сумму без значения.
	// Traceability: openspec:ui/screens#scenario-detail-closing-entries-shown
	// Идентификатор пометки и предупреждения доносят до экрана действие строки
	// позиции и предупреждение об избыточной закрывающей записи.
	// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
	private async Task<(List<ConstructionClosingEntryRow> Rows, List<RedundantClosingEntryWarning> Warnings)> ReadClosingEntriesAsync(
		long constructionId,
		CancellationToken cancellationToken)
	{
		var result = await _positions.ListAsync(constructionId, cancellationToken).ConfigureAwait(false);
		return (
			result.ClosingEntries
				.Select(entry => new ConstructionClosingEntryRow(
					entry.ClosedAt,
					entry.Kind,
					entry.Symbol,
					entry.Quantity,
					entry.Price,
					entry.Price is null ? null : -entry.Quantity * entry.Price.Value,
					ManualMarkIdOf(entry)))
				.ToList(),
			result.Warnings.ToList());
	}

	/// <summary>
	/// Извлекает идентификатор ручной пометки из ключа источника «manual:{id}»
	/// единого потока закрывающих записей; у биржевых записей идентификатора нет.
	/// </summary>
	private static long? ManualMarkIdOf(PositionClosingEntry entry)
	{
		if (entry.Kind != PositionClosingKind.ManualMark
			|| entry.SourceKey.StartsWith(ManualMarkKeyPrefix, StringComparison.Ordinal) == false)
		{
			return null;
		}

		return long.TryParse(entry.SourceKey[ManualMarkKeyPrefix.Length..], CultureInfo.InvariantCulture, out var markId)
			? markId
			: null;
	}

	/// <summary>Строки таблицы внешних корректировок PnL, упорядоченные по дате.</summary>
	/// <remarks>SQLite не транслирует ORDER BY DateTimeOffset — сортировка выполняется в памяти.</remarks>
	private static async Task<List<ConstructionAdjustmentRow>> ReadAdjustmentsAsync(
		JournalDbContext db,
		long constructionId,
		CancellationToken cancellationToken)
	{
		var adjustments = await db.PnLAdjustments
			.AsNoTracking()
			.Where(adjustment => adjustment.ConstructionId == constructionId)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return adjustments
			.OrderBy(adjustment => adjustment.Date)
			.ThenBy(adjustment => adjustment.Id)
			.Select(adjustment => new ConstructionAdjustmentRow(
				adjustment.Id,
				adjustment.Date,
				adjustment.Comment,
				adjustment.Source,
				adjustment.AmountUsdt))
			.ToList();
	}

	#endregion
}
