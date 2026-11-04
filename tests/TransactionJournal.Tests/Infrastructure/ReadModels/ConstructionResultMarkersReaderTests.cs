using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.ReadModels;

/// <summary>
/// Проверки читателя последних маркеров истории результата: по одной записи
/// на конструкцию с последним выведенным маркером — итогом, временем события
/// и перечнем операций расшифровки; незакрытая фьючерсная позиция в итог не
/// входит, подавленные порогом операции входят в перечень следующего
/// выведенного маркера, нарушенные аргументы отклоняются явной ошибкой.
// Traceability: openspec:analytics/performance#requirement-result-markers-contract
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
	[Description("Читатель отдаёт последний выведенный маркер с итогом, временем и перечнем операций")]
	// Итог последнего маркера — сумма вкладов операций: незакрытая фьючерсная
	// сделка вклада не создаёт, корректировка даёт маркер на своё время;
	// конструкция без событий несёт null вместо маркера.
	// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
	// Traceability: openspec:analytics/performance#scenario-futures-open-position-not-counted
	public async Task TryIfReaderReturnsLastMarkersForJournalEvents()
	{
		// Arrange: конструкция с открытой фьючерсной позицией (покупка 28-го
		// числа) и корректировкой +5 29-го числа, конструкция только с открытой
		// фьючерсной позицией и конструкция без событий журнала.
		await SeedAsync(
			Header("Длинный BTC", ConstructionStatus.Open, 1000m),
			Header("Пустая", ConstructionStatus.Open, 1000m),
			Header("Открытый фьючерс", ConstructionStatus.Open, 1000m));
		await SeedLinearInstrumentAsync();
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-open", "Buy", "0.2", "42000", "1", ExecMs(2023, 12, 28, 11, 0));
		await new TradeBindingService(CreateOptions()).BindBatchAsync(1, ["exec-buy"]);
		await new TradeBindingService(CreateOptions()).BindBatchAsync(3, ["exec-open"]);
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

		// Потоки позиций читаются живой read-моделью метрик — тем же источником,
		// что питает карточку: итог последнего маркера сходится с карточкой.
		var metrics = await new JournalMetricsReadModel(CreateOptions(), new EmptyFreshQuoteSource()).ReadAsync();
		var reader = new ConstructionResultMarkersReader(CreateOptions(), new PositionReadModel(CreateOptions()));

		// Act: читаем последние маркеры всех конструкций журнала.
		var tails = await reader.ReadAsync(metrics.PositionEntries);

		// Assert: по записи на конструкцию, упорядоченные по идентификатору;
		// у конструкции с корректировкой маркер один — вклад незакрытой сделки
		// в историю не входит, у остальных выведенных маркеров нет.
		Assert.That(tails.Select(tail => tail.ConstructionId).ToArray(), Is.EqualTo(new long[] { 1, 2, 3 }));
		var withEvents = tails.Single(tail => tail.ConstructionId == 1);
		Assert.That(withEvents.LastMarker, Is.Not.Null);
		Assert.That(withEvents.LastMarker!.TotalUsdt, Is.EqualTo(5m));
		Assert.That(withEvents.LastMarker!.EventAt, Is.EqualTo(new DateTimeOffset(2023, 12, 29, 9, 0, 0, TimeSpan.Zero)));
		var operations = withEvents.LastMarker!.Operations;
		Assert.That(operations, Has.Count.EqualTo(1));
		Assert.That(operations[0].Kind, Is.EqualTo(ResultMarkerKind.PnLAdjustment));
		Assert.That(operations[0].EventTime, Is.EqualTo(new DateTimeOffset(2023, 12, 29, 9, 0, 0, TimeSpan.Zero)));
		Assert.That(operations[0].AmountUsdt, Is.EqualTo(5m));
		Assert.That(tails.Single(tail => tail.ConstructionId == 2).LastMarker, Is.Null);
		Assert.That(tails.Single(tail => tail.ConstructionId == 3).LastMarker, Is.Null);
	}

	[TestMethod]
	[Description("Подавленные порогом операции входят в перечень последнего маркера")]
	// Порог 5% планового риска (100 USDT) подавляет промежуточную операцию +2:
	// перечень последнего маркера несёт её вместе с финальной, сумма перечня
	// равна изменению маркера — итог последнего минус итог предыдущего.
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	public async Task TryIfSuppressedOperationsJoinLastMarkerOperations()
	{
		// Arrange: конструкция с плановым риском 100 USDT и тремя корректировками
		// +3, +2, +1 — вторая остаётся под порогом, третья выводится последней.
		await SeedAsync(Header("Корректировки", ConstructionStatus.Open, 1000m, riskValue: 100m, riskUnit: TargetUnit.Usdt));
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.PnLAdjustments.AddRange(
				new PnLAdjustment
				{
					ConstructionId = 1,
					Date = new DateTimeOffset(2023, 12, 27, 9, 0, 0, TimeSpan.Zero),
					Source = PnLAdjustmentSource.Manual,
					AmountUsdt = 3m,
				},
				new PnLAdjustment
				{
					ConstructionId = 1,
					Date = new DateTimeOffset(2023, 12, 28, 9, 0, 0, TimeSpan.Zero),
					Source = PnLAdjustmentSource.Manual,
					AmountUsdt = 2m,
				},
				new PnLAdjustment
				{
					ConstructionId = 1,
					Date = new DateTimeOffset(2023, 12, 29, 9, 0, 0, TimeSpan.Zero),
					Source = PnLAdjustmentSource.Manual,
					AmountUsdt = 1m,
				});
			await db.SaveChangesAsync();
		}

		var reader = new ConstructionResultMarkersReader(CreateOptions(), new PositionReadModel(CreateOptions()));

		// Act: читаем последние маркеры; корректировкам потоки позиций не нужны.
		var tails = await reader.ReadAsync(
			new Dictionary<long, IReadOnlyDictionary<string, IReadOnlyList<PositionFifoEntry>>>());

		// Assert: последний маркер несёт подавленную операцию +2 вместе с
		// финальной +1, сумма перечня равна изменению 6 − 3 = 3.
		var tail = tails.Single();
		Assert.That(tail.LastMarker, Is.Not.Null);
		Assert.That(tail.LastMarker!.TotalUsdt, Is.EqualTo(6m));
		Assert.That(tail.LastMarker!.EventAt, Is.EqualTo(new DateTimeOffset(2023, 12, 29, 9, 0, 0, TimeSpan.Zero)));
		var operations = tail.LastMarker!.Operations;
		Assert.That(operations, Has.Count.EqualTo(2));
		Assert.That(operations[0].EventTime, Is.EqualTo(new DateTimeOffset(2023, 12, 28, 9, 0, 0, TimeSpan.Zero)));
		Assert.That(operations[0].AmountUsdt, Is.EqualTo(2m));
		Assert.That(operations[1].EventTime, Is.EqualTo(new DateTimeOffset(2023, 12, 29, 9, 0, 0, TimeSpan.Zero)));
		Assert.That(operations[1].AmountUsdt, Is.EqualTo(1m));
		Assert.That(operations.Sum(operation => operation.AmountUsdt), Is.EqualTo(3m));
	}

	[TestMethod]
	[Description("Null-потоки позиций отклоняются чтением читателя")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnNullPositionEntries()
	{
		// Arrange — Act — Assert
		var reader = new ConstructionResultMarkersReader(CreateOptions(), new PositionReadModel(CreateOptions()));
		await reader.ReadAsync(null!);
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

	private static Construction Header(
		string name,
		ConstructionStatus status,
		decimal? capital,
		decimal? riskValue = null,
		TargetUnit? riskUnit = null) => new()
	{
		Name = name,
		Status = status,
		AllocatedCapitalUsdt = capital,
		RiskValue = riskValue,
		RiskUnit = riskUnit,
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

	/// <summary>Заглушка без котировок: открытые остатки остаются без нереализованной оценки.</summary>
	private sealed class EmptyFreshQuoteSource : IFreshInstrumentQuoteSource
	{
		public Task<InstrumentQuoteSnapshot?> GetFreshQuoteAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentQuoteSnapshot?>(null);
	}

	#endregion
}
