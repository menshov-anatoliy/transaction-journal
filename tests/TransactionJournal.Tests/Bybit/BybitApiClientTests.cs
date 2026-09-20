using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Проверки HttpClient-обёртки Bybit на фиктивном транспорте: подписанный GET-запрос
/// несёт заголовки официальной схемы и побайтово совпадающую queryString в URL и подписи,
/// часы сверяются с /v5/market/time, ошибка 10003 вызывает повтор после сверки,
/// а ошибки биржи и транспорта приводят к понятному исключению.
/// </summary>
[TestClass]
public class BybitApiClientTests
{
	private const string TestBaseUrl = "http://bybit-test.local";
	private const string ApiKey = "test-api-key";
	private const string ApiSecret = "test-api-secret";
	private const long ServerTimeMs = 1701680884232L;

	private ScriptedHttpMessageHandler _handler = null!;
	private BybitApiClient _client = null!;

	[TestInitialize]
	public void Initialize()
	{
		_handler = new ScriptedHttpMessageHandler();
		var credentials = new BybitCredentials(ApiKey, ApiSecret);
		var credentialsProvider = Mock.Of<IBybitCredentialsProvider>(
			provider => provider.GetCredentials() == credentials);
		_client = new BybitApiClient(
			new HttpClient(_handler),
			credentialsProvider,
			new BybitClientOptions { BaseUrl = TestBaseUrl });
	}

	[TestMethod]
	[Description("Подписанный запрос несёт заголовки официальной схемы и ту же queryString, что и в подписи")]
	public void TryIfSignedRequestCarriesOfficialHeadersAndIdenticalQueryString()
	{
		// Arrange: обёртка выполняет только GET-запросы чтения истории и справочников.
		// Traceability: openspec:sync/bybit-history#requirement-read-only-access
		// Traceability: change:add-bybit-sync/design#d1
		_handler.EnqueueJson("""{"retCode":0,"retMsg":"OK","result":{"list":[]}}""");

		// Act: подписываем запрос с несколькими параметрами.
		var body = _client.GetAsync(
			"/v5/execution/list",
			new List<KeyValuePair<string, string>>
			{
				new("category", "linear"),
				new("limit", "100"),
			}).GetAwaiter().GetResult();

		// Assert: URL содержит вручную собранную строку запроса; подпись вычислена по ней же;
		// заголовки повторяют официальный пример, метод — только GET.
		var request = _handler.Requests.Single();
		Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
		Assert.That(request.RequestUri!.ToString(), Is.EqualTo($"{TestBaseUrl}/v5/execution/list?category=linear&limit=100"));
		Assert.That(request.Headers.GetValues("X-BAPI-API-KEY").Single(), Is.EqualTo(ApiKey));
		Assert.That(request.Headers.GetValues("X-BAPI-RECV-WINDOW").Single(), Is.EqualTo("5000"));
		Assert.That(request.Headers.GetValues("X-BAPI-SIGN-TYPE").Single(), Is.EqualTo("2"));

		var timestamp = long.Parse(request.Headers.GetValues("X-BAPI-TIMESTAMP").Single());
		var expectedSignature = BybitRequestSigner.ComputeSignature(
			timestamp, ApiKey, 5000, "category=linear&limit=100", ApiSecret);
		Assert.That(request.Headers.GetValues("X-BAPI-SIGN").Single(), Is.EqualTo(expectedSignature));
		Assert.That(timestamp, Is.GreaterThan(0));
		Assert.That(body, Does.Contain("\"retCode\":0"));
	}

	[TestMethod]
	[Description("После сверки часов timestamp подписи берётся по серверному времени")]
	public async Task TryIfTimestampUsesServerTimeAfterSynchronization()
	{
		// Arrange: публичный эндпоинт времени отдаёт фиксированное серверное время.
		// Traceability: change:add-bybit-sync/design#d6
		_handler.EnqueueJson(ServerTimeResponseBody());
		_handler.EnqueueJson("""{"retCode":0,"retMsg":"OK","result":{"list":[]}}""");

		// Act: сверяем часы, затем выполняем подписанный запрос.
		await _client.SynchronizeClockAsync();
		await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());

		// Assert: смещение запомнено, а timestamp подписи равен серверному времени в пределах окна.
		var timestamp = long.Parse(_handler.Requests[^1].Headers.GetValues("X-BAPI-TIMESTAMP").Single());
		Assert.That(_client.ServerTimeOffsetMs, Is.EqualTo(ServerTimeMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Within(5000));
		Assert.That(Math.Abs(timestamp - ServerTimeMs), Is.LessThan(5000));
	}

	[TestMethod]
	[Description("Ошибка 10003 запускает сверку часов и повтор запроса с новой подписью")]
	public async Task TryIfSignErrorTriggersClockResyncAndRetry()
	{
		// Arrange: первый ответ — ошибка подписи 10003 (классический рассинхрон часов),
		// после сверки часов повторный запрос успешен.
		// Traceability: change:add-bybit-sync/design#d6
		_handler.EnqueueJson("""{"retCode":10003,"retMsg":"sign error","result":{}}""");
		_handler.EnqueueJson(ServerTimeResponseBody());
		_handler.EnqueueJson("""{"retCode":0,"retMsg":"OK","result":{"list":[]}}""");

		// Act: обёртка сама сверяет часы и повторяет подписанный запрос один раз.
		var body = await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());

		// Assert: выполнено ровно три запроса (ошибка, время, успех); повтор подписан
		// timestamp уже по серверному времени; итоговое тело — успешный ответ.
		Assert.That(_handler.Requests, Has.Count.EqualTo(3));
		Assert.That(_handler.Requests[1].RequestUri!.AbsolutePath, Is.EqualTo("/v5/market/time"));
		var retryTimestamp = long.Parse(_handler.Requests[2].Headers.GetValues("X-BAPI-TIMESTAMP").Single());
		Assert.That(Math.Abs(retryTimestamp - ServerTimeMs), Is.LessThan(5000));
		Assert.That(body, Does.Contain("\"retCode\":0"));
	}

	[TestMethod]
	[DataRow(10001, "params error")]
	[DataRow(10006, "Too many visits!")]
	[Description("Ошибка retCode в теле успешного ответа — исключение с кодом биржи")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnApiErrorResponse(int retCode, string retMsg)
	{
		// Arrange: биржа отвечает ошибкой внутри конверта retCode/retMsg.
		_handler.EnqueueJson(
			"{\"retCode\":" + retCode + ",\"retMsg\":\"" + retMsg + "\",\"result\":{}}");

		// Act — ошибка биржи прерывает запрос исключением с кодом и телом ответа.
		try
		{
			_client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>()).GetAwaiter().GetResult();
		}
		catch (BybitApiException exception)
		{
			// Assert: код и сообщение биржи сохранены в исключении для диагностики.
			Assert.That(exception.RetCode, Is.EqualTo(retCode));
			Assert.That(exception.Message, Does.Contain(retMsg));
			throw;
		}
	}

	[TestMethod]
	[Description("Неудачный HTTP-статус без конверта retCode — исключение с телом ответа")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnNonSuccessHttpStatus()
	{
		// Arrange: транспорт отдаёт 500 с телом, не являющимся конвертом V5.
		_handler.EnqueueJson("Internal Server Error", HttpStatusCode.InternalServerError);

		// Act — неудачный статус прерывается исключением обёртки.
		_client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>()).GetAwaiter().GetResult();
	}

	[TestMethod]
	[Description("Некорректное тело эндпоинта времени — исключение разбора ответа")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnMalformedServerTimeBody()
	{
		// Arrange: эндпоинт времени отвечает без поля timeNano — сверка невозможна.
		// Traceability: change:add-bybit-sync/design#d6
		_handler.EnqueueJson("""{"retCode":0,"retMsg":"OK","result":{}}""");

		// Act — сверка часов прерывается понятной ошибкой разбора.
		_client.SynchronizeClockAsync().GetAwaiter().GetResult();
	}

	#region Помощники

	private static string ServerTimeResponseBody()
	{
		// Формат ответа /v5/market/time: секунды и наносекунды строками в result.
		return "{\"retCode\":0,\"retMsg\":\"OK\",\"result\":{\"timeSecond\":\"1701680884\","
			+ "\"timeNano\":\"1701680884232618083\"},\"retExt\":null,\"time\":" + ServerTimeMs + "}";
	}

	#endregion
}
