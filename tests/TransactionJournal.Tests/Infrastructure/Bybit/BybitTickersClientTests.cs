using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.Bybit;

/// <summary>
/// Проверки публичного клиента тикеров против зафиксированных HTTP-ответов из официальной
/// документации Bybit: запрос уходит к публичному эндпоинту тикеров без заголовков
/// аутентификации и без API-ключа торгового аккаунта, а тело ответа разбирается
/// в типизированные марки инструментов.
/// </summary>
[TestClass]
public class BybitTickersClientTests
{
	private const string TestBaseUrl = "http://bybit-test.local";

	private ScriptedHttpMessageHandler _handler = null!;
	private BybitTickersClient _client = null!;

	[TestInitialize]
	public void Initialize()
	{
		_handler = new ScriptedHttpMessageHandler();
		_client = new BybitTickersClient(
			new HttpClient(_handler),
			new BybitClientOptions { BaseUrl = TestBaseUrl });
	}

	[TestMethod]
	[Description("Тикеры разбирают зафиксированные ответы option и linear в марки без заголовков аутентификации")]
	public async Task TryIfTickersParseRecordedResponsesIntoMarksWithoutAuthentication()
	{
		// Arrange: два зафиксированных ответа официальной документации — тикер опциона
		// с маркой 24.77 и тикер линейного контракта с маркой 27739.74; оба эндпоинт
		// отдаёт без аутентификации. Провайдер марок работает без API-ключа.
		// Traceability: openspec:analytics/performance#scenario-public-tickers-no-auth
		_handler.EnqueueJson(LoadFixture("tickers-option.json"));
		_handler.EnqueueJson(LoadFixture("tickers-linear.json"));

		// Act: запрашиваем марки конкретных инструментов обеих категорий.
		var optionTickers = await _client.GetTickersAsync(new BybitTickerQuery
		{
			Category = "option",
			Symbol = "BTC-29DEC23-25000-C",
		});
		var linearTickers = await _client.GetTickersAsync(new BybitTickerQuery
		{
			Category = "linear",
			Symbol = "BTCUSDT",
		});

		// Assert: запросы ушли к публичному эндпоинту тикеров GET-методом
		// с детерминированной строкой параметров.
		Assert.That(_handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get));
		Assert.That(_handler.Requests[0].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/market/tickers?category=option&symbol=BTC-29DEC23-25000-C"));
		Assert.That(_handler.Requests[1].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/market/tickers?category=linear&symbol=BTCUSDT"));

		// Assert: в запросах нет ни одного заголовка аутентификации торгового аккаунта —
		// сценарий «марки получаются без аутентификации».
		// Traceability: openspec:analytics/performance#scenario-public-tickers-no-auth
		foreach (var request in _handler.Requests)
		{
			Assert.That(request.Headers.Contains("X-BAPI-API-KEY"), Is.False);
			Assert.That(request.Headers.Contains("X-BAPI-SIGN"), Is.False);
			Assert.That(request.Headers.Contains("X-BAPI-TIMESTAMP"), Is.False);
			Assert.That(request.Headers.Contains("X-BAPI-RECV-WINDOW"), Is.False);
			Assert.That(request.Headers.Contains("X-BAPI-SIGN-TYPE"), Is.False);
		}

		// Assert: строковые числа марок разобраны инвариантной культурой.
		var option = optionTickers.Single();
		Assert.That(option.Symbol, Is.EqualTo("BTC-29DEC23-25000-C"));
		Assert.That(option.MarkPrice, Is.EqualTo(24.77m));
		var linear = linearTickers.Single();
		Assert.That(linear.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(linear.MarkPrice, Is.EqualTo(27739.74m));
	}

	[TestMethod]
	[Description("Тикеры отдают опциональные рыночные поля чата из зафиксированных ответов обеих категорий")]
	public async Task TryIfTickersExposeOptionalMarketFieldsForChatTools()
	{
		// Arrange: те же зафиксированные ответы официальной документации — тикер опциона
		// с греками и волатильностями, тикер линейного перпа со ставкой фандинга.
		_handler.EnqueueJson(LoadFixture("tickers-option.json"));
		_handler.EnqueueJson(LoadFixture("tickers-linear.json"));

		// Act: запрашиваем тикеры обеих категорий.
		var option = (await _client.GetTickersAsync(new BybitTickerQuery
		{
			Category = "option",
			Symbol = "BTC-29DEC23-25000-C",
		})).Single();
		var linear = (await _client.GetTickersAsync(new BybitTickerQuery
		{
			Category = "linear",
			Symbol = "BTCUSDT",
		})).Single();

		// Assert: опционный тикер отдаёт греки, волатильности, бид-аск и открытый
		// интерес — сырьё компактной проекции доски для инструментов чата.
		// Traceability: change:add-assistant-chat/design#d3
		Assert.Multiple(() =>
		{
			Assert.That(option.Delta, Is.EqualTo(0.3426m));
			Assert.That(option.Gamma, Is.EqualTo(0.00010967m));
			Assert.That(option.Vega, Is.EqualTo(31.1594m));
			Assert.That(option.Theta, Is.EqualTo(-11.2011m));
			Assert.That(option.MarkIv, Is.EqualTo(0.5417m));
			Assert.That(option.Bid1Iv, Is.EqualTo(0.5389m));
			Assert.That(option.Ask1Iv, Is.EqualTo(0.5445m));
			Assert.That(option.Bid1Price, Is.EqualTo(24.5m));
			Assert.That(option.Bid1Size, Is.EqualTo(2m));
			Assert.That(option.Ask1Price, Is.EqualTo(25.5m));
			Assert.That(option.Ask1Size, Is.EqualTo(2m));
			Assert.That(option.OpenInterest, Is.EqualTo(5770m));
		});

		// Assert: чужие категории полей у опциона пусты — поля опциональны,
		// поведение потребителя марки от их появления не меняется.
		Assert.Multiple(() =>
		{
			Assert.That(option.FundingRate, Is.Null);
			Assert.That(option.OpenInterestValue, Is.Null);
		});

		// Assert: линейный перп отдаёт ставку фандинга, открытый интерес в обеих
		// валютах и бид-аск — сырьё снимка рынка для инструментов чата,
		// а греков и волатильностей у него нет.
		// Traceability: change:add-assistant-chat/design#d3
		Assert.Multiple(() =>
		{
			Assert.That(linear.FundingRate, Is.EqualTo(0.0001m));
			Assert.That(linear.OpenInterest, Is.EqualTo(74033.459m));
			Assert.That(linear.OpenInterestValue, Is.EqualTo(2052656378.86m));
			Assert.That(linear.Bid1Price, Is.EqualTo(27744.5m));
			Assert.That(linear.Bid1Size, Is.EqualTo(84.149m));
			Assert.That(linear.Ask1Price, Is.EqualTo(27745.5m));
			Assert.That(linear.Ask1Size, Is.EqualTo(81.116m));
			Assert.That(linear.Delta, Is.Null);
			Assert.That(linear.MarkIv, Is.Null);
		});
	}

	[TestMethod]
	[Description("Запрос тикеров без категории отклоняется до сетевого вызова")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnTickersQueryWithoutCategory()
	{
		// Arrange: категория обязательна для эндпоинта тикеров по документации биржи.
		var query = new BybitTickerQuery { Category = " ", Symbol = "BTCUSDT" };

		// Act — пустая категория прерывается аргумент-исключением, не отправляя запрос.
		try
		{
			_client.GetTickersAsync(query).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Запрос тикеров без самой записи отклоняется до сетевого вызова")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnTickersQueryNotProvided()
	{
		// Arrange: запрос обязан быть задан.
		// Act — отсутствие записи прерывается до сетевого вызова.
		try
		{
			_client.GetTickersAsync(null!).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Ошибка конверта тикеров превращается в ошибку биржи с телом ответа")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnTickersEnvelopeError()
	{
		// Arrange: биржа ответила ненулевым retCode — категория отклонена.
		_handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error: category invalid","result":{}}""");

		// Act — ошибка конверта поднимается исключением.
		try
		{
			_client.GetTickersAsync(new BybitTickerQuery { Category = "invalid-category" }).GetAwaiter().GetResult();
		}
		catch (BybitApiException exception)
		{
			// Assert: код и тело ответа сохранены для диагностики.
			Assert.That(exception.RetCode, Is.EqualTo(10001));
			Assert.That(exception.ResponseBody, Does.Contain("params error"));
			throw;
		}
	}

	#region Помощники

	private static string LoadFixture(string fileName)
	{
		// Зафиксированные ответы лежат рядом с тестовой сборкой в Bybit/Fixtures.
		return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Bybit", "Fixtures", fileName));
	}

	#endregion
}
