using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Materialization;

namespace TransactionJournal.Analytics;

/// <summary>
/// Read-модель метрик журнала: сводит читающий слой в метрики для экранов UI.
/// Сырые записи синхронизации материализуются в сделки и закрывающие записи
/// экспираций, привязки собирают записи в потоки позиций «конструкция ×
/// инструмент», ручные пометки вливаются в те же потоки, конвейер строится
/// заново при каждом чтении — без хранимых результатов и промежуточного
/// состояния. Композиция повторяет правила read-модели позиций: хронология по
/// моменту, рангу вида и ключу источника, избыточные закрывающие записи в поток
/// не входят, пометка закрывает остаток на её момент.
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

	/// <summary>Создаёт read-модель над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="freshMarkSource">Источник свежих марок для оценки нереализованного PnL открытых остатков.</param>
	/// <param name="lastMarkSource">Источник последних марок для ручных пометок без цены; null — цена остаётся неизвестной.</param>
	/// <exception cref="ArgumentNullException">Опции или источник свежих марок не заданы.</exception>
	public JournalMetricsReadModel(
		DbContextOptions<JournalDbContext> options,
		IFreshInstrumentMarkSource freshMarkSource,
		IInstrumentMarkSource? lastMarkSource = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_freshMarkSource = freshMarkSource ?? throw new ArgumentNullException(nameof(freshMarkSource));
		_lastMarkSource = lastMarkSource;
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
		var adjustments = await db.PnLAdjustments
			.AsNoTracking()
			.OrderBy(adjustment => adjustment.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// Конвейер проекции строится заново при каждом чтении: сделки и закрывающие
		// записи экспираций выводятся из одного и того же сырья детерминированно.
		var resolver = new InstrumentResolver(new InstrumentCatalog(rawInstruments));
		var tradeMaterializer = new TradeMaterializer(resolver);
		var trades = tradeMaterializer.Materialize(rawExecutions);

		// Привязки передаются материализатору экспираций в строковой форме его контракта;
		// непривязанные сделки образуют остаток «Входящих» и в метриках не участвуют.
		var constructionIdByExecId = boundUserdata.ToDictionary(
			userdata => userdata.ExecId,
			userdata => userdata.ConstructionId!.Value);
		var assignments = constructionIdByExecId.ToDictionary(
			pair => pair.Key,
			pair => (string?)pair.Value.ToString(CultureInfo.InvariantCulture));
		var expiry = new ExpiryMaterializer(tradeMaterializer, resolver)
			.Materialize(rawExecutions, rawDeliveries, assignments, DateTimeOffset.UtcNow);

		var streams = new Dictionary<(long ConstructionId, string Symbol), List<StreamEvent>>();
		foreach (var trade in trades)
		{
			if (constructionIdByExecId.TryGetValue(trade.ExecId, out var constructionId) == false)
			{
				continue;
			}

			Append(streams, constructionId, trade.Symbol, new StreamEvent(
				trade.ExecutedAt, TradeRank, PositionFifoEntryKind.Trade, trade.ExecId, trade.Quantity, trade.Price, trade.Fee));
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
			// Traceability: openspec:domain/constructions#scenario-manual-mark-default-last-mark
			var price = mark.Price ?? await ResolveLastMarkAsync(mark.Symbol, cancellationToken).ConfigureAwait(false) ?? 0m;
			Append(streams, mark.ConstructionId, mark.Symbol, new StreamEvent(
				mark.MarkedAt, ManualMarkRank, PositionFifoEntryKind.ManualMark, ManualSourceKey(mark.Id), null, price, 0m));
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

		var constructionCalculator = new ConstructionMetricsCalculator();
		var constructionMetrics = constructions
			.Select(construction => constructionCalculator.Calculate(
				construction.Id,
				construction.AllocatedCapitalUsdt,
				evaluation.Positions.Where(position => position.ConstructionId == construction.Id),
				adjustments
					.Where(adjustment => adjustment.ConstructionId == construction.Id)
					.Select(adjustment => new ConstructionPnLAdjustment
					{
						Date = adjustment.Date,
						AmountUsdt = adjustment.AmountUsdt,
					}),
				DateTimeOffset.UtcNow))
			.ToList();

		// Итог по журналу — сумма итогов конструкций: сбой марок хотя бы одной конструкции
		// делает итог неполным (null), не подменяя его частичной суммой.
		// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
		var totals = constructionMetrics.Select(metrics => metrics.TotalPnL).ToList();
		decimal? totalPnL = totals.Any(total => total == null) ? null : totals.Sum(total => total.GetValueOrDefault());

		return new JournalMetrics
		{
			Constructions = constructionMetrics,
			Positions = evaluation.Positions,
			TotalPnL = totalPnL,
			MarksAsOf = evaluation.MarksAsOf,
			HasMarkFailure = evaluation.HasMarkFailure,
		};
	}

	#region Помощники

	/// <summary>
	/// Применяет события потока в хронологии позиции: избыточные закрывающие записи
	/// (остаток на момент применения уже нулевой) в поток не входят, а ручная пометка
	/// закрывает ровно тот остаток, что накопился к её моменту.
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
					// Пометка закрывает ровно тот остаток, что накопился к её моменту.
					if (residual == 0m)
					{
						continue;
					}

					entries.Add(ToFifoEntry(streamEvent, -residual));
					residual = 0m;
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

	/// <summary>Запрашивает последнюю известную марку инструмента у источника марок; без источника марка неизвестна.</summary>
	private async Task<decimal?> ResolveLastMarkAsync(string symbol, CancellationToken cancellationToken)
	{
		return _lastMarkSource == null ? null : await _lastMarkSource.GetLastMarkAsync(symbol, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Событие единого потока позиции до применения: количество ручной пометки выводится применением потока.</summary>
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
