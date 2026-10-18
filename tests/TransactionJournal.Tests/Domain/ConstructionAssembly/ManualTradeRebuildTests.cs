using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.Sync;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.Chats;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Интеграционные проверки переживания ручных сделок полным пересбором на
/// SQLite-базе: ручная сделка — сырьё журнала, поэтому пересбор сохраняет её
/// строку со всеми атрибутами и привязкой, а новая сборка считает позиции,
/// FIFO и метрики из обоих источников наравне — ручная сделка питает позицию
/// и реализованный результат новой конструкции, не выпадая из проекций.
/// Стек команды пересбора, виртуальные часы и сеяние сырья повторяют
/// ConstructionAssemblyServiceTests; проекции читаются теми же read-моделями,
/// что показывают данные пользователю.
/// Требование: полный пересбор сохраняет ручные сделки как сырьё журнала,
/// и они участвуют в новой сборке.
/// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
/// </summary>
[TestClass]
public class ManualTradeRebuildTests
{
	private const string LinearSymbol = "ETHUSDT";
	private const string EthCall1600Dec = "ETH-25DEC26-1600-C-USDT";
	private const string EthPut1600Dec = "ETH-25DEC26-1600-P-USDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Момент «сейчас» проверок: сделки истории позади, экспирация доски ещё не наступила.</summary>
	private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private string _chatsDatabasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-rebuild-tests-{Guid.NewGuid():N}.db");
		_chatsDatabasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-rebuild-chats-tests-{Guid.NewGuid():N}.db");
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			foreach (var candidate in new[] { _databasePath, _chatsDatabasePath })
			{
				var file = candidate + suffix;
				if (File.Exists(file))
				{
					File.Delete(file);
				}
			}
		}
	}

	[TestMethod]
	[Description("Полный пересбор сохраняет ручные сделки как сырьё журнала со всеми атрибутами и привязкой")]
	// Ручная сделка — сырьё, а не содержимое конструкции: пересбор стирает
	// конструкции, но строки ручных сделок остаются без изменений — и атрибуты,
	// и отметки создания/правки, и привязка, выведенная по соответствию в
	// пересобранную конструкцию. Непривязанная сделка остаётся во «Входящих».
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-survives-rebuild
	// Traceability: change:manual-journal-management/design#d1
	public async Task TryIfRebuildPreservesManualTradesAsRawMaterial()
	{
		// Arrange: биржевое сырьё собрано в конструкцию, поверх внесены ручные
		// сделки — привязанная к конструкции и непривязанная во «Входящих».
		SeedRawStorage();
		await CreateService().RebuildAsync();
		long constructionId;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			constructionId = db.Constructions.Single().Id;
		}

		var seededBound = await SeedManualTradeAsync(constructionId, "Sell", 0.3m, 3100m, 0.4m);
		var seededInbox = await SeedManualTradeAsync(null, "Buy", 0.1m, 3050m, 0.1m);

		// Act: полный пересбор журнала.
		await CreateService().RebuildAsync();

		// Assert: сырьё ручных сделок не стёрто — обе строки на месте.
		long newConstructionId;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var manualTrades = await db.ManualTrades.OrderBy(trade => trade.Id).ToListAsync();
			Assert.That(manualTrades, Has.Count.EqualTo(2), "Пересбор не стирает сырьё ручных сделок");
			var bound = manualTrades.Single(trade => trade.Id == seededBound.Id);
			Assert.That(bound.ExecutedAt, Is.EqualTo(seededBound.ExecutedAt), "Время исполнения пережитой сделки сохранено");
			Assert.That(bound.Symbol, Is.EqualTo(LinearSymbol), "Инструмент пережитой сделки сохранён");
			Assert.That(bound.Side, Is.EqualTo("Sell"), "Сторона пережитой сделки сохранена");
			Assert.That(bound.Quantity, Is.EqualTo(0.3m), "Количество пережитой сделки сохранено");
			Assert.That(bound.Price, Is.EqualTo(3100m), "Цена пережитой сделки сохранена");
			Assert.That(bound.FeeUsdt, Is.EqualTo(0.4m), "Комиссия пережитой сделки сохранена");
			Assert.That(bound.CreatedAt, Is.EqualTo(seededBound.CreatedAt), "Отметка создания пережитой сделки сохранена");
			Assert.That(bound.UpdatedAt, Is.EqualTo(seededBound.UpdatedAt), "Отметка правки пережитой сделки сохранена");

			// Биржевое сырьё тоже не тронуто: пересбор стирает доменные таблицы, не сырье.
			Assert.That(await db.RawExecutions.CountAsync(), Is.EqualTo(3), "Сырые биржевые записи не изменены");

			// Конструкция пересобрана заново, привязки биржевых сделок восстановлены планом,
			// привязка ручной сделки выведена в пересобранную конструкцию.
			var constructions = db.Constructions.ToList();
			Assert.That(constructions, Has.Count.EqualTo(1), "Конструкция пересобрана из сырья");
			newConstructionId = constructions.Single().Id;
			Assert.That(bound.ConstructionId, Is.EqualTo(newConstructionId), "Привязка ручной сделки выведена в пересобранную конструкцию");
			Assert.That(
				await db.TradeUserdata.CountAsync(userdata => userdata.ConstructionId == newConstructionId),
				Is.EqualTo(3),
				"Привязки биржевых сделок восстановлены планом");
		}

		// «Входящие» — общая проекция сырья: непривязанная ручная сделка осталась
		// в списке, привязанная ушла в конструкцию.
		var inbox = await new InboxReadModel(CreateOptions()).ListAsync();
		Assert.That(inbox, Has.Count.EqualTo(1), "«Входящие» показывают непривязанную ручную сделку");
		var inboxTrade = inbox.Single();
		Assert.That(inboxTrade.Source, Is.EqualTo(TradeSource.Manual), "Источник пережитой сделки — ручной ввод");
		Assert.That(inboxTrade.ManualTradeId, Is.EqualTo(seededInbox.Id), "Во «Входящих» осталась непривязанная ручная сделка");
		Assert.That(inboxTrade.ExecId, Is.Null, "У ручной сделки execId отсутствует");
		Assert.That(inboxTrade.Quantity, Is.EqualTo(0.1m), "Атрибуты пережитой сделки показаны проекцией");
	}

	[TestMethod]
	[Description("Ручные сделки участвуют в новой сборке: позиции, FIFO и метрики считаются из обоих источников")]
	// Ручная продажа закрывает часть биржевой покупки внутри конструкции: после
	// полного пересбора новая конструкция считает тот же чистый остаток и тот же
	// реализованный результат, что и до пересбора, — ручная сделка кормит новую
	// сборку наравне с биржевой.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-survives-rebuild
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-feeds-position-and-fifo
	public async Task TryIfManualTradesFeedNewAssemblyAfterRebuild()
	{
		// Arrange: биржевой стреддл с фьючерсной ногой собран, ручная продажа
		// привязана к конструкции и закрывает часть фьючерсной покупки.
		SeedRawStorage();
		await CreateService().RebuildAsync();
		long constructionId;
		using (var db = new JournalDbContext(CreateOptions()))
		{
			constructionId = db.Constructions.Single().Id;
		}

		await SeedManualTradeAsync(constructionId, "Sell", 0.3m, 3100m, 0.4m);

		// Контроль до пересбора: позиция фьючерса — биржевая покупка за вычетом
		// ручной продажи, реализованный результат — FIFO обоих источников.
		var positionReader = new PositionReadModel(CreateOptions(), timeProvider: new FixedTimeProvider(Now));
		var metricsReader = new JournalMetricsReadModel(CreateOptions(), new EmptyMarkSource(), timeProvider: new FixedTimeProvider(Now));
		var positionsBefore = await positionReader.ListAsync(constructionId);
		Assert.That(
			positionsBefore.Positions.Single(position => position.Symbol == LinearSymbol).Residual,
			Is.EqualTo(0.2m),
			"До пересбора позиция считается из биржевой покупки и ручной продажи");
		var metricsBefore = await metricsReader.ReadAsync();
		Assert.That(
			metricsBefore.Constructions.Single(metrics => metrics.ConstructionId == constructionId).RealizedPnL,
			Is.EqualTo(29.6m),
			"До пересбора FIFO считает ручную продажу против биржевой покупки с её комиссией");

		// Act: полный пересбор журнала.
		await CreateService().RebuildAsync();

		// Assert: новая сборка считает те же позиции и тот же результат из
		// обоих источников — ручная сделка осталась полноправным сырьём.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var newConstructionId = db.Constructions.Single().Id;
			var positionsAfter = await positionReader.ListAsync(newConstructionId);
			Assert.That(
				positionsAfter.Positions.Single(position => position.Symbol == LinearSymbol).Residual,
				Is.EqualTo(0.2m),
				"Новая сборка считает позицию из биржевой покупки и ручной продажи");
			Assert.That(
				positionsAfter.Positions.Single(position => position.Symbol == EthCall1600Dec).Residual,
				Is.EqualTo(1m),
				"Биржевые опционные ноги новой сборки не изменены");
			Assert.That(
				positionsAfter.Positions.Single(position => position.Symbol == EthPut1600Dec).Residual,
				Is.EqualTo(1m),
				"Биржевые опционные ноги новой сборки не изменены");
			var metricsAfter = await metricsReader.ReadAsync();
			Assert.That(
				metricsAfter.Constructions.Single(metrics => metrics.ConstructionId == newConstructionId).RealizedPnL,
				Is.EqualTo(29.6m),
				"Новая сборка считает FIFO из ручной и биржевой сделок с комиссией");
		}
	}

	#region Помощники

	/// <summary>Команда пересбора над настоящим адаптером сырого хранилища и единым хранилищем чатов, как в работе.</summary>
	private ConstructionAssemblyService CreateService() => new(
		new JournalSyncStore(CreateOptions()),
		new StubJournalBackupService(),
		CreateOptions(),
		new ChatStore(_chatsDatabasePath),
		new FixedTimeProvider(Now));

	/// <summary>Создаёт опции контекста журнала над временной SQLite-базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Сеет ручную сделку поверх пересобранного журнала и возвращает сохранённую строку сырья.</summary>
	/// <param name="constructionId">Конструкция привязки; null — сделка остаётся во «Входящих».</param>
	/// <param name="side">Сторона сделки биржевым значением.</param>
	/// <param name="quantity">Количество сделки.</param>
	/// <param name="price">Цена сделки.</param>
	/// <param name="feeUsdt">Комиссия сделки в USDT.</param>
	private async Task<ManualTrade> SeedManualTradeAsync(
		long? constructionId,
		string side,
		decimal quantity,
		decimal price,
		decimal feeUsdt)
	{
		var trade = new ManualTrade
		{
			ExecutedAt = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero),
			Symbol = LinearSymbol,
			Side = side,
			Quantity = quantity,
			Price = price,
			FeeUsdt = feeUsdt,
			ConstructionId = constructionId,
			CreatedAt = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero),
			UpdatedAt = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero),
		};
		using var db = new JournalDbContext(CreateOptions());
		db.ManualTrades.Add(trade);
		await db.SaveChangesAsync();
		return trade;
	}

	/// <summary>
	/// Наполняет сырьё: справочник инструментов, стреддл дальнего срока и
	/// фьючерсная сделка робота внутри его жизни.
	/// </summary>
	private void SeedRawStorage()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(OptionInstrument(EthCall1600Dec, "Call"));
		db.RawInstruments.Add(OptionInstrument(EthPut1600Dec, "Put"));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson = """{"symbol":"ETHUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});

		db.RawExecutions.Add(Raw("e1", "option", EthCall1600Dec, Ms(2026, 7, 10, 9, 0),
			ExecutionPayload("e1", EthCall1600Dec, "Buy", "100", "1", "0", "USDC", Ms(2026, 7, 10, 9, 0))));
		db.RawExecutions.Add(Raw("e2", "option", EthPut1600Dec, Ms(2026, 7, 10, 9, 10),
			ExecutionPayload("e2", EthPut1600Dec, "Buy", "80", "1", "0", "USDC", Ms(2026, 7, 10, 9, 10))));
		db.RawExecutions.Add(Raw("e3", "linear", LinearSymbol, Ms(2026, 7, 12, 10, 0),
			ExecutionPayload("e3", LinearSymbol, "Buy", "3000", "0.5", "0", "USDT", Ms(2026, 7, 12, 10, 0))));

		db.SaveChanges();
	}

	/// <summary>Спецификация опциона в справочнике с каноническим временем доставки 08:00 UTC.</summary>
	private static RawInstrument OptionInstrument(string symbol, string optionsType)
	{
		var deliveryMs = Ms(2026, 12, 25, 8, 0);
		return new RawInstrument
		{
			Symbol = symbol,
			Category = "option",
			PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""",
			FetchedAt = FetchedAt,
		};
	}

	private static RawExecution Raw(string execId, string category, string symbol, long execTimeMs, string payloadJson) => new()
	{
		ExecId = execId,
		Category = category,
		Symbol = symbol,
		ExecTimeMs = execTimeMs,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>Запись исполнения в форме ответа execution-list: числа биржа шлёт строками.</summary>
	private static string ExecutionPayload(
		string execId,
		string symbol,
		string side,
		string execPrice,
		string execQty,
		string execFee,
		string feeCurrency,
		long execTimeMs) =>
		$$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""";

	private static long Ms(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Поставщик фиксированного времени для детерминированных проверок.</summary>
	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}

	/// <summary>Источник без известных марок: реализованные метрики от марок не зависят.</summary>
	private sealed class EmptyMarkSource : IFreshInstrumentMarkSource, IInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentMarkSnapshot?>(null);

		public Task<decimal?> GetLastMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<decimal?>(null);
	}

	#endregion
}
