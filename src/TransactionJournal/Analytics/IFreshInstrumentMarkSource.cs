namespace TransactionJournal.Analytics;

/// <summary>
/// Источник свежих марок инструментов — контракт провайдера марок с оценкой
/// нереализованного PnL. Свежая марка запрашивается у публичных тикеров на
/// момент вызова и штампует кэш последней известной марки временем получения.
// Traceability: openspec:analytics/performance#requirement-mark-provider
// Traceability: change:add-analytics/design#d2
/// </summary>
public interface IFreshInstrumentMarkSource
{
	/// <summary>
	/// Запрашивает свежую марку инструмента у публичного эндпоинта тикеров на
	/// момент вызова; null — инструмент неизвестен справочнику или биржа не
	/// отдала марку.
	/// </summary>
	/// <param name="symbol">Символ инструмента, например BTCUSDT или BTC-29DEC23-25000-C.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов; решение о деградации остаётся за вызывающим слоем.</exception>
	Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default);
}
