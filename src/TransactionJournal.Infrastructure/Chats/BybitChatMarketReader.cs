namespace TransactionJournal.Infrastructure.Chats;

using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Infrastructure.Data;

/// <summary>
/// Адаптер рыночных данных инструментов чата поверх публичного клиента
/// тикеров Bybit: снимок фьючерсного рынка — один запрос категории linear по
/// символу перпа, доска опционов — один запрос категории option с фильтром
/// baseCoin. Троттлер и resilience клиента наследуются целиком, собственных
/// счётчиков запросов нет; сбой биржи — управляемая недоступность в записи
/// результата, а не исключение: вместе с пометкой недоступности адаптер
/// возвращает последнюю кэшированную проекцию из кэша марок InstrumentMarkProvider
/// с явным as-of кэша, чтобы инструмент чата ответил структурированным
/// «недоступно + кэш». Деградация покрывает и ошибки конверта биржи, и
/// транспортные сбои после всех повторов resilience: сеть, DNS, отказ соединения
/// и таймаут HTTP — для инструмента чата это одна недоступность биржи.
// Traceability: openspec:chats/sources#requirement-sources-single-request-per-call
// Traceability: openspec:chats/sources#requirement-sources-degradation-cached-asof
/// </summary>
public sealed class BybitChatMarketReader : IChatMarketReader
{
	/// <summary>Категория фьючерсных тикеров: перп базового актива несёт марку и ставку фандинга.</summary>
	private const string LinearCategory = "linear";

	/// <summary>Категория опционных тикеров: доска запрашивается фильтром baseCoin.</summary>
	private const string OptionCategory = "option";

	private readonly BybitTickersClient _tickersClient;

	private readonly DbContextOptions<JournalDbContext> _markCacheOptions;

	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт адаптер над клиентом тикеров и кэшем марок журнала.</summary>
	/// <param name="tickersClient">Единый клиент тикеров Bybit с троттлером и resilience.</param>
	/// <param name="markCacheOptions">Опции контекста журнала: чтение кэша марок при деградации — короткоживущий контекст на каждое чтение.</param>
	/// <param name="timeProvider">Поставщик момента as-of ответа; по умолчанию системные часы.</param>
	public BybitChatMarketReader(
		BybitTickersClient tickersClient,
		DbContextOptions<JournalDbContext> markCacheOptions,
		TimeProvider? timeProvider = null)
	{
		_tickersClient = tickersClient ?? throw new ArgumentNullException(nameof(tickersClient));
		_markCacheOptions = markCacheOptions ?? throw new ArgumentNullException(nameof(markCacheOptions));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Ровно один HTTP-запрос категории linear: перп {baseCoin}USDT несёт марку,
	/// бид-аск, открытый интерес и ставку фандинга; спот отдельным запросом не
	/// запрашивается — глубину биржевых обращений ограничивает потолок итераций
	/// агентного цикла. При сбое биржи снимок деградирует в кэш: последняя
	/// кэшированная марка перпа с моментом её получения в роли as-of.
	// Traceability: openspec:chats/sources#requirement-sources-single-request-per-call
	/// </remarks>
	public async Task<ChatMarketSnapshot> ReadSnapshotAsync(string baseCoin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		try
		{
			var tickers = await _tickersClient.GetTickersAsync(
				new BybitTickerQuery { Category = LinearCategory, Symbol = baseCoin + "USDT" },
				cancellationToken).ConfigureAwait(false);
			var ticker = tickers.FirstOrDefault();
			return new ChatMarketSnapshot
			{
				BaseCoin = baseCoin,
				AsOf = _timeProvider.GetUtcNow(),
				IsAvailable = true,
				Symbol = ticker?.Symbol,
				MarkPrice = ticker?.MarkPrice,
				Bid1Price = ticker?.Bid1Price,
				Bid1Size = ticker?.Bid1Size,
				Ask1Price = ticker?.Ask1Price,
				Ask1Size = ticker?.Ask1Size,
				OpenInterest = ticker?.OpenInterest,
				FundingRate = ticker?.FundingRate,
			};
		}
		catch (BybitApiException exception)
		{
			// Деградация при сбое биржи: вместе с пометкой недоступности возвращается
			// последняя кэшированная марка перпа из кэша InstrumentMarkProvider, а as-of
			// снимка — момент её получения, а не момент сбоя.
			// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
			return await SnapshotFromCacheAsync(baseCoin, exception.Message, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException exception)
		{
			// Транспортный сбой (сеть, DNS, отказ соединения) после всех повторов
			// resilience деградирует так же, как ошибка биржи: для инструмента
			// чата это одна недоступность биржи с кэшем и её as-of.
			// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
			return await SnapshotFromCacheAsync(baseCoin, exception.Message, cancellationToken).ConfigureAwait(false);
		}
		catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
		{
			// Таймаут HTTP-запроса — та же недоступность биржи; отмена вызывающим
			// кодом сюда не попадает: у неё нет внутреннего TimeoutException.
			// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
			return await SnapshotFromCacheAsync(baseCoin, exception.InnerException.Message, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Ровно один HTTP-запрос категории option с фильтром baseCoin — биржа
	/// требует для опционов фильтр symbol или baseCoin и отвечает всей доской
	/// актива. Символы вне формата опционов отбрасываются с подсчётом, котировки
	/// приводятся к каноническим частям символа. При сбое биржи доска деградирует
	/// в кэш марок: последние известные цены опционов актива с as-of кэша.
	// Traceability: openspec:chats/sources#requirement-sources-single-request-per-call
	/// </remarks>
	public async Task<ChatOptionBoard> ReadOptionBoardAsync(string baseCoin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		try
		{
			var tickers = await _tickersClient.GetTickersAsync(
				new BybitTickerQuery { Category = OptionCategory, BaseCoin = baseCoin },
				cancellationToken).ConfigureAwait(false);
			var quotes = new List<ChatOptionQuote>(tickers.Count);
			foreach (var ticker in tickers)
			{
				if (OptionSymbolParser.TryParse(ticker.Symbol, out var parts) == false)
				{
					continue;
				}

				quotes.Add(new ChatOptionQuote
				{
					Symbol = ticker.Symbol,
					Expiry = DateOnly.FromDateTime(parts!.ExpiryDate),
					Strike = parts.Strike,
					Type = parts.Type,
					MarkPrice = ticker.MarkPrice,
					MarkIv = ticker.MarkIv,
					Delta = ticker.Delta,
					Gamma = ticker.Gamma,
					Vega = ticker.Vega,
					Theta = ticker.Theta,
					OpenInterest = ticker.OpenInterest,
					Bid1Price = ticker.Bid1Price,
					Bid1Size = ticker.Bid1Size,
					Ask1Price = ticker.Ask1Price,
					Ask1Size = ticker.Ask1Size,
				});
			}

			return new ChatOptionBoard
			{
				BaseCoin = baseCoin,
				AsOf = _timeProvider.GetUtcNow(),
				IsAvailable = true,
				UnderlyingPrice = tickers.Select(ticker => ticker.UnderlyingPrice).FirstOrDefault(price => price is not null),
				TotalTickerCount = tickers.Count,
				Quotes = quotes,
			};
		}
		catch (BybitApiException exception)
		{
			// Деградация при сбое биржи: доска собирается из кэша марок
			// InstrumentMarkProvider — только последние известные цены опционов
			// актива, as-of — момент получения самой старой из них; IV, греки и
			// бид-аск кэш не хранит, якорь окна — кэшированная марка перпа.
			// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
			return await BoardFromCacheAsync(baseCoin, exception.Message, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException exception)
		{
			// Транспортный сбой после всех повторов resilience деградирует так же,
			// как ошибка биржи: для инструмента чата это одна недоступность
			// биржи с кэшем марок и её as-of.
			// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
			return await BoardFromCacheAsync(baseCoin, exception.Message, cancellationToken).ConfigureAwait(false);
		}
		catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
		{
			// Таймаут HTTP-запроса — та же недоступность биржи; отмена вызывающим
			// кодом сюда не попадает: у неё нет внутреннего TimeoutException.
			// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
			return await BoardFromCacheAsync(baseCoin, exception.InnerException.Message, cancellationToken).ConfigureAwait(false);
		}
	}

	#region Деградация в кэш марок

	/// <summary>
	/// Собирает недоступный снимок: причина сбоя биржи и последняя кэшированная
	/// марка перпа с моментом её получения в роли as-of; кэш пуст — марки нет,
	/// as-of совпадает с моментом сбоя.
	/// </summary>
	/// <param name="baseCoin">Базовый актив, чей перп ищется в кэше марок.</param>
	/// <param name="reason">Причина недоступности из исключения сбоя.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Недоступный снимок с кэшированной маркой, когда она известна.</returns>
	private async Task<ChatMarketSnapshot> SnapshotFromCacheAsync(
		string baseCoin,
		string reason,
		CancellationToken cancellationToken)
	{
		var cached = await ReadCachedMarkAsync(baseCoin + "USDT", cancellationToken).ConfigureAwait(false);
		return new ChatMarketSnapshot
		{
			BaseCoin = baseCoin,
			AsOf = cached?.ReceivedAt ?? _timeProvider.GetUtcNow(),
			IsAvailable = false,
			UnavailableReason = reason,
			Symbol = cached is null ? null : baseCoin + "USDT",
			MarkPrice = cached?.Price,
		};
	}

	/// <summary>
	/// Собирает недоступную доску: причина сбоя биржи и последняя кэшированная
	/// проекция марок опционов актива, as-of — момент получения самой старой
	/// марки; якорь окна — кэшированная марка перпа, кэш пуст — пустая проекция.
	/// </summary>
	/// <param name="baseCoin">Базовый актив, чьи опционные марки ищутся в кэше.</param>
	/// <param name="reason">Причина недоступности из исключения сбоя.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Недоступная доска с кэшированными марками, когда они известны.</returns>
	private async Task<ChatOptionBoard> BoardFromCacheAsync(
		string baseCoin,
		string reason,
		CancellationToken cancellationToken)
	{
		var quotes = await ReadCachedOptionQuotesAsync(baseCoin, cancellationToken).ConfigureAwait(false);
		decimal? underlyingPrice = null;
		if (quotes.Count > 0)
		{
			underlyingPrice = (await ReadCachedMarkAsync(baseCoin + "USDT", cancellationToken).ConfigureAwait(false))?.Price;
		}

		return new ChatOptionBoard
		{
			BaseCoin = baseCoin,
			AsOf = quotes.Count > 0 ? quotes.Min(quote => quote.ReceivedAt) : _timeProvider.GetUtcNow(),
			IsAvailable = false,
			UnavailableReason = reason,
			UnderlyingPrice = underlyingPrice,
			TotalTickerCount = quotes.Count,
			Quotes = [.. quotes.Select(quote => quote.Quote)],
		};
	}

	/// <summary>Кэшированная марка инструмента с моментом её получения; null — кэш марки не знает.</summary>
	private async Task<CachedMark?> ReadCachedMarkAsync(string symbol, CancellationToken cancellationToken)
	{
		using var db = new JournalDbContext(_markCacheOptions);
		var row = await db.InstrumentMarks
			.AsNoTracking()
			.Where(mark => mark.Symbol == symbol)
			.Select(mark => new { mark.MarkPrice, mark.ReceivedAt })
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
		return row is null ? null : new CachedMark(row.MarkPrice, row.ReceivedAt);
	}

	/// <summary>
	/// Кэшированные марки опционов актива: символы с префиксом {baseCoin}- из кэша
	/// марок, приведённые к каноническим частям символа; нечитаемые символы отбрасываются.
	/// </summary>
	/// <param name="baseCoin">Базовый актив, чьи опционные марки выбираются из кэша.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Кэшированные котировки с моментом получения каждой марки, упорядоченные по символу.</returns>
	private async Task<IReadOnlyList<CachedOptionQuote>> ReadCachedOptionQuotesAsync(string baseCoin, CancellationToken cancellationToken)
	{
		using var db = new JournalDbContext(_markCacheOptions);
		var rows = await db.InstrumentMarks
			.AsNoTracking()
			.Where(mark => mark.Symbol.StartsWith(baseCoin + "-"))
			.OrderBy(mark => mark.Symbol)
			.Select(mark => new { mark.Symbol, mark.MarkPrice, mark.ReceivedAt })
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var quotes = new List<CachedOptionQuote>(rows.Count);
		foreach (var row in rows)
		{
			if (OptionSymbolParser.TryParse(row.Symbol, out var parts) == false)
			{
				continue;
			}

			quotes.Add(new CachedOptionQuote(
				new ChatOptionQuote
				{
					Symbol = row.Symbol,
					Expiry = DateOnly.FromDateTime(parts!.ExpiryDate),
					Strike = parts.Strike,
					Type = parts.Type,
					MarkPrice = row.MarkPrice,
				},
				row.ReceivedAt));
		}

		return quotes;
	}

	/// <summary>Кэшированная марка: цена и момент её получения.</summary>
	private sealed record CachedMark(decimal Price, DateTimeOffset ReceivedAt);

	/// <summary>Кэшированная котировка опциона: приведённые части символа плюс момент получения марки.</summary>
	private sealed record CachedOptionQuote(ChatOptionQuote Quote, DateTimeOffset ReceivedAt);

	#endregion
}
