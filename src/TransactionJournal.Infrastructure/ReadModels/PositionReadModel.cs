using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.ReadModels;


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
	private const int ManualAnnotationRank = 2;

	private readonly DbContextOptions<JournalDbContext> _options;

	private readonly IInstrumentQuoteSource? _quoteSource;

	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт read-модель над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="quoteSource">Источник последних котировок для ручных пометок без цены; null — цена остаётся неизвестной.</param>
	/// <param name="timeProvider">Поставщик момента «сейчас» для вывода OTM-закрывающих; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public PositionReadModel(
		DbContextOptions<JournalDbContext> options,
		IInstrumentQuoteSource? quoteSource = null,
		TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_quoteSource = quoteSource;
		_timeProvider = timeProvider ?? TimeProvider.System;
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
	/// где закрывающая запись обнуляет позицию либо её заданную часть, а пометка
	/// без количества закрывает весь остаток на момент применения.
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
		var quotes = await db.ManualCloseAnnotations
			.AsNoTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		// Ручные сделки — второе сырьё позиций: читаются тем же чтением, что и
		// биржевые записи, и материализуются в общий поток наравне с ними.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
		var manualTrades = await db.ManualTrades
			.AsNoTracking()
			.OrderBy(trade => trade.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// Конвейер проекции строится заново при каждом чтении: сделки и закрывающие
		// записи экспираций выводятся из одного и того же сырья детерминированно.
		var resolver = new InstrumentResolver(new InstrumentCatalog(rawInstruments));
		var tradeMaterializer = new TradeMaterializer(resolver);
		var trades = tradeMaterializer.Materialize(rawExecutions, manualTrades).Trades;

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
			// Ручные сделки — второе сырьё остатков и для экспираций: delivery/OTM
			// закрывают и ручную часть позиции, иначе позиции расходятся с метриками.
			// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
			.Materialize(rawExecutions, rawDeliveries, assignments, _timeProvider.GetUtcNow(), manualTrades);

		var timelines = new Dictionary<(long ConstructionId, string Symbol), List<TimelineEvent>>();
		foreach (var trade in trades)
		{
			// Привязка ручной сделки живёт в её сырье, биржевой — в пользовательских
			// данных по execId: обе попадают в хронологию позиций одинаковым событием.
			// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
			var tradeConstructionId = trade.Source == TradeSource.Manual
				? trade.ConstructionId
				: constructionIdByExecId.TryGetValue(trade.ExecId!, out var boundId) ? boundId : null;
			if (tradeConstructionId is null
				|| scopeIds.Contains(tradeConstructionId.Value) == false)
			{
				continue;
			}

			Append(timelines, tradeConstructionId.Value, trade.Symbol,
				new TradeEvent(trade.ExecutedAt, TradeSourceKey(trade), trade.Quantity));
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

		foreach (var quote in quotes)
		{
			if (scopeIds.Contains(quote.ConstructionId) == false)
			{
				continue;
			}

			// Цена пометки без пользовательского значения подставляется последней известной
			// котировкой инструмента при чтении; без источника котировок цена остаётся неизвестной.
			// Traceability: openspec:domain/constructions#scenario-manual-mark-default-last-mark
			var price = quote.Price ?? await ResolveLastQuoteAsync(quote.Symbol, cancellationToken).ConfigureAwait(false);
			Append(timelines, quote.ConstructionId, quote.Symbol,
				new ManualAnnotationEvent(quote.AnnotatedAt, ManualSourceKey(quote.Id), price, quote.Quantity));
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

					case ManualAnnotationEvent annotationEvent:
						if (residual == 0m)
						{
							warnings.Add(CreateWarning(key, annotationEvent.At, PositionClosingKind.ManualAnnotation, annotationEvent.SourceKey));
							break;
						}

						// Количество пометки: незаданное закрывает весь остаток на момент
						// применения по хронологии; заданное клэмпится остатком — закрывается
						// запрошенная часть, позиция остаётся открытой на разницу. Знак
						// закрывающей записи противоположен остатку.
						// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
						// Traceability: change:manual-journal-management/design#d6
						var quantity = annotationEvent.Quantity is { } requested
							? -Math.Sign(residual) * Math.Min(Math.Abs(requested), Math.Abs(residual))
							: -residual;
						residual += quantity;
						closingEntries.Add(new PositionClosingEntry
						{
							ConstructionId = key.ConstructionId,
							Symbol = key.Symbol,
							Kind = PositionClosingKind.ManualAnnotation,
							ClosedAt = annotationEvent.At,
							Quantity = quantity,
							Price = annotationEvent.Price,
							SourceKey = annotationEvent.SourceKey,
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
	private static string ManualSourceKey(long annotationId) => $"manual:{annotationId}";

	/// <summary>
	/// Строит ключ события-сделки в хронологии позиции: execId биржевой записи,
	/// ключ строки сырья у ручной сделки — у неё execId отсутствует.
	/// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
	/// </summary>
	private static string TradeSourceKey(MaterializedTrade trade) => trade.Source == TradeSource.Manual
		? $"manual:{trade.ManualTradeId?.ToString(CultureInfo.InvariantCulture)}"
		: trade.ExecId!;

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

	/// <summary>Запрашивает последнюю известную котировку инструмента у источника котировок; без источника котировка неизвестна.</summary>
	private async Task<decimal?> ResolveLastQuoteAsync(string symbol, CancellationToken cancellationToken)
	{
		return _quoteSource == null ? null : await _quoteSource.GetLastQuoteAsync(symbol, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Событие хронологии позиции: момент, ранг устойчивости и ключ источника.</summary>
	private abstract record TimelineEvent(DateTimeOffset At, int Rank, string SourceKey);

	/// <summary>Сделка конструкции: меняет остаток своим знаковым количеством.</summary>
	private sealed record TradeEvent(DateTimeOffset At, string ExecId, decimal Quantity)
		: TimelineEvent(At, TradeRank, ExecId);

	/// <summary>Биржевая закрывающая запись экспирации с фиксированным количеством и эффективной ценой.</summary>
	private sealed record ExpiryEvent(DateTimeOffset At, string SourceKey, PositionClosingKind Kind, decimal Quantity, decimal Price)
		: TimelineEvent(At, ExpiryRank, SourceKey);

	/// <summary>Ручная пометка закрытия: незаданное количество закрывает весь остаток на момент применения, заданное клэмпится остатком.</summary>
	private sealed record ManualAnnotationEvent(DateTimeOffset At, string SourceKey, decimal? Price, decimal? Quantity)
		: TimelineEvent(At, ManualAnnotationRank, SourceKey);

	#endregion
}

