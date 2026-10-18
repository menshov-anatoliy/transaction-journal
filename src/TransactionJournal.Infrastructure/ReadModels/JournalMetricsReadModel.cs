using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Application;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Materialization;

namespace TransactionJournal.Infrastructure.ReadModels;

/// <summary>
/// Read-модель метрик журнала: сводит читающий слой в метрики для экранов UI.
/// Сырые записи синхронизации материализуются в сделки и закрывающие записи
/// экспираций, привязки собирают записи в потоки позиций «конструкция ×
/// инструмент», ручные пометки вливаются в те же потоки, конвейер строится
/// заново при каждом чтении — без хранимых результатов и промежуточного
/// состояния. Композиция повторяет правила read-модели позиций: хронология по
/// моменту, рангу вида и ключу источника, избыточные закрывающие записи в поток
/// не входят, пометка без количества закрывает остаток на её момент, заданное
/// количество клэмпится остатком.
// Traceability: openspec:analytics/performance#requirement-analytics-computed-on-read
// Traceability: change:add-ui-screens/design#d7
/// </summary>
public sealed class JournalMetricsReadModel : IJournalMetricsReadModel
{
	/// <summary>Ранг события-сделки в хронологии: сделка раньше закрывающих записей того же момента.</summary>
	private const int TradeRank = 0;

	/// <summary>Ранг биржевой закрывающей записи: при равном времени приоритетнее ручной пометки.</summary>
	private const int ExpiryRank = 1;

	/// <summary>Ранг ручной пометки: fallback-запись уступает биржевой записи того же момента.</summary>
	private const int ManualMarkRank = 2;

	private readonly DbContextOptions<JournalDbContext> _options;

	private readonly IFreshInstrumentMarkSource _freshMarkSource;

	private readonly IInstrumentMarkSource? _lastMarkSource;

	/// <summary>Поставщик времени чтения: граница длительности конструкций и возраст снимков истории.</summary>
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт read-модель над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="freshMarkSource">Источник свежих марок для оценки нереализованного PnL открытых остатков.</param>
	/// <param name="lastMarkSource">Источник последних марок для ручных пометок без цены; null — цена остаётся неизвестной.</param>
	/// <param name="timeProvider">Поставщик времени чтения; по умолчанию системный.</param>
	/// <exception cref="ArgumentNullException">Опции или источник свежих марок не заданы.</exception>
	public JournalMetricsReadModel(
		DbContextOptions<JournalDbContext> options,
		IFreshInstrumentMarkSource freshMarkSource,
		IInstrumentMarkSource? lastMarkSource = null,
		TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_freshMarkSource = freshMarkSource ?? throw new ArgumentNullException(nameof(freshMarkSource));
		_lastMarkSource = lastMarkSource;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc cref="IJournalMetricsReadModel.ReadAsync" />
	public async Task<JournalMetrics> ReadAsync(CancellationToken cancellationToken = default)
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
		var rawDeliveries = await db.RawDeliveries
			.OrderBy(delivery => delivery.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var boundUserdata = await db.TradeUserdata
			.AsNoTracking()
			.Where(userdata => userdata.ConstructionId != null)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var marks = await db.ManualCloseMarks
			.AsNoTracking()
			.OrderBy(mark => mark.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		// Ручные сделки — второе сырьё метрик: читаются тем же чтением, что и биржевые
		// записи, и материализуются в общие потоки позиций наравне с ними.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
		var manualTrades = await db.ManualTrades
			.AsNoTracking()
			.OrderBy(trade => trade.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var adjustments = await db.PnLAdjustments
			.AsNoTracking()
			.OrderBy(adjustment => adjustment.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// Конвейер проекции строится заново при каждом чтении: сделки и закрывающие
		// записи экспираций выводятся из одного и того же сырья детерминированно.
		var resolver = new InstrumentResolver(new InstrumentCatalog(rawInstruments));
		var tradeMaterializer = new TradeMaterializer(resolver);
		var trades = tradeMaterializer.Materialize(rawExecutions, manualTrades).Trades;

		// Привязки передаются материализатору экспираций в строковой форме его контракта;
		// непривязанные сделки образуют остаток «Входящих» и в метриках не участвуют.
		var constructionIdByExecId = boundUserdata.ToDictionary(
			userdata => userdata.ExecId,
			userdata => userdata.ConstructionId!.Value);
		var assignments = constructionIdByExecId.ToDictionary(
			pair => pair.Key,
			pair => (string?)pair.Value.ToString(CultureInfo.InvariantCulture));
		var expiry = new ExpiryMaterializer(tradeMaterializer, resolver)
			.Materialize(rawExecutions, rawDeliveries, assignments, DateTimeOffset.UtcNow, manualTrades);

		var streams = new Dictionary<(long ConstructionId, string Symbol), List<StreamEvent>>();
		foreach (var trade in trades)
		{
			// Привязка ручной сделки живёт в её сырье, биржевой — в пользовательских
			// данных по execId: обе питают поток метрик одинаковым событием-сделкой.
			// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
			var constructionId = trade.Source == TradeSource.Manual
				? trade.ConstructionId
				: constructionIdByExecId.TryGetValue(trade.ExecId!, out var boundId) ? boundId : null;
			if (constructionId is null)
			{
				continue;
			}

			Append(streams, constructionId.Value, trade.Symbol, new StreamEvent(
				trade.ExecutedAt, TradeRank, PositionFifoEntryKind.Trade, TradeSourceKey(trade), trade.Quantity, trade.Price, trade.Fee));
		}

		foreach (var entry in expiry.ClosingEntries)
		{
			if (entry.ConstructionId is null
				|| long.TryParse(entry.ConstructionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryConstructionId) == false)
			{
				continue;
			}

			Append(streams, entryConstructionId, entry.Symbol, new StreamEvent(
				entry.ClosedAt, ExpiryRank, PositionFifoEntryKind.ExpiryClosing, entry.SourceKey, entry.Quantity, entry.EffectivePrice, entry.Fee));
		}

		foreach (var mark in marks)
		{
			// Цена пометки без пользовательского значения подставляется последней известной
			// маркой инструмента при чтении; без источника марок цена остаётся неизвестной.
			// Количество пометки передаётся потоку как есть: незаданное закроет весь
			// остаток, заданное клэмпится остатком при применении.
			// Traceability: openspec:domain/constructions#scenario-manual-mark-default-last-mark
			// Traceability: change:manual-journal-management/design#d6
			var price = mark.Price ?? await ResolveLastMarkAsync(mark.Symbol, cancellationToken).ConfigureAwait(false) ?? 0m;
			Append(streams, mark.ConstructionId, mark.Symbol, new StreamEvent(
				mark.MarkedAt, ManualMarkRank, PositionFifoEntryKind.ManualMark, ManualSourceKey(mark.Id), mark.Quantity, price, 0m));
		}

		var positionCalculator = new PositionMetricsCalculator();
		var positionMetrics = new List<PositionMetrics>();
		var orderedKeys = streams.Keys
			.OrderBy(key => key.ConstructionId)
			.ThenBy(key => key.Symbol, StringComparer.Ordinal);
		foreach (var key in orderedKeys)
		{
			var entries = Replay(key, streams[key]);
			if (entries.Count > 0)
			{
				positionMetrics.Add(positionCalculator.Calculate(key.ConstructionId, key.Symbol, entries));
			}
		}

		// Нереализованная часть открытых остатков оценивается свежими марками на момент
		// запроса тем же обращением к провайдеру, что и марка позиции; сбой деградирует
		// только нереализованные величины и отметку времени марок.
		// Traceability: openspec:analytics/performance#requirement-unrealized-pnl-current-marks
		var evaluation = await new UnrealizedPnlMarkEvaluator(_freshMarkSource)
			.EvaluateAsync(positionMetrics, cancellationToken)
			.ConfigureAwait(false);

		var constructionCalculator = new ConstructionMetricsCalculator(_timeProvider);
		var lifetimeByConstruction = await BuildLifetimeInputsAsync(db, constructions, evaluation, resolver, cancellationToken).ConfigureAwait(false);
		var constructionMetrics = constructions
			.Select(construction => constructionCalculator.Calculate(
				construction.Id,
				// Незаданный капитал передаётся как есть: калькулятор сам не строит
				// проценты без базы процентов.
				construction.AllocatedCapitalUsdt,
				evaluation.Positions.Where(position => position.ConstructionId == construction.Id),
				adjustments
					.Where(adjustment => adjustment.ConstructionId == construction.Id)
					.Select(adjustment => new ConstructionPnLAdjustment
					{
						Date = adjustment.Date,
						AmountUsdt = adjustment.AmountUsdt,
					}),
				_timeProvider.GetUtcNow(),
				// Метрики времени жизни строятся тем же чтением: ноги конструкции
				// разрешены из справочника, свежие тикеры и история снимков общие —
				// саммари и предупреждение об экспирации попадают в метрики одним
				// проходом, без второго обхода тикеров.
				// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
				lifetimeByConstruction.GetValueOrDefault(construction.Id)))
			.ToList();

		// Итог по журналу — сумма итогов конструкций: сбой марок хотя бы одной конструкции
		// делает итог неполным (null), не подменяя его частичной суммой.
		// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
		var totals = constructionMetrics.Select(metrics => metrics.TotalPnL).ToList();
		decimal? totalPnL = totals.Any(total => total == null) ? null : totals.Sum(total => total.GetValueOrDefault());

		// Разбивка итога по журналу: реализованная часть — сумма по всем конструкциям,
		// включая архивные, и доступна всегда; нереализованная гаснет в null при сбое
		// марок хотя бы одной конструкции — то же правило, что у итога. Внешние
		// корректировки PnL в разбивку не входят: они живут только в итоге.
		// Traceability: openspec:analytics/performance#requirement-journal-pnl-aggregates
		var realizedPnL = constructionMetrics.Sum(metrics => metrics.RealizedPnL);
		decimal? unrealizedPnL = constructionMetrics.Any(metrics => metrics.UnrealizedPnL == null)
			? null
			: constructionMetrics.Sum(metrics => metrics.UnrealizedPnL.GetValueOrDefault());

		return new JournalMetrics
		{
			Constructions = constructionMetrics,
			Positions = evaluation.Positions,
			TotalPnL = totalPnL,
			RealizedPnL = realizedPnL,
			UnrealizedPnL = unrealizedPnL,
			MarksAsOf = evaluation.MarksAsOf,
			HasMarkFailure = evaluation.HasMarkFailure,
		};
	}

	#region Помощники

	/// <summary>
	/// Применяет события потока в хронологии позиции: избыточные закрывающие записи
	/// (остаток на момент применения уже нулевой) в поток не входят, а ручная пометка
	/// без количества закрывает весь остаток, накопленный к её моменту, заданная —
	/// запрошенную часть остатка.
	/// </summary>
	private static List<PositionFifoEntry> Replay((long ConstructionId, string Symbol) key, List<StreamEvent> events)
	{
		var ordered = events
			.OrderBy(streamEvent => streamEvent.At)
			.ThenBy(streamEvent => streamEvent.Rank)
			.ThenBy(streamEvent => streamEvent.SourceKey, StringComparer.Ordinal);

		var entries = new List<PositionFifoEntry>();
		var residual = 0m;
		foreach (var streamEvent in ordered)
		{
			switch (streamEvent.Kind)
			{
				case PositionFifoEntryKind.Trade:
					residual += streamEvent.Quantity!.Value;
					entries.Add(ToFifoEntry(streamEvent, streamEvent.Quantity.Value));
					break;

				case PositionFifoEntryKind.ExpiryClosing:
					// Закрывающая запись к нулевому остатку избыточна: в поток позиции она не входит.
					if (residual == 0m)
					{
						continue;
					}

					residual += streamEvent.Quantity!.Value;
					entries.Add(ToFifoEntry(streamEvent, streamEvent.Quantity.Value));
					break;

				case PositionFifoEntryKind.ManualMark:
					// Незаданное количество пометки закрывает весь остаток на её момент;
					// заданное клэмпится остатком — закрывается запрошенная часть,
					// позиция остаётся открытой на разницу.
					// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
					// Traceability: change:manual-journal-management/design#d6
					if (residual == 0m)
					{
						continue;
					}

					var markQuantity = streamEvent.Quantity is { } requested
						? -Math.Sign(residual) * Math.Min(Math.Abs(requested), Math.Abs(residual))
						: -residual;
					entries.Add(ToFifoEntry(streamEvent, markQuantity));
					residual += markQuantity;
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(events), streamEvent.Kind, "Неизвестный вид записи потока позиции.");
			}
		}

		return entries;
	}

	private static PositionFifoEntry ToFifoEntry(StreamEvent streamEvent, decimal quantity) => new()
	{
		At = streamEvent.At,
		Kind = streamEvent.Kind,
		SourceKey = streamEvent.SourceKey,
		Quantity = quantity,
		Price = streamEvent.Price,
		Fee = streamEvent.Fee,
	};

	/// <summary>Добавляет событие в поток пары «конструкция × инструмент», создавая список при первом событии.</summary>
	private static void Append(
		Dictionary<(long ConstructionId, string Symbol), List<StreamEvent>> streams,
		long constructionId,
		string symbol,
		StreamEvent streamEvent)
	{
		var key = (constructionId, symbol);
		if (streams.TryGetValue(key, out var events) == false)
		{
			events = [];
			streams.Add(key, events);
		}

		events.Add(streamEvent);
	}

	/// <summary>Строит ключ источника ручной пометки в едином потоке позиции.</summary>
	private static string ManualSourceKey(long markId) => $"manual:{markId}";

	/// <summary>
	/// Строит ключ события-сделки в потоке позиции: execId биржевой записи, ключ
	/// строки сырья у ручной сделки — у неё execId отсутствует.
	/// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
	/// </summary>
	private static string TradeSourceKey(MaterializedTrade trade) => trade.Source == TradeSource.Manual
		? $"manual:{trade.ManualTradeId?.ToString(CultureInfo.InvariantCulture)}"
		: trade.ExecId!;

	/// <summary>Запрашивает последнюю известную марку инструмента у источника марок; без источника марка неизвестна.</summary>
	private async Task<decimal?> ResolveLastMarkAsync(string symbol, CancellationToken cancellationToken)
	{
		return _lastMarkSource == null ? null : await _lastMarkSource.GetLastMarkAsync(symbol, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Собирает расчётные данные метрик времени жизни по каждой конструкции:
	/// открытые опционные ноги разрешаются из справочника инструментов (страйк,
	/// тип, каноническое время доставки), свежие тикеры текущего чтения и
	/// опорные снимки суточной давности общие для всех конструкций. Символ вне
	/// формата опциона или неразрешимый справочником ногу исключает — метрика
	/// ноги деградирует прочерком, чтение метрик конструкции не ломается.
	/// Traceability: change:add-option-lifetime-metrics/design#d2
	/// </summary>
	private async Task<IReadOnlyDictionary<long, OptionLifetimeInput>> BuildLifetimeInputsAsync(
		JournalDbContext db,
		IReadOnlyList<Construction> constructions,
		UnrealizedPnlEvaluation evaluation,
		InstrumentResolver resolver,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(db);
		ArgumentNullException.ThrowIfNull(constructions);
		ArgumentNullException.ThrowIfNull(evaluation);
		ArgumentNullException.ThrowIfNull(resolver);

		// Опционные ноги ищутся только среди открытых остатков: закрытая позиция
		// в метриках времени жизни не участвует вовсе.
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-intrinsic-time-value
		var openPositions = evaluation.Positions
			.Where(position => position.Residual != 0m)
			.ToList();
		var optionSymbols = openPositions
			.Select(position => position.Symbol)
			.Where(symbol => OptionSymbolParser.TryParse(symbol, out _))
			.Distinct(StringComparer.Ordinal)
			.ToList();

		// Опора прироста читается один раз на чтение метрик журнала: SQL отбирает
		// по символу единственный снимок суточной давности, вычислитель повторно
		// проверяет его давность на клиенте. История append-only и не очищается,
		// поэтому грузить её целиком значило бы дорожать с каждым чтением.
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-acceleration
		var snapshotHistory = optionSymbols.Count == 0
			? []
			: await ReadDecayBaselinesAsync(db, optionSymbols, cancellationToken)
				.ConfigureAwait(false);

		var inputs = new Dictionary<long, OptionLifetimeInput>();
		foreach (var construction in constructions)
		{
			var legs = new List<OptionLegLifetimeInput>();
			foreach (var position in openPositions.Where(position => position.ConstructionId == construction.Id))
			{
				if (OptionSymbolParser.TryParse(position.Symbol, out _) == false)
				{
					continue;
				}

				try
				{
					var resolved = resolver.ResolveOption(position.Symbol);
					legs.Add(new OptionLegLifetimeInput
					{
						Symbol = position.Symbol,
						Strike = resolved.Strike,
						Type = resolved.OptionsType,
						DeliveryTime = resolved.DeliveryTime,
						Residual = position.Residual,
					});
				}
				catch (InstrumentResolveException)
				{
					// Неразрешимый справочником опцион деградирует прочерком:
					// без страйка и времени доставки метрики времени жизни
					// для ноги не строятся, остальные метрики не затронуты.
					// Traceability: openspec:analytics/option-lifetime#scenario-leg-value-degradation-no-tickers
				}
			}

			inputs.Add(construction.Id, new OptionLifetimeInput
			{
				Legs = legs,
				Marks = evaluation.Marks,
				SnapshotHistory = snapshotHistory,
			});
		}

		return inputs;
	}

	/// <summary>
	/// Читает по каждому опционному символу единственный опорный снимок прироста
	/// распада — ближайший к границе суточной давности с известной тетой.
	/// История снимков append-only и не очищается, поэтому отбор опоры выполняется
	/// в SQL оконной функцией по индексу «символ + момент получения»: чтение
	/// метрик не дорожает с ростом истории.
	/// </summary>
	/// <param name="db">Контекст журнала текущего чтения метрик.</param>
	/// <param name="optionSymbols">Символы открытых опционных ног.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Опорные снимки суточной давности — не более одного на символ.</returns>
	private async Task<List<InstrumentMarketSnapshot>> ReadDecayBaselinesAsync(
		JournalDbContext db,
		IReadOnlyList<string> optionSymbols,
		CancellationToken cancellationToken)
	{
		// Граница давности считается тем же поставщиком времени, что отдан
		// вычислителю прироста: отбор опоры и её проверка согласованы.
		var cutoff = _timeProvider.GetUtcNow() - OptionLifetimeMetricsCalculator.SnapshotMinAge;

		var parameters = new List<object> { cutoff };
		var symbolParameters = new List<string>(optionSymbols.Count);
		foreach (var symbol in optionSymbols)
		{
			parameters.Add(symbol);
			symbolParameters.Add($"@p{parameters.Count - 1}");
		}

		// Момент получения хранится текстом, поэтому граница сравнивается в SQL
		// лексикографически: единственный писатель истории штампует момент
		// получения текущим UTC, у всех строк смещение «+00:00», и текстовый
		// порядок совпадает с хронологическим. Снимки без теты отбрасываются
		// здесь же — как и клиентским выбором вычислителя: опорой остаётся
		// ближайшая известная тета давностью не менее суток.
		var sql = $"""
			SELECT "w"."Id", "w"."Symbol", "w"."MarkPrice", "w"."UnderlyingPrice", "w"."Theta", "w"."ReceivedAt"
			FROM (
				SELECT "i"."Id", "i"."Symbol", "i"."MarkPrice", "i"."UnderlyingPrice", "i"."Theta", "i"."ReceivedAt",
					ROW_NUMBER() OVER (
						PARTITION BY "i"."Symbol"
						ORDER BY "i"."ReceivedAt" DESC, "i"."Id" DESC) AS "BaselineRank"
				FROM "InstrumentMarketSnapshots" AS "i"
				WHERE "i"."Symbol" IN ({string.Join(", ", symbolParameters)})
					AND "i"."ReceivedAt" <= @p0
					AND "i"."Theta" IS NOT NULL
			) AS "w"
			WHERE "w"."BaselineRank" = 1
			ORDER BY "w"."Symbol"
			""";
		return await db.InstrumentMarketSnapshots
			.FromSqlRaw(sql, [.. parameters])
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Событие единого потока позиции до применения: у ручной пометки это её собственное количество — незаданное закроет весь остаток, заданное клэмпится остатком.</summary>
	private sealed record StreamEvent(
		DateTimeOffset At,
		int Rank,
		PositionFifoEntryKind Kind,
		string SourceKey,
		decimal? Quantity,
		decimal Price,
		decimal Fee);

	#endregion
}
