namespace TransactionJournal.Infrastructure.Consultations;

using TransactionJournal.Application.Bybit;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Infrastructure.Bybit;

/// <summary>
/// Адаптер рыночных данных инструментов консультаций поверх публичного клиента
/// тикеров Bybit: снимок фьючерсного рынка — один запрос категории linear по
/// символу перпа, доска опционов — один запрос категории option с фильтром
/// baseCoin. Троттлер и resilience клиента наследуются целиком, собственных
/// счётчиков запросов нет; сбой биржи — управляемая недоступность в записи
/// результата, а не исключение: инструмент консультаций обязан ответить
/// структурированным «недоступно».
// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
// Traceability: openspec:consultations/tools#requirement-tools-degradation-cached-asof
/// </summary>
public sealed class BybitConsultationMarketReader : IConsultationMarketReader
{
	/// <summary>Категория фьючерсных тикеров: перп базового актива несёт марку и ставку фандинга.</summary>
	private const string LinearCategory = "linear";

	/// <summary>Категория опционных тикеров: доска запрашивается фильтром baseCoin.</summary>
	private const string OptionCategory = "option";

	private readonly BybitTickersClient _tickersClient;

	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт адаптер поверх клиента тикеров.</summary>
	/// <param name="tickersClient">Единый клиент тикеров Bybit с троттлером и resilience.</param>
	/// <param name="timeProvider">Поставщик момента as-of ответа; по умолчанию системные часы.</param>
	public BybitConsultationMarketReader(
		BybitTickersClient tickersClient,
		TimeProvider? timeProvider = null)
	{
		_tickersClient = tickersClient ?? throw new ArgumentNullException(nameof(tickersClient));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Ровно один HTTP-запрос категории linear: перп {baseCoin}USDT несёт марку,
	/// бид-аск, открытый интерес и ставку фандинга; спот отдельным запросом не
	/// запрашивается — глубину биржевых обращений ограничивает потолок итераций
	/// агентного цикла.
	// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
	/// </remarks>
	public async Task<ConsultationMarketSnapshot> ReadSnapshotAsync(string baseCoin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		try
		{
			var tickers = await _tickersClient.GetTickersAsync(
				new BybitTickerQuery { Category = LinearCategory, Symbol = baseCoin + "USDT" },
				cancellationToken).ConfigureAwait(false);
			var ticker = tickers.FirstOrDefault();
			return new ConsultationMarketSnapshot
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
			return UnavailableSnapshot(baseCoin, exception.Message);
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Ровно один HTTP-запрос категории option с фильтром baseCoin — биржа
	/// требует для опционов фильтр symbol или baseCoin и отвечает всей доской
	/// актива. Символы вне формата опционов отбрасываются с подсчётом, котировки
	/// приводятся к каноническим частям символа.
	// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
	/// </remarks>
	public async Task<ConsultationOptionBoard> ReadOptionBoardAsync(string baseCoin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		try
		{
			var tickers = await _tickersClient.GetTickersAsync(
				new BybitTickerQuery { Category = OptionCategory, BaseCoin = baseCoin },
				cancellationToken).ConfigureAwait(false);
			var quotes = new List<ConsultationOptionQuote>(tickers.Count);
			foreach (var ticker in tickers)
			{
				if (OptionSymbolParser.TryParse(ticker.Symbol, out var parts) == false)
				{
					continue;
				}

				quotes.Add(new ConsultationOptionQuote
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

			return new ConsultationOptionBoard
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
			return UnavailableBoard(baseCoin, exception.Message);
		}
	}

	#region Вспомогательные члены

	/// <summary>Запись недоступного снимка с причиной сбоя биржи.</summary>
	private ConsultationMarketSnapshot UnavailableSnapshot(string baseCoin, string reason) => new()
	{
		BaseCoin = baseCoin,
		AsOf = _timeProvider.GetUtcNow(),
		IsAvailable = false,
		UnavailableReason = reason,
	};

	/// <summary>Запись недоступной доски с причиной сбоя биржи.</summary>
	private ConsultationOptionBoard UnavailableBoard(string baseCoin, string reason) => new()
	{
		BaseCoin = baseCoin,
		AsOf = _timeProvider.GetUtcNow(),
		IsAvailable = false,
		UnavailableReason = reason,
		TotalTickerCount = 0,
		Quotes = [],
	};

	#endregion
}
