using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Infrastructure.Analytics;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Tests.Infrastructure.Bybit;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.Analytics;

/// <summary>
/// Проверки провайдера котировок на стыке публичных тикеров и хранилища журнала:
/// свежая котировка штампует кэш ценой и временем получения, последняя известная
/// котировка служит дефолтом ручной пометки закрытия без цены пользователя, а сбои
/// биржи не вытирают уже известную котировку. Негативные проверки отказывают пустой
/// инструмент и конструирование без зависимостей.
/// </summary>
[TestClass]
public class InstrumentQuoteProviderTests
{
	private const string TestBaseUrl = "http://bybit-test.local";
	private const string LinearSymbol = "BTCUSDT";
	private const string OptionSymbol = "BTC-25JUN27-106000-P-USDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private ManualTimeProvider _clock = null!;
	private ScriptedHttpMessageHandler _handler = null!;
	private BybitTickersClient _tickersClient = null!;
	private InstrumentQuoteProvider _provider = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-quote-cache-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstrumentCatalog(db);
		}

		// Виртуальные часы детерминируют штамп времени кэша; scripted-транспорт
		// отвечает зафиксированными телами тикеров без сетевых вызовов.
		_clock = new ManualTimeProvider();
		_handler = new ScriptedHttpMessageHandler(_clock);
		_tickersClient = new BybitTickersClient(
			new HttpClient(_handler),
			new BybitClientOptions { BaseUrl = TestBaseUrl },
			timeProvider: _clock);
		_provider = new InstrumentQuoteProvider(CreateOptions(), _tickersClient, _clock);
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
	[Description("Свежая котировка штампует кэш ценой и временем получения; новое получение обновляет ту же строку")]
	public async Task TryIfFreshMarkStampesCacheWithPriceAndReceivedTime()
	{
		// Arrange: справочник знает линейный перп; биржа отдаёт котировку 27739.74,
		// виртуальные часы стоят на старте.
		var start = _clock.GetUtcNow();
		_handler.EnqueueJson(TickersBody("27739.74"));

		// Act: запрашиваем свежую котировку инструмента.
		var first = await _provider.GetFreshQuoteAsync(LinearSymbol);

		// Assert: котировка вернулась со временем получения по часам провайдера,
		// а кэш сохранил и цену, и штамп времени получения — сценарий
		// «кэш котировок штампуется временем».
		// Traceability: openspec:analytics/performance#scenario-mark-cache-timestamped
		Assert.That(first, Is.Not.Null);
		Assert.That(first!.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(first.QuotePrice, Is.EqualTo(27739.74m));
		Assert.That(first.ReceivedAt, Is.EqualTo(start));
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var cached = await db.InstrumentQuotes.SingleAsync();
			Assert.That(cached.Symbol, Is.EqualTo(LinearSymbol));
			Assert.That(cached.QuotePrice, Is.EqualTo(27739.74m));
			Assert.That(cached.ReceivedAt, Is.EqualTo(start));
		}

		// Act: время идёт, биржа отдаёт новую котировку — кэш обновляется той же строкой.
		_clock.Advance(TimeSpan.FromMinutes(2));
		var beforeSecondFetch = _clock.GetUtcNow();
		_handler.EnqueueJson(TickersBody("28000.10"));
		var second = await _provider.GetFreshQuoteAsync(LinearSymbol);

		// Assert: строка кэша одна, цена и штамп времени заменены на новые.
		Assert.That(second!.QuotePrice, Is.EqualTo(28000.10m));
		Assert.That(second.ReceivedAt, Is.GreaterThan(first.ReceivedAt));
		Assert.That(second.ReceivedAt, Is.GreaterThanOrEqualTo(beforeSecondFetch));
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var cached = await db.InstrumentQuotes.SingleAsync();
			Assert.That(cached.QuotePrice, Is.EqualTo(28000.10m));
			Assert.That(cached.ReceivedAt, Is.EqualTo(second.ReceivedAt));
		}
	}

	[TestMethod]
	[Description("Свежий тикер опциона дописывает строку истории снимков: котировка, индекс, тета, момент получения")]
	public async Task TryIfFreshOptionTickerAppendsMarketSnapshotHistoryRow()
	{
		// Arrange: справочник знает опционный символ; биржа отдаёт котировку, индексную
		// цену и тету, виртуальные часы стоят на старте.
		var start = _clock.GetUtcNow();
		_handler.EnqueueJson(OptionTickersBody("472.10", "103850.55", "-19.40"));

		// Act: запрашиваем свежую котировку опциона.
		var fresh = await _provider.GetFreshQuoteAsync(OptionSymbol);

		// Assert: возвращённый снимок несёт nullable-поля теты и индекса, а история
		// рыночных снимков пополнилась строкой со всеми величинами и штампом
		// момента получения — сценарий «свежие тикеры создают снимок истории».
		// Traceability: openspec:analytics/option-lifetime#scenario-snapshot-written-on-fresh-tickers
		Assert.That(fresh, Is.Not.Null);
		Assert.That(fresh!.Symbol, Is.EqualTo(OptionSymbol));
		Assert.That(fresh.QuotePrice, Is.EqualTo(472.10m));
		Assert.That(fresh.UnderlyingPrice, Is.EqualTo(103850.55m));
		Assert.That(fresh.Theta, Is.EqualTo(-19.40m));
		Assert.That(fresh.ReceivedAt, Is.EqualTo(start));
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var snapshot = await db.InstrumentMarketSnapshots.SingleAsync();
			Assert.That(snapshot.Symbol, Is.EqualTo(OptionSymbol));
			Assert.That(snapshot.QuotePrice, Is.EqualTo(472.10m));
			Assert.That(snapshot.UnderlyingPrice, Is.EqualTo(103850.55m));
			Assert.That(snapshot.Theta, Is.EqualTo(-19.40m));
			Assert.That(snapshot.ReceivedAt, Is.EqualTo(start));
		}

		// Act: время идёт, биржа отвечает новыми значениями — история append-only:
		// вторая строка дописывается, прежняя остаётся доступной для сравнения.
		_clock.Advance(TimeSpan.FromHours(6));
		var later = _clock.GetUtcNow();
		_handler.EnqueueJson(OptionTickersBody("470.00", "103900.00", "-20.15"));
		await _provider.GetFreshQuoteAsync(OptionSymbol);

		// Assert: в истории две строки с разными моментами получения, первая не
		// перезаписана — сценарий «история накапливается без очистки».
		// Traceability: openspec:analytics/option-lifetime#scenario-snapshot-history-append-only
		using (var db = new JournalDbContext(CreateOptions()))
		{
			// SQLite не сортирует DateTimeOffset на стороне базы — упорядочиваем на клиенте.
			var history = (await db.InstrumentMarketSnapshots.ToListAsync())
				.OrderBy(snapshot => snapshot.ReceivedAt)
				.ToList();
			Assert.That(history, Has.Count.EqualTo(2));
			Assert.That(history[0].QuotePrice, Is.EqualTo(472.10m));
			Assert.That(history[1].QuotePrice, Is.EqualTo(470.00m));
			Assert.That(history[1].Theta, Is.EqualTo(-20.15m));
			Assert.That(history[1].ReceivedAt, Is.EqualTo(later));
		}
	}

	[TestMethod]
	[Description("Свежая котировка линейного перпа тоже дописывает историю снимков: индекс и тета пусты")]
	public async Task TryIfFreshLinearMarkAppendsSnapshotWithoutOptionFields()
	{
		// Arrange: биржа отдаёт только котировку линейного перпа — поля опциона пустые.
		_handler.EnqueueJson(TickersBody("27739.74"));

		// Act: запрашиваем свежую котировку.
		var fresh = await _provider.GetFreshQuoteAsync(LinearSymbol);

		// Assert: снимок в истории дописан с пустыми nullable-полями индекса и
		// теты: строка фиксирует сам факт выборки любого инструмента провайдера,
		// а опционные поля остаются прочерком.
		// Traceability: openspec:analytics/option-lifetime#requirement-market-snapshot-history
		Assert.That(fresh, Is.Not.Null);
		Assert.That(fresh!.UnderlyingPrice, Is.Null);
		Assert.That(fresh.Theta, Is.Null);
		using var db = new JournalDbContext(CreateOptions());
		var snapshot = await db.InstrumentMarketSnapshots.SingleAsync();
		Assert.That(snapshot.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(snapshot.QuotePrice, Is.EqualTo(27739.74m));
		Assert.That(snapshot.UnderlyingPrice, Is.Null);
		Assert.That(snapshot.Theta, Is.Null);
	}

	[TestMethod]
	[Description("Пустой ответ биржи не пополняет историю снимков и сохраняет кэш последней котировки")]
	public async Task TryIfMissingExchangeMarkLeavesHistoryAndCacheIntact()
	{
		// Arrange: одна успешная выборка уже создала строку истории и штамп кэша;
		// следующий ответ биржи — пустая котировка опционного инструмента.
		_handler.EnqueueJson(OptionTickersBody("472.10", "103850.55", "-19.40"));
		await _provider.GetFreshQuoteAsync(OptionSymbol);
		_handler.EnqueueJson(OptionMissingQuoteBody());

		// Act: запрашиваем свежую котировку — биржа значения не отдала.
		var fresh = await _provider.GetFreshQuoteAsync(OptionSymbol);

		// Assert: свежей котировки нет, история не пополнилась, а кэш хранит последнюю
		// известную котировку — сценарий «сбой тикеров не пополняет историю».
		// Traceability: openspec:analytics/option-lifetime#scenario-snapshot-not-written-on-failure
		Assert.That(fresh, Is.Null);
		Assert.That(await _provider.GetLastQuoteAsync(OptionSymbol), Is.EqualTo(472.10m));
		using var db = new JournalDbContext(CreateOptions());
		var history = await db.InstrumentMarketSnapshots.ToListAsync();
		Assert.That(history, Has.Count.EqualTo(1));
		Assert.That(history.Single().QuotePrice, Is.EqualTo(472.10m));
	}

	[TestMethod]
	[Description("Последняя известная котировка из кэша служит дефолтом ручной пометки закрытия без цены")]
	public async Task TryIfLastKnownMarkServesAsManualCloseMarkDefault()
	{
		// Arrange: кэш провайдера уже несёт котировку 27739.74; у конструкции — позиция
		// линейного перпа и ручная пометка закрытия без цены пользователя.
		_handler.EnqueueJson(TickersBody("27739.74"));
		await _provider.GetFreshQuoteAsync(LinearSymbol);
		var construction = await new ConstructionService(CreateOptions()).CreateAsync("Диапазон BTC", 1000m);
		await AddLinearTradeAsync("exec-buy-1", "Buy", "0.01");
		await new TradeBindingService(CreateOptions()).BindAsync(construction.Id, "exec-buy-1");
		await new ManualCloseAnnotationService(CreateOptions()).AddAsync(
			construction.Id, LinearSymbol, new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero));

		// Act: читаем позиции с провайдером котировок в роли источника дефолта.
		var readModel = new PositionReadModel(CreateOptions(), _provider);
		var result = await readModel.ListAsync();

		// Assert: ручная пометка закрыла позицию ценой последней известной котировки
		// из кэша провайдера — сценарий «последняя известная котировка служит дефолтом
		// ручной пометки». Дефолт читается из кэша: новых сетевых запросов нет.
		// Traceability: openspec:analytics/performance#scenario-last-known-mark-serves-manual-default
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.Kind, Is.EqualTo(PositionClosingKind.ManualAnnotation));
		Assert.That(entry.Price, Is.EqualTo(27739.74m));
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0m));
		Assert.That(position.IsOpen, Is.False);
		Assert.That(_handler.Requests, Has.Count.EqualTo(1));
		Assert.That(await _provider.GetLastQuoteAsync(LinearSymbol), Is.EqualTo(27739.74m));
	}

	[TestMethod]
	[Description("Свежая котировка неизвестного справочнику инструмента не запрашивается и не кэшируется")]
	public async Task TryIfFreshMarkSkipsRequestForUnknownSymbol()
	{
		// Arrange: справочник не знает ETHUSDT — категорию запроса тикеров не построить.

		// Act: запрашиваем свежую котировку и последнюю известную.
		var fresh = await _provider.GetFreshQuoteAsync("ETHUSDT");
		var last = await _provider.GetLastQuoteAsync("ETHUSDT");

		// Assert: обе котировки неизвестны, сетевых запросов не было, кэш пуст.
		Assert.That(fresh, Is.Null);
		Assert.That(last, Is.Null);
		Assert.That(_handler.Requests, Is.Empty);
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(await db.InstrumentQuotes.CountAsync(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Биржа без котировки не трогает кэш: последняя известная котировка остаётся прежней")]
	public async Task TryIfMissingExchangeMarkLeavesCacheIntact()
	{
		// Arrange: кэш уже несёт котировку, а биржа отвечает пустой котировкой инструмента.
		_handler.EnqueueJson(TickersBody("27739.74"));
		await _provider.GetFreshQuoteAsync(LinearSymbol);
		_handler.EnqueueJson(MissingQuoteBody());

		// Act: запрашиваем свежую котировку — биржа значения не отдала.
		var fresh = await _provider.GetFreshQuoteAsync(LinearSymbol);

		// Assert: свежей котировки нет, но последняя известная котировка в кэше не изменилась.
		Assert.That(fresh, Is.Null);
		Assert.That(await _provider.GetLastQuoteAsync(LinearSymbol), Is.EqualTo(27739.74m));
	}

	[TestMethod]
	[Description("Ошибка тикеров поднимается наружу, не вытирая последнюю известную котировку из кэша")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnTickersFailureKeepsLastKnownMarkInCache()
	{
		// Arrange: кэш уже несёт котировку; следующий ответ биржи — ошибка конверта retCode.
		_handler.EnqueueJson(TickersBody("27739.74"));
		_provider.GetFreshQuoteAsync(LinearSymbol).GetAwaiter().GetResult();
		_handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error","result":{}}""");

		// Act — ошибка биржи поднимается вызывающему слою: решение о деградации
		// остаётся за ним, а не за провайдером.
		try
		{
			_provider.GetFreshQuoteAsync(LinearSymbol).GetAwaiter().GetResult();
		}
		catch (BybitApiException)
		{
			// Assert: сбой не вытер кэш — последняя известная котировка по-прежнему
			// доступна читающим слоям без сети.
			Assert.That(_provider.GetLastQuoteAsync(LinearSymbol).GetAwaiter().GetResult(), Is.EqualTo(27739.74m));
			throw;
		}
	}

	[TestMethod]
	[Description("Свежая котировка без инструмента отклоняется до запроса к бирже")]
	[DataRow("")]
	[DataRow(" ")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnFreshMarkWithoutSymbol(string symbol)
	{
		// Act — пустой инструмент прерывается аргумент-исключением.
		try
		{
			_provider.GetFreshQuoteAsync(symbol).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Свежая котировка без самого инструмента отклоняется до запроса к бирже")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnFreshMarkNotProvided()
	{
		// Act — отсутствие инструмента прерывается до сетевого вызова.
		try
		{
			_provider.GetFreshQuoteAsync(null!).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Последняя известная котировка без инструмента отклоняется без обращения к базе")]
	[DataRow("")]
	[DataRow(" ")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnLastMarkWithoutSymbol(string symbol)
	{
		// Act — пустой инструмент прерывается аргумент-исключением.
		try
		{
			_provider.GetLastQuoteAsync(symbol).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Последняя известная котировка без самого инструмента отклоняется без обращения к базе")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnLastMarkNotProvided()
	{
		// Act — отсутствие инструмента прерывается до обращения к базе.
		try
		{
			_provider.GetLastQuoteAsync(null!).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Провайдер без опций контекста или клиента тикеров не создаётся")]
	[DataRow(true, false)]
	[DataRow(false, true)]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnProviderCreatedWithoutDependencies(bool skipOptions, bool skipClient)
	{
		// Act — отсутствие обязательной зависимости прерывается аргумент-исключением.
		var options = skipOptions ? null! : CreateOptions();
		var client = skipClient ? null! : _tickersClient;
		new InstrumentQuoteProvider(options, client);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Справочник инструментов: линейный перп BTCUSDT и опцион доски BTC.</summary>
	private static void SeedInstrumentCatalog(JournalDbContext db)
	{
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson = """{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = OptionSymbol,
			Category = "option",
			PayloadJson = """{"symbol":"BTC-25JUN27-106000-P-USDT","optionsType":"Put","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"1811481600000"}""",
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Добавляет сырую запись исполнения линейного перпа BTCUSDT.</summary>
	private async Task AddLinearTradeAsync(string execId, string side, string execQty)
	{
		var execTimeMs = new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "linear",
			Symbol = LinearSymbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{LinearSymbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"0.0001","execId":"{{execId}}","execPrice":"42000","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"BTC","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Успешный ответ тикеров с котировкой линейного перпа.</summary>
	private static string TickersBody(string quotePrice) =>
		"{\"retCode\":0,\"retMsg\":\"OK\",\"result\":{\"category\":\"linear\",\"list\":[{\"symbol\":\"BTCUSDT\",\"markPrice\":\"" + quotePrice + "\"}]}}";

	/// <summary>Успешный ответ тикеров опциона: котировка, индексная цена и тета; пустая строка — значения нет.</summary>
	private static string OptionTickersBody(string quotePrice, string underlyingPrice, string theta) =>
		"{\"retCode\":0,\"retMsg\":\"OK\",\"result\":{\"category\":\"option\",\"list\":[{\"symbol\":\"" + OptionSymbol
		+ "\",\"markPrice\":\"" + quotePrice + "\",\"underlyingPrice\":\"" + underlyingPrice + "\",\"theta\":\"" + theta + "\"}]}}";

	/// <summary>Успешный ответ тикеров с пустой котировкой инструмента: биржа значения не отдала.</summary>
	private static string MissingQuoteBody() =>
		"""{"retCode":0,"retMsg":"OK","result":{"category":"linear","list":[{"symbol":"BTCUSDT","markPrice":""}]}}""";

	/// <summary>Успешный ответ тикеров опциона с пустой котировкой: биржа значения не отдала.</summary>
	private static string OptionMissingQuoteBody() =>
		"""{"retCode":0,"retMsg":"OK","result":{"category":"option","list":[{"symbol":"BTC-25JUN27-106000-P-USDT","markPrice":""}]}}""";

	#endregion
}
