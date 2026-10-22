namespace TransactionJournal.Domain;

/// <summary>
/// Источник последних известных котировок инструментов — контракт домена со слоем
/// синхронизации/аналитики. Котировка нужна ручной пометке закрытия, когда пользователь
/// не задал цену: производный слой подставляет последнюю известную котировку при чтении.
// Traceability: change:add-core-domain/design#d3
/// </summary>
public interface IInstrumentQuoteSource
{
	/// <summary>Возвращает последнюю известную котировку инструмента или null, если котировка ещё не известна.</summary>
	/// <param name="symbol">Инструмент, котировка которого нужна.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<decimal?> GetLastQuoteAsync(string symbol, CancellationToken cancellationToken = default);
}
