using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.UseCases;

namespace TransactionJournal.Infrastructure.ReadModels;

/// <summary>
/// Реализация read-модели деталей конструкции: метрики и позиции читаются у
/// read-модели метрик журнала, закрывающие записи — у read-модели позиций,
/// заголовок, сделки с комментариями и корректировки — короткоживущим контекстом
/// из хранилища; синтетические закрытия вычисляются при чтении чистым
/// распознавателем над привязанными сделками конструкции. Чтение ничего не
/// пишет в базу и безопасно в длительных сессиях Blazor Server.
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
		var trades = await ReadTradesAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var synthetic = await ReadSyntheticClosesAsync(db, constructionId, trades.MaterializedTrades, cancellationToken).ConfigureAwait(false);
		var positions = await ReadPositionsAsync(db, constructionId, construction.AllocatedCapitalUsdt, metrics, constructionMetrics.Lifetime, synthetic.QuantityByLinearSymbol, cancellationToken).ConfigureAwait(false);
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
			trades.Rows,
			closing.Rows,
			closing.Warnings,
			adjustments,
			synthetic.Rows,
			// Тексты предупреждений идут из тех же метрик, что и счётчик бейджа:
			// один вызов вычислителя при чтении исключает расхождение перечня
			// карточки и бейджа; без открытых остатков список пуст.
			// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
			RiskWarnings: constructionMetrics.RiskWarnings.Select(warning => warning.Text).ToArray(),
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
	/// процентов от стоимости. Остаток линейной ноги распадается на синтетическую
	/// и прочую части по количествам неоспоренных связок. Открытым опционным
	/// ногам величины времени жизни проходят из вычислителя аналитики по символу:
	/// закрытая позиция и фьючерсная нога остаётся прочерком.
	/// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#scenario-detail-linear-leg-splits-synthetic-and-scalps
	/// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
	/// </summary>
	private static async Task<List<ConstructionPositionRow>> ReadPositionsAsync(
		JournalDbContext db,
		long constructionId,
		decimal? allocatedCapitalUsdt,
		JournalMetrics metrics,
		OptionLifetimeEvaluation? lifetime,
		IReadOnlyDictionary<string, decimal> syntheticQuantityByLinearSymbol,
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

		// Порядок строк: открытые позиции раньше закрытых; внутри группы опционы
		// дальних дат экспирации, при равной экспирации больших страйков, при равном
		// страйке CALL раньше PUT; фьючерсы и прочие неопционные инструменты — в
		// конце группы. Тикер по кодам символов — детерминирующий тай-брейк.
		// Traceability: openspec:ui/screens#scenario-detail-positions-ordered-by-status-and-type
		// Величины времени жизни открытых опционных ног идут в строки по символу:
		// закрытая позиция и фьючерсная нога в вычислителе не участвуют и несут
		// прочерк, деградация без тикеров решена в аналитике.
		// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
		var lifetimeBySymbol = (lifetime?.Legs ?? [])
			.GroupBy(leg => leg.Symbol, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		return metrics.Positions
			.Where(position => position.ConstructionId == constructionId)
			.OrderBy(position => position.Residual == 0m)
			.ThenByDescending(position => InstrumentExpiry(position.Symbol))
			.ThenByDescending(position => InstrumentStrike(position.Symbol))
			.ThenBy(position => InstrumentTypeRank(position.Symbol))
			.ThenBy(position => position.Symbol, StringComparer.Ordinal)
			.Select(position =>
			{
				// Распад остатка линейной ноги «синтетика / прочий остаток» строится
				// только при неоспоренном синтетическом содержимом: без связок и при
				// оспоренных связках колонки остаются null — экран показывает
				// отсутствие распада одинаково.
				// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#scenario-detail-synthetic-line-absent
				decimal? syntheticResidual = null;
				decimal? otherResidual = null;
				if (syntheticQuantityByLinearSymbol.TryGetValue(position.Symbol, out var syntheticQuantity))
				{
					syntheticResidual = syntheticQuantity;
					otherResidual = position.Residual - syntheticQuantity;
				}

				return new ConstructionPositionRow(
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
					// Стоимость и процент изменения цены открытого остатка проходят
					// из метрик позиции как есть: их деградация при закрытом остатке
					// и сбое марок уже решена слоем марок.
					// Traceability: openspec:analytics/performance#requirement-open-remainder-price-change-percent
					MarkValue: position.MarkValue,
					PriceChangePercent: position.PriceChangePercent,
					SyntheticResidual: syntheticResidual,
					OtherResidual: otherResidual,
					// Величины времени жизни открытой опционной ноги проходят из
					// вычислителя аналитики по символу: сутки, распад и состав
					// тултипа «внутренняя/временная стоимость, вчера → сегодня»;
					// у закрытой и фьючерсной ног значения нет — прочерк.
					// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
					DaysToExpiry: lifetimeBySymbol.TryGetValue(position.Symbol, out var lifetimeLeg)
						? lifetimeLeg.DaysToExpiry
						: null,
					DailyDecayUsdt: lifetimeBySymbol.GetValueOrDefault(position.Symbol)?.DailyDecayUsdt,
					IntrinsicValueUsdt: lifetimeBySymbol.GetValueOrDefault(position.Symbol)?.IntrinsicValueUsdt,
					TimeValueUsdt: lifetimeBySymbol.GetValueOrDefault(position.Symbol)?.TimeValueUsdt,
					DecayDayAgoUsdt: lifetimeBySymbol.GetValueOrDefault(position.Symbol)?.DecayDayAgoUsdt);
			})
			.ToList();
	}

	/// <summary>
	/// Ранг типа инструмента для сортировки: CALL раньше PUT, неразобранные
	/// линейные и иные символы закрепляются рангом 2 после опционов группы.
	/// </summary>
	private static int InstrumentTypeRank(string symbol) => OptionSymbolParser.TryParse(symbol, out var parts)
		? parts!.Type == OptionType.Call ? 0 : 1
		: 2;

	/// <summary>
	/// Дата экспирации для сортировки опционов по убыванию — дальние серии выше
	/// ближних; символ вне формата опциона получает сентинелл наименьшей даты и
	/// в убывающем ключе уходит в конец группы.
	/// </summary>
	private static DateTime InstrumentExpiry(string symbol) => OptionSymbolParser.TryParse(symbol, out var parts)
		? parts!.ExpiryDate
		: DateTime.MinValue;

	/// <summary>
	/// Страйк для сортировки опционов по убыванию — большие страйки выше малых;
	/// символ вне формата опциона получает сентинелл наименьшего значения и
	/// в убывающем ключе уходит в конец группы.
	/// </summary>
	private static decimal InstrumentStrike(string symbol) => OptionSymbolParser.TryParse(symbol, out var parts)
		? parts!.Strike
		: decimal.MinValue;

	/// <summary>Стабильная строка источника сделки в контракте таблицы: exchange либо manual.</summary>
	// Признак источника живёт в сырье и показывается проекцией: строки таблиц
	// различаются бейджем источника.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
	private static string SerializeTradeSource(TradeSource source) => source switch
	{
		TradeSource.Exchange => "exchange",
		TradeSource.Manual => "manual",
		_ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
	};

	/// <summary>
	/// Строки таблицы сделок: сделки материализуются проекцией журнала из сырья обоих
	/// источников — сырых исполнений биржи и ручных сделок, затем фильтруются
	/// по действующей привязке к конструкции; комментарии читаются из пользовательских
	/// данных биржевых сделок. В слой связок уходят все привязанные сделки:
	/// распознаватель работает по биржевым сделкам, ручные связки подключаются
	/// поверх прогона отдельными ручными данными и ссылаются на сделки обоих
	/// источников.
	/// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
	/// </summary>
	private static async Task<(List<ConstructionTradeRow> Rows, List<MaterializedTrade> MaterializedTrades)> ReadTradesAsync(
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
		// Привязанные ручные сделки читаются собственным фильтром сырья: их привязка
		// живёт в строке ManualTrades, а не в пользовательских данных по execId.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
		var manualTrades = await db.ManualTrades
			.AsNoTracking()
			.Where(trade => trade.ConstructionId == constructionId)
			.OrderBy(trade => trade.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		var materializer = new TradeMaterializer(new InstrumentResolver(new InstrumentCatalog(rawInstruments)));
		var boundTrades = materializer.Materialize(rawExecutions, manualTrades).Trades
			.Where(trade => trade.Source == TradeSource.Manual
				? trade.ConstructionId == constructionId
				: boundExecIds.Contains(trade.ExecId!))
			.OrderByDescending(trade => trade.ExecutedAt)
			.ThenByDescending(trade => trade.ExecId ?? $"manual:{trade.ManualTradeId?.ToString(CultureInfo.InvariantCulture)}", StringComparer.Ordinal)
			.ToList();
		// Порядок строк сделок — по времени исполнения от новых к старым; при равном
		// времени строки детерминированы тай-брейком идентификатора по убыванию (execId
		// биржевой записи либо ключ строки сырья ручной) и не меняются между перечитываниями.
		// Traceability: openspec:ui/screens#scenario-detail-trades-newest-first
		return (boundTrades
			.Select(trade => new ConstructionTradeRow(
				trade.ExecId,
				trade.Symbol,
				trade.ExecutedAt,
				trade.Quantity > 0m,
				Math.Abs(trade.Quantity),
				trade.Price,
				Math.Abs(trade.Quantity) * trade.Price,
				trade.Fee,
				// Комментарий адресуется execId: у ручной сделки его нет, пользовательский
				// комментарий к ней появится вместе с сервисом ручных сделок.
				trade.Source == TradeSource.Manual ? null : commentsByExecId.GetValueOrDefault(trade.ExecId!),
				// Источник берётся из сырья и передаётся экрану: таблица различает
				// биржевые и ручные записи бейджем источника, ручные адресует
				// командами правки и удаления по идентификатору строки сырья.
				// Traceability: openspec:ui/screens#scenario-source-badge-shown
				SerializeTradeSource(trade.Source),
				trade.ManualTradeId))
			.ToList(),
			// В слой связок уходят все привязанные сделки обоих источников: распознаватель
			// берёт из них биржевые, ручные связки адресуют сделки по единому ключу
			// «execId» либо «manual:{id}».
			// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
			boundTrades);
	}

	/// <summary>
	/// Строки синтетических закрытий конструкции: распознаватель связок
	/// вычисляется над потоком привязанных биржевых сделок конструкции и не
	/// хранится в сборке; величина лока каждой связки считается чистым
	/// калькулятором по записям FIFO закрываемой ноги — формула лока ручными
	/// связками не меняется. Действующие связки логического слоя складываются
	/// из распознанных без оспоренных и ручных связок: ручная связка вступает
	/// в силу наравне с распознанными. Оспоренные связки остаются в списке
	/// с признаком спора — их лок не входит в логический слой; у ручной связки
	/// спора нет, величина показывается при любом состоянии прогона.
	/// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	/// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-locked-value
	/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
	/// Traceability: change:manual-journal-management/design#d5
	/// Traceability: change:synthetic-close-logical-layer/design#d1
	/// </summary>
	private static async Task<(List<SyntheticCloseRow> Rows, IReadOnlyDictionary<string, decimal> QuantityByLinearSymbol)> ReadSyntheticClosesAsync(
		JournalDbContext db,
		long constructionId,
		List<MaterializedTrade> materializedTrades,
		CancellationToken cancellationToken)
	{
		// Спор — команда пользователя, read-модель только читает перечень споров
		// напрямую: сервис спора владеет командным сценарием и здесь не используется.
		var disputes = await db.SyntheticCloseDisputes
			.AsNoTracking()
			.Where(dispute => dispute.ConstructionId == constructionId)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		// Ручные связки — ручные данные логического слоя, read-модель читает их
		// перечень: создание и удаление обслуживает use-case сервис связок.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		var manualLinks = await db.SyntheticCloseManualLinks
			.AsNoTracking()
			.Where(link => link.ConstructionId == constructionId)
			.OrderBy(link => link.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// Поток распознавания повторяет вход сборки по биржевым сделкам:
		// материализованная сделка переносится в запись исполнения теми же полями,
		// время — из DateTimeOffset; ручные сделки автопарой не связываются.
		var exchangeTrades = materializedTrades
			.Where(trade => trade.Source == TradeSource.Exchange)
			.ToList();
		var assemblies = exchangeTrades
			.Select(trade => new AssemblyExecution
			{
				ExecId = trade.ExecId,
				Category = trade.Category,
				Symbol = trade.Symbol,
				ExecTimeMs = trade.ExecutedAt.ToUnixTimeMilliseconds(),
				SignedQuantity = trade.Quantity,
			})
			.ToArray();
		var links = SyntheticCloseRecognizer.Recognize(assemblies);
		// Правило «спор скрывает связку» едино в чистом фильтре домена:
		// его неоспоренный прогон определяет, какие связки двигают логический слой.
		// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
		var logicalKeys = SyntheticCloseDisputeFilter.ExcludeDisputed(links, disputes)
			.Select(link => (link.LinearExecId, link.CounterOptionExecId))
			.ToHashSet();
		var disputedKeys = disputes
			.Select(dispute => (dispute.LinearExecId, dispute.CounterOptionExecId))
			.ToHashSet();
		var rows = new List<SyntheticCloseRow>();
		var quantityByLinearSymbol = new Dictionary<string, decimal>(StringComparer.Ordinal);
		foreach (var link in links)
		{
			var key = (link.LinearExecId, link.CounterOptionExecId);
			var valuation = SyntheticCloseValuationOf(link, materializedTrades);
			var lockedValue = SyntheticCloseValuationCalculator.CalculateToExpiry(link, valuation);
			// Лок спорной связки не выходит из строки: величина показывается
			// с признаком спора, логический слой ноги спорной связкой не трогается.
			// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-locked-value-marks-failure-independent
			// Пара execId распознанной связки пробрасывается в конец строки
			// аддитивно: по ней UI адресует связку в командах оспаривания
			// и возврата.
			// Traceability: change:add-synthetic-close-dispute-ui/design#d1
			rows.Add(new SyntheticCloseRow(link.ClosedLegSymbol, link.Quantity, lockedValue, disputedKeys.Contains(key), link.LinearExecId, link.CounterOptionExecId));
			if (logicalKeys.Contains(key))
			{
				var linearTrade = TradeOf(link.LinearExecId, materializedTrades);
				quantityByLinearSymbol[linearTrade.Symbol] =
					quantityByLinearSymbol.GetValueOrDefault(linearTrade.Symbol) + link.Quantity;
			}
		}

		// Ручные связки добавляются к прогону распознавания: каждая действует
		// в логическом слое наравне с неоспоренными распознанными, строка
		// показывает величину лока по той же формуле калькулятора. Спор ручной
		// связки правилами журнала не предусмотрен — признак спора всегда ложный.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-locked-value
		foreach (var manualLink in manualLinks)
		{
			// Осиротевшая связка — сделка или закрываемая нога исчезли после
			// создания (удаление ручной сделки, снятие привязки): строка не
			// строится, логический слой осиротевшей связкой не двигается.
			if (TryBuildManualLink(manualLink, materializedTrades) is not { } link)
			{
				continue;
			}

			var valuation = SyntheticCloseValuationOf(link, materializedTrades);
			var lockedValue = SyntheticCloseValuationCalculator.CalculateToExpiry(link, valuation);
			rows.Add(new SyntheticCloseRow(link.ClosedLegSymbol, link.Quantity, lockedValue, Disputed: false, link.LinearExecId, link.CounterOptionExecId));
			var linearTrade = TradeOf(link.LinearExecId, materializedTrades);
			quantityByLinearSymbol[linearTrade.Symbol] =
				quantityByLinearSymbol.GetValueOrDefault(linearTrade.Symbol) + link.Quantity;
		}

		return (rows, quantityByLinearSymbol);
	}

	/// <summary>
	/// Переводит ручную связку в связку логического слоя: обе сделки находятся
	/// в конструкции по единому ключу «execId» либо «manual:{id}», закрываемая
	/// нога выводится из встречного опциона тем же правилом, которым валидирует
	/// сервис связок. Связка с осиротевшей сделкой или без длинной закрываемой
	/// ноги не переводится.
	/// </summary>
	private static SyntheticCloseLink? TryBuildManualLink(
		SyntheticCloseManualLink manualLink,
		List<MaterializedTrade> materializedTrades)
	{
		var linear = SyntheticCloseManualLinkMaterial.FindTrade(materializedTrades, manualLink.LinearTradeId);
		var counter = SyntheticCloseManualLinkMaterial.FindTrade(materializedTrades, manualLink.OptionTradeId);
		if (linear is null
			|| counter is null
			|| OptionSymbolParser.TryParse(counter.Symbol, out var counterParts) == false
			|| counterParts is null
			|| SyntheticCloseManualLinkMaterial.TryResolveClosedLeg(materializedTrades, counterParts, out var closedLegSymbol) == false)
		{
			return null;
		}

		return new SyntheticCloseLink
		{
			LinearExecId = manualLink.LinearTradeId,
			LinearExecTimeMs = linear.ExecutedAt.ToUnixTimeMilliseconds(),
			CounterOptionExecId = manualLink.OptionTradeId,
			CounterOptionExecTimeMs = counter.ExecutedAt.ToUnixTimeMilliseconds(),
			BaseCoin = counterParts.BaseCoin,
			ClosedLegSymbol = closedLegSymbol!,
			ClosedLegType = counterParts.Type == OptionType.Call ? OptionType.Put : OptionType.Call,
			Strike = counterParts.Strike,
			BoardExpiryDate = counterParts.ExpiryDate,
			Quantity = manualLink.Quantity,
		};
	}

	/// <summary>
	/// Сделка связки по единому ключу «execId» либо «manual:{id}»; сделка приходит
	/// из того же потока, по которому работали распознаватель и правило вывода
	/// закрываемой ноги, и потому присутствует.
	/// </summary>
	private static MaterializedTrade TradeOf(string tradeKey, List<MaterializedTrade> materializedTrades) =>
		SyntheticCloseManualLinkMaterial.FindTrade(materializedTrades, tradeKey)
		?? throw new InvalidOperationException($"Сделка «{tradeKey}» исчезла из конструкции между шагами чтения.");

	/// <summary>
	/// Записи FIFO закрываемой ноги связки: сделки ноги из того же потока
	/// привязанных сделок упорядочены по времени исполнения и единому ключу
	/// «execId» либо «manual:{id}» — тот же порядок, что у распознавателя
	/// связок и потоков позиций.
	/// </summary>
	private static SyntheticCloseValuation SyntheticCloseValuationOf(SyntheticCloseLink link, List<MaterializedTrade> materializedTrades)
	{
		var linearTrade = TradeOf(link.LinearExecId, materializedTrades);
		var counterTrade = TradeOf(link.CounterOptionExecId, materializedTrades);
		var legEntries = materializedTrades
			.Where(trade => trade.Symbol == link.ClosedLegSymbol)
			.OrderBy(trade => trade.ExecutedAt)
			.ThenBy(SyntheticCloseManualLinkMaterial.TradeKey, StringComparer.Ordinal)
			.Select(trade => new PositionFifoEntry
			{
				At = trade.ExecutedAt,
				Kind = PositionFifoEntryKind.Trade,
				SourceKey = SyntheticCloseManualLinkMaterial.TradeKey(trade),
				Quantity = trade.Quantity,
				Price = trade.Price,
				Fee = trade.Fee,
			})
			.ToArray();
		return new SyntheticCloseValuation
		{
			LinearPrice = linearTrade.Price,
			LinearFee = linearTrade.Fee,
			CounterOptionPrice = counterTrade.Price,
			CounterOptionFee = counterTrade.Fee,
			ClosedLegEntries = legEntries,
		};
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

