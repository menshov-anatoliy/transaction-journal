using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.Analytics;

/// <summary>
/// Провайдер котировок инструментов: публичный эндпоинт тикеров без аутентификации
/// и кэш последней известной котировки в хранилище журнала. Свежая котировка запрашивается
/// у биржи по символу и тут же штампуется временем получения; каждая успешная
/// выборка дополнительно дописывает append-only строку истории рыночных снимков
/// с индексной ценой и тетой ответа — материал прироста распада. Читающие слои
/// берут последнюю известную котировку из кэша без сетевого запроса — так ручная
/// пометка закрытия без цены получает дефолт, а оценка нереализованного PnL —
/// котировку на момент запроса. Каждый вызов создаёт собственный короткоживущий
/// контекст, поэтому провайдер безопасен в длительных сессиях Blazor Server.
// Traceability: openspec:analytics/performance#requirement-mark-provider
// Traceability: openspec:analytics/option-lifetime#requirement-market-snapshot-history
// Traceability: change:add-analytics/design#d2
/// </summary>
public sealed class InstrumentQuoteProvider : IInstrumentQuoteSource, IFreshInstrumentQuoteSource
{
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly BybitTickersClient _tickersClient;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт провайдер над опциями контекста журнала и клиентом публичных тикеров; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="tickersClient">Клиент публичного эндпоинта тикеров — источник свежих котировок.</param>
	/// <param name="timeProvider">Поставщик времени для штамповки момента получения котировки; по умолчанию системный.</param>
	/// <exception cref="ArgumentNullException">Опции или клиент тикеров не заданы.</exception>
	public InstrumentQuoteProvider(
		DbContextOptions<JournalDbContext> options,
		BybitTickersClient tickersClient,
		TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_tickersClient = tickersClient ?? throw new ArgumentNullException(nameof(tickersClient));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	#region Свежая котировка

	/// <summary>
	/// Запрашивает свежую котировку инструмента у публичного эндпоинта тикеров и
	/// штампует ею кэш: в хранилище сохраняются и цена, и время её получения,
	/// поэтому строка кэша всегда несёт последнюю известную котировку с моментом
	/// её получения. Каждая успешная выборка дополнительно дописывает строку
	/// в append-only историю рыночных снимков — с индексной ценой и тетой
	/// ответа тикера; у не-опционов биржа поля не отдаёт и в снимок пишется
	/// null. Категория запроса выводится из справочника инструментов
	/// синхронизации; неизвестный справочнику символ не запрашивается вовсе.
	/// </summary>
	/// <param name="symbol">Символ инструмента, например BTCUSDT или BTC-29DEC23-25000-C.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Свежая котировка со временем получения и nullable-полями теты и индексной цены; null — инструмент неизвестен справочнику или биржа не отдала котировку.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов; решение о деградации остаётся за вызывающим слоем.</exception>
	public async Task<InstrumentQuoteSnapshot?> GetFreshQuoteAsync(string symbol, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Категория обязательна для эндпоинта тикеров: берём её из справочника
		// инструментов, который ведёт синхронизация, — по одному запросу на символ.
		// Traceability: change:add-analytics/design#d2
		string? category;
		using (var db = new JournalDbContext(_options))
		{
			category = await db.RawInstruments
				.AsNoTracking()
				.Where(instrument => instrument.Symbol == symbol)
				.Select(instrument => instrument.Category)
				.FirstOrDefaultAsync(cancellationToken)
				.ConfigureAwait(false);
		}

		if (category == null)
		{
			// Инструмент вне справочника — запрос тикеров не построить; вызывающий
			// слой получает null и сам решает, как деградировать.
			return null;
		}

		var tickers = await _tickersClient.GetTickersAsync(new BybitTickerQuery
		{
			Category = category,
			Symbol = symbol,
		}, cancellationToken).ConfigureAwait(false);
		var ticker = tickers.FirstOrDefault(ticker => string.Equals(ticker.Symbol, symbol, StringComparison.Ordinal));
		var quotePrice = ticker?.MarkPrice;
		if (quotePrice == null)
		{
			// Биржа не отдала котировку инструмента — кэш не трогаем, последняя известная
			// котировка остаётся прежней.
			return null;
		}

		// Кэш штампуется временем: вместе с ценой сохраняется момент её получения.
		// Traceability: openspec:analytics/performance#scenario-mark-cache-timestamped
		var receivedAt = _timeProvider.GetUtcNow();
		using (var db = new JournalDbContext(_options))
		{
			var cached = await db.InstrumentQuotes
				.FirstOrDefaultAsync(quote => quote.Symbol == symbol, cancellationToken)
				.ConfigureAwait(false);
			if (cached == null)
			{
				db.InstrumentQuotes.Add(new InstrumentQuote
				{
					Symbol = symbol,
					QuotePrice = quotePrice.Value,
					ReceivedAt = receivedAt,
				});
			}
			else
			{
				cached.QuotePrice = quotePrice.Value;
				cached.ReceivedAt = receivedAt;
			}

			// Каждая успешная выборка тикеров дописывает append-only строку истории
			// рыночных снимков: котировку, индексную цену и тету ответа с моментом
			// получения — той же записью, что и штамп кэша. Пустой ответ биржи и
			// сбой тикеров историю не пополняют: материал прироста распада
			// появляется только из фактических ответов биржи.
			// Traceability: openspec:analytics/option-lifetime#requirement-market-snapshot-history
			db.InstrumentMarketSnapshots.Add(new InstrumentMarketSnapshot
			{
				Symbol = symbol,
				QuotePrice = quotePrice.Value,
				UnderlyingPrice = ticker!.UnderlyingPrice,
				Theta = ticker!.Theta,
				ReceivedAt = receivedAt,
			});

			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return new InstrumentQuoteSnapshot(symbol, quotePrice.Value, receivedAt, ticker!.UnderlyingPrice, ticker!.Theta);
	}

	#endregion

	#region Последняя известная котировка

	/// <summary>
	/// Возвращает последнюю известную котировку инструмента из кэша провайдера —
	/// без сетевого запроса. Служит дефолтом цены ручной пометки закрытия,
	/// когда пользователь не задал цену: read-модель позиций подставляет котировку
	/// при чтении. Котировка неизвестна, пока провайдер не получил ни одной свежей
	/// котировки инструмента.
	/// </summary>
	/// <param name="symbol">Инструмент, котировка которого нужна.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Последняя известная котировка или null, если котировка ещё не известна.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	public async Task<decimal?> GetLastQuoteAsync(string symbol, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Дефолт ручной пометки читает кэш: пользователь вправе задать цену явно,
		// а возраст последней известной котировки показывается по её штампу времени.
		// Traceability: openspec:analytics/performance#scenario-last-known-mark-serves-manual-default
		using var db = new JournalDbContext(_options);
		var quotePrice = await db.InstrumentQuotes
			.AsNoTracking()
			.Where(quote => quote.Symbol == symbol)
			.Select(quote => (decimal?)quote.QuotePrice)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
		return quotePrice;
	}

	#endregion
}
