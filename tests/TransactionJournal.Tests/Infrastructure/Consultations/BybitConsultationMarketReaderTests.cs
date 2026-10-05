using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Infrastructure.Consultations;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using OptionType = TransactionJournal.Domain.Materialization.OptionType;

namespace TransactionJournal.Tests.Infrastructure.Consultations;

using TransactionJournal.Tests.Infrastructure.Bybit;

/// <summary>
/// Проверки Bybit-адаптера рыночных данных консультаций против зафиксированных
/// HTTP-ответов: каждое чтение порта выполняет ровно один запрос к публичному
/// эндпоинту тикеров, снимок — линейным запросом по символу перпа, доска —
/// опционным запросом с фильтром baseCoin, а ошибка биржи превращается в
/// недоступную запись с причиной вместо исключения.
/// </summary>
[TestClass]
public sealed class BybitConsultationMarketReaderTests
{
	/// <summary>Тестовый адрес API Bybit без завершающего слэша.</summary>
	private const string TestBaseUrl = "http://bybit-test.local";

	/// <summary>Фиксированный момент as-of для детерминированных проверок времени.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Снимок рынка читается одним линейным запросом и проецируется в поля порта")]
	public async Task ReadSnapshotAsync_SingleLinearRequestAndProjection()
	{
		// Arrange: зафиксированный ответ линейного перпа BTCUSDT с маркой,
		// бид-аском, открытым интересом и ставкой фандинга — общий fixture клиента тикеров.
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson(LoadFixture(Path.Combine("Bybit", "Fixtures", "tickers-linear.json")));

		var reader = CreateReader(handler);

		// Act: читаем снимок по базовому активу.
		var snapshot = await reader.ReadSnapshotAsync("BTC");

		// Assert: ровно один HTTP-запрос к линейному эндпоинту тикеров —
		// требование «один вызов инструмента = один биржевой запрос».
		// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
		Assert.That(handler.Requests.Count, Is.EqualTo(1));
		Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get));
		Assert.That(handler.Requests[0].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/market/tickers?category=linear&symbol=BTCUSDT"));

		// Assert: поля тикера спроецированы в снимок, as-of — от поставщика времени.
		Assert.That(snapshot.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(snapshot.IsAvailable, Is.True);
		Assert.That(snapshot.UnavailableReason, Is.Null);
		Assert.That(snapshot.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(snapshot.MarkPrice, Is.EqualTo(27739.74m));
		Assert.That(snapshot.Bid1Price, Is.EqualTo(27744.5m));
		Assert.That(snapshot.Bid1Size, Is.EqualTo(84.149m));
		Assert.That(snapshot.Ask1Price, Is.EqualTo(27745.5m));
		Assert.That(snapshot.Ask1Size, Is.EqualTo(81.116m));
		Assert.That(snapshot.OpenInterest, Is.EqualTo(74033.459m));
		Assert.That(snapshot.FundingRate, Is.EqualTo(0.0001m));
		Assert.That(snapshot.AsOf, Is.EqualTo(FixedNow));
	}

	[TestMethod]
	[Description("Доска опционов читается одним запросом baseCoin, котировки разобраны, нечитаемые символы отброшены")]
	public async Task ReadOptionBoardAsync_SingleOptionRequestAndQuotesProjection()
	{
		// Arrange: зафиксированный ответ доски BTC — два экспирации, страйки
		// внутри и вне проекции, пара колл/пут и один нечитаемый символ.
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson(LoadFixture(Path.Combine("Consultations", "Fixtures", "tickers-option-board.json")));

		var reader = CreateReader(handler);

		// Act: читаем доску опционов по базовому активу.
		var board = await reader.ReadOptionBoardAsync("BTC");

		// Assert: ровно один HTTP-запрос к опционному эндпоинту с фильтром baseCoin —
		// доска не запрашивается по одному символу за вызов.
		// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
		Assert.That(handler.Requests.Count, Is.EqualTo(1));
		Assert.That(handler.Requests[0].RequestUri!.ToString(), Is.EqualTo(
			$"{TestBaseUrl}/v5/market/tickers?category=option&baseCoin=BTC"));

		// Assert: котировки разобраны в канонические части символа, нечитаемый
		// символ отброшен, а TotalTickerCount считает сырые записи биржи.
		Assert.That(board.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(board.IsAvailable, Is.True);
		Assert.That(board.UnderlyingPrice, Is.EqualTo(106000.12345678m));
		Assert.That(board.TotalTickerCount, Is.EqualTo(9));
		Assert.That(board.Quotes, Has.Count.EqualTo(8));
		Assert.That(board.AsOf, Is.EqualTo(FixedNow));

		var call = board.Quotes.Single(quote => quote.Symbol == "BTC-26DEC25-95000-C");
		Assert.That(call.Expiry, Is.EqualTo(new DateOnly(2025, 12, 26)));
		Assert.That(call.Strike, Is.EqualTo(95000m));
		Assert.That(call.Type, Is.EqualTo(OptionType.Call));
		Assert.That(call.MarkPrice, Is.EqualTo(5160.25m));
		Assert.That(call.MarkIv, Is.EqualTo(0.4865m));
		Assert.That(call.Delta, Is.EqualTo(0.6211m));
		Assert.That(call.Gamma, Is.EqualTo(0.0000421m));
		Assert.That(call.Vega, Is.EqualTo(168.5012m));
		Assert.That(call.Theta, Is.EqualTo(-88.7210m));
		Assert.That(call.OpenInterest, Is.EqualTo(4210m));
		Assert.That(call.Bid1Price, Is.EqualTo(5100.5m));
		Assert.That(call.Bid1Size, Is.EqualTo(3m));
		Assert.That(call.Ask1Price, Is.EqualTo(5220.0m));
		Assert.That(call.Ask1Size, Is.EqualTo(2m));

		var put = board.Quotes.Single(quote => quote.Symbol == "BTC-27MAR26-120000-P");
		Assert.That(put.Expiry, Is.EqualTo(new DateOnly(2026, 3, 27)));
		Assert.That(put.Strike, Is.EqualTo(120000m));
		Assert.That(put.Type, Is.EqualTo(OptionType.Put));
	}

	[TestMethod]
	[Description("Ошибка биржи retCode не выбрасывается наружу: снимок помечен недоступным с причиной")]
	public async Task ReadSnapshotAsync_BybitApiError_ReturnsUnavailableSnapshot()
	{
		// Arrange: биржа отвечает ошибкой конверта retCode 10001 — клиент бросает
		// BybitApiException, адаптер обязан вернуть управляемую недоступность.
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error","result":{"category":"linear","list":[]},"retExt":{},"time":1}""");

		var reader = CreateReader(handler);

		// Act: читаем снимок вопреки ошибке биржи.
		var snapshot = await reader.ReadSnapshotAsync("BTC");

		// Assert: исключение не вышло наружу, причина сбоя сохранена в записи.
		Assert.That(handler.Requests.Count, Is.EqualTo(1));
		Assert.That(snapshot.IsAvailable, Is.False);
		Assert.That(snapshot.UnavailableReason, Does.Contain("10001"));
		Assert.That(snapshot.UnavailableReason, Does.Contain("params error"));
	}

	/// <summary>Создаёт адаптер над клиентом тикеров с фиктивным транспортом и фиксированным временем.</summary>
	private static BybitConsultationMarketReader CreateReader(ScriptedHttpMessageHandler handler)
	{
		var tickersClient = new BybitTickersClient(
			new HttpClient(handler) { BaseAddress = new Uri(TestBaseUrl) },
			new BybitClientOptions { BaseUrl = TestBaseUrl });
		return new BybitConsultationMarketReader(tickersClient, new FixedTimeProvider(FixedNow));
	}

	#region Помощники

	private static string LoadFixture(string relativePath)
	{
		// Зафиксированные ответы лежат рядом с тестовой сборкой: доски консультаций —
		// в Consultations/Fixtures, общие ответы клиента тикеров — в Bybit/Fixtures.
		return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, relativePath));
	}

	#endregion
}
