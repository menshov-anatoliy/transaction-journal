using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Интеграционные проверки read-модели экрана «Конструкции» на стыке с метриками
/// аналитики: строки списка соединяют заголовки хранилища (имя, ручной статус,
/// капитал) с метриками журнала, архивные конструкции скрыты из строк и счётчика
/// сводки, а итог и отметка марок проходят из аналитики без пересчёта.
/// Traceability: openspec:ui/screens#requirement-construction-list-screen
/// </summary>
[TestClass]
public class ConstructionListReadModelTests
{
	private static readonly DateTimeOffset OpenedAt = new(2026, 6, 20, 9, 30, 0, TimeSpan.Zero);

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
