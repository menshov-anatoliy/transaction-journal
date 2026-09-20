using System.Globalization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Параметры запроса GET /v5/market/instruments-info — публичный справочник спецификаций
/// инструментов; limit — диапазон [1..1000], без параметра биржа отдаёт 500 записей.
/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
/// </summary>
public sealed record BybitInstrumentInfoQuery
{
	/// <summary>Торговая категория: linear или option; обязательный параметр эндпоинта.</summary>
	public required string Category { get; init; }

	/// <summary>Фильтр по символу инструмента.</summary>
	public string? Symbol { get; init; }

	/// <summary>Фильтр по базовому активу (для категории option).</summary>
	public string? BaseCoin { get; init; }

	/// <summary>Фильтр по дате экспирации в формате биржи (например 25MAR22).</summary>
	public string? ExpDate { get; init; }

	/// <summary>Размер страницы [1..1000].</summary>
	public int? Limit { get; init; }

	/// <summary>Курсор следующей страницы из предыдущего ответа; передаётся как есть.</summary>
	public string? Cursor { get; init; }

	/// <summary>
	/// Собирает параметры в фиксированном порядке: queryString собирается вручную и
	/// побайтово совпадает в URL и подписи, поэтому порядок пар должен быть детерминированным.
	/// </summary>
	/// <exception cref="ArgumentException">Категория не задана.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Limit вне диапазона [1..1000].</exception>
	internal IReadOnlyList<KeyValuePair<string, string>> ToQueryParameters()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(Category);
		if (Limit is < 1 or > 1000)
		{
			throw new ArgumentOutOfRangeException(nameof(Limit), Limit, "limit эндпоинта instruments-info должен быть в диапазоне [1..1000].");
		}

		var query = new List<KeyValuePair<string, string>>(6)
		{
			new("category", Category),
		};
		if (Symbol is not null)
		{
			query.Add(new("symbol", Symbol));
		}

		if (BaseCoin is not null)
		{
			query.Add(new("baseCoin", BaseCoin));
		}

		if (ExpDate is not null)
		{
			query.Add(new("expDate", ExpDate));
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
