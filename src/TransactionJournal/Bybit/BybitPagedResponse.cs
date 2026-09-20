using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Страница списочного ответа Bybit: элементы list и курсор следующей страницы.
/// Пустой nextPageCursor означает, что страницы закончились.
/// </summary>
/// <typeparam name="TItem">Тип элемента списка — типизированная запись биржи.</typeparam>
public sealed class BybitPagedResponse<TItem>
{
	/// <summary>Элементы страницы; пуст, когда данных в запрошенном окне нет.</summary>
	[JsonPropertyName("list")]
	public IReadOnlyList<TItem> List { get; init; } = [];

	/// <summary>Курсор следующей страницы; биржа отдаёт его уже URL-кодированным и принимает как есть.</summary>
	[JsonPropertyName("nextPageCursor")]
	public string? NextPageCursor { get; init; }

	/// <summary>Признак наличия следующей страницы: курсор не пуст.</summary>
	public bool HasNextPage => string.IsNullOrEmpty(NextPageCursor) == false;
}
