namespace TransactionJournal.Domain;

/// <summary>
/// Источник последних известных марок инструментов — контракт домена со слоем
/// синхронизации/аналитики. Марка нужна ручной пометке закрытия, когда пользователь
/// не задал цену: производный слой подставляет последнюю известную марку при чтении.
// Traceability: change:add-core-domain/design#d3
/// </summary>
public interface IInstrumentMarkSource
{
	/// <summary>Возвращает последнюю известную марку инструмента или null, если марка ещё не известна.</summary>
	/// <param name="symbol">Инструмент, марка которого нужна.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<decimal?> GetLastMarkAsync(string symbol, CancellationToken cancellationToken = default);
}
