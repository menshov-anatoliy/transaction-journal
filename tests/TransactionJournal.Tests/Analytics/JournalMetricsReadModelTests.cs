using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки read-модели метрик журнала — композиции читающего слоя для экранов UI:
/// метрики конструкций и итог по журналу вычисляются из текущих данных при каждом
/// чтении, открытые остатки оцениваются свежими марками, а сбой марок делает итог
/// журнала неполным (null), не подменяя его частичной суммой и не затрагивая
/// реализованные метрики.
/// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
/// </summary>
[TestClass]
public class JournalMetricsReadModelTests
{
	private const string LinearSymbol = "BTCUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private JournalMetricsReadModel _readModel = null!;
	private ScriptedMarkSource _markSource = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-metrics-read-model-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			db.RawInstruments.Add(new RawInstrument
			{
				Symbol = LinearSymbol,
				Category = "linear",
				PayloadJson =
					"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
				FetchedAt = FetchedAt,
			});
			db.SaveChanges();
		}

		_markSource = new ScriptedMarkSource();
		_readModel = new JournalMetricsReadModel(CreateOptions(), _markSource, _markSource);
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
	[Description("Итог по журналу равен сумме итогов конструкций с корректировками; пустой журнал даёт ноль")]
	public async Task TryIfJournalTotalSumsConstructionTotals()
	{
		// Arrange: конструкция с закрытой линейной позицией (покупка и продажа 0.1
		// с комиссиями 1+1) и корректировкой робота +20 даёт итог 118; вторая
		// конструкция без записей даёт ноль.
		var scalper = await CreateConstructionAsync("Скальп BTC", 1000m);
		await CreateConstructionAsync("Пустая", 500m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell", "Sell", "0.1", "43000", "1", ExecMs(2023, 12, 28, 11, 0));
		await BindAsync(scalper, "exec-buy", "exec-sell");
		await AddAdjustmentAsync(scalper, 20m);

		// Act
		var metrics = await _readModel.ReadAsync();

		// Assert: итог журнала — сумма итогов конструкций: (100 − 2 комиссии + 20) + 0.
		// Требование: итог панели вычисляется по текущим данным журнала.
		// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
		// Traceability: openspec:analytics/performance#scenario-construction-total-includes-adjustments
		Assert.That(metrics.Constructions, Has.Count.EqualTo(2));
		var scalperMetrics = metrics.Constructions.Single(m => m.ConstructionId == scalper);
		Assert.That(scalperMetrics.TotalPnL, Is.EqualTo(118m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(118m));
		Assert.That(metrics.HasMarkFailure, Is.False);

		// Разбивка не содержит корректировку +20: корректировки входят только
		// в итог журнала, реализованный и нереализованный агрегаты складываются
		// из частей конструкций без них.
		// Traceability: openspec:analytics/performance#scenario-journal-breakdown-excludes-adjustments
		Assert.That(metrics.RealizedPnL, Is.EqualTo(98m));
		Assert.That(metrics.UnrealizedPnL, Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Разбивка журнала суммирует реализованные и нереализованные части всех конструкций")]
	public async Task TryIfJournalBreakdownSumsConstructions()
	{
		// Arrange: закрытая позиция даёт реализованный PnL 98 (100 − 2 комиссии),
		// открытый остаток с маркой 42500 даёт реализованный −1 и нереализованный +50.
		var scalper = await CreateConstructionAsync("Скальп BTC", 1000m);
		var swing = await CreateConstructionAsync("Длинный BTC", 1000m);
		await AddLinearTradeAsync("exec-buy-1", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell-1", "Sell", "0.1", "43000", "1", ExecMs(2023, 12, 28, 11, 0));
		await BindAsync(scalper, "exec-buy-1", "exec-sell-1");
		await AddLinearTradeAsync("exec-buy-2", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 12, 0));
		await BindAsync(swing, "exec-buy-2");
		_markSource.FreshMarks[LinearSymbol] = (42500m, FetchedAt);

		// Act
		var metrics = await _readModel.ReadAsync();

		// Assert: реализованный 98 + (−1) = 97, нереализованный 0 + 50 = 50,
		// итог 98 + 49 = 147 — разбивка и итог складываются из частей конструкций.
		// Требование: разбивка журнала суммирует конструкции.
		// Traceability: openspec:analytics/performance#scenario-journal-breakdown-sums-constructions
		Assert.That(metrics.RealizedPnL, Is.EqualTo(97m));
		Assert.That(metrics.UnrealizedPnL, Is.EqualTo(50m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(147m));
	}

	[TestMethod]
	[Description("Архивные конструкции входят в агрегаты журнала наравне с активными")]
	public async Task TryIfJournalAggregatesIncludeArchivedConstruction()
	{
		// Arrange: скальп переведён в архив после закрытия позиции (реализованный 98),
		// активная конструкция держит открытый остаток с маркой (−1 и +50).
		var archived = await CreateConstructionAsync("Архив скальпа", 1000m, ConstructionStatus.Archived);
		var active = await CreateConstructionAsync("Длинный BTC", 1000m);
		await AddLinearTradeAsync("exec-buy-a", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell-a", "Sell", "0.1", "43000", "1", ExecMs(2023, 12, 28, 11, 0));
		await BindAsync(archived, "exec-buy-a", "exec-sell-a");
		await AddLinearTradeAsync("exec-buy-o", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 12, 0));
		await BindAsync(active, "exec-buy-o");
		_markSource.FreshMarks[LinearSymbol] = (42500m, FetchedAt);

		// Act
		var metrics = await _readModel.ReadAsync();

		// Assert: результат архивной конструкции входит во все три агрегата:
		// без него реализованный был бы −1, а не 98 + (−1) = 97.
		// Требование: архивные конструкции входят в агрегаты журнала.
		// Traceability: openspec:analytics/performance#scenario-journal-aggregates-include-archived
		Assert.That(metrics.RealizedPnL, Is.EqualTo(97m));
		Assert.That(metrics.UnrealizedPnL, Is.EqualTo(50m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(147m));
	}

	[TestMethod]
	[Description("Пустой журнал без конструкций возвращает нулевой итог")]
	public async Task TryIfEmptyJournalTotalIsZero()
	{
		var metrics = await _readModel.ReadAsync();

		Assert.That(metrics.Constructions, Is.Empty);
		Assert.That(metrics.Positions, Is.Empty);
		Assert.That(metrics.TotalPnL, Is.EqualTo(0m));
		Assert.That(metrics.HasMarkFailure, Is.False);
	}

	[TestMethod]
	[Description("Открытый остаток оценён свежей маркой: итог включает нереализованную часть с отметкой времени марок")]
	public async Task TryIfOpenResidualValuedByFreshMark()
	{
		// Arrange: конструкция с открытым остатком — покупка 0.1 по 42000 с комиссией 1.
		var construction = await CreateConstructionAsync("Длинный BTC", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await BindAsync(construction, "exec-buy");
		_markSource.FreshMarks[LinearSymbol] = (42500m, FetchedAt);

		// Act
		var metrics = await _readModel.ReadAsync();

		// Assert: нереализованная оценка (42500 − 42000) × 0.1 = 50 входит в итог
		// вместе с комиссией: −1 + 50 = 49; отметка времени марок возвращается.
		// Traceability: openspec:analytics/performance#scenario-open-residual-valued-at-request
		Assert.That(metrics.TotalPnL, Is.EqualTo(49m));
		Assert.That(metrics.MarksAsOf, Is.EqualTo(FetchedAt));
		Assert.That(metrics.HasMarkFailure, Is.False);
	}

	[TestMethod]
	[Description("Сбой марок делает итог журнала неполным: реализованные метрики возвращаются как есть")]
	public async Task TryIfMarkFailureMakesJournalTotalIncomplete()
	{
		// Arrange: открытый остаток без доступной свежей марки — тикеры недоступны.
		var construction = await CreateConstructionAsync("Длинный BTC", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await BindAsync(construction, "exec-buy");
		_markSource.FailFresh = true;

		// Act
		var metrics = await _readModel.ReadAsync();

		// Assert: итог журнала неполный (null), нереализованная часть и отметка времени
		// марок деградировали, реализованный результат (−1 комиссия) читается как есть.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
		// Traceability: change:add-ui-screens/design#d4
		Assert.That(metrics.TotalPnL, Is.Null);
		Assert.That(metrics.MarksAsOf, Is.Null);
		Assert.That(metrics.HasMarkFailure, Is.True);
		Assert.That(metrics.Constructions.Single().RealizedPnL, Is.EqualTo(-1m));
	}

	[TestMethod]
	[Description("Сбой марок одной конструкции гасит нереализованный агрегат журнала, реализованный остаётся суммой")]
	public async Task TryIfAnyConstructionMarkFailureNullsJournalUnrealizedOnly()
	{
		// Arrange: у конструкции с BTC свежая марка есть, у конструкции с ETH — нет:
		// сбой одной конструкции должен погасить нереализованный агрегат журнала.
		var btc = await CreateConstructionAsync("Длинный BTC", 1000m);
		var eth = await CreateConstructionAsync("Длинный ETH", 1000m);
		await SeedLinearInstrumentAsync("ETHUSDT");
		await AddLinearTradeAsync("exec-btc", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await BindAsync(btc, "exec-btc");
		await AddLinearTradeAsync("exec-eth", "Buy", "0.1", "3000", "1", ExecMs(2023, 12, 28, 10, 0), "ETHUSDT");
		await BindAsync(eth, "exec-eth");
		_markSource.FreshMarks[LinearSymbol] = (42500m, FetchedAt);

		// Act
		var metrics = await _readModel.ReadAsync();

		// Assert: нереализованный агрегат и итог отсутствуют — частичная сумма
		// по отмеченной конструкции не подменяет их; реализованный остаётся
		// суммой по всем конструкциям: (−1) + (−1) = −2.
		// Требование: сбой марок хотя бы одной конструкции гасит нереализованную часть.
		// Traceability: openspec:analytics/performance#scenario-journal-unrealized-null-on-any-mark-failure
		Assert.That(metrics.HasMarkFailure, Is.True);
		Assert.That(metrics.UnrealizedPnL, Is.Null);
		Assert.That(metrics.TotalPnL, Is.Null);
		Assert.That(metrics.RealizedPnL, Is.EqualTo(-2m));
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private async Task<long> CreateConstructionAsync(string name, decimal capital, ConstructionStatus status = ConstructionStatus.Open)
	{
		using var db = new JournalDbContext(CreateOptions());
		var construction = new Construction { Name = name, Status = status, AllocatedCapitalUsdt = capital };
		db.Constructions.Add(construction);
		await db.SaveChangesAsync();
		return construction.Id;
	}

	private async Task AddLinearTradeAsync(string execId, string side, string execQty, string execPrice, string execFee, long execTimeMs, string? symbol = null)
	{
		var tradeSymbol = symbol ?? LinearSymbol;
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "linear",
			Symbol = tradeSymbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = ExecutionPayload(execId, tradeSymbol, side, execPrice, execQty, execFee, "USDT", execTimeMs),
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Сохраняет линейный инструмент в справочник сырых записей синхронизации: базовой монетой служит префикс символа до USDT.</summary>
	private async Task SeedLinearInstrumentAsync(string symbol)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = symbol,
			Category = "linear",
			PayloadJson =
				$$"""{"symbol":"{{symbol}}","contractType":"LinearPerpetual","status":"Trading","baseCoin":"{{symbol[..^4]}}","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	private async Task BindAsync(long constructionId, params string[] execIds)
	{
		var binding = new TradeBindingService(CreateOptions());
		await binding.BindBatchAsync(constructionId, execIds);
	}

	private async Task AddAdjustmentAsync(long constructionId, decimal amount)
	{
		var adjustments = new PnLAdjustmentService(CreateOptions());
		await adjustments.AddAsync(constructionId, FetchedAt, PnLAdjustmentSource.Robot, amount);
	}

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

	/// <summary>
	/// Сценарный источник марок: свежие и последние марки выдаются из словаря,
	/// режим сбоя имитирует недоступность публичных тикеров ошибкой сети.
	/// </summary>
	private sealed class ScriptedMarkSource : IFreshInstrumentMarkSource, IInstrumentMarkSource
	{
		public Dictionary<string, (decimal Price, DateTimeOffset ReceivedAt)> FreshMarks { get; } = new();

		public bool FailFresh { get; set; }

		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default)
		{
			if (FailFresh)
			{
				throw new HttpRequestException("публичные тикеры недоступны");
			}

			return FreshMarks.TryGetValue(symbol, out var mark)
				? Task.FromResult<InstrumentMarkSnapshot?>(new InstrumentMarkSnapshot(symbol, mark.Price, mark.ReceivedAt))
				: Task.FromResult<InstrumentMarkSnapshot?>(null);
		}

		public Task<decimal?> GetLastMarkAsync(string symbol, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<decimal?>(FreshMarks.TryGetValue(symbol, out var mark) ? mark.Price : null);
		}
	}

	#endregion
}
