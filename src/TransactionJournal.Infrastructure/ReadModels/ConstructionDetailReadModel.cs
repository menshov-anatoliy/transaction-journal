using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application;

namespace TransactionJournal.Infrastructure.ReadModels;

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

		// Проценты позиций строятся от текущего капитала конструкции: незаданный
		// капитал передаётся как есть, без базы процентов величины остаются null.
		var positions = await ReadPositionsAsync(db, constructionId, construction.AllocatedCapitalUsdt, metrics, cancellationToken).ConfigureAwait(false);
		var trades = await ReadTradesAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var closing = await ReadClosingEntriesAsync(constructionId, cancellationToken).ConfigureAwait(false);
		var adjustments = await ReadAdjustmentsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);

		// Открытый остаток требует марок своей нереализованной оценке: без него
		// марки не нужны, с ним null нереализованной части означает сбой марок.
		// Сводка сопровождает нереализованные величины отметкой времени марок.
		// Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period
		var hasOpenResidual = positions.Any(position => position.IsOpen);
		// Величины риска и профита сводки вычисляются обеими единицами чистым
		// конвертером от текущего капитала при чтении; вычисленная пара не хранится.
		// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
		var risk = ConstructionTargetConverter.Convert(construction.RiskValue, construction.RiskUnit, construction.AllocatedCapitalUsdt);
		var profit = ConstructionTargetConverter.Convert(construction.ProfitValue, construction.ProfitUnit, construction.AllocatedCapitalUsdt);
		return new ConstructionDetailData(
			construction.Id,
			construction.Name,
			construction.Status,
			// Незаданный капитал передаётся как есть: представление решает,
			// как показать его отсутствие.
			construction.AllocatedCapitalUsdt,
			risk.Percent,
			risk.Usdt,
			profit.Percent,
			profit.Usdt,
			construction.Comment,
			constructionMetrics,
			hasOpenResidual,
			hasOpenResidual && constructionMetrics.UnrealizedPnL is null,
			metrics.MarksAsOf,
			positions,
			trades,
			closing.Rows,
			closing.Warnings,
			adjustments,
			// Единица ввода проходит в DTO: форма правки предзаполняет поле
			// первоисточника, не подменяя введённую единицу вычисленной.
			RiskUnit: construction.RiskUnit,
			ProfitUnit: construction.ProfitUnit);
	}

	#region Чтение таблиц

	/// <summary>
	/// Строки таблицы позиций: метрики позиций конструкции из аналитики
	/// с комментариями позиций по ключу «конструкция × инструмент»; процент
	/// общего P&L считается здесь, потому что метрики позиции капиталом
	/// конструкции не владеют, а процент P&L от стоимости — потому что
	/// стоимость позиции уже выведена здесь же. Незаданный или нулевой капитал
	/// базы процентов от капитала не образует, неположительная стоимость — базы
	/// процентов от стоимости.
	/// </summary>
	private static async Task<List<ConstructionPositionRow>> ReadPositionsAsync(
		JournalDbContext db,
		long constructionId,
		decimal? allocatedCapitalUsdt,
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
		// конструкции: незаданный или нулевой капитал базы не образует, процент
		// остаётся null вместе со своей величиной.
		// Traceability: openspec:ui/screens#scenario-detail-no-capital-no-percent
		decimal? PercentOfCapital(decimal? pnl) => pnl == null || allocatedCapitalUsdt is null or 0m
			? null
			: pnl.Value / allocatedCapitalUsdt.Value * 100m;

		// Процент P&L от стоимости считается только от положительной стоимости:
		// нулевой стоимости нечем делить, а отрицательная база дала бы процент
		// с перевёрнутым знаком.
		// Traceability: openspec:analytics/performance#requirement-position-pnl-percent-of-value
		decimal? PercentOfValue(decimal? pnl, decimal? markValue) => pnl == null || markValue is not > 0m
			? null
			: pnl.Value / markValue.Value * 100m;

		// Порядок строк: открытые позиции раньше закрытых, затем CALL, PUT и прочие
		// инструменты, затем тикер по кодам символов и экспирация по возрастанию —
		// ближайшие серии опционов выше дальних.
		// Traceability: openspec:ui/screens#requirement-construction-detail-screen
		return metrics.Positions
			.Where(position => position.ConstructionId == constructionId)
			.OrderBy(position => position.Residual == 0m)
			.ThenBy(position => InstrumentTypeRank(position.Symbol))
			.ThenBy(position => position.Symbol, StringComparer.Ordinal)
			.ThenBy(position => InstrumentExpiry(position.Symbol))
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
				commentsBySymbol.GetValueOrDefault(position.Symbol),
				position.MarkValue,
				PercentOfValue(position.TotalPnL, position.MarkValue)))
			.ToList();
	}

	/// <summary>
	/// Ранг типа инструмента для сортировки: CALL раньше PUT, неразобранные
	/// линейные и иные символы идут после опционов.
	/// </summary>
	private static int InstrumentTypeRank(string symbol) => OptionSymbolParser.TryParse(symbol, out var parts)
		? parts!.Type == OptionType.Call ? 0 : 1
		: 2;

	/// <summary>
	/// Дата экспирации для сортировки опционов; символ вне формата опциона
	/// уходит в конец экспирационной сортировки внутри своего ранга типа.
	/// </summary>
	private static DateTime InstrumentExpiry(string symbol) => OptionSymbolParser.TryParse(symbol, out var parts)
		? parts!.ExpiryDate
		: DateTime.MaxValue;

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

