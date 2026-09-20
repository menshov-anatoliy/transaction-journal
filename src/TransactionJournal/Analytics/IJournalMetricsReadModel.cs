namespace TransactionJournal.Analytics;

/// <summary>
/// Read-модель метрик журнала для экранов UI: композиция читающего слоя, которая
/// при каждом вызове выводит метрики позиций и конструкций из текущих данных —
/// сырых записей синхронизации, привязок, ручных пометок, корректировок и
/// капитала, — и сводит их в итог по журналу. Мутирующего API нет: экраны
/// выполняют изменения через сервисы домена и перечитывают метрики заново.
// Traceability: openspec:analytics/performance#requirement-analytics-computed-on-read
/// Traceability: change:add-ui-screens/design#d7
/// </summary>
public interface IJournalMetricsReadModel
{
	/// <summary>
	/// Читает метрики журнала из текущих данных: метрики позиций и конструкций,
	/// итог по журналу, отметку времени марок и признак сбоя марок.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="Materialization.TradeMaterializationException">Сырая запись исполнения повреждена или конфликтует с другой записью того же execId.</exception>
	/// <exception cref="Materialization.ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует с другой записью того же ключа.</exception>
	/// <exception cref="Materialization.InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	Task<JournalMetrics> ReadAsync(CancellationToken cancellationToken = default);
}
