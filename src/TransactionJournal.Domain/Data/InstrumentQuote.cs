namespace TransactionJournal.Domain.Data;

/// <summary>
/// Кэш последней известной котировки инструмента: цена и время её получения.
/// Единственное новое хранилище аналитики результата — провайдер котировок штампует
/// строку при каждом получении свежей котировки от публичных тикеров, а читающие
/// слои (дефолт ручной пометки закрытия, оценка нереализованного PnL) берут
/// отсюда последнюю известную котировку без сетевого запроса.
// Traceability: openspec:analytics/performance#requirement-mark-provider
/// Traceability: change:add-analytics/design#d2
/// </summary>
public sealed class InstrumentQuote
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Символ инструмента; уникален — одна строка на инструмент хранит последнюю котировку.</summary>
	public required string Symbol { get; set; }

	/// <summary>Последняя известная котировка инструмента.</summary>
	public decimal QuotePrice { get; set; }

	/// <summary>Время получения котировки: штампуется в момент прихода свежей котировки от биржи.</summary>
	public required DateTimeOffset ReceivedAt { get; set; }
}
