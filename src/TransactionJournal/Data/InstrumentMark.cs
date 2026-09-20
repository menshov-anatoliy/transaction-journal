namespace TransactionJournal.Data;

/// <summary>
/// Кэш последней известной марки инструмента: цена и время её получения.
/// Единственное новое хранилище аналитики результата — провайдер марок штампует
/// строку при каждом получении свежей марки от публичных тикеров, а читающие
/// слои (дефолт ручной пометки закрытия, оценка нереализованного PnL) берут
/// отсюда последнюю известную марку без сетевого запроса.
// Traceability: openspec:analytics/performance#requirement-mark-provider
/// Traceability: change:add-analytics/design#d2
/// </summary>
public sealed class InstrumentMark
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Символ инструмента; уникален — одна строка на инструмент хранит последнюю марку.</summary>
	public required string Symbol { get; set; }

	/// <summary>Последняя известная марка инструмента.</summary>
	public decimal MarkPrice { get; set; }

	/// <summary>Время получения марки: штампуется в момент прихода свежей марки от биржи.</summary>
	public required DateTimeOffset ReceivedAt { get; set; }
}
