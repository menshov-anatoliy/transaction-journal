using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Infrastructure.UseCases;

namespace TransactionJournal.Infrastructure.ReadModels;

/// <summary>
/// Хвост истории результата конструкции — компактный итог для строк списка
/// и превью, где полная история маркеров не публикуется: время последнего
/// события журнала конструкции, сумма локов действующих связок и сумма
/// внешних корректировок. Накопленный зафиксированный денежный итог строки
/// вычисляет read-модель списка поверх готовых метрик: реализованный
/// результат плюс хвостовые суммы минус входная стоимость открытых остатков —
/// та же формула, что у вычислителя полных историй карточки; источник один —
/// журнал, поэтому время и итог последнего маркера списка совпадают
/// с карточкой.
/// </summary>
/// <remarks>
/// Хвост вычисляется при чтении из тех же событий журнала, что и полные
/// истории карточки; хранением не живёт.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-contract
// Traceability: change:add-finresult-result-history-markers/design#d4
/// </remarks>
public sealed record ConstructionResultMarkerTail
{
	/// <summary>Конструкция, к которой относится хвост истории.</summary>
	public required long ConstructionId { get; init; }

	/// <summary>Время последнего события журнала конструкции; null, когда событий истории ещё нет.</summary>
	public required DateTimeOffset? LastEventAt { get; init; }

	/// <summary>Сумма локов действующих связок синтетического закрытия: оспоренные не входят, ручные входят наравне с распознанными.</summary>
	public required decimal LockedTotalUsdt { get; init; }

	/// <summary>Сумма внешних корректировок PnL конструкции.</summary>
	public required decimal AdjustmentsTotalUsdt { get; init; }
}

/// <summary>
/// Читатель хвостов истории результата по всем конструкциям журнала: события
/// истории — окна опционных исполнений по правилам сборки v5, фьючерсные
/// сделки, границы интервалов связок, корректировки и действующие закрывающие
/// записи — те же, что у вычислителя полных историй; читатель берёт из них
/// только время последнего события и текущие суммы. Вычисляется при чтении,
/// в базе не хранится; спор связки задним числом меняет и хвост.
/// </summary>
/// <remarks>
/// Хвосты строятся из тех же событий, что и полные истории карточки:
/// правила окон и ключей объявлены один раз в общем материале хронологии.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
// Traceability: change:add-finresult-result-history-markers/design#d1
/// </remarks>
public sealed class ConstructionResultMarkersReader
{
	private readonly DbContextOptions<JournalDbContext> _options;

	private readonly PositionReadModel _positions;

	/// <summary>Создаёт читатель над опциями контекста журнала и read-моделью позиций — источником действующих закрывающих записей.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="positions">Read-модель позиций: единый поток применённых закрывающих записей.</param>
	/// <exception cref="ArgumentNullException">Аргументы не заданы.</exception>
	public ConstructionResultMarkersReader(DbContextOptions<JournalDbContext> options, PositionReadModel positions)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_positions = positions ?? throw new ArgumentNullException(nameof(positions));
	}

	/// <summary>
	/// Читает хвосты истории результата всех конструкций журнала. Закрывающие
	/// записи берутся из единого потока read-модели позиций — только применённые
	/// записи попадали и в потоки аналитики; связки вычисляются чистым
	/// распознавателем над привязанными биржевыми сделками с ручными поверх
	/// прогона, лок спорной связки в суммы не входит.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>По одному хвосту на конструкцию, упорядоченные по идентификатору.</returns>
	public async Task<IReadOnlyList<ConstructionResultMarkerTail>> ReadAsync(CancellationToken cancellationToken = default)
	{
		using var db = new JournalDbContext(_options);
		var constructions = await db.Constructions
			.AsNoTracking()
			.OrderBy(construction => construction.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var rawExecutions = await db.RawExecutions
			.OrderBy(execution => execution.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var rawInstruments = await db.RawInstruments
			.OrderBy(instrument => instrument.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var boundUserdata = await db.TradeUserdata
			.AsNoTracking()
			.Where(userdata => userdata.ConstructionId != null)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var manualTrades = await db.ManualTrades
			.AsNoTracking()
			.OrderBy(trade => trade.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var adjustments = await db.PnLAdjustments
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var disputes = await db.SyntheticCloseDisputes
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var manualLinks = await db.SyntheticCloseManualLinks
			.AsNoTracking()
			.OrderBy(link => link.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// Привязанные сделки конструкции: биржевые — по пользовательским данным,
		// ручные — по привязке в собственном сырье.
		var constructionIdByExecId = boundUserdata.ToDictionary(
			userdata => userdata.ExecId,
			userdata => userdata.ConstructionId!.Value);
		var trades = new TradeMaterializer(new InstrumentResolver(new InstrumentCatalog(rawInstruments)))
			.Materialize(rawExecutions, manualTrades).Trades;
		var boundTradesByConstruction = trades
			.Where(trade => trade.Source == TradeSource.Manual
				? trade.ConstructionId is not null
				: constructionIdByExecId.ContainsKey(trade.ExecId!))
			.GroupBy(trade => trade.Source == TradeSource.Manual
				? trade.ConstructionId!.Value
				: constructionIdByExecId[trade.ExecId!])
			.ToDictionary(group => group.Key, group => group.OrderBy(trade => trade.ExecutedAt).ThenBy(ResultMarkerJournalMaterial.TradeKey, StringComparer.Ordinal).ToList());

		// Действующие закрывающие записи — единый поток read-модели позиций:
		// избыточные записи в историю не попадают, как и в потоки аналитики.
		var closingEntries = (await _positions.ListAsync(cancellationToken).ConfigureAwait(false)).ClosingEntries;
		var closingsByConstruction = closingEntries
			.GroupBy(entry => entry.ConstructionId)
			.ToDictionary(group => group.Key, group => group.ToList());
		var disputesByConstruction = disputes
			.GroupBy(dispute => dispute.ConstructionId)
			.ToDictionary(group => group.Key, group => group.ToList());
		var manualLinksByConstruction = manualLinks
			.GroupBy(link => link.ConstructionId)
			.ToDictionary(group => group.Key, group => group.ToList());
		var adjustmentsByConstruction = adjustments
			.GroupBy(adjustment => adjustment.ConstructionId)
			.ToDictionary(group => group.Key, group => group.ToList());

		var tails = new List<ConstructionResultMarkerTail>(constructions.Count);
		foreach (var construction in constructions)
		{
			var constructionTrades = boundTradesByConstruction.GetValueOrDefault(construction.Id, []);
			var constructionClosings = closingsByConstruction.GetValueOrDefault(construction.Id, []);
			var intervals = BuildLinkIntervals(
				constructionTrades,
				disputesByConstruction.GetValueOrDefault(construction.Id, []),
				manualLinksByConstruction.GetValueOrDefault(construction.Id, []));

			// Последнее событие журнала конструкции: максимум по времени всех
			// видов событий — окон, фьючерсных сделок, границ связок,
			// корректировок и закрывающих записей.
			// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
			DateTimeOffset? lastEventAt = null;
			foreach (var moment in EventMoments(constructionTrades, constructionClosings, intervals, adjustmentsByConstruction.GetValueOrDefault(construction.Id, [])))
			{
				lastEventAt = lastEventAt is null || moment > lastEventAt ? moment : lastEventAt;
			}

			tails.Add(new ConstructionResultMarkerTail
			{
				ConstructionId = construction.Id,
				LastEventAt = lastEventAt,
				// Действующие связки — интервалы без снятия спором: их лок входит
				// в накопленный итог последнего маркера.
				// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-synthetic-link-change-marker
				LockedTotalUsdt = intervals.Where(interval => interval.Until is null).Sum(interval => interval.LockedValueUsdt),
				AdjustmentsTotalUsdt = adjustmentsByConstruction
					.GetValueOrDefault(construction.Id, [])
					.Sum(adjustment => adjustment.AmountUsdt),
			});
		}

		return tails;
	}

	#region События хвоста

	/// <summary>
	/// Времена всех событий журнала конструкции: последние исполнения окон
	/// опционных сделок (правила сборки v5 из общего материала хронологии,
	/// разрыв на закрывающих записях экспираций), моменты фьючерсных сделок,
	/// границы интервалов связок, даты корректировок и моменты закрывающих
	/// записей.
	/// </summary>
	private static IEnumerable<DateTimeOffset> EventMoments(
		IReadOnlyList<MaterializedTrade> trades,
		IReadOnlyList<PositionClosingEntry> closingEntries,
		IReadOnlyList<ResultMarkerLinkInterval> intervals,
		IReadOnlyList<PnLAdjustment> adjustments)
	{
		// Те же окна, что у вычислителя полных историй: правила объявлены
		// один раз в общем материале хронологии, время последнего события
		// списка совпадает с карточкой.
		// Traceability: change:add-finresult-result-history-markers/design#d2
		foreach (var window in ResultMarkerJournalMaterial.BuildOptionWindows(trades, closingEntries))
		{
			yield return DateTimeOffset.FromUnixTimeMilliseconds(window.Executions[^1].ExecTimeMs);
		}

		foreach (var trade in trades)
		{
			if (string.Equals(trade.Category, ResultMarkerJournalMaterial.LinearCategory, StringComparison.Ordinal))
			{
				yield return trade.ExecutedAt;
			}
		}

		foreach (var interval in intervals)
		{
			yield return interval.Since;
			if (interval.Until is { } until)
			{
				yield return until;
			}
		}

		foreach (var adjustment in adjustments)
		{
			yield return adjustment.Date;
		}

		foreach (var closing in closingEntries)
		{
			yield return closing.ClosedAt;
		}
	}

	#endregion

	#region Интервалы связок

	/// <summary>
	/// Интервалы действия связок конструкции: распознанные действуют от поздней
	/// из двух сделок до момента спора, ручные — от поздней из двух сделок
	/// без конца (спор ручной связки правилами не предусмотрен). Величина лока
	/// считается тем же калькулятором, что в строках связок карточки; связка
	/// с осиротевшей сделкой интервал не образует.
	/// </summary>
	private static List<ResultMarkerLinkInterval> BuildLinkIntervals(
		IReadOnlyList<MaterializedTrade> trades,
		IReadOnlyCollection<SyntheticCloseDispute> disputes,
		IReadOnlyList<SyntheticCloseManualLink> manualLinks)
	{
		var intervals = new List<ResultMarkerLinkInterval>();

		var exchangeTrades = trades
			.Where(trade => trade.Source == TradeSource.Exchange)
			.ToList();
		var assemblies = exchangeTrades
			.Select(trade => new AssemblyExecution
			{
				ExecId = trade.ExecId!,
				Category = trade.Category,
				Symbol = trade.Symbol,
				ExecTimeMs = trade.ExecutedAt.ToUnixTimeMilliseconds(),
				SignedQuantity = trade.Quantity,
			})
			.ToArray();
		foreach (var link in SyntheticCloseRecognizer.Recognize(assemblies))
		{
			// Спор снимает связку с момента записи оспаривания: лок выходит
			// из логического слоя и из истории результата.
			// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#scenario-link-dispute-recomputes-history
			var until = disputes.FirstOrDefault(dispute => dispute.LinearExecId == link.LinearExecId
				&& dispute.CounterOptionExecId == link.CounterOptionExecId)?.DisputedAt;
			intervals.Add(IntervalOf(link, until, trades));
		}

		foreach (var manualLink in manualLinks)
		{
			if (TryBuildManualLink(manualLink, trades) is not { } link)
			{
				continue;
			}

			intervals.Add(IntervalOf(link, null, trades));
		}

		return intervals;
	}

	/// <summary>Интервал связки: появление — поздняя из двух сделок, снятие — момент спора.</summary>
	private static ResultMarkerLinkInterval IntervalOf(SyntheticCloseLink link, DateTimeOffset? until, IReadOnlyList<MaterializedTrade> trades)
	{
		var linearTrade = TradeOf(link.LinearExecId, trades);
		var counterTrade = TradeOf(link.CounterOptionExecId, trades);
		return new ResultMarkerLinkInterval
		{
			LinkId = $"{link.LinearExecId}|{link.CounterOptionExecId}",
			Since = linearTrade.ExecutedAt > counterTrade.ExecutedAt ? linearTrade.ExecutedAt : counterTrade.ExecutedAt,
			Until = until,
			LockedValueUsdt = SyntheticCloseValuationCalculator.CalculateToExpiry(link, ValuationOf(link, trades)),
		};
	}

	/// <summary>Переводит ручную связку в связку логического слоя тем же правилом, что валидирует сервис связок.</summary>
	private static SyntheticCloseLink? TryBuildManualLink(
		SyntheticCloseManualLink manualLink,
		IReadOnlyList<MaterializedTrade> trades)
	{
		var linear = SyntheticCloseManualLinkMaterial.FindTrade(trades, manualLink.LinearTradeId);
		var counter = SyntheticCloseManualLinkMaterial.FindTrade(trades, manualLink.OptionTradeId);
		if (linear is null
			|| counter is null
			|| OptionSymbolParser.TryParse(counter.Symbol, out var counterParts) == false
			|| counterParts is null
			|| SyntheticCloseManualLinkMaterial.TryResolveClosedLeg(trades, counterParts, out var closedLegSymbol) == false)
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

	/// <summary>Сделка связки по единому ключу; сделка приходит из того же потока привязанных сделок конструкции.</summary>
	private static MaterializedTrade TradeOf(string tradeKey, IReadOnlyList<MaterializedTrade> trades) =>
		SyntheticCloseManualLinkMaterial.FindTrade(trades, tradeKey)
		?? throw new InvalidOperationException($"Сделка «{tradeKey}» не найдена в конструкции при вычислении хвоста истории.");

	/// <summary>Записи FIFO закрываемой ноги связки: сделки ноги из потока привязанных сделок в хронологии исполнения.</summary>
	private static SyntheticCloseValuation ValuationOf(SyntheticCloseLink link, IReadOnlyList<MaterializedTrade> trades)
	{
		var linearTrade = TradeOf(link.LinearExecId, trades);
		var counterTrade = TradeOf(link.CounterOptionExecId, trades);
		return new SyntheticCloseValuation
		{
			LinearPrice = linearTrade.Price,
			LinearFee = linearTrade.Fee,
			CounterOptionPrice = counterTrade.Price,
			CounterOptionFee = counterTrade.Fee,
			ClosedLegEntries = trades
				.Where(trade => trade.Symbol == link.ClosedLegSymbol)
				.OrderBy(trade => trade.ExecutedAt)
				.ThenBy(ResultMarkerJournalMaterial.TradeKey, StringComparer.Ordinal)
				.Select(trade => new PositionFifoEntry
				{
					At = trade.ExecutedAt,
					Kind = PositionFifoEntryKind.Trade,
					SourceKey = ResultMarkerJournalMaterial.TradeKey(trade),
					Quantity = trade.Quantity,
					Price = trade.Price,
					Fee = trade.Fee,
				})
				.ToArray(),
		};
	}

	#endregion
}
