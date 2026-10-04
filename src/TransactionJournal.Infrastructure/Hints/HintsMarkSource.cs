namespace TransactionJournal.Infrastructure.Hints;

using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Адаптер рыночных марок прохода поверх провайдера свежих марок: партия марок
/// собирается по символам открытых позиций, сбой биржи — управляемая
/// недоступность источника, а не исключение. Символов нет — сетевого запроса
/// нет: проход без открытых позиций в марках не нуждается.
// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
/// </summary>
public sealed class HintsMarkSource : IMarkSource
{
	private readonly IFreshInstrumentMarkSource _freshMarkSource;

	/// <summary>Создаёт источник марок прохода поверх провайдера свежих марок.</summary>
	/// <param name="freshMarkSource">Провайдер свежих марок — тот же, что штампует кэш марок.</param>
	/// <exception cref="ArgumentNullException">Провайдер не задан.</exception>
	public HintsMarkSource(IFreshInstrumentMarkSource freshMarkSource)
	{
		_freshMarkSource = freshMarkSource ?? throw new ArgumentNullException(nameof(freshMarkSource));
	}

	/// <inheritdoc cref="IMarkSource.GetMarksAsync" />
	public async Task<MarkBatch> GetMarksAsync(
		IReadOnlyCollection<string> symbols,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(symbols);

		// Пустой набор инструментов — запрос к бирже не строится: проходу
		// без открытых позиций марки не нужны, источник считается доступным.
		if (symbols.Count == 0)
		{
			return new MarkBatch
			{
				Marks = new Dictionary<string, decimal>(),
				FailureReason = null,
			};
		}

		var marks = new Dictionary<string, decimal>(StringComparer.Ordinal);
		foreach (var symbol in symbols)
		{
			decimal? mark;
			try
			{
				mark = (await _freshMarkSource.GetFreshMarkAsync(symbol, cancellationToken).ConfigureAwait(false))?.MarkPrice;
			}
			catch (BybitApiException exception)
			{
				// Сбой биржи — недоступность источника целиком: проход
				// пропускается с диагностикой, частичные партии не выдаются.
				// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
				return MarkBatch.Unavailable(exception.Message);
			}

			// Отсутствие марки отдельного инструмента недоступностью не считается:
			// достаточность данных триггер решает по своему условию.
			if (mark != null)
			{
				marks[symbol] = mark.Value;
			}
		}

		return new MarkBatch
		{
			Marks = marks,
			FailureReason = null,
		};
	}
}
