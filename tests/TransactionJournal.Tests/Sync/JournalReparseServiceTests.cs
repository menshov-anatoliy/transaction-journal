using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using ManualTimeProvider = TransactionJournal.Tests.Bybit.ManualTimeProvider;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки команды «Переразобрать сырые записи заново»: доменные представления
/// строятся из локального сырья хранилища без сетевых запросов, пустое хранилище
/// даёт пустую проекцию, обязательные зависимости конструктора отклоняются.
/// </summary>
[TestClass]
public class JournalReparseServiceTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	// Каноническое время delivery инструмента опциона из справочника: 29DEC23 08:00 UTC.
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-reparse-tests-{Guid.NewGuid():N}.db");
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();
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
	[Description("Переразбор строит проекцию из локального сырья: сделки с атрибутами справочника, без сетевых запросов")]
	public async Task TryIfReparseRebuildsProjectionFromRawStorage()
	{
		// Arrange: сырые записи легли в хранилище синхронизацией — справочник
		// инструментов, покупка опциона и продажа линейного перпа.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			SeedInstrumentCatalog(db);
			var optionExecMs = ExecMs(2023, 12, 28, 10, 0);
			var linearExecMs = ExecMs(2023, 12, 28, 10, 30);
			db.RawExecutions.Add(Raw("exec-opt-buy", "option", "BTC-29DEC23-45000-C", optionExecMs,
				ExecutionPayload("exec-opt-buy", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC",
					optionExecMs, isMaker: true)));
			db.RawExecutions.Add(Raw("exec-linear-sell", "linear", "BTCUSDT", linearExecMs,
				ExecutionPayload("exec-linear-sell", "BTCUSDT", "Sell", "42000", "0.01", "-0.0001", "BTC", linearExecMs)));
			await db.SaveChangesAsync();
		}

		var service = CreateService();

		// Act: выполняем переразбор сырых записей заново.
		var result = await service.ReparseAsync();

		// Assert: проекция собрана из локального сырья — обе сделки во «Входящих»
		// с атрибутами биржевой записи, канонические атрибуты опциона взяты из
		// справочника инструментов; опцион вне денег к наступившему delivery
		// получает автоматическую OTM-закрывающую запись, предупреждений нет.
		// Требование: переразбор работает только над локальным сырьём.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		Assert.That(result.InboxTrades, Has.Count.EqualTo(2));
		Assert.That(result.ReconciliationWarnings, Is.Empty);

		var option = result.InboxTrades.Single(trade => trade.ExecId == "exec-opt-buy");
		Assert.That(option.Option, Is.Not.Null);
		Assert.That(option.Option!.DeliveryTime, Is.EqualTo(OptionDelivery));
		Assert.That(option.FeeCurrency, Is.EqualTo("USDT"));

		Assert.That(result.ExpiryClosingEntries, Has.Count.EqualTo(1));
		var otmClose = result.ExpiryClosingEntries.Single();
		Assert.That(otmClose.Kind, Is.EqualTo(ExpiryClosingKind.OtmExpiry));
		Assert.That(otmClose.Symbol, Is.EqualTo("BTC-29DEC23-45000-C"));
		Assert.That(otmClose.Quantity, Is.EqualTo(-0.0001m));
		Assert.That(otmClose.ConstructionId, Is.Null);
	}

	[TestMethod]
	[Description("Пустое хранилище даёт пустую проекцию без ошибок")]
	public async Task TryIfEmptyStorageGivesEmptyProjection()
	{
		// Arrange: сырых записей нет — команда доступна и на пустом журнале.
		var service = CreateService();

		// Act: выполняем переразбор пустого хранилища.
		var result = await service.ReparseAsync();

		// Assert: проекция согласованно пуста — ни сделок, ни закрывающих
		// записей, ни предупреждений сверки.
		Assert.That(result.InboxTrades, Is.Empty);
		Assert.That(result.ExpiryClosingEntries, Is.Empty);
		Assert.That(result.ReconciliationWarnings, Is.Empty);
	}

	[TestMethod]
	[Description("Null-хранилище сырых записей отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawSnapshotStore()
	{
		// Arrange — Act — Assert
		new JournalReparseService(null!, new JournalMaterializer());
	}

	#region Помощники

	/// <summary>Команда переразбора над настоящим адаптером сырого хранилища, как в работе.</summary>
	private JournalReparseService CreateService() => new(
		new JournalSyncStore(CreateOptions()),
		new JournalMaterializer(),
		new ManualTimeProvider());

	/// <summary>Создаёт опции контекста журнала над временной SQLite-базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Справочник инструментов: опцион BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.</summary>
	private static void SeedInstrumentCatalog(JournalDbContext db)
	{
		var deliveryMs = OptionDelivery.ToUnixTimeMilliseconds();
		var optionPayload =
			$$"""{"symbol":"BTC-29DEC23-45000-C","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = "BTC-29DEC23-45000-C",
			Category = "option",
			PayloadJson = optionPayload,
			FetchedAt = FetchedAt,
		});
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = "BTCUSDT",
			Category = "linear",
			PayloadJson = linearPayload,
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
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
		string? feeCurrency,
		long execTimeMs,
		bool isMaker = false)
	{
		var feeCurrencyJson = feeCurrency is null ? "null" : $"\"{feeCurrency}\"";
		var isMakerJson = isMaker ? "true" : "false";
		return $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":{{feeCurrencyJson}},"isMaker":{{isMakerJson}}}""";
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
