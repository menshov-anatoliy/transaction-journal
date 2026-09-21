using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Интеграционные проверки read-модели экрана «Конструкции» на стыке с метриками
/// аналитики: строки списка соединяют заголовки хранилища (имя, ручной статус,
/// капитал) с метриками журнала, архивные конструкции скрыты из строк и счётчика
/// сводки, итог и отметка марок проходят из аналитики без пересчёта, а сбой
/// марок провайдера доходит до данных экрана признаком.
/// Traceability: openspec:ui/screens#requirement-construction-list-screen
/// </summary>
[TestClass]
public class ConstructionListReadModelTests
{
	private static readonly DateTimeOffset OpenedAt = new(2026, 6, 20, 9, 30, 0, TimeSpan.Zero);

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private const string LinearSymbol = "BTCUSDT";

	private string _databasePath = null!;
	private Mock<IJournalMetricsReadModel> _metrics = null!;
	private ConstructionListReadModel _readModel = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке;
		// метрики аналитики подменяются моком — модель списка их не считает.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-construction-list-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_metrics = new Mock<IJournalMetricsReadModel>();
		_readModel = new ConstructionListReadModel(CreateOptions(), _metrics.Object);
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
	[Description("Архивная конструкция скрыта из строк списка и счётчика сводки")]
	public async Task TryIfArchivedConstructionHiddenFromListAndCounter()
	{
		// Arrange: в журнале три конструкции — открытая, закрытая и архивная;
		// метрики аналитики посчитаны по всем трём, включая архивную.
		await SeedAsync(
			Header("Календарь сентябрь", ConstructionStatus.Open, 3000m),
			Header("Контртренд ETH", ConstructionStatus.Closed, 2000m),
			Header("Песочница робота", ConstructionStatus.Archived, 500m));
		SetupMetrics(243.52m,
			MetricsOf(1, 200m, 43.52m),
			MetricsOf(2, 43.52m, 0m),
			MetricsOf(3, 38.15m, 0m));

		// Act: читаем данные экрана «Конструкции».
		var data = await _readModel.ReadAsync();

		// Assert: архивная конструкция не появляется в таблице списка — ни строкой,
		// ни местом в счётчике; её вклад остаётся только в итоге журнала.
		// Требование: архивные конструкции скрыты из списка.
		// Traceability: openspec:ui/screens#scenario-archived-not-in-list
		Assert.That(data.Items.Select(item => item.ConstructionId).ToArray(), Is.EqualTo(new long[] { 1, 2 }));
		Assert.That(data.Items.Select(item => item.Name), Does.Not.Contain("Песочница робота"));
		Assert.That(data.ConstructionCount, Is.EqualTo(2));
		Assert.That(data.OpenCount, Is.EqualTo(1));
		Assert.That(data.TotalPnL, Is.EqualTo(243.52m));
	}

	[TestMethod]
	[Description("Строки списка соединяют заголовки хранилища с метриками аналитики")]
	public async Task TryIfRowsJoinHeadersWithAnalyticsMetrics()
	{
		// Arrange: открытая конструкция с капиталом и метриками всех столбцов списка.
		await SeedAsync(Header("Календарь сентябрь", ConstructionStatus.Open, 3000m));
		SetupMetrics(null, MetricsOf(1, 214.32m, -58.2m));

		// Act: читаем данные экрана.
		var data = await _readModel.ReadAsync();

		// Assert: строка несёт имя и статус из хранилища, капитал и метрики —
		// из аналитики; недоступная нереализованная оценка передаётся как null.
		var row = data.Items.Single();
		Assert.That(row.Name, Is.EqualTo("Календарь сентябрь"));
		Assert.That(row.Status, Is.EqualTo(ConstructionStatus.Open));
		Assert.That(row.AllocatedCapitalUsdt, Is.EqualTo(3000m));
		Assert.That(row.RealizedPnL, Is.EqualTo(214.32m));
		Assert.That(row.UnrealizedPnL, Is.EqualTo(-58.2m));
		Assert.That(row.AdjustmentsPnL, Is.EqualTo(0m));
		Assert.That(row.OpenedAt, Is.EqualTo(OpenedAt));
		Assert.That(row.ClosedAt, Is.Null);
	}

	[TestMethod]
	[Description("Пустой журнал даёт пустой список и нулевой счётчик")]
	public async Task TryIfEmptyJournalGivesEmptyList()
	{
		// Arrange: constructions нет, метрики пусты, итог журнала ноль.
		SetupMetrics(0m);

		// Act: читаем данные экрана.
		var data = await _readModel.ReadAsync();

		// Assert: список пуст, счётчики нулевые — экран покажет явное сообщение.
		Assert.That(data.Items, Is.Empty);
		Assert.That(data.ConstructionCount, Is.EqualTo(0));
		Assert.That(data.OpenCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Сбой марок на заглушке провайдера доходит до данных экрана: нереализованная оценка и итог деградируют")]
	public async Task TryIfMarkFailurePassesFromProviderStubToScreenData()
	{
		// Arrange: конструкция с открытым остатком — покупка 0.1 BTC по 42000
		// с комиссией 1; заглушка провайдера марок имитирует недоступность
		// публичных тикеров ошибкой сети.
		await SeedAsync(Header("Длинный BTC", ConstructionStatus.Open, 1000m));
		await SeedLinearInstrumentAsync();
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await new TradeBindingService(CreateOptions()).BindBatchAsync(1, ["exec-buy"]);
		var metrics = new JournalMetricsReadModel(CreateOptions(), new FailingMarkSource());
		var readModel = new ConstructionListReadModel(CreateOptions(), metrics);

		// Act: чтение данных экрана «Конструкции» при сбойном провайдере.
		var data = await readModel.ReadAsync();

		// Assert: сбой марок доходит до данных списка — нереализованная оценка
		// и итог строки null, итог журнала неполный, отметка времени марок
		// неизвестна с признаком сбоя; реализованный результат (−1 комиссия)
		// остаётся видимым.
		// Требование: сбой марок показывается признаком в нереализованных
		// столбцах, реализованные величины остаются видимыми.
		// Traceability: openspec:ui/screens#scenario-list-marks-failure-indicated
		// Traceability: change:add-ui-screens/design#d4
		Assert.That(data.HasMarkFailure, Is.True);
		Assert.That(data.TotalPnL, Is.Null);
		Assert.That(data.MarksAsOf, Is.Null);
		var row = data.Items.Single();
		Assert.That(row.UnrealizedPnL, Is.Null);
		Assert.That(row.TotalPnL, Is.Null);
		Assert.That(row.RealizedPnL, Is.EqualTo(-1m));
	}

	[TestMethod]
	[Description("Повреждённое сырьё метрик останавливает чтение списка внятной ошибкой")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public async Task ThrowOnBrokenRawMetrics()
	{
		// Arrange: read-модель метрик падает на повреждённой сырой записи исполнения.
		_metrics
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new TradeMaterializationException("exec-broken", "повреждённая запись"));

		// Act — Assert: чтение списка не глотает ошибку сырья — экран покажет
		// явное состояние недоступности журнала.
		await _readModel.ReadAsync();
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new ConstructionListReadModel(null!, _metrics.Object);
	}

	[TestMethod]
	[Description("Null-модель метрик отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullMetrics()
	{
		// Arrange — Act — Assert
		new ConstructionListReadModel(CreateOptions(), null!);
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

	private static Construction Header(string name, ConstructionStatus status, decimal capital) => new()
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

	/// <summary>
	/// Заглушка провайдера марок для сценария сбоя: свежая марка всегда недоступна —
	/// сеть до публичных тикеров не доходит, аналитика деградирует оценкой, а не ошибкой.
	/// </summary>
	private sealed class FailingMarkSource : IFreshInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			throw new HttpRequestException("публичные тикеры недоступны");
	}

	/// <summary>Подменяет метрики журнала: итог и список метрик конструкций по идентификаторам.</summary>
	private void SetupMetrics(decimal? totalPnL, params ConstructionMetrics[] constructions) =>
		_metrics
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new JournalMetrics
			{
				Constructions = constructions,
				Positions = [],
				TotalPnL = totalPnL,
				MarksAsOf = null,
				HasMarkFailure = totalPnL is null,
			});

	/// <summary>Метрики конструкции с простыми значениями: корректировки нулевые, итог — сумма слагаемых.</summary>
	private static ConstructionMetrics MetricsOf(long constructionId, decimal realized, decimal? unrealized) => new()
	{
		ConstructionId = constructionId,
		AllocatedCapitalUsdt = 1000m,
		RealizedPnL = realized,
		UnrealizedPnL = unrealized,
		AdjustmentsPnL = 0m,
		TotalPnL = unrealized is null ? null : realized + unrealized.Value,
		RealizedPnLPercent = realized / 10m,
		UnrealizedPnLPercent = unrealized / 10m,
		AdjustmentsPnLPercent = 0m,
		TotalPnLPercent = unrealized is null ? null : (realized + unrealized.Value) / 10m,
		OpenedAt = OpenedAt,
		ClosedAt = null,
		Duration = TimeSpan.FromDays(92),
	};

	#endregion
}
