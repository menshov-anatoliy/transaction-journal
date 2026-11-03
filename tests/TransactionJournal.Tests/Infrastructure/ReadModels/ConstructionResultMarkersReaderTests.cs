using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.ReadModels;

/// <summary>
/// Проверки читателя хвостов истории результата: по одной записи на конструкцию
/// с временем последнего события журнала, суммой локов действующих связок
/// и суммой внешних корректировок; конструкция без событий несёт null вместо
/// времени, нарушенный конструктор отклоняется явной ошибкой.
/// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
/// </summary>
[TestClass]
public class ConstructionResultMarkersReaderTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private const string LinearSymbol = "BTCUSDT";

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке
		// со справочником линейного перпа.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-result-markers-reader-tests-{Guid.NewGuid():N}.db");
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
	[Description("Читатель отдаёт по хвосту на конструкцию с временем последнего события и суммами")]
	// События журнала — фьючерсная сделка и внешняя корректировка — дают время
	// последнего события и сумму корректировок; конструкция без событий несёт
	// null вместо времени и нулевые суммы.
	// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
	public async Task TryIfReaderReturnsTailsForJournalEvents()
	{
		// Arrange: конструкция с фьючерсной сделкой 28-го числа и корректировкой
		// 29-го числа рядом с конструкцией без событий журнала.
		await SeedAsync(
			Header("Длинный BTC", ConstructionStatus.Open, 1000m),
			Header("Пустая", ConstructionStatus.Open, 1000m));
		await SeedLinearInstrumentAsync();
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await new TradeBindingService(CreateOptions()).BindBatchAsync(1, ["exec-buy"]);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.PnLAdjustments.Add(new PnLAdjustment
			{
				ConstructionId = 1,
				Date = new DateTimeOffset(2023, 12, 29, 9, 0, 0, TimeSpan.Zero),
				Source = PnLAdjustmentSource.Manual,
				AmountUsdt = 5m,
			});
			await db.SaveChangesAsync();
		}

		var reader = new ConstructionResultMarkersReader(CreateOptions(), new PositionReadModel(CreateOptions()));

		// Act: читаем хвосты истории всех конструкций журнала.
		var tails = await reader.ReadAsync();

		// Assert: по хвосту на конструкцию, упорядоченные по идентификатору;
		// событиями управляют сделка и корректировка, локов и споров нет.
		Assert.That(tails.Select(tail => tail.ConstructionId).ToArray(), Is.EqualTo(new long[] { 1, 2 }));
		var withEvents = tails.Single(tail => tail.ConstructionId == 1);
		Assert.That(withEvents.LastEventAt, Is.EqualTo(new DateTimeOffset(2023, 12, 29, 9, 0, 0, TimeSpan.Zero)));
		Assert.That(withEvents.AdjustmentsTotalUsdt, Is.EqualTo(5m));
		Assert.That(withEvents.LockedTotalUsdt, Is.EqualTo(0m));
		var withoutEvents = tails.Single(tail => tail.ConstructionId == 2);
		Assert.That(withoutEvents.LastEventAt, Is.Null);
		Assert.That(withoutEvents.LockedTotalUsdt, Is.EqualTo(0m));
		Assert.That(withoutEvents.AdjustmentsTotalUsdt, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором читателя")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new ConstructionResultMarkersReader(null!, new PositionReadModel(CreateOptions()));
	}

	[TestMethod]
	[Description("Null-read-модель позиций отклоняется конструктором читателя")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullPositionsReadModel()
	{
		// Arrange — Act — Assert
		new ConstructionResultMarkersReader(CreateOptions(), null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Сохраняет конструкции в тестовую базу; идентификаторы выдаёт хранилище по порядку вставки.</summary>
	private async Task SeedAsync(params Construction[] constructions)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.Constructions.AddRange(constructions);
		await db.SaveChangesAsync();
	}

	private static Construction Header(string name, ConstructionStatus status, decimal? capital) => new()
	{
		Name = name,
		Status = status,
		AllocatedCapitalUsdt = capital,
	};

	/// <summary>Сохраняет линейный инструмент в справочник сырых записей синхронизации.</summary>
	private async Task SeedLinearInstrumentAsync()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson =
				"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Сохраняет запись исполнения в форме ответа execution-list: числа биржа шлёт строками.</summary>
	private async Task AddLinearTradeAsync(string execId, string side, string execQty, string execPrice, string execFee, long execTimeMs)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "linear",
			Symbol = LinearSymbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{LinearSymbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"USDT","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
