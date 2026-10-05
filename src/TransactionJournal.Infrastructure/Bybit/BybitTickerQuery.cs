using TransactionJournal.Application.Bybit;
namespace TransactionJournal.Infrastructure.Bybit;

/// <summary>
/// Параметры запроса GET /v5/market/tickers — публичные тикеры инструментов категории
/// linear/option; для категории option биржа требует фильтр symbol или baseCoin.
/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
/// </summary>
public sealed record BybitTickerQuery
{
	/// <summary>Торговая категория: linear или option; обязательный параметр эндпоинта.</summary>
	public required string Category { get; init; }

	/// <summary>Фильтр по символу инструмента; провайдер марок запрашивает конкретный инструмент.</summary>
	public string? Symbol { get; init; }

	/// <summary>
	/// Фильтр по базовому активу доски опционов; инструменты консультаций
	/// запрашивают целую доску одним запросом. По документации биржи применим
	/// только к категории option.
	// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
	/// </summary>
	public string? BaseCoin { get; init; }

	/// <summary>
	/// Собирает параметры в фиксированном порядке: queryString собирается вручную,
	/// поэтому порядок пар должен быть детерминированным.
	/// </summary>
	/// <exception cref="ArgumentException">Категория не задана.</exception>
	internal IReadOnlyList<KeyValuePair<string, string>> ToQueryParameters()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(Category);

		var query = new List<KeyValuePair<string, string>>(3)
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

		return query;
	}
}
