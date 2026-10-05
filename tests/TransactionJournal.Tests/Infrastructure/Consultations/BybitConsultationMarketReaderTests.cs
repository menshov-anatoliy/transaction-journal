using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Infrastructure.Consultations;
using TransactionJournal.Infrastructure.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using OptionType = TransactionJournal.Domain.Materialization.OptionType;

namespace TransactionJournal.Tests.Infrastructure.Consultations;

using TransactionJournal.Tests.Infrastructure.Bybit;

/// <summary>
/// Проверки Bybit-адаптера рыночных данных консультаций против зафиксированных
/// HTTP-ответов: каждое чтение порта выполняет ровно один запрос к публичному
/// эндпоинту тикеров, снимок — линейным запросом по символу перпа, доска —
/// опционным запросом с фильтром baseCoin, ошибка биржи превращается в
/// недоступную запись с причиной вместо исключения, а недоступность деградирует
/// в кэш марок провайдера — последняя известная проекция с её as-of.
/// </summary>
[TestClass]
public sealed class BybitConsultationMarketReaderTests
{
	/// <summary>Тестовый адрес API Bybit без завершающего слэша.</summary>
	private const string TestBaseUrl = "http://bybit-test.local";

	/// <summary>Фиксированный момент as-of для детерминированных проверок времени.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Момент получения кэшированных марок для проверок деградации.</summary>
	private static readonly DateTimeOffset CachedAt = new(2029, 12, 31, 10, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой кэша марок во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"consultation-market-reader-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база и соседние WAL/SHM-файлы удаляются после каждой проверки.
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

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

	[TestMethod]
	[Description("Сбой биржи деградирует снимок в кэш: последняя марка перпа с моментом её получения в роли as-of")]
	public async Task ReadSnapshotAsync_BybitApiError_ReturnsCachedPerpMarkWithItsAsOf()
	{
		// Arrange: кэш марок уже знает последнюю марку перпа BTCUSDT, биржа
		// отвечает ошибкой конверта retCode.
		SeedCache(("BTCUSDT", 106000m, CachedAt));
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error","result":{"category":"linear","list":[]},"retExt":{},"time":1}""");

		var reader = CreateReader(handler);

		// Act: читаем снимок вопреки сбою биржи.
		var snapshot = await reader.ReadSnapshotAsync("BTC");

		// Assert: недоступность + последняя кэшированная марка с as-of кэша;
		// биржевой запрос по-прежнему ровно один.
		// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
		Assert.That(handler.Requests.Count, Is.EqualTo(1));
		Assert.That(snapshot.IsAvailable, Is.False);
		Assert.That(snapshot.UnavailableReason, Does.Contain("params error"));
		Assert.That(snapshot.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(snapshot.MarkPrice, Is.EqualTo(106000m));
		Assert.That(snapshot.AsOf, Is.EqualTo(CachedAt));
	}

	[TestMethod]
	[Description("Сбой биржи без кэшированной марки перпа отдаёт недоступность без марки, as-of — момент сбоя")]
	public async Task ReadSnapshotAsync_BybitApiErrorWithoutCache_ReturnsUnavailableWithoutMark()
	{
		// Arrange: кэш марок пуст, биржа отвечает ошибкой.
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error","result":{"category":"linear","list":[]},"retExt":{},"time":1}""");

		var reader = CreateReader(handler);

		// Act: читаем снимок вопреки сбою биржи.
		var snapshot = await reader.ReadSnapshotAsync("BTC");

		// Assert: деградировать нечем — марки нет, as-of совпадает с моментом сбоя.
		// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
		Assert.That(snapshot.IsAvailable, Is.False);
		Assert.That(snapshot.Symbol, Is.Null);
		Assert.That(snapshot.MarkPrice, Is.Null);
		Assert.That(snapshot.AsOf, Is.EqualTo(FixedNow));
	}

	[TestMethod]
	[Description("Сбой биржи деградирует доску в кэш: марки опционов актива с as-of самой старой марки, чужие и нечитаемые символы отброшены")]
	public async Task ReadOptionBoardAsync_BybitApiError_ReturnsCachedOptionMarksWithTheirAsOf()
	{
		// Arrange: кэш несёт марку перпа-якоря, две опционные марки BTC и шум —
		// опцион чужого актива и нечитаемый символ с префиксом BTC-.
		SeedCache(
			("BTCUSDT", 106000m, new DateTimeOffset(2029, 12, 31, 10, 30, 0, TimeSpan.Zero)),
			("BTC-26DEC25-95000-C", 5160.25m, new DateTimeOffset(2029, 12, 31, 10, 0, 0, TimeSpan.Zero)),
			("BTC-26DEC25-95000-P", 626.10m, new DateTimeOffset(2029, 12, 31, 9, 30, 0, TimeSpan.Zero)),
			("ETH-26DEC25-4000-C", 180m, new DateTimeOffset(2029, 12, 31, 9, 0, 0, TimeSpan.Zero)),
			("BTC-NOTANOPTION", 1m, new DateTimeOffset(2029, 12, 31, 8, 0, 0, TimeSpan.Zero)));
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error","result":{"category":"option","list":[]},"retExt":{},"time":1}""");

		var reader = CreateReader(handler);

		// Act: читаем доску вопреки сбою биржи.
		var board = await reader.ReadOptionBoardAsync("BTC");

		// Assert: недоступность + кэшированные марки только опционов BTC; as-of —
		// момент получения самой старой марки проекции; IV, греки и бид-аск кэш
		// не хранит; биржевой запрос по-прежнему ровно один.
		// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
		Assert.That(handler.Requests.Count, Is.EqualTo(1));
		Assert.That(board.IsAvailable, Is.False);
		Assert.That(board.UnavailableReason, Does.Contain("params error"));
		Assert.That(board.UnderlyingPrice, Is.EqualTo(106000m));
		Assert.That(board.AsOf, Is.EqualTo(new DateTimeOffset(2029, 12, 31, 9, 30, 0, TimeSpan.Zero)));
		Assert.That(board.TotalTickerCount, Is.EqualTo(2));
		Assert.That(board.Quotes, Has.Count.EqualTo(2));

		var call = board.Quotes.Single(quote => quote.Type == OptionType.Call);
		Assert.That(call.Symbol, Is.EqualTo("BTC-26DEC25-95000-C"));
		Assert.That(call.Expiry, Is.EqualTo(new DateOnly(2025, 12, 26)));
		Assert.That(call.Strike, Is.EqualTo(95000m));
		Assert.That(call.MarkPrice, Is.EqualTo(5160.25m));
		Assert.That(call.MarkIv, Is.Null);
		Assert.That(call.Delta, Is.Null);
		Assert.That(call.OpenInterest, Is.Null);
		Assert.That(call.Bid1Price, Is.Null);
	}

	[TestMethod]
	[Description("Сбой биржи без кэшированных марок опционов отдаёт недоступность с пустой доской")]
	public async Task ReadOptionBoardAsync_BybitApiErrorWithoutCache_ReturnsUnavailableEmptyBoard()
	{
		// Arrange: кэш марок пуст, биржа отвечает ошибкой.
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error","result":{"category":"option","list":[]},"retExt":{},"time":1}""");

		var reader = CreateReader(handler);

		// Act: читаем доску вопреки сбою биржи.
		var board = await reader.ReadOptionBoardAsync("BTC");

		// Assert: деградировать нечем — проекция пуста, as-of совпадает с моментом сбоя.
		// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
		Assert.That(board.IsAvailable, Is.False);
		Assert.That(board.UnderlyingPrice, Is.Null);
		Assert.That(board.TotalTickerCount, Is.EqualTo(0));
		Assert.That(board.Quotes, Is.Empty);
		Assert.That(board.AsOf, Is.EqualTo(FixedNow));
	}

	[TestMethod]
	[Description("Транспортный сбой биржи деградирует снимок в кэш так же, как ошибка конверта")]
	public async Task ReadSnapshotAsync_TransportOutage_ReturnsCachedPerpMarkWithItsAsOf()
	{
		// Arrange: биржа физически недоступна (отказ соединения) — транспорт кидает
		// HttpRequestException, resilience исчерпывает повторы и отдаёт его наружу.
		SeedCache(("BTCUSDT", 106000m, CachedAt));
		var handler = new ScriptedHttpMessageHandler();
		handler.SetResponder(_ => throw new HttpRequestException("No connection could be made because the target machine actively refused it."));

		var reader = CreateReader(handler, new BybitResilienceOptions { NetworkRetryCount = 1 });

		// Act: читаем снимок при недоступной бирже.
		var snapshot = await reader.ReadSnapshotAsync("BTC");

		// Assert: исключение не вышло наружу — управляемая недоступность с кэшем
		// и его as-of; попыток две (исходная + один повтор сети resilience),
		// второй биржевой запрос ридер сам не инициирует.
		// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
		Assert.That(handler.Requests.Count, Is.EqualTo(2));
		Assert.That(snapshot.IsAvailable, Is.False);
		Assert.That(snapshot.UnavailableReason, Does.Contain("actively refused"));
		Assert.That(snapshot.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(snapshot.MarkPrice, Is.EqualTo(106000m));
		Assert.That(snapshot.AsOf, Is.EqualTo(CachedAt));
	}

	[TestMethod]
	[Description("Транспортный сбой биржи деградирует доску в кэш так же, как ошибка конверта")]
	public async Task ReadOptionBoardAsync_TransportOutage_ReturnsCachedOptionMarksWithTheirAsOf()
	{
		// Arrange: кэш несёт марку перпа-якоря и опционную марку BTC, биржа
		// физически недоступна — транспорт кидает HttpRequestException.
		SeedCache(
			("BTCUSDT", 106000m, new DateTimeOffset(2029, 12, 31, 10, 30, 0, TimeSpan.Zero)),
			("BTC-26DEC25-95000-C", 5160.25m, new DateTimeOffset(2029, 12, 31, 10, 0, 0, TimeSpan.Zero)));
		var handler = new ScriptedHttpMessageHandler();
		handler.SetResponder(_ => throw new HttpRequestException("No connection could be made because the target machine actively refused it."));

		var reader = CreateReader(handler, new BybitResilienceOptions { NetworkRetryCount = 1 });

		// Act: читаем доску при недоступной бирже.
		var board = await reader.ReadOptionBoardAsync("BTC");

		// Assert: управляемая недоступность с кэшированной проекцией и её as-of.
		// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
		Assert.That(board.IsAvailable, Is.False);
		Assert.That(board.UnavailableReason, Does.Contain("actively refused"));
		Assert.That(board.UnderlyingPrice, Is.EqualTo(106000m));
		Assert.That(board.AsOf, Is.EqualTo(new DateTimeOffset(2029, 12, 31, 10, 0, 0, TimeSpan.Zero)));
		Assert.That(board.TotalTickerCount, Is.EqualTo(1));
		Assert.That(board.Quotes.Single().Symbol, Is.EqualTo("BTC-26DEC25-95000-C"));
	}

	/// <summary>Создаёт адаптер над клиентом тикеров с фиктивным транспортом, кэшем марок и фиксированным временем.</summary>
	private BybitConsultationMarketReader CreateReader(
		ScriptedHttpMessageHandler handler,
		BybitResilienceOptions? resilienceOptions = null)
	{
		var tickersClient = new BybitTickersClient(
			new HttpClient(handler) { BaseAddress = new Uri(TestBaseUrl) },
			new BybitClientOptions { BaseUrl = TestBaseUrl },
			resilienceOptions);
		return new BybitConsultationMarketReader(tickersClient, CreateOptions(), new FixedTimeProvider(FixedNow));
	}

	#region Помощники

	/// <summary>Опции контекста журнала над временной базой кэша марок.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Складывает марки в кэш провайдера: символ, цена, момент получения.</summary>
	private void SeedCache(params (string Symbol, decimal Price, DateTimeOffset ReceivedAt)[] marks)
	{
		using var db = new JournalDbContext(CreateOptions());
		foreach (var mark in marks)
		{
			db.InstrumentMarks.Add(new InstrumentMark
			{
				Symbol = mark.Symbol,
				MarkPrice = mark.Price,
				ReceivedAt = mark.ReceivedAt,
			});
		}

		db.SaveChanges();
	}

	/// <summary>Читает зафиксированный ответ биржи рядом с тестовой сборкой.</summary>
	private static string LoadFixture(string relativePath)
	{
		// Зафиксированные ответы лежат рядом с тестовой сборкой: доски консультаций —
		// в Consultations/Fixtures, общие ответы клиента тикеров — в Bybit/Fixtures.
		return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, relativePath));
	}

	#endregion
}
