namespace TransactionJournal.Sync;

/// <summary>Параметры синхронизации истории исполнения одной торговой категории.</summary>
public sealed record ExecutionCategorySyncOptions
{
	/// <summary>Размер страницы запроса [1..100]; по умолчанию максимум биржи — 100 записей.</summary>
	public int PageSize { get; init; } = 100;

	/// <summary>
	/// Перекрытие назад инкрементального окна от водяного знака, мс. Защита от граничных
	/// гонок времени: записи на границе окна догружаются повторно и отсекаются как уже
	/// известные журналу. По умолчанию сутки.
	/// Traceability: change:add-bybit-sync/design#d4
	/// </summary>
	public long IncrementalOverlapMs { get; init; } = 86_400_000L;
}
