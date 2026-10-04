using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Components.Pages;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.ReadModels;

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

	private static readonly DateTimeOffset OldMoment = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

	private static readonly DateTimeOffset NewMoment = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

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
	[Description("Список упорядочен по статусу и датам: открытые выше закрытых, внутри групп от новых к старым")]
	public async Task TryIfListSortedByStatusThenCloseDateThenOpenDate()
	{
		// Arrange: пять конструкций в порядке вставки идентификаторов — старая
		// закрытая, новая закрытая, закрытая без даты закрытия, старая открытая,
		// новая открытая; даты приходят из метрик аналитики.
		await SeedAsync(
			Header("Старая закрытая", ConstructionStatus.Closed, 1000m),
			Header("Новая закрытая", ConstructionStatus.Closed, 1000m),
			Header("Закрытая без даты", ConstructionStatus.Closed, 1000m),
			Header("Старая открытая", ConstructionStatus.Open, 1000m),
			Header("Новая открытая", ConstructionStatus.Open, 1000m));
		SetupMetrics(null,
			MetricsWithMoments(1, closedAt: OldMoment, openedAt: OldMoment),
			MetricsWithMoments(2, closedAt: NewMoment, openedAt: OldMoment),
			MetricsWithMoments(3, closedAt: null, openedAt: OldMoment),
			MetricsWithMoments(4, closedAt: null, openedAt: OldMoment),
			MetricsWithMoments(5, closedAt: null, openedAt: NewMoment));

		// Act: читаем данные экрана «Конструкции».
		var data = await _readModel.ReadAsync();

		// Assert: сначала открытые — по дате открытия от более новых к более
		// старым, затем закрытые — по дате закрытия от более новых к более
		// старым, строки без даты закрытия завершают группу.
		// Требование: список упорядочен по статусу и датам.
		// Traceability: openspec:ui/screens#scenario-list-sorted-by-status-and-dates
		Assert.That(data.Items.Select(item => item.ConstructionId).ToArray(), Is.EqualTo(new long[] { 5, 4, 2, 1, 3 }));
	}

	[TestMethod]
	[Description("Строка конструкции без капитала несёт незаданный капитал и только введённые единицы риск/профит")]
	public async Task TryIfRowWithoutCapitalKeepsNullCapitalAndOnlyEnteredTargetUnit()
	{
		// Arrange: конструкция без выделенного капитала с риском 5% и профитом
		// 150 USDT; метрики аналитики соответствуют отсутствию базы процентов.
		await SeedAsync(new Construction
		{
			Name = "Без бюджета",
			Status = ConstructionStatus.Open,
			AllocatedCapitalUsdt = null,
			RiskValue = 5m,
			RiskUnit = TargetUnit.Percent,
			ProfitValue = 150m,
			ProfitUnit = TargetUnit.Usdt,
		});
		SetupMetrics(20m, new ConstructionMetrics
		{
			ConstructionId = 1,
			AllocatedCapitalUsdt = null,
			RealizedPnL = 20m,
			UnrealizedPnL = 0m,
			AdjustmentsPnL = 0m,
			TotalPnL = 20m,
			RealizedPnLPercent = null,
			UnrealizedPnLPercent = null,
			AdjustmentsPnLPercent = null,
			TotalPnLPercent = null,
			OpenedAt = OpenedAt,
			ClosedAt = null,
			Duration = TimeSpan.FromDays(1),
		});

		// Act: читаем данные экрана.
		var data = await _readModel.ReadAsync();

		// Assert: незаданный капитал доходит до строки как null — прочерк ставит
		// представление; абсолютные величины результата видимы, процент от
		// капитала отсутствует; без капитала конвертер вернул только введённые
		// единицы: проценты риска без USDT и USDT профита без процентов.
		// Требование: строка без капитала показывает прочерк вместо процентов.
		// Traceability: openspec:ui/screens#scenario-list-no-capital-percent-dash
		// Traceability: openspec:analytics/performance#scenario-conversion-needs-capital
		var row = data.Items.Single();
		Assert.That(row.AllocatedCapitalUsdt, Is.Null);
		Assert.That(row.RealizedPnL, Is.EqualTo(20m));
		Assert.That(row.TotalPnL, Is.EqualTo(20m));
		Assert.That(row.TotalPnLPercent, Is.Null);
		Assert.That(row.RiskPercent, Is.EqualTo(5m));
		Assert.That(row.RiskUsdt, Is.Null);
		Assert.That(row.ProfitUsdt, Is.EqualTo(150m));
		Assert.That(row.ProfitPercent, Is.Null);
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
	[Description("Сводка переносит разбивку PnL журнала из метрик аналитики без пересчёта")]
	public async Task TryIfSummaryCarriesPnlBreakdownFromMetrics()
	{
		// Arrange: аналитика вернула разбивку, отличную от итога и от частей
		// строк, — модель списка должна пронести её как есть, не пересчитывая.
		// Настройка мока собственная: общая заглушка SetupMetrics согласует
		// разбивку с итогом, здесь важна независимость величин.
		// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
		await SeedAsync(
			Header("Календарь сентябрь", ConstructionStatus.Open, 3000m),
			Header("Контртренд ETH", ConstructionStatus.Closed, 2000m));
		_metrics
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new JournalMetrics
			{
				Constructions =
				[
					MetricsOf(1, 214.32m, -58.2m),
					MetricsOf(2, 60m, 5m),
				],
				Positions = [],
				TotalPnL = 221.12m,
				RealizedPnL = 274.32m,
				UnrealizedPnL = -53.2m,
				MarksAsOf = null,
				HasMarkFailure = false,
			});

		// Act: читаем данные экрана.
		var data = await _readModel.ReadAsync();

		// Assert: реализованный и нереализованный агрегаты журнала дошли до
		// сводки ровно в том виде, в каком их отдала аналитика.
		// Требование: правила агрегации и деградации принадлежат аналитике,
		// список их не повторяет.
		// Traceability: openspec:analytics/performance#requirement-journal-pnl-aggregates
		Assert.That(data.TotalPnL, Is.EqualTo(221.12m));
		Assert.That(data.RealizedPnL, Is.EqualTo(274.32m));
		Assert.That(data.UnrealizedPnL, Is.EqualTo(-53.2m));
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
		// и итог строки null, итог журнала неполный, нереализованный агрегат
		// погашен, отметка времени марок неизвестна с признаком сбоя;
		// реализованный агрегат (−1 комиссия) остаётся суммой по всем конструкциям.
		// Требование: сбой марок показывается признаком в нереализованных
		// столбцах, реализованные величины остаются видимыми.
		// Traceability: openspec:ui/screens#scenario-list-marks-failure-indicated
		// Traceability: openspec:analytics/performance#scenario-journal-unrealized-null-on-any-mark-failure
		// Traceability: change:add-ui-screens/design#d4
		Assert.That(data.HasMarkFailure, Is.True);
		Assert.That(data.TotalPnL, Is.Null);
		Assert.That(data.RealizedPnL, Is.EqualTo(-1m));
		Assert.That(data.UnrealizedPnL, Is.Null);
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
				// Разбивка списком не читается: слагаемые согласованы с итогом.
				RealizedPnL = totalPnL ?? 0m,
				UnrealizedPnL = totalPnL is null ? null : 0m,
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

	/// <summary>Метрики конструкции с заданными моментами открытия и закрытия: остальное — простые значения.</summary>
	private static ConstructionMetrics MetricsWithMoments(long constructionId, DateTimeOffset? closedAt, DateTimeOffset openedAt) => new()
	{
		ConstructionId = constructionId,
		AllocatedCapitalUsdt = 1000m,
		RealizedPnL = 10m,
		UnrealizedPnL = 0m,
		AdjustmentsPnL = 0m,
		TotalPnL = 10m,
		RealizedPnLPercent = 1m,
		UnrealizedPnLPercent = 0m,
		AdjustmentsPnLPercent = 0m,
		TotalPnLPercent = 1m,
		OpenedAt = openedAt,
		ClosedAt = closedAt,
		Duration = TimeSpan.FromDays(92),
	};

	#endregion
}
