using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;
using TransactionJournal.Tests.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Приёмочная композиционная проверка read-only характера доступа: полный запуск
/// оркестратора «Синхронизировать» собирается из production-звеньев — реальный
/// BybitHistoryGateway поверх подписанного BybitApiClient, движки категорий,
/// реальное SQLite-хранилище — и выполняется против фиктивного HTTP-транспорта,
/// отвечающего по пути и параметрам запроса. Доказывает, что всей синхронизации
/// хватает API-ключа с правами только на чтение: каждый ушедший в биржу запрос —
/// GET одного из четырёх read-only эндпоинтов, торговых вызовов в конвейере нет.
/// Traceability: openspec:sync/bybit-history#requirement-read-only-access
/// </summary>
[TestClass]
public class ReadOnlyAccessAcceptanceTests
{
	private const string TestBaseUrl = "http://bybit-test.local";
	private const string ApiKey = "test-api-key";
	private const string ApiSecret = "test-api-secret";
	private const string OptionSymbol = "BTC-15DEC25-45000-C";

	// Виртуальные часы движков фиксированы на 2026-01-01: окна backfill детерминированы.
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly long NowMs = Now.ToUnixTimeMilliseconds();
	private static readonly long DayMs = 86_400_000L;
	private static readonly long WeekMs = ExecutionWindowPass.MaxWindowMs;

	/// <summary>Каноническое время delivery опциона: 15DEC25 08:00 UTC.</summary>
	private static readonly long OptionDeliveryMs =
		new DateTimeOffset(2025, 12, 15, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Read-only поверхность клиента: история, доставка, справочник и публичное время.</summary>
	private static readonly IReadOnlySet<string> ReadOnlyPaths = new HashSet<string>(StringComparer.Ordinal)
	{
		"/v5/execution/list",
		"/v5/asset/delivery-record",
		"/v5/market/instruments-info",
		"/v5/market/time",
	};

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		_databasePath = Path.Combine(Path.GetTempPath(), $"read-only-acceptance-tests-{Guid.NewGuid():N}.db");
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
	[Description("Полный синк через реальный HTTP-клиент обходится только GET-запросами read-only эндпоинтов")]
	public async Task TryIfFullSyncUsesOnlyReadOnlyGetEndpoints()
	{
		// Arrange: фиктивная биржа отвечает по запросу — первое окно исполнения каждой
		// категории отдаёт по одной сделке (линейный перп и колл), глубже история пуста,
		// delivery-записей нет, спецификации инструментов выдаются фильтром по символу.
		// Требование: при настроенном ключе с правами только на чтение синхронизация
		// выполняется полностью и не требует дополнительных прав — конвейер не должен
		// выпустить ни одного запроса за пределы read-only поверхности клиента.
		// Traceability: openspec:sync/bybit-history#scenario-sync-with-read-only-key
		var handler = new ScriptedHttpMessageHandler(new ManualTimeProvider());
		handler.SetResponder(request => RespondByRequest(request.RequestUri!));
		var credentials = new BybitCredentials(ApiKey, ApiSecret);
		var credentialsProvider = Mock.Of<IBybitCredentialsProvider>(
			provider => provider.GetCredentials() == credentials);
		var client = new BybitApiClient(
			new HttpClient(handler),
			credentialsProvider,
			new BybitClientOptions { BaseUrl = TestBaseUrl },
			timeProvider: new ManualTimeProvider());
		var gateway = new BybitHistoryGateway(client);

		// Полный стек оркестратора на production-шлюзе и реальном хранилище; список
		// активов опционной доски отдаёт заглушка — приёмочная проверка читает только
		// read-only эндпоинты истории, доставки и справочника.
		var store = new JournalSyncStore(CreateOptions(), new ManualTimeProvider());
		var service = new JournalSyncService(
			new ExecutionCategorySync(
				new ExecutionWindowPass(gateway, store),
				store,
				new FakeOptionBaseCoinSource("BTC"),
				new ManualTimeProvider(),
				store),
			new DeliveryCategorySync(new DeliveryWindowPass(gateway, store), store, new ManualTimeProvider(), store),
			new InstrumentReferenceSync(gateway, store),
			store,
			store,
			store,
			new JournalMaterializer(),
			new ExecutionCategorySyncOptions(),
			new DeliveryCategorySyncOptions(),
			new ManualTimeProvider());

		// Act: единственная ручная команда — полный первичный backfill обеих категорий.
		var result = await service.SyncAsync();

		// Assert: синхронизация прошла целиком — обе сделки сохранены, справочник
		// пополнен, проекция перестроена из сырья без ошибок разбора.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.EqualTo(2));
		Assert.That(result.Run.NewInstruments, Is.EqualTo(2));
		Assert.That(result.ProjectionError, Is.Null);
		Assert.That(result.Projection!.InboxTrades.Count, Is.EqualTo(2));

		// Assert (read-only доступ): каждый запрос конвейера — GET, и каждый путь входит
		// в read-only поверхность из четырёх эндпоинтов; торговых вызовов не появилось,
		// поэтому ключа с правами только на чтение синхронизации хватает.
		Assert.That(handler.Requests, Is.Not.Empty);
		var requestedPaths = handler.Requests.Select(request => request.RequestUri!.AbsolutePath).ToList();
		Assert.That(requestedPaths.Distinct(), Is.SubsetOf(ReadOnlyPaths));
		Assert.That(handler.Requests.Select(request => request.Method).Distinct(), Is.EqualTo(new[] { HttpMethod.Get }));

		// Синхронизация действительно прошла все read-only источники: историю исполнения,
		// delivery-записи и справочник инструментов.
		Assert.That(requestedPaths, Does.Contain("/v5/execution/list"));
		Assert.That(requestedPaths, Does.Contain("/v5/asset/delivery-record"));
		Assert.That(requestedPaths, Does.Contain("/v5/market/instruments-info"));
	}

	#region Фиктивная биржа

	/// <summary>
	/// Отвечает на запрос по пути и параметрам: самое свежее окно исполнения каждой
	/// категории отдаёт одну сделку, старые окна и delivery-история пусты, а
	/// instruments-info выдаёт спецификацию запрошенного символа. Тела собираются
	/// сериализацией анонимных объектов: десериализатор клиента читает числа и без кавычек.
	/// </summary>
	private static HttpResponseMessage RespondByRequest(Uri requestUri)
	{
		var query = ParseQuery(requestUri);
		var path = requestUri.AbsolutePath;
		var json = path switch
		{
			"/v5/execution/list" => ExecutionListBody(query),
			"/v5/asset/delivery-record" => EmptyPageBody(),
			"/v5/market/instruments-info" => InstrumentsInfoBody(query),
			_ => EmptyPageBody(),
		};

		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
	}

	/// <summary>
	/// Страница истории исполнения категории: самое свежее 7-дневное окно содержит одну
	/// сделку (линейный перп либо опционный колл), старые окна пусты — история исчерпана.
	/// </summary>
	private static string ExecutionListBody(IReadOnlyDictionary<string, string> query)
	{
		var category = query["category"];
		var startTimeMs = long.Parse(query["startTime"], CultureInfo.InvariantCulture);
		var firstWindowStartMs = NowMs - WeekMs;
		if (startTimeMs < firstWindowStartMs)
		{
			return EmptyPageBody();
		}

		var isLinear = string.Equals(category, "linear", StringComparison.Ordinal);
		var execPrice = isLinear ? 42000m : 100m;
		var execQty = isLinear ? 0.01m : 0.0003m;
		var execution = new
		{
			symbol = isLinear ? "BTCUSDT" : OptionSymbol,
			orderId = "order-acceptance",
			orderLinkId = string.Empty,
			side = "Buy",
			orderPrice = execPrice,
			orderQty = execQty,
			leavesQty = 0m,
			createType = "CreateByUser",
			orderType = "Limit",
			stopOrderType = "UNKNOWN",
			execFee = isLinear ? 0.0042m : 0.0002m,
			execId = isLinear ? "exec-lin-read-only" : "exec-opt-read-only",
			execPrice,
			execQty,
			execType = "Trade",
			execValue = 420m,
			execTime = NowMs - DayMs,
			feeCurrency = isLinear ? "USDT" : "USDC",
			isMaker = false,
			seq = 17823123L,
		};

		return PageBody(category, execution);
	}

	/// <summary>Пустая страница без курсора: окно не дало записей.</summary>
	private static string EmptyPageBody() => PageBody(category: null);

	/// <summary>Страница конверта Bybit со списком записей result.list.</summary>
	private static string PageBody(string? category, params object[] items) =>
		JsonSerializer.Serialize(new
		{
			retCode = 0,
			retMsg = "OK",
			result = new
			{
				nextPageCursor = string.Empty,
				category,
				list = items,
			},
		});

	/// <summary>Спецификация запрошенного символа: линейный перп или опционный колл.</summary>
	private static string InstrumentsInfoBody(IReadOnlyDictionary<string, string> query)
	{
		var category = query["category"];
		var symbol = query.GetValueOrDefault("symbol") ?? string.Empty;
		object? instrument = string.Equals(symbol, "BTCUSDT", StringComparison.Ordinal)
			? new
			{
				symbol,
				contractType = "LinearPerpetual",
				status = "Trading",
				baseCoin = "BTC",
				quoteCoin = "USDT",
				settleCoin = "USDT",
				deliveryTime = 0L,
				deliveryFeeRate = string.Empty,
				fundingInterval = 480,
			}
			: string.Equals(symbol, OptionSymbol, StringComparison.Ordinal)
				? new
				{
					symbol,
					status = "Trading",
					baseCoin = "BTC",
					quoteCoin = "USD",
					settleCoin = "USDC",
					deliveryTime = OptionDeliveryMs,
					deliveryFeeRate = 0.0001m,
					optionsType = "Call",
				}
				: null;

		return PageBody(category, instrument is null ? Array.Empty<object>() : new[] { instrument });
	}

	private static IReadOnlyDictionary<string, string> ParseQuery(Uri requestUri)
	{
		var pairs = requestUri.Query.TrimStart('?')
			.Split('&', StringSplitOptions.RemoveEmptyEntries);
		var query = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var pair in pairs)
		{
			var separatorIndex = pair.IndexOf('=');
			if (separatorIndex > 0)
			{
				query[pair[..separatorIndex]] = pair[(separatorIndex + 1)..];
			}
		}

		return query;
	}

	#endregion

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>
	/// Фиктивный источник базовых активов опционной доски: возвращает заготовленный
	/// список активов без обращений к бирже.
	/// </summary>
	private sealed class FakeOptionBaseCoinSource : IOptionBaseCoinSource
	{
		private readonly IReadOnlyList<string> _baseCoins;

		public FakeOptionBaseCoinSource(params string[] baseCoins)
		{
			_baseCoins = baseCoins;
		}

		public Task<IReadOnlyList<string>> GetBaseCoinsAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(_baseCoins);
	}

	#endregion
}
