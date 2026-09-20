namespace TransactionJournal.Sync;

/// <summary>Параметры синхронизации delivery-истории одной торговой категории.</summary>
public sealed record DeliveryCategorySyncOptions
{
	/// <summary>Размер страницы запроса [1..50]; по умолчанию максимум биржи — 50 записей.</summary>
	public int PageSize { get; init; } = 50;

	/// <summary>
	/// Перекрытие назад инкрементального окна от водяного знака, мс. Защита от граничных
	/// гонок времени: delivery-записи на границе окна догружаются повторно и отсекаются
	/// как уже известные журналу по symbol + deliveryTime. По умолчанию сутки.
	/// Traceability: change:add-bybit-sync/design#d4
	/// </summary>
	public long IncrementalOverlapMs { get; init; } = 86_400_000L;
}
