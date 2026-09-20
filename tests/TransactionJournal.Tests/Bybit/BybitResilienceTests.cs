using System.Globalization;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Проверки устойчивости клиента Bybit на фиктивном транспорте и виртуальном времени:
/// минимальный интервал между запросами, пауза при исчерпании остатка X-Bapi-Limit-*,
/// короткий нарастающий бэкофф на сеть/5xx, пауза секундами на retCode 10006
/// и длинная пауза с ограничением попыток на HTTP 403.
/// </summary>
[TestClass]
public class BybitResilienceTests
{
	private const string TestBaseUrl = "http://bybit-test.local";
	private const string ApiKey = "test-api-key";
	private const string ApiSecret = "test-api-secret";
	private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);
	private static readonly string OkBody = """{"retCode":0,"retMsg":"OK","result":{"list":[]}}""";

	private ScriptedHttpMessageHandler _handler = null!;
	private ManualTimeProvider _time = null!;
	private BybitApiClient _client = null!;

	[TestInitialize]
	public void Initialize()
	{
		_time = new ManualTimeProvider();
		_handler = new ScriptedHttpMessageHandler(_time);
		var credentials = new BybitCredentials(ApiKey, ApiSecret);
		var credentialsProvider = Mock.Of<IBybitCredentialsProvider>(
			provider => provider.GetCredentials() == credentials);
		_client = new BybitApiClient(
			new HttpClient(_handler),
			credentialsProvider,
			new BybitClientOptions { BaseUrl = TestBaseUrl },
			new BybitResilienceOptions { MinRequestInterval = MinInterval },
			_time);
	}

	[TestMethod]
	[Description("Между запросами выдерживается минимальный интервал; положительный остаток лимита паузы не требует")]
	public async Task TryIfMinimumIntervalIsKeptBetweenRequests()
	{
		// Arrange: три успешных ответа с положительным остатком лимита в заголовках —
		// пауза по лимитам не нужна, действует только минимальный интервал.
		// Требование: клиент уважает лимиты биржи и не бомбардирует её запросами.
		// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
		// Traceability: change:add-bybit-sync/design#d5
		var headers = new Dictionary<string, string> { ["X-Bapi-Limit-Status"] = "42" };
		_handler.EnqueueJson(OkBody, headers: headers);
		_handler.EnqueueJson(OkBody, headers: headers);
		_handler.EnqueueJson(OkBody, headers: headers);

		// Act: три последовательных запроса без прохождения реального времени между ними.
		await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());
		await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());
		await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());

		// Assert: каждая следующая отправка отложена ровно на минимальный интервал.
		Assert.That(_handler.Requests, Has.Count.EqualTo(3));
		var timestamps = _handler.RequestTimestampsMs;
		Assert.That(timestamps[1] - timestamps[0], Is.EqualTo(MinInterval.TotalMilliseconds));
		Assert.That(timestamps[2] - timestamps[1], Is.EqualTo(MinInterval.TotalMilliseconds));
	}

	[TestMethod]
	[Description("Исчерпанный остаток X-Bapi-Limit задерживает следующий запрос до отметки сброса окна")]
	public async Task TryIfExhaustedLimitStatusDelaysNextRequestUntilReset()
	{
		// Arrange: биржа сообщает остаток 0 и отметку сброса окна через 3 секунды.
		// Требование: при исчерпании остатка лимита клиент выдерживает паузу до запроса.
		// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
		// Traceability: openspec:sync/bybit-history#scenario-rate-limit-backoff
		// Traceability: change:add-bybit-sync/design#d5
		var resetAtMs = _time.GetUtcNow().ToUnixTimeMilliseconds() + 3000;
		_handler.EnqueueJson(OkBody, headers: new Dictionary<string, string>
		{
			["X-Bapi-Limit-Status"] = "0",
			["X-Bapi-Limit-Reset-Timestamp"] = resetAtMs.ToString(CultureInfo.InvariantCulture),
		});
		_handler.EnqueueJson(OkBody);

		// Act: первый запрос исчерпывает окно, второй должен подождать до отметки сброса.
		await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());
		await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());

		// Assert: второй запрос ушёл ровно в момент сброса окна, а не раньше.
		Assert.That(_handler.RequestTimestampsMs[1], Is.EqualTo(resetAtMs));
	}

	[TestMethod]
	[Description("Сетевые ошибки и HTTP 5xx повторяются с коротким нарастающим бэкоффом")]
	public async Task TryIfNetworkAndServerErrorsAreRetriedWithShortBackoff()
	{
		// Arrange: первый ответ — обрыв сети, второй — HTTP 500, третий успешен.
		// Требование: сетевые и серверные ошибки обрабатываются повторами с нарастающей паузой.
		// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
		// Traceability: change:add-bybit-sync/design#d5
		_handler.EnqueueException(new HttpRequestException("connection refused"));
		_handler.EnqueueJson("Internal Server Error", HttpStatusCode.InternalServerError);
		_handler.EnqueueJson(OkBody);

		// Act: один вызов проходит через все три попытки и завершается успехом.
		var body = await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());

		// Assert: ровно три отправки; паузы растут экспоненциально от базовой 200 мс.
		Assert.That(_handler.Requests, Has.Count.EqualTo(3));
		Assert.That(body, Does.Contain("\"retCode\":0"));
		Assert.That(_time.Delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400) }));
	}

	[TestMethod]
	[Description("retCode 10006 повторяется после паузы секундами и завершается успехом")]
	public async Task TryIfRateLimitRetCodeIsRetriedAfterSecondsPause()
	{
		// Arrange: первый ответ — превышение частоты запросов per-UID, второй успешен.
		// Требование: ответ с признаком превышения частоты обрабатывается повтором с паузой.
		// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
		// Traceability: openspec:sync/bybit-history#scenario-rate-limit-backoff
		// Traceability: change:add-bybit-sync/design#d5
		_handler.EnqueueJson("""{"retCode":10006,"retMsg":"Too many visits!","result":{}}""");
		_handler.EnqueueJson(OkBody);

		// Act: повтор уходит после секундной паузы и получает успешный ответ.
		var body = await _client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>());

		// Assert: две отправки; пауза повтора измеряется секундами от базовой секунды.
		Assert.That(_handler.Requests, Has.Count.EqualTo(2));
		Assert.That(body, Does.Contain("\"retCode\":0"));
		Assert.That(_time.Delays.Single(), Is.EqualTo(TimeSpan.FromSeconds(1)));
	}

	[TestMethod]
	[Description("HTTP 403 повторяется один раз с длинной паузой, затем падает понятной ошибкой")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnAccessBlockedAfterLongPauseRetryLimit()
	{
		// Arrange: биржа дважды отвечает 403 «access too frequent» — IP-лимит исчерпан,
		// повтор с длинной паузой тоже отвергнут.
		// Требование: длинная пауза с ограниченным числом попыток и внятная ошибка пользователю.
		// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
		// Traceability: change:add-bybit-sync/design#d5
		_handler.EnqueueJson("Forbidden", HttpStatusCode.Forbidden);
		_handler.EnqueueJson("Forbidden", HttpStatusCode.Forbidden);

		// Act — после повтора с длинной паузой вызов завершается понятным исключением.
		try
		{
			_client.GetAsync("/v5/execution/list", new List<KeyValuePair<string, string>>()).GetAwaiter().GetResult();
		}
		catch (BybitApiException exception)
		{
			// Assert: ровно две отправки (попытка и один повтор), пауза — ~10 минут,
			// сообщение объясняет блокировку IP, тело ответа сохранено для диагностики.
			Assert.That(_handler.Requests, Has.Count.EqualTo(2));
			Assert.That(_time.Delays.Single(), Is.EqualTo(TimeSpan.FromMinutes(10)));
			Assert.That(exception.HttpStatusCode, Is.EqualTo(403));
			Assert.That(exception.Message, Does.Contain("IP-лимит"));
			Assert.That(exception.ResponseBody, Is.EqualTo("Forbidden"));
			throw;
		}
	}
}
