namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Свежая марка инструмента со временем её получения: результат запроса провайдера
/// к публичным тикерам. Время получения становится отметкой `marks_as_of` оценки
/// нереализованного PnL и штампом строки кэша. Снимок дополняется nullable-полями
/// рыночных данных тикера — индексной ценой и суточной тетой на контракт в
/// котируемой монете; биржа отдаёт их только опционам, поэтому у не-опционов
/// поля равны null. Поля — материал вычислителя метрик времени жизни; кэш
/// последней марки их не хранит.
/// Traceability: openspec:analytics/option-lifetime#requirement-market-snapshot-history
/// Traceability: change:add-option-lifetime-metrics/design#d1
/// </summary>
/// <param name="Symbol">Символ инструмента, например BTCUSDT или BTC-25JUN27-106000-P-USDT.</param>
/// <param name="MarkPrice">Публичная марка инструмента на момент ответа биржи.</param>
/// <param name="ReceivedAt">Момент получения марки по часам провайдера.</param>
/// <param name="UnderlyingPrice">Индексная цена базового актива опциона; у не-опционов null.</param>
/// <param name="Theta">Суточная тета опциона на контракт; у не-опционов null.</param>
public sealed record InstrumentMarkSnapshot(
	string Symbol,
	decimal MarkPrice,
	DateTimeOffset ReceivedAt,
	decimal? UnderlyingPrice = null,
	decimal? Theta = null);
