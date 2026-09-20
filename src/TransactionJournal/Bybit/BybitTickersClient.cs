using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// HttpClient-обёртка публичного эндпоинта тикеров Bybit GET /v5/market/tickers —
/// источник марок инструментов для аналитики результата. Эндпоинт общедоступен:
/// запрос не подписывается и не использует API-ключ торгового аккаунта, поэтому
/// конструктор клиента сознательно не принимает поставщика ключей. Каждый запрос
/// проходит через общий конвейер устойчивости: минимальный интервал между запросами,
/// учёт заголовков лимитов X-Bapi-Limit-* и повторы Polly по классам сбоев.
/// Traceability: openspec:analytics/performance#requirement-mark-provider
/// Traceability: change:add-analytics/design#d2
/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
/// </summary>
public sealed class BybitTickersClient
{
	private const string TickersPath = "/v5/market/tickers";

	private readonly HttpClient _httpClient;
	private readonly BybitClientOptions _options;
	private readonly BybitResilience _resilience;

	/// <summary>Создаёт обёртку над готовым HttpClient; аутентификация не нужна и не принимается.</summary>
	public BybitTickersClient(
		HttpClient httpClient,
		BybitClientOptions? options = null,
		BybitResilienceOptions? resilienceOptions = null,
		TimeProvider? timeProvider = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_options = options ?? new BybitClientOptions();
		// Устойчивость к лимитам и сбоям биржи включена всегда; тесты подменяют поставщик времени.
		_resilience = new BybitResilience(resilienceOptions ?? new BybitResilienceOptions(), timeProvider);
	}

	/// <summary>
	/// GET /v5/market/tickers — публичные тикеры инструментов категории linear/option
	/// с маркой каждого инструмента. Запрос уходит без подписи и без заголовков
	/// аутентификации: API-ключ торгового аккаунта не используется.
	/// </summary>
	/// <remarks>
	/// Для категории option биржа требует фильтр symbol или baseCoin; ответ не пагинируется.
	/// Traceability: openspec:analytics/performance#scenario-public-tickers-no-auth
	/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
	/// </remarks>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	public async Task<IReadOnlyList<BybitTicker>> GetTickersAsync(
		BybitTickerQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		// Строка запроса собирается вручную в детерминированном порядке параметров.
		var queryString = BybitRequestSigner.BuildQueryString(query.ToQueryParameters());
		var body = await _resilience.SendAsync(
			_httpClient,
			() => new HttpRequestMessage(HttpMethod.Get, BuildUri(TickersPath, queryString)),
			BybitResponse.ThrowIfApiError,
			cancellationToken).ConfigureAwait(false);

		var result = BybitResponse.DeserializeResult<BybitTickersResult>(body, TickersPath);
		return result.List;
	}

	#region Вспомогательные члены

	private Uri BuildUri(string path, string queryString)
	{
		var url = _options.BaseUrl.TrimEnd('/') + path;
		if (queryString.Length > 0)
		{
			url += "?" + queryString;
		}

		return new Uri(url, UriKind.Absolute);
	}

	/// <summary>Списочная часть ответа тикеров; ответ не пагинируется, курсора в нём нет.</summary>
	private sealed class BybitTickersResult
	{
		[JsonPropertyName("list")]
		public IReadOnlyList<BybitTicker> List { get; init; } = [];
	}

	#endregion
}
