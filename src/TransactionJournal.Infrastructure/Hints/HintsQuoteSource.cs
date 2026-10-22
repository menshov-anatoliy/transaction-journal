namespace TransactionJournal.Infrastructure.Hints;

using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Адаптер рыночных котировок прохода поверх провайдера свежих котировок: партия котировок
/// собирается по символам открытых позиций, сбой биржи — управляемая
/// недоступность источника, а не исключение. Символов нет — сетевого запроса
/// нет: проход без открытых позиций в котировках не нуждается.
// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
/// </summary>
public sealed class HintsQuoteSource : IQuoteSource
{
	private readonly IFreshInstrumentQuoteSource _freshQuoteSource;

	/// <summary>Создаёт источник котировок прохода поверх провайдера свежих котировок.</summary>
	/// <param name="freshQuoteSource">Провайдер свежих котировок — тот же, что штампует кэш котировок.</param>
	/// <exception cref="ArgumentNullException">Провайдер не задан.</exception>
	public HintsQuoteSource(IFreshInstrumentQuoteSource freshQuoteSource)
	{
		_freshQuoteSource = freshQuoteSource ?? throw new ArgumentNullException(nameof(freshQuoteSource));
	}

	/// <inheritdoc cref="IQuoteSource.GetQuotesAsync" />
	public async Task<QuoteBatch> GetQuotesAsync(
		IReadOnlyCollection<string> symbols,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(symbols);

		// Пустой набор инструментов — запрос к бирже не строится: проходу
		// без открытых позиций котировки не нужны, источник считается доступным.
		if (symbols.Count == 0)
		{
			return new QuoteBatch
			{
				Quotes = new Dictionary<string, decimal>(),
				FailureReason = null,
			};
		}

		var quotes = new Dictionary<string, decimal>(StringComparer.Ordinal);
		foreach (var symbol in symbols)
		{
			decimal? quote;
			try
			{
				quote = (await _freshQuoteSource.GetFreshQuoteAsync(symbol, cancellationToken).ConfigureAwait(false))?.QuotePrice;
			}
			catch (BybitApiException exception)
			{
				// Сбой биржи — недоступность источника целиком: проход
				// пропускается с диагностикой, частичные партии не выдаются.
				// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
				return QuoteBatch.Unavailable(exception.Message);
			}

			// Отсутствие котировки отдельного инструмента недоступностью не считается:
			// достаточность данных триггер решает по своему условию.
			if (quote != null)
			{
				quotes[symbol] = quote.Value;
			}
		}

		return new QuoteBatch
		{
			Quotes = quotes,
			FailureReason = null,
		};
	}
}
