using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;
using TransactionJournal.Materialization;

namespace TransactionJournal.Domain;

/// <summary>
/// Read-модель позиций: чистый остаток по «конструкция × инструмент», вычисляемый
/// при чтении из привязанных сделок материализатора синхронизации и единого потока
/// закрывающих записей — delivery/OTM-экспираций из sync-слоя и ручных пометок
/// пользователя. Мутирующего API у модели нет: количество позиции меняется только
/// сделками и закрывающими записями. Чтение ничего не пишет в базу и безопасно
/// в длительных сессиях Blazor Server.
// Traceability: openspec:domain/constructions#requirement-position-derived-residual
// Traceability: change:add-core-domain/design#d5
/// </summary>
public sealed class PositionReadModel
{
	/// <summary>Ранг события-сделки в хронологии: сделка раньше закрывающих записей того же момента.</summary>
	private const int TradeRank = 0;

	/// <summary>Ранг биржевой закрывающей записи: при равном времени приоритетнее ручной пометки.</summary>
	private const int ExpiryRank = 1;

	/// <summary>Ранг ручной пометки: fallback-запись уступает биржевой записи того же момента.</summary>
	private const int ManualMarkRank = 2;

	private readonly DbContextOptions<JournalDbContext> _options;

	private readonly IInstrumentMarkSource? _markSource;

	/// <summary>Создаёт read-модель над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="markSource">Источник последних марок для ручных пометок без цены; null — цена остаётся неизвестной.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public PositionReadModel(DbContextOptions<JournalDbContext> options, IInstrumentMarkSource? markSource = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_markSource = markSource;
	}

	/// <summary>
	/// Возвращает позиции всех конструкций: чистые остатки по «конструкция × инструмент»,
	/// единый поток применённых закрывающих записей и предупреждения об избыточных записях.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="TradeMaterializationException">Сырая запись исполнения повреждена или конфликтует с другой записью того же execId.</exception>
	/// <exception cref="ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует с другой записью того же ключа.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	public async Task<PositionReadResult> ListAsync(CancellationToken cancellationToken = default)
	{
		return await ComputeAsync(null, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Возвращает позиции одной конструкции: чистые остатки по её инструментам,
	/// поток её закрывающих записей и предупреждения об избыточных записях.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	/// <exception cref="TradeMaterializationException">Сырая запись исполнения повреждена или конфликтует с другой записью того же execId.</exception>
	/// <exception cref="ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует с другой записью того же ключа.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	public async Task<PositionReadResult> ListAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		return await ComputeAsync(constructionId, cancellationToken).ConfigureAwait(false);
	}

	#region Вычисление позиций

	/// <summary>
	/// Строит снапшоты позиций из сырых записей и пользовательских данных: сделки
	/// материализуются конвейером синхронизации, закрывающие записи экспираций —
	/// материализатором экспираций, ручные пометки — записями домена; все три вида
	/// сливаются в единую хронологию по каждой паре «конструкция × инструмент»,
	/// где закрывающая запись с количеством, равным остатку на момент применения,
	/// обнуляет позицию.
	/// </summary>
	/// <param name="constructionId">Фильтр по конструкции; null — все конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция фильтра не найдена.</exception>
	private async Task<PositionReadResult> ComputeAsync(long? constructionId, CancellationToken cancellationToken)
	{
		using var db = new JournalDbContext(_options);

		// Конструкции задают область чтения: без сделок и записей конструкции в позициях не участвуют.
		var constructionsQuery = db.Constructions.AsNoTracking();
		if (constructionId is { } filterId)
		{
			constructionsQuery = constructionsQuery.Where(construction => construction.Id == filterId);
		}

		var constructions = await constructionsQuery
			.OrderBy(construction => construction.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		if (constructionId is { } missingId && constructions.Count == 0)
		{
			throw new ConstructionNotFoundException(missingId);
		}

		var scopeIds = constructions.Select(construction => construction.Id).ToHashSet();
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
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// Конвейер проекции строится заново при каждом чтении: сделки и закрывающие
		// записи экспираций выводятся из одного и того же сырья детерминированно.
		var resolver = new InstrumentResolver(new InstrumentCatalog(rawInstruments));
		var tradeMaterializer = new TradeMaterializer(resolver);
		var trades = tradeMaterializer.Materialize(rawExecutions);

		// Привязки передаются материализатору экспираций в строковой форме его контракта;
		// непривязанные сделки образуют остаток «Входящих» и в позициях не участвуют.
		// Traceability: change:add-core-domain/design#d6
		var constructionIdByExecId = boundUserdata.ToDictionary(
			userdata => userdata.ExecId,
			userdata => userdata.ConstructionId!.Value);
		Dictionary<string, string?> assignments = constructionIdByExecId.ToDictionary(
			pair => pair.Key,
			pair => (string?)pair.Value.ToString(CultureInfo.InvariantCulture));
		var expiry = new ExpiryMaterializer(tradeMaterializer, resolver)
			.Materialize(rawExecutions, rawDeliveries, assignments, DateTimeOffset.UtcNow);

		var timelines = new Dictionary<(long ConstructionId, string Symbol), List<TimelineEvent>>();
		foreach (var trade in trades)
		{
			if (constructionIdByExecId.TryGetValue(trade.ExecId, out var tradeConstructionId) == false
				|| scopeIds.Contains(tradeConstructionId) == false)
			{
				continue;
			}

			Append(timelines, tradeConstructionId, trade.Symbol,
				new TradeEvent(trade.ExecutedAt, trade.ExecId, trade.Quantity));
		}

		foreach (var entry in expiry.ClosingEntries)
		{
			if (entry.ConstructionId is null
				|| long.TryParse(entry.ConstructionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryConstructionId) == false
				|| scopeIds.Contains(entryConstructionId) == false)
			{
				continue;
			}

			Append(timelines, entryConstructionId, entry.Symbol, new ExpiryEvent(
				entry.ClosedAt, entry.SourceKey, MapKind(entry.Kind), entry.Quantity, entry.EffectivePrice));
		}

		foreach (var mark in marks)
		{
			if (scopeIds.Contains(mark.ConstructionId) == false)
			{
				continue;
			}

			// Цена пометки без пользовательского значения подставляется последней известной
			// маркой инструмента при чтении; без источника марок цена остаётся неизвестной.
			// Traceability: openspec:domain/constructions#scenario-manual-mark-default-last-mark
			var price = mark.Price ?? await ResolveLastMarkAsync(mark.Symbol, cancellationToken).ConfigureAwait(false);
			Append(timelines, mark.ConstructionId, mark.Symbol,
				new ManualMarkEvent(mark.MarkedAt, ManualSourceKey(mark.Id), price));
		}

		var positions = new List<PositionSnapshot>();
		var closingEntries = new List<PositionClosingEntry>();
		var warnings = new List<RedundantClosingEntryWarning>();
		var orderedKeys = timelines.Keys
			.OrderBy(key => key.ConstructionId)
			.ThenBy(key => key.Symbol, StringComparer.Ordinal);
		foreach (var key in orderedKeys)
		{
			// Остаток вычисляется проходом по хронологии: сделки меняют остаток, закрывающие
			// записи обнуляют его количеством на момент применения; запись к уже нулевому
			// остатку избыточна и превращается в предупреждение, не переворачивая позицию.
			// Traceability: openspec:domain/constructions#requirement-position-derived-residual
			var residual = 0m;
			var events = timelines[key]
				.OrderBy(timelineEvent => timelineEvent.At)
				.ThenBy(timelineEvent => timelineEvent.Rank)
				.ThenBy(timelineEvent => timelineEvent.SourceKey, StringComparer.Ordinal);
			foreach (var timelineEvent in events)
			{
				switch (timelineEvent)
				{
					case TradeEvent trade:
						residual += trade.Quantity;
						break;

					case ExpiryEvent expiryEvent:
						if (residual == 0m)
						{
							warnings.Add(CreateWarning(key, expiryEvent.At, expiryEvent.Kind, expiryEvent.SourceKey));
							break;
						}

						residual += expiryEvent.Quantity;
						closingEntries.Add(new PositionClosingEntry
						{
							ConstructionId = key.ConstructionId,
							Symbol = key.Symbol,
							Kind = expiryEvent.Kind,
							ClosedAt = expiryEvent.At,
							Quantity = expiryEvent.Quantity,
							Price = expiryEvent.Price,
							SourceKey = expiryEvent.SourceKey,
						});
						break;

					case ManualMarkEvent markEvent:
						if (residual == 0m)
						{
							warnings.Add(CreateWarning(key, markEvent.At, PositionClosingKind.ManualMark, markEvent.SourceKey));
							break;
						}

						// Количество ручной пометки — остаток на момент применения по хронологии:
						// пометка закрывает ровно то, что накопилось к её времени.
						// Traceability: change:add-core-domain/design#d3
						var quantity = -residual;
						residual = 0m;
						closingEntries.Add(new PositionClosingEntry
						{
							ConstructionId = key.ConstructionId,
							Symbol = key.Symbol,
							Kind = PositionClosingKind.ManualMark,
							ClosedAt = markEvent.At,
							Quantity = quantity,
							Price = markEvent.Price,
							SourceKey = markEvent.SourceKey,
						});
						break;
				}
			}

			// Позиция открыта, пока остаток не нулевой: обнуление закрывает её без специальных записей.
			// Traceability: openspec:domain/constructions#scenario-close-by-offsetting-trades
			positions.Add(new PositionSnapshot
			{
				ConstructionId = key.ConstructionId,
				Symbol = key.Symbol,
				Residual = residual,
				IsOpen = residual != 0m,
			});
		}

		return new PositionReadResult
		{
			Positions = positions,
			ClosingEntries = closingEntries
				.OrderBy(entry => entry.ClosedAt)
				.ThenBy(entry => entry.ConstructionId)
				.ThenBy(entry => entry.Symbol, StringComparer.Ordinal)
				.ThenBy(entry => entry.Kind)
				.ThenBy(entry => entry.SourceKey, StringComparer.Ordinal)
				.ToList(),
			Warnings = warnings
				.OrderBy(warning => warning.ClosedAt)
				.ThenBy(warning => warning.ConstructionId)
				.ThenBy(warning => warning.Symbol, StringComparer.Ordinal)
				.ThenBy(warning => warning.SourceKey, StringComparer.Ordinal)
				.ToList(),
		};
	}

	#endregion

	#region Помощники

	/// <summary>Добавляет событие в хронологию пары «конструкция × инструмент», создавая список при первом событии.</summary>
	private static void Append(
		Dictionary<(long ConstructionId, string Symbol), List<TimelineEvent>> timelines,
		long constructionId,
		string symbol,
		TimelineEvent timelineEvent)
	{
		var key = (constructionId, symbol);
		if (timelines.TryGetValue(key, out var events) == false)
		{
			events = [];
			timelines.Add(key, events);
		}

		events.Add(timelineEvent);
	}

	/// <summary>Переводит вид биржевой закрывающей записи в вид единого потока позиций.</summary>
	private static PositionClosingKind MapKind(ExpiryClosingKind kind) => kind switch
	{
		ExpiryClosingKind.Delivery => PositionClosingKind.Delivery,
		ExpiryClosingKind.OtmExpiry => PositionClosingKind.OtmExpiry,
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид закрывающей записи экспирации."),
	};

	/// <summary>Строит ключ источника ручной пометки в едином потоке закрывающих записей.</summary>
	private static string ManualSourceKey(long markId) => $"manual:{markId}";

	/// <summary>Создаёт предупреждение об избыточной закрывающей записи: остаток на момент применения уже нулевой.</summary>
	private static RedundantClosingEntryWarning CreateWarning(
		(long ConstructionId, string Symbol) key,
		DateTimeOffset closedAt,
		PositionClosingKind kind,
		string sourceKey) => new()
	{
		ConstructionId = key.ConstructionId,
		Symbol = key.Symbol,
		Kind = kind,
		ClosedAt = closedAt,
		SourceKey = sourceKey,
	};

	/// <summary>Запрашивает последнюю известную марку инструмента у источника марок; без источника марка неизвестна.</summary>
	private async Task<decimal?> ResolveLastMarkAsync(string symbol, CancellationToken cancellationToken)
	{
		return _markSource == null ? null : await _markSource.GetLastMarkAsync(symbol, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Событие хронологии позиции: момент, ранг устойчивости и ключ источника.</summary>
	private abstract record TimelineEvent(DateTimeOffset At, int Rank, string SourceKey);

	/// <summary>Сделка конструкции: меняет остаток своим знаковым количеством.</summary>
	private sealed record TradeEvent(DateTimeOffset At, string ExecId, decimal Quantity)
		: TimelineEvent(At, TradeRank, ExecId);

	/// <summary>Биржевая закрывающая запись экспирации с фиксированным количеством и эффективной ценой.</summary>
	private sealed record ExpiryEvent(DateTimeOffset At, string SourceKey, PositionClosingKind Kind, decimal Quantity, decimal Price)
		: TimelineEvent(At, ExpiryRank, SourceKey);

	/// <summary>Ручная пометка закрытия: количество вычисляется применением — остаток на её момент.</summary>
	private sealed record ManualMarkEvent(DateTimeOffset At, string SourceKey, decimal? Price)
		: TimelineEvent(At, ManualMarkRank, SourceKey);

	#endregion
}
