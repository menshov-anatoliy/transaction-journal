using System.Globalization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Параметры запроса GET /v5/asset/delivery-record. Окно startTime/endTime ограничено
/// тридцатью днями, limit — диапазоном [1..50]; сортировка по deliveryTime по убыванию.
/// Traceability: doc:docs/research/bybit-api.md#21-основной-источник--get-delivery-record
/// </summary>
public sealed record BybitDeliveryRecordQuery
{
	/// <summary>Торговая категория: option или linear; обязательный параметр эндпоинта.</summary>
	public required string Category { get; init; }

	/// <summary>Фильтр по символу инструмента.</summary>
	public string? Symbol { get; init; }

	/// <summary>Фильтр по дате экспирации в формате биржи (например 25MAR22).</summary>
	public string? ExpDate { get; init; }

	/// <summary>Начало окна времени, мс.</summary>
	public long? StartTimeMs { get; init; }

	/// <summary>Конец окна времени, мс.</summary>
	public long? EndTimeMs { get; init; }

	/// <summary>Размер страницы [1..50]; без параметра биржа отдаёт 20 записей.</summary>
	public int? Limit { get; init; }

	/// <summary>Курсор следующей страницы из предыдущего ответа; передаётся как есть.</summary>
	public string? Cursor { get; init; }

	/// <summary>
	/// Собирает параметры в фиксированном порядке: queryString собирается вручную и
	/// побайтово совпадает в URL и подписи, поэтому порядок пар должен быть детерминированным.
	/// </summary>
	/// <exception cref="ArgumentException">Категория не задана.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Limit вне диапазона [1..50].</exception>
	internal IReadOnlyList<KeyValuePair<string, string>> ToQueryParameters()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(Category);
		if (Limit is < 1 or > 50)
		{
			throw new ArgumentOutOfRangeException(nameof(Limit), Limit, "limit эндпоинта delivery-record должен быть в диапазоне [1..50].");
		}

		var query = new List<KeyValuePair<string, string>>(7)
		{
			new("category", Category),
		};
		if (Symbol is not null)
		{
			query.Add(new("symbol", Symbol));
		}

		if (ExpDate is not null)
		{
			query.Add(new("expDate", ExpDate));
		}

		if (StartTimeMs is { } startTime)
		{
			query.Add(new("startTime", startTime.ToString(CultureInfo.InvariantCulture)));
		}

		if (EndTimeMs is { } endTime)
		{
			query.Add(new("endTime", endTime.ToString(CultureInfo.InvariantCulture)));
		}

		if (Limit is { } limit)
		{
			query.Add(new("limit", limit.ToString(CultureInfo.InvariantCulture)));
		}

		if (Cursor is not null)
		{
			query.Add(new("cursor", Cursor));
		}

		return query;
	}
}
