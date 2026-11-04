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
/// Последний маркер истории результата конструкции — компактная запись для
/// строк списка и превью, где полная история маркеров не публикуется:
/// накопленный зафиксированный денежный итог — сумма вкладов операций
/// последнего выведенного маркера, время события и перечень операций
/// расшифровки. null вместо маркера означает отсутствие выведенных маркеров,
/// а не нулевой итог.
/// </summary>
/// <remarks>
/// Итог и перечень вычисляются вычислителем при чтении из тех же событий
/// журнала, что и полные истории карточки; хранением не живут.
// Traceability: openspec:analytics/performance#requirement-result-markers-contract
// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
/// </remarks>
public sealed record ConstructionResultMarkerTail
{
	/// <summary>Конструкция, к которой относится последний маркер истории.</summary>
	public required long ConstructionId { get; init; }

	/// <summary>Последний выведенный маркер истории — итог, время события и перечень операций расшифровки; null, когда выведенных маркеров нет.</summary>
	public required ResultMarkerSummary? LastMarker { get; init; }
}

/// <summary>
/// Читатель последних маркеров истории результата по всем конструкциям
/// журнала: для каждой конструкции собирается полный вход вычислителя —
/// потоки позиций, привязанные сделки, действующие закрывающие записи,
/// интервалы связок и корректировки — и прогоняется
/// <see cref="ConstructionResultMarkersCalculator"/>; из выведенной истории
/// берётся последний маркер с итогом, временем и перечнем операций
/// расшифровки. Потоки FIFO-записей передаются чтением метрик журнала — тем
/// же источником, что питает карточку, поэтому итог последнего маркера списка
/// сходится с карточкой без второго конвейера. Вычисляется при чтении,
/// в базе не хранится; спор связки задним числом меняет и последний маркер.
/// </summary>
/// <remarks>
/// Правила окон и ключей объявлены один раз в общем материале хронологии —
/// последние маркеры читателя выведены из тех же событий, что и полные
/// истории карточки.
// Traceability: openspec:analytics/performance#requirement-result-markers-contract
// Traceability: openspec:analytics/performance#requirement-result-marker-value
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
	/// Читает последние маркеры истории результата всех конструкций журнала.
	/// Потоки FIFO-записей позиций передаются чтением метрик журнала — тем же
	/// источником, что питает карточку; привязанные сделки, закрывающие записи,
	/// связки и корректировки читатель собирает сам. Закрывающие записи берутся
	/// из единого потока read-модели позиций — только применённые записи
	/// попадали и в потоки аналитики; связки вычисляются чистым
	/// распознавателем над привязанными биржевыми сделками с ручными поверх
	/// прогона, лок спорной связки в историю после спора не входит.
	/// </summary>
	/// <param name="positionEntries">Потоки FIFO-записей позиций по конструкциям и инструментам — те же потоки, из которых метрики считают реализованный результат.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>По одному последнему маркеру на конструкцию, упорядоченные по идентификатору.</returns>
	/// <exception cref="ArgumentNullException">Потоки позиций не заданы.</exception>
	public async Task<IReadOnlyList<ConstructionResultMarkerTail>> ReadAsync(
		IReadOnlyDictionary<long, IReadOnlyDictionary<string, IReadOnlyList<PositionFifoEntry>>> positionEntries,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(positionEntries);

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

		var calculator = new ConstructionResultMarkersCalculator();
		var tails = new List<ConstructionResultMarkerTail>(constructions.Count);
		foreach (var construction in constructions)
		{
			var constructionTrades = boundTradesByConstruction.GetValueOrDefault(construction.Id, []);
			var intervals = BuildLinkIntervals(
				constructionTrades,
				disputesByConstruction.GetValueOrDefault(construction.Id, []),
				manualLinksByConstruction.GetValueOrDefault(construction.Id, []));

			// Плановый риск конструкции — база порога проявления вычислителя:
			// тот же плановый риск, что у границ индикатора сводки карточки.
			var plannedRisk = ConstructionTargetConverter.Convert(
				construction.RiskValue,
				construction.RiskUnit,
				construction.AllocatedCapitalUsdt);

			// Полная история маркеров вычисляется чистым калькулятором над тем
			// же входом, что и в карточке: читатель берёт из выведенной истории
			// только последний маркер — итог, время и перечень операций
			// расшифровки, включая подавленные порогом операции.
			// Traceability: openspec:analytics/performance#requirement-result-markers-contract
			var markers = calculator.Calculate(new ResultMarkersInput
			{
				EntriesBySymbol = positionEntries.GetValueOrDefault(construction.Id)
					?? new Dictionary<string, IReadOnlyList<PositionFifoEntry>>(StringComparer.Ordinal),
				Trades = constructionTrades,
				ClosingEntries = closingsByConstruction.GetValueOrDefault(construction.Id, []),
				Links = intervals,
				Adjustments = adjustmentsByConstruction
					.GetValueOrDefault(construction.Id, [])
					.Select(adjustment => new ResultMarkerAdjustment
					{
						AdjustmentId = adjustment.Id,
						Date = adjustment.Date,
						AmountUsdt = adjustment.AmountUsdt,
					})
					.ToList(),
				PlannedRiskUsdt = plannedRisk.Usdt,
			});

			// Последний маркер выводится безусловно и равен сумме вкладов всех
			// операций — текущему накопленному зафиксированному результату;
			// у конструкции без событий истории маркеров нет: null, а не ноль.
			// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
			tails.Add(new ConstructionResultMarkerTail
			{
				ConstructionId = construction.Id,
				LastMarker = markers.LastMarker is { } lastMarker
					? new ResultMarkerSummary(lastMarker.TotalUsdt, lastMarker.EventTime, lastMarker.Operations)
					: null,
			});
		}

		return tails;
	}

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
			// Спор снимает связку с момента записи оспаривания: снятие даёт
			// операцию с обратным вкладом и пересчитывает последующие маркеры.
			// Traceability: openspec:analytics/performance#scenario-link-dispute-recomputes-history
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
		?? throw new InvalidOperationException($"Сделка «{tradeKey}» не найдена в конструкции при вычислении истории результата.");

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
