using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Интеграционные проверки типизированных read-only эндпоинтов Bybit-клиента против
/// зафиксированных HTTP-ответов из официальной документации биржи: подписанный запрос
/// уходит с детерминированной строкой параметров, а тело ответа разбирается в
/// типизированные записи с корректной интерпретацией строковых чисел и пустых значений.
/// </summary>
[TestClass]
public class BybitApiClientEndpointsTests
{
	private const string TestBaseUrl = "http://bybit-test.local";
	private const string ApiKey = "test-api-key";
	private const string ApiSecret = "test-api-secret";

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
	[Description("Execution list разбирает зафиксированные ответы option и linear в типизированные записи")]
	public async Task TryIfExecutionListParsesRecordedResponsesIntoTypedList()
	{
		// Arrange: два зафиксированных ответа официальной документации — исполнения
		// опциона (с IV-полями и курсором) и линейного контракта (без опционных полей).
		// Traceability: doc:docs/research/bybit-api.md#1-история-исполнения-сделок-unified-аккаунта-execution-list
		_handler.EnqueueJson(LoadFixture("execution-list-option.json"));
		_handler.EnqueueJson(LoadFixture("execution-list-linear.json"));

		// Act: запрашиваем окно категории option с курсором предыдущей страницы, затем linear.
		var optionPage = await _client.GetExecutionListAsync(new BybitExecutionListQuery
		{
			Category = "option",
			StartTimeMs = 1672166400000,
			EndTimeMs = 1672252800000,
			Limit = 100,
			Cursor = "5a373bfe-188d-4913-9c81-d57ab5be8068%3A1672214887231523642",
		});
		var linearPage = await _client.GetExecutionListAsync(new BybitExecutionListQuery
		{
			Category = "linear",
			StartTimeMs = 1672166400000,
			EndTimeMs = 1672252800000,
		});

		// Assert: строка параметров собрана в детерминированном порядке и ушла в URL подписанного GET.
		Assert.That(_handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get));
		Assert.That(_handler.Requests[0].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/execution/list?category=option&startTime=1672166400000&endTime=1672252800000&limit=100&cursor=5a373bfe-188d-4913-9c81-d57ab5be8068%3A1672214887231523642"));
		Assert.That(_handler.Requests[1].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/execution/list?category=linear&startTime=1672166400000&endTime=1672252800000"));

		// Assert: типизированная запись опциона содержит строковые числа, флаг мейкера и IV-поля.
		var option = optionPage.List.Single();
		Assert.That(option.Symbol, Is.EqualTo("BTC-29DEC23-45000-C"));
		Assert.That(option.ExecId, Is.EqualTo("5a373bfe-188d-4913-9c81-d57ab5be8068"));
		Assert.That(option.Side, Is.EqualTo("Sell"));
		Assert.That(option.OrderPrice, Is.Null);
		Assert.That(option.ExecPrice, Is.EqualTo(45000m));
		Assert.That(option.ExecQty, Is.EqualTo(0.0001m));
		Assert.That(option.ExecFee, Is.EqualTo(0.00000001m));
		Assert.That(option.FeeCurrency, Is.EqualTo("BTC"));
		Assert.That(option.ExecTimeMs, Is.EqualTo(1672214887232L));
		Assert.That(option.IsMaker, Is.False);
		Assert.That(option.Seq, Is.EqualTo(291930L));
		Assert.That(option.MarkIv, Is.EqualTo(0.6173m));
		Assert.That(option.TradeIv, Is.EqualTo(0.6175m));
		Assert.That(option.UnderlyingPrice, Is.EqualTo(16761.67370605m));
		Assert.That(optionPage.NextPageCursor, Is.EqualTo("5a373bfe-188d-4913-9c81-d57ab5be8068%3A1672214887231523642"));
		Assert.That(optionPage.HasNextPage, Is.True);

		// Assert: linear-запись без опционных полей; пустой курсор означает последнюю страницу.
		var linear = linearPage.List.Single();
		Assert.That(linear.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(linear.OrderPrice, Is.EqualTo(10969.5m));
		Assert.That(linear.FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(linear.TradeIv, Is.Null);
		Assert.That(linear.MarkIv, Is.Null);
		Assert.That(linear.UnderlyingPrice, Is.Null);
		Assert.That(linearPage.HasNextPage, Is.False);
	}

	[TestMethod]
	[Description("Delivery-record разбирает зафиксированный ответ экспирации в типизированную запись")]
	public async Task TryIfDeliveryRecordParsesRecordedResponseIntoTypedList()
	{
		// Arrange: зафиксированный ответ официального примера Get Delivery Record —
		// экспирация опциона с расчётной ценой и реализованным PnL доставки.
		// Спека требует получать delivery-записи именно отдельным эндпоинтом экспираций.
		// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
		_handler.EnqueueJson(LoadFixture("delivery-record.json"));

		// Act: окно 30 дней по категории option с фильтром символа.
		var page = await _client.GetDeliveryRecordAsync(new BybitDeliveryRecordQuery
		{
			Category = "option",
			Symbol = "BTC-29DEC22-16000-P",
			StartTimeMs = 1670140800000,
			EndTimeMs = 1672732800000,
			Limit = 50,
		});

		// Assert: URL содержит фильтр символа и границы окна в детерминированном порядке.
		Assert.That(_handler.Requests.Single().RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/asset/delivery-record?category=option&symbol=BTC-29DEC22-16000-P&startTime=1670140800000&endTime=1672732800000&limit=50"));

		// Assert: время доставки приходит числом, суммы — строками с инвариантным разделителем.
		var delivery = page.List.Single();
		Assert.That(delivery.DeliveryTimeMs, Is.EqualTo(1672300800860L));
		Assert.That(delivery.Symbol, Is.EqualTo("BTC-29DEC22-16000-P"));
		Assert.That(delivery.Side, Is.EqualTo("Buy"));
		Assert.That(delivery.Position, Is.EqualTo(0.01m));
		Assert.That(delivery.EntryPrice, Is.EqualTo(16699.93217371m));
		Assert.That(delivery.DeliveryPrice, Is.EqualTo(16541.86369547m));
		Assert.That(delivery.Strike, Is.EqualTo(16000m));
		Assert.That(delivery.Fee, Is.EqualTo(0m));
		Assert.That(delivery.DeliveryRpl, Is.EqualTo(3.5m));
		Assert.That(page.HasNextPage, Is.True);
	}

	[TestMethod]
	[Description("Instruments-info разбирает зафиксированные ответы option и linear в справочник")]
	public async Task TryIfInstrumentsInfoParsesRecordedResponsesIntoTypedList()
	{
		// Arrange: два зафиксированных ответа — справочник опционов (Put и Call)
		// и линейных контрактов (бессрочный перп без времени доставки).
		// Спека требует строить справочник из публичного эндпоинта спецификаций.
		// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
		_handler.EnqueueJson(LoadFixture("instruments-info-option.json"));
		_handler.EnqueueJson(LoadFixture("instruments-info-linear.json"));

		// Act: запрашиваем справочник опционов по базовому активу и линейных по символу.
		var optionPage = await _client.GetInstrumentsInfoAsync(new BybitInstrumentInfoQuery
		{
			Category = "option",
			BaseCoin = "BTC",
			Limit = 1000,
		});
		var linearPage = await _client.GetInstrumentsInfoAsync(new BybitInstrumentInfoQuery
		{
			Category = "linear",
			Symbol = "BTCUSDT",
		});

		// Assert: параметры собраны в детерминированном порядке.
		Assert.That(_handler.Requests[0].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/market/instruments-info?category=option&baseCoin=BTC&limit=1000"));
		Assert.That(_handler.Requests[1].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/market/instruments-info?category=linear&symbol=BTCUSDT"));

		// Assert: канонические поля опционов берутся из справочника, а не из строки символа.
		var put = optionPage.List.Single(item => item.OptionsType == "Put");
		var call = optionPage.List.Single(item => item.OptionsType == "Call");
		Assert.That(put.Symbol, Is.EqualTo("BTC-24JUN23-56000-P"));
		Assert.That(put.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(put.SettleCoin, Is.EqualTo("USDC"));
		Assert.That(put.DeliveryTimeMs, Is.EqualTo(1674873600000L));
		Assert.That(put.DeliveryFeeRate, Is.EqualTo(0.0001m));
		Assert.That(call.Symbol, Is.EqualTo("ETH-24JUN23-3000-C"));
		Assert.That(optionPage.HasNextPage, Is.False);

		// Assert: у перпа нулевое время доставки и пустая ставка комиссии доставки.
		var perp = linearPage.List.Single();
		Assert.That(perp.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(perp.ContractType, Is.EqualTo("LinearPerpetual"));
		Assert.That(perp.OptionsType, Is.Null);
		Assert.That(perp.DeliveryTimeMs, Is.EqualTo(0L));
		Assert.That(perp.DeliveryFeeRate, Is.Null);
		Assert.That(perp.FundingInterval, Is.EqualTo(480));
	}

	[TestMethod]
	[Description("Market time разбирает зафиксированный ответ в типизированное серверное время")]
	public async Task TryIfServerTimeParsesRecordedResponseIntoTypedValue()
	{
		// Arrange: зафиксированный ответ официального примера /v5/market/time —
		// секунды и наносекунды строками; запрос публичный и не подписывается.
		// Traceability: change:add-bybit-sync/design#d6
		_handler.EnqueueJson(LoadFixture("market-time.json"));

		// Act
		var serverTime = await _client.GetServerTimeAsync();

		// Assert: строки сохранены как есть, миллисекунды вычислены из первых 13 разрядов timeNano.
		Assert.That(_handler.Requests.Single().RequestUri!.ToString(), Is.EqualTo($"{TestBaseUrl}/v5/market/time"));
		Assert.That(_handler.Requests.Single().Headers.Contains("X-BAPI-SIGN"), Is.False);
		Assert.That(serverTime.TimeSecond, Is.EqualTo("1665366615"));
		Assert.That(serverTime.TimeNano, Is.EqualTo("1665366615623637869"));
		Assert.That(serverTime.Milliseconds, Is.EqualTo(1665366615623L));
	}

	[TestMethod]
	[Description("Запрос execution list без категории отклоняется до сетевого вызова")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnExecutionListQueryWithoutCategory()
	{
		// Arrange: категория обязательна для эндпоинта по документации биржи.
		var query = new BybitExecutionListQuery { Category = null!, Limit = 100 };

		// Act — клиент отвергает запрос аргумент-исключением, не отправляя его бирже.
		try
		{
			_client.GetExecutionListAsync(query).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(101)]
	[Description("Limit вне документированного диапазона [1..100] отклоняется")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnExecutionListLimitOutsideDocumentedRange(int limit)
	{
		// Arrange: биржа ограничивает размер страницы execution list диапазоном [1..100].
		var query = new BybitExecutionListQuery { Category = "option", Limit = limit };

		// Act — выход за диапазон прерывается до запроса.
		try
		{
			_client.GetExecutionListAsync(query).GetAwaiter().GetResult();
		}
		catch (ArgumentOutOfRangeException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Запрос delivery-record без категории отклоняется до сетевого вызова")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnDeliveryRecordQueryWithoutCategory()
	{
		// Arrange: категория обязательна для эндпоинта delivery-record.
		var query = new BybitDeliveryRecordQuery { Category = " ", Limit = 50 };

		// Act — пустая категория прерывается до запроса.
		try
		{
			_client.GetDeliveryRecordAsync(query).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Запрос instruments-info без категории отклоняется до сетевого вызова")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnInstrumentsInfoQueryWithoutCategory()
	{
		// Arrange: категория обязательна для эндпоинта instruments-info.
		var query = new BybitInstrumentInfoQuery { Category = null! };

		// Act — отсутствие категории прерывается до запроса.
		try
		{
			_client.GetInstrumentsInfoAsync(query).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Успешный конверт с результатом несовместимой формы — ошибка разбора с телом ответа")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnIncompatibleResultShape()
	{
		// Arrange: биржа ответила успехом, но result.list повреждён и не является списком.
		_handler.EnqueueJson("""{"retCode":0,"retMsg":"OK","result":{"list":"повреждено"}}""");

		// Act — разбор прерывается исключением с сохранением тела для диагностики.
		try
		{
			_client.GetExecutionListAsync(new BybitExecutionListQuery { Category = "option" }).GetAwaiter().GetResult();
		}
		catch (BybitApiException exception)
		{
			// Assert: тело ответа сохранено в исключении.
			Assert.That(exception.ResponseBody, Does.Contain("повреждено"));
			throw;
		}
	}

	[TestMethod]
	[Description("Ответ времени без корректного timeNano — ошибка разбора")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnServerTimeWithoutTimeNano()
	{
		// Arrange: эндпоинт времени ответил без наносекунд — сверка часов невозможна.
		// Traceability: change:add-bybit-sync/design#d6
		_handler.EnqueueJson("""{"retCode":0,"retMsg":"OK","result":{"timeSecond":"1665366615"}}""");

		// Act — получение времени прерывается понятной ошибкой разбора.
		_client.GetServerTimeAsync().GetAwaiter().GetResult();
	}

	#region Помощники

	private static string LoadFixture(string fileName)
	{
		// Зафиксированные ответы лежат рядом с тестовой сборкой в Bybit/Fixtures.
		return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Bybit", "Fixtures", fileName));
	}

	#endregion
}
