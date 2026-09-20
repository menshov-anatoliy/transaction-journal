namespace TransactionJournal.Sync;

/// <summary>Параметры прохода окна истории исполнения.</summary>
public sealed record ExecutionWindowPassOptions
{
	/// <summary>Размер страницы запроса [1..100]; по умолчанию максимум биржи — 100 записей.</summary>
	public int PageSize { get; init; } = 100;

	/// <summary>
	/// Ранняя остановка, когда страница целиком состоит из известных журналу execId.
	/// Включена для инкрементальной догрузки: сортировка по убыванию гарантирует,
	/// что глубже целиком известной страницы новых записей нет. Backfill выключает
	/// остановку: при возобновлении после обрыва известные страницы не означают,
	/// что хвост окна уже сохранён.
	/// Traceability: change:add-bybit-sync/design#d4
	/// </summary>
	public bool EarlyStopOnKnownPage { get; init; } = true;
}
