using System.Globalization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Параметры запроса GET /v5/execution/list. Окно startTime/endTime ограничено семью
/// днями, limit — диапазоном [1..100]; выбор окон и пагинацию выполняет sync engine,
/// запрос остаётся чистым описанием одного вызова эндпоинта.
/// Traceability: doc:docs/research/bybit-api.md#1-история-исполнения-сделок-unified-аккаунта-execution-list
/// </summary>
public sealed record BybitExecutionListQuery
{
	/// <summary>Торговая категория: linear или option; обязательный параметр эндпоинта.</summary>
	public required string Category { get; init; }

	/// <summary>Фильтр по символу инструмента.</summary>
	public string? Symbol { get; init; }

	/// <summary>Фильтр по базовому активу (для категории option).</summary>
	public string? BaseCoin { get; init; }

	/// <summary>Начало окна времени, мс.</summary>
	public long? StartTimeMs { get; init; }

	/// <summary>Конец окна времени, мс.</summary>
	public long? EndTimeMs { get; init; }

	/// <summary>Размер страницы [1..100]; без параметра биржа отдаёт 50 записей.</summary>
	public int? Limit { get; init; }

	/// <summary>Курсор следующей страницы из предыдущего ответа; передаётся как есть.</summary>
	public string? Cursor { get; init; }

	/// <summary>
	/// Собирает параметры в фиксированном порядке: queryString собирается вручную и
	/// побайтово совпадает в URL и подписи, поэтому порядок пар должен быть детерминированным.
	/// </summary>
	/// <exception cref="ArgumentException">Категория не задана.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Limit вне диапазона [1..100].</exception>
	internal IReadOnlyList<KeyValuePair<string, string>> ToQueryParameters()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(Category);
		if (Limit is < 1 or > 100)
		{
			throw new ArgumentOutOfRangeException(nameof(Limit), Limit, "limit эндпоинта execution/list должен быть в диапазоне [1..100].");
		}

		var query = new List<KeyValuePair<string, string>>(7)
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
