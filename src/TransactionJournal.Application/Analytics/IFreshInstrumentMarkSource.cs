namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Источник свежих марок инструментов — контракт провайдера марок с оценкой
/// нереализованного PnL. Свежая марка запрашивается у публичных тикеров на
/// момент вызова, штампует кэш последней известной марки временем получения
/// и дописывает append-only историю рыночных снимков. Возвращаемый снимок
/// несёт nullable-поля теты и индексной цены: биржа отдаёт их только опционам,
/// поэтому вычислитель метрик времени жизни читает их как «есть или прочерк».
// Traceability: openspec:analytics/performance#requirement-mark-provider
// Traceability: openspec:analytics/option-lifetime#requirement-market-snapshot-history
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
