namespace TransactionJournal.Sync;

/// <summary>Параметры прохода окна delivery-истории.</summary>
public sealed record DeliveryWindowPassOptions
{
	/// <summary>Размер страницы запроса [1..50]; по умолчанию максимум биржи — 50 записей.</summary>
	public int PageSize { get; init; } = 50;
}
