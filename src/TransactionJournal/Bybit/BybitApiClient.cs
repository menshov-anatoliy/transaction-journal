using System.Globalization;
using System.Text.Json;

namespace TransactionJournal.Bybit;

/// <summary>
/// HttpClient-обёртка Bybit V5 API для синхронизации журнала.
/// Подписывает GET-запросы по официальной схеме HMAC-SHA256, сверяет часы с публичным
/// эндпоинтом /v5/market/time и намеренно ограничена методами только на чтение:
/// торговых операций в клиенте нет, поэтому хватает ключа с правами readonly.
/// Каждый запрос проходит через подсистему устойчивости: минимальный интервал между
/// запросами, учёт заголовков лимитов X-Bapi-Limit-* и повторы Polly по классам сбоев.
/// Traceability: change:add-bybit-sync/design#d1
/// Traceability: change:add-bybit-sync/design#d5
/// Traceability: change:add-bybit-sync/design#d6
/// Traceability: openspec:sync/bybit-history#requirement-read-only-access
/// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
/// </summary>
public sealed class BybitApiClient
{
	private const string ServerTimePath = "/v5/market/time";
	private const string ExecutionListPath = "/v5/execution/list";
	private const string DeliveryRecordPath = "/v5/asset/delivery-record";
	private const string InstrumentsInfoPath = "/v5/market/instruments-info";
	private const string RetCodePropertyName = "retCode";
	private const string RetMsgPropertyName = "retMsg";
	private const string ResultPropertyName = "result";
	private const string ApiKeyHeaderName = "X-BAPI-API-KEY";
	private const string TimestampHeaderName = "X-BAPI-TIMESTAMP";
	private const string RecvWindowHeaderName = "X-BAPI-RECV-WINDOW";
	private const string SignHeaderName = "X-BAPI-SIGN";
	private const string SignTypeHeaderName = "X-BAPI-SIGN-TYPE";

	/// <summary>Код ошибки класса 10003 — неверная подпись/время; лечится сверкой часов.</summary>
	private const int SignErrorRetCode = 10003;

	private readonly HttpClient _httpClient;
	private readonly IBybitCredentialsProvider _credentialsProvider;
	private readonly BybitClientOptions _options;
	private readonly BybitResilience _resilience;
	private long _serverTimeOffsetMs;

	/// <summary>Создаёт обёртку над готовым HttpClient с поставщиком ключа и параметрами.</summary>
	public BybitApiClient(
		HttpClient httpClient,
		IBybitCredentialsProvider credentialsProvider,
		BybitClientOptions? options = null,
		BybitResilienceOptions? resilienceOptions = null,
		TimeProvider? timeProvider = null)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_credentialsProvider = credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));
		_options = options ?? new BybitClientOptions();
		// Устойчивость к лимитам и сбоям биржи включена всегда; тесты подменяют поставщик времени.
		_resilience = new BybitResilience(resilienceOptions ?? new BybitResilienceOptions(), timeProvider);
	}

	/// <summary>Текущее смещение серверного времени относительно локного, мс; выставляется сверкой часов.</summary>
	public long ServerTimeOffsetMs => Interlocked.Read(ref _serverTimeOffsetMs);

	/// <summary>
	/// Выполняет подписанный GET-запрос и возвращает тело ответа биржи как строку JSON.
	/// Значения параметров запроса подставляются в URL и подпись как есть, поэтому
	/// они должны быть готовы к использованию в URL (без символов, требующих кодирования).
	/// Запрос проходит через устойчивость: минимальный интервал, учёт заголовков лимитов
	/// и повторы по классам сбоев (сеть/5xx, retCode 10006, HTTP 403).
	/// При ошибке класса 10003 сверяет часы с биржей и повторяет запрос один раз.
	/// </summary>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	public async Task<string> GetAsync(
		string path,
		IReadOnlyList<KeyValuePair<string, string>> query,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		try
		{
			return await SendSignedGetAsync(path, query, cancellationToken).ConfigureAwait(false);
		}
		catch (BybitApiException exception) when (exception.RetCode == SignErrorRetCode)
		{
			// Ошибка 10003 чаще всего означает рассинхрон часов: сверяем время с /v5/market/time
			// и повторяем подписанный запрос один раз с новым timestamp.
			// Traceability: change:add-bybit-sync/design#d6
			await SynchronizeClockAsync(cancellationToken).ConfigureAwait(false);
			return await SendSignedGetAsync(path, query, cancellationToken).ConfigureAwait(false);
		}
	}

	#region Типизированные read-only эндпоинты

	/// <summary>
	/// GET /v5/execution/list — история исполнения сделок категории linear/option
	/// с курсорной пагинацией; записи приходят по убыванию execTime.
	/// Метод только читает историю и не выполняет торговых операций, поэтому
	/// хватает API-ключа с правами readonly.
	/// </summary>
	/// <remarks>
	/// Окно startTime/endTime ограничено семью днями, limit — диапазоном [1..100].
	/// Traceability: doc:docs/research/bybit-api.md#1-история-исполнения-сделок-unified-аккаунта-execution-list
	/// Traceability: openspec:sync/bybit-history#requirement-read-only-access
	/// </remarks>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом.</exception>
	public async Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
		BybitExecutionListQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return await GetPagedResultAsync<BybitExecution>(ExecutionListPath, query.ToQueryParameters(), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// GET /v5/asset/delivery-record — delivery-записи экспираций опционов и датированных
	/// фьючерсов: отдельный источник закрывающих записей журнала с расчётной ценой доставки
	/// вместо цены исполнения.
	/// </summary>
	/// <remarks>
	/// Окно startTime/endTime ограничено тридцатью днями, limit — диапазоном [1..50],
	/// сортировка по deliveryTime по убыванию.
	/// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
	/// Traceability: doc:docs/research/bybit-api.md#21-основной-источник--get-delivery-record
	/// </remarks>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом.</exception>
	public async Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
		BybitDeliveryRecordQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return await GetPagedResultAsync<BybitDeliveryRecord>(DeliveryRecordPath, query.ToQueryParameters(), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// GET /v5/market/instruments-info — публичный справочник спецификаций инструментов:
	/// канонические категория, тип опциона, базовый актив, расчётная валюта и время delivery.
	/// </summary>
	/// <remarks>
	/// Справочник журнала строится из этого эндпоинта, а не из разбора строки символа.
	/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
	/// Traceability: doc:docs/research/bybit-api.md#3-публичные-марки-tickers-без-аутентификации
	/// </remarks>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом.</exception>
	public async Task<BybitPagedResponse<BybitInstrumentInfo>> GetInstrumentsInfoAsync(
		BybitInstrumentInfoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return await GetPagedResultAsync<BybitInstrumentInfo>(InstrumentsInfoPath, query.ToQueryParameters(), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// GET /v5/market/time — публичное серверное время биржи строками секунд и наносекунд.
	/// Запрос не подписывается: эндпоинт публичный и доступен без ключа, но проходит
	/// через общий троттлинг интервала и лимитов.
	/// </summary>
	/// <remarks>
	/// Серверное время используется для сверки часов перед подписью запросов.
	/// Traceability: change:add-bybit-sync/design#d6
	/// </remarks>
	/// <exception cref="BybitApiException">Эндпоинт недоступен или вернул некорректное время.</exception>
	public async Task<BybitServerTime> GetServerTimeAsync(CancellationToken cancellationToken = default)
	{
		var body = await _resilience.SendAsync(
			_httpClient,
			() => new HttpRequestMessage(HttpMethod.Get, BuildUri(ServerTimePath, queryString: string.Empty)),
			ThrowIfApiError,
			cancellationToken).ConfigureAwait(false);

		var serverTime = DeserializeResult<BybitServerTime>(body, ServerTimePath);
		if (serverTime.TryGetMilliseconds(out _) == false)
		{
			throw BybitApiException.FromMalformedBody("Ответ /v5/market/time не содержит корректное поле timeNano.", body);
		}

		return serverTime;
	}

	#endregion

	/// <summary>
	/// Сверяет часы с публичным эндпоинтом /v5/market/time и запоминает смещение серверного
	/// времени относительно локного; последующие подписи используют скорректированный timestamp.
	/// </summary>
	/// <exception cref="BybitApiException">Эндпоинт времени недоступен или вернул некорректный ответ.</exception>
	public async Task SynchronizeClockAsync(CancellationToken cancellationToken = default)
	{
		// Валидация timeNano уже выполнена в GetServerTimeAsync — здесь остаётся расчёт смещения.
		var serverTime = await GetServerTimeAsync(cancellationToken).ConfigureAwait(false);
		var offset = serverTime.Milliseconds - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		Interlocked.Exchange(ref _serverTimeOffsetMs, offset);
	}

	#region Вспомогательные методы

	private Task<string> SendSignedGetAsync(
		string path,
		IReadOnlyList<KeyValuePair<string, string>> query,
		CancellationToken cancellationToken)
	{
		// Строка запроса собирается вручную и побайтово совпадает в URL и подписи.
		// Traceability: change:add-bybit-sync/design#d1
		var queryString = BybitRequestSigner.BuildQueryString(query);
		return _resilience.SendAsync(
			_httpClient,
			() => BuildSignedRequest(path, queryString),
			ThrowIfApiError,
			cancellationToken);
	}

	private HttpRequestMessage BuildSignedRequest(string path, string queryString)
	{
		// Запрос создаётся на каждую попытку: после длинных пауз повторов старый timestamp
		// вышел бы за recv_window и был бы отвергнут биржей.
		var credentials = _credentialsProvider.GetCredentials();
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ServerTimeOffsetMs;
		var signature = BybitRequestSigner.ComputeSignature(
			timestamp, credentials.ApiKey, _options.RecvWindowMs, queryString, credentials.ApiSecret);

		var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, queryString));
		request.Headers.Add(ApiKeyHeaderName, credentials.ApiKey);
		request.Headers.Add(TimestampHeaderName, timestamp.ToString(CultureInfo.InvariantCulture));
		request.Headers.Add(RecvWindowHeaderName, _options.RecvWindowMs.ToString(CultureInfo.InvariantCulture));
		request.Headers.Add(SignHeaderName, signature);
		// Тип подписи 2 (HMAC) передаётся, как в официальном C#-примере Bybit.
		request.Headers.Add(SignTypeHeaderName, "2");
		return request;
	}

	private async Task<BybitPagedResponse<TItem>> GetPagedResultAsync<TItem>(
		string path,
		IReadOnlyList<KeyValuePair<string, string>> query,
		CancellationToken cancellationToken)
	{
		var body = await GetAsync(path, query, cancellationToken).ConfigureAwait(false);
		return DeserializeResult<BybitPagedResponse<TItem>>(body, path);
	}

	/// <summary>
	/// Разбирает поле result конверта Bybit в типизированный ответ; ошибки формы
	/// приводятся к понятному исключению с сохранением тела для диагностики.
	/// </summary>
	private static T DeserializeResult<T>(string body, string path)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			var result = document.RootElement.GetProperty(ResultPropertyName);
			return result.Deserialize<T>(BybitJson.Options)
				?? throw BybitApiException.FromMalformedBody(
					$"Ответ {path} не удалось разобрать как {typeof(T).Name}.", body);
		}
		catch (JsonException exception)
		{
			throw BybitApiException.FromMalformedBody(
				$"Ответ {path} не соответствует ожидаемому формату результата: {exception.Message}", body);
		}
		catch (KeyNotFoundException)
		{
			throw BybitApiException.FromMalformedBody($"Ответ {path} не содержит поле result.", body);
		}
	}

	private Uri BuildUri(string path, string queryString)
	{
		var url = _options.BaseUrl.TrimEnd('/') + path;
		if (queryString.Length > 0)
		{
			url += "?" + queryString;
		}

		return new Uri(url, UriKind.Absolute);
	}

	private static void ThrowIfApiError(string body)
	{
		if (TryReadEnvelope(body, out var retCode, out var retMsg))
		{
			// Нулевой retCode — успешный ответ биржи; любое другое значение — ошибка.
			if (retCode != 0)
			{
				throw new BybitApiException(retCode, retMsg ?? string.Empty, body);
			}

			return;
		}

		throw BybitApiException.FromMalformedBody("Ответ Bybit не содержит корректное поле retCode.", body);
	}

	private static bool TryReadEnvelope(string body, out int retCode, out string? retMsg)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Object
				&& root.TryGetProperty(RetCodePropertyName, out var retCodeElement)
				&& retCodeElement.ValueKind == JsonValueKind.Number
				&& retCodeElement.TryGetInt32(out retCode))
			{
				retMsg = root.TryGetProperty(RetMsgPropertyName, out var retMsgElement)
					&& retMsgElement.ValueKind == JsonValueKind.String
					? retMsgElement.GetString()
					: null;
				return true;
			}
		}
		catch (JsonException)
		{
			// Некорректный JSON возвращается вызывающей стороне как «нет конверта retCode».
		}

		retCode = 0;
		retMsg = null;
		return false;
	}

	#endregion
}
