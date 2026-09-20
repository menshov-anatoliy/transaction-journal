namespace TransactionJournal.Sync;

/// <summary>
/// Одно окно delivery-истории для прохода: категория и границы времени.
/// Длительность окна ограничена тридцатью днями — предел эндпоинта delivery-record,
/// поэтому перебор истории назад выполняется цепочкой таких окон.
/// Traceability: change:add-bybit-sync/design#d4
/// </summary>
public sealed record DeliveryWindow
{
	/// <summary>Торговая категория окна: option или linear.</summary>
	public required string Category { get; init; }

	/// <summary>Начало окна, мс Unix-эпохи.</summary>
	public long StartMs { get; init; }

	/// <summary>Конец окна, мс Unix-эпохи; обязан быть позже начала.</summary>
	public long EndMs { get; init; }
}
