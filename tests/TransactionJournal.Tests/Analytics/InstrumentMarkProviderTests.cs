using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Tests.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки провайдера марок на стыке публичных тикеров и хранилища журнала:
/// свежая марка штампует кэш ценой и временем получения, последняя известная
/// марка служит дефолтом ручной пометки закрытия без цены пользователя, а сбои
/// биржи не вытирают уже известную марку. Негативные проверки отказывают пустой
/// инструмент и конструирование без зависимостей.
/// </summary>
[TestClass]
public class InstrumentMarkProviderTests
{
	private const string TestBaseUrl = "http://bybit-test.local";
	private const string LinearSymbol = "BTCUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private ManualTimeProvider _clock = null!;
	private ScriptedHttpMessageHandler _handler = null!;
	private BybitTickersClient _tickersClient = null!;
	private InstrumentMarkProvider _provider = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-mark-cache-tests-{Guid.NewGuid():N}.db");
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
		_provider = new InstrumentMarkProvider(CreateOptions(), _tickersClient, _clock);
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
	[Description("Свежая марка штампует кэш ценой и временем получения; новое получение обновляет ту же строку")]
	public async Task TryIfFreshMarkStampesCacheWithPriceAndReceivedTime()
	{
		// Arrange: справочник знает линейный перп; биржа отдаёт марку 27739.74,
		// виртуальные часы стоят на старте.
		var start = _clock.GetUtcNow();
		_handler.EnqueueJson(TickersBody("27739.74"));

		// Act: запрашиваем свежую марку инструмента.
		var first = await _provider.GetFreshMarkAsync(LinearSymbol);

		// Assert: марка вернулась со временем получения по часам провайдера,
		// а кэш сохранил и цену, и штамп времени получения — сценарий
		// «кэш марок штампуется временем».
		// Traceability: openspec:analytics/performance#scenario-mark-cache-timestamped
		Assert.That(first, Is.Not.Null);
		Assert.That(first!.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(first.MarkPrice, Is.EqualTo(27739.74m));
		Assert.That(first.ReceivedAt, Is.EqualTo(start));
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var cached = await db.InstrumentMarks.SingleAsync();
			Assert.That(cached.Symbol, Is.EqualTo(LinearSymbol));
			Assert.That(cached.MarkPrice, Is.EqualTo(27739.74m));
			Assert.That(cached.ReceivedAt, Is.EqualTo(start));
		}

		// Act: время идёт, биржа отдаёт новую марку — кэш обновляется той же строкой.
		_clock.Advance(TimeSpan.FromMinutes(2));
		var beforeSecondFetch = _clock.GetUtcNow();
		_handler.EnqueueJson(TickersBody("28000.10"));
		var second = await _provider.GetFreshMarkAsync(LinearSymbol);

		// Assert: строка кэша одна, цена и штамп времени заменены на новые.
		Assert.That(second!.MarkPrice, Is.EqualTo(28000.10m));
		Assert.That(second.ReceivedAt, Is.GreaterThan(first.ReceivedAt));
		Assert.That(second.ReceivedAt, Is.GreaterThanOrEqualTo(beforeSecondFetch));
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var cached = await db.InstrumentMarks.SingleAsync();
			Assert.That(cached.MarkPrice, Is.EqualTo(28000.10m));
			Assert.That(cached.ReceivedAt, Is.EqualTo(second.ReceivedAt));
		}
	}

	[TestMethod]
	[Description("Последняя известная марка из кэша служит дефолтом ручной пометки закрытия без цены")]
	public async Task TryIfLastKnownMarkServesAsManualCloseMarkDefault()
	{
		// Arrange: кэш провайдера уже несёт марку 27739.74; у конструкции — позиция
		// линейного перпа и ручная пометка закрытия без цены пользователя.
		_handler.EnqueueJson(TickersBody("27739.74"));
		await _provider.GetFreshMarkAsync(LinearSymbol);
		var construction = await new ConstructionService(CreateOptions()).CreateAsync("Диапазон BTC", 1000m);
		await AddLinearTradeAsync("exec-buy-1", "Buy", "0.01");
		await new TradeBindingService(CreateOptions()).BindAsync(construction.Id, "exec-buy-1");
		await new ManualCloseMarkService(CreateOptions()).AddAsync(
			construction.Id, LinearSymbol, new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero));

		// Act: читаем позиции с провайдером марок в роли источника дефолта.
		var readModel = new PositionReadModel(CreateOptions(), _provider);
		var result = await readModel.ListAsync();

		// Assert: ручная пометка закрыла позицию ценой последней известной марки
		// из кэша провайдера — сценарий «последняя известная марка служит дефолтом
		// ручной пометки». Дефолт читается из кэша: новых сетевых запросов нет.
		// Traceability: openspec:analytics/performance#scenario-last-known-mark-serves-manual-default
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(entry.Price, Is.EqualTo(27739.74m));
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0m));
		Assert.That(position.IsOpen, Is.False);
		Assert.That(_handler.Requests, Has.Count.EqualTo(1));
		Assert.That(await _provider.GetLastMarkAsync(LinearSymbol), Is.EqualTo(27739.74m));
	}

	[TestMethod]
	[Description("Свежая марка неизвестного справочнику инструмента не запрашивается и не кэшируется")]
	public async Task TryIfFreshMarkSkipsRequestForUnknownSymbol()
	{
		// Arrange: справочник не знает ETHUSDT — категорию запроса тикеров не построить.

		// Act: запрашиваем свежую марку и последнюю известную.
		var fresh = await _provider.GetFreshMarkAsync("ETHUSDT");
		var last = await _provider.GetLastMarkAsync("ETHUSDT");

		// Assert: обе марки неизвестны, сетевых запросов не было, кэш пуст.
		Assert.That(fresh, Is.Null);
		Assert.That(last, Is.Null);
		Assert.That(_handler.Requests, Is.Empty);
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(await db.InstrumentMarks.CountAsync(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Биржа без марки не трогает кэш: последняя известная марка остаётся прежней")]
	public async Task TryIfMissingExchangeMarkLeavesCacheIntact()
	{
		// Arrange: кэш уже несёт марку, а биржа отвечает пустой маркой инструмента.
		_handler.EnqueueJson(TickersBody("27739.74"));
		await _provider.GetFreshMarkAsync(LinearSymbol);
		_handler.EnqueueJson(MissingMarkBody());

		// Act: запрашиваем свежую марку — биржа значения не отдала.
		var fresh = await _provider.GetFreshMarkAsync(LinearSymbol);

		// Assert: свежей марки нет, но последняя известная марка в кэше не изменилась.
		Assert.That(fresh, Is.Null);
		Assert.That(await _provider.GetLastMarkAsync(LinearSymbol), Is.EqualTo(27739.74m));
	}

	[TestMethod]
	[Description("Ошибка тикеров поднимается наружу, не вытирая последнюю известную марку из кэша")]
	[ExpectedException(typeof(BybitApiException))]
	public void ThrowOnTickersFailureKeepsLastKnownMarkInCache()
	{
		// Arrange: кэш уже несёт марку; следующий ответ биржи — ошибка конверта retCode.
		_handler.EnqueueJson(TickersBody("27739.74"));
		_provider.GetFreshMarkAsync(LinearSymbol).GetAwaiter().GetResult();
		_handler.EnqueueJson("""{"retCode":10001,"retMsg":"params error","result":{}}""");

		// Act — ошибка биржи поднимается вызывающему слою: решение о деградации
		// остаётся за ним, а не за провайдером.
		try
		{
			_provider.GetFreshMarkAsync(LinearSymbol).GetAwaiter().GetResult();
		}
		catch (BybitApiException)
		{
			// Assert: сбой не вытер кэш — последняя известная марка по-прежнему
			// доступна читающим слоям без сети.
			Assert.That(_provider.GetLastMarkAsync(LinearSymbol).GetAwaiter().GetResult(), Is.EqualTo(27739.74m));
			throw;
		}
	}

	[TestMethod]
	[Description("Свежая марка без инструмента отклоняется до запроса к бирже")]
	[DataRow("")]
	[DataRow(" ")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnFreshMarkWithoutSymbol(string symbol)
	{
		// Act — пустой инструмент прерывается аргумент-исключением.
		try
		{
			_provider.GetFreshMarkAsync(symbol).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Свежая марка без самого инструмента отклоняется до запроса к бирже")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnFreshMarkNotProvided()
	{
		// Act — отсутствие инструмента прерывается до сетевого вызова.
		try
		{
			_provider.GetFreshMarkAsync(null!).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Последняя известная марка без инструмента отклоняется без обращения к базе")]
	[DataRow("")]
	[DataRow(" ")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnLastMarkWithoutSymbol(string symbol)
	{
		// Act — пустой инструмент прерывается аргумент-исключением.
		try
		{
			_provider.GetLastMarkAsync(symbol).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(_handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Последняя известная марка без самого инструмента отклоняется без обращения к базе")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnLastMarkNotProvided()
	{
		// Act — отсутствие инструмента прерывается до обращения к базе.
		try
		{
			_provider.GetLastMarkAsync(null!).GetAwaiter().GetResult();
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
		new InstrumentMarkProvider(options, client);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Справочник инструментов: линейный перп BTCUSDT, которого достаточно провайдеру.</summary>
	private static void SeedInstrumentCatalog(JournalDbContext db)
	{
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson = """{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
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

	/// <summary>Успешный ответ тикеров с маркой линейного перпа.</summary>
	private static string TickersBody(string markPrice) =>
		"{\"retCode\":0,\"retMsg\":\"OK\",\"result\":{\"category\":\"linear\",\"list\":[{\"symbol\":\"BTCUSDT\",\"markPrice\":\"" + markPrice + "\"}]}}";

	/// <summary>Успешный ответ тикеров с пустой маркой инструмента: биржа значения не отдала.</summary>
	private static string MissingMarkBody() =>
		"""{"retCode":0,"retMsg":"OK","result":{"category":"linear","list":[{"symbol":"BTCUSDT","markPrice":""}]}}""";

	#endregion
}
