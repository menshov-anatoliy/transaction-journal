using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using TransactionJournal.Domain.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.UseCases;

/// <summary>
/// Проверки use-case сервиса оспаривания связок: оспоренная связка исключается
/// из распознавания до явного возврата, повторное оспаривание безвредно,
/// возврат снова открывает связку распознаванию; отказы на неизвестную
/// конструкцию, пустой execId и неоспоренную пару.
/// </summary>
[TestClass]
public class SyntheticCloseDisputeServiceTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";

	private string _databasePath = null!;
	private SyntheticCloseDisputeService _disputeService = null!;
	private ConstructionService _constructionService = null!;
	private FixedTimeProvider _timeProvider = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-dispute-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 10, 15, 13, 30, 0, TimeSpan.Zero));
		_disputeService = new SyntheticCloseDisputeService(CreateOptions(), _timeProvider);
		_constructionService = new ConstructionService(CreateOptions());
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
	[Description("Спор исключает распознанную связку из логического слоя до явного возврата")]
	// После оспаривания фильтр над списком споров сервиса скрывает связку:
	// логическая нога возвращается к полному размеру. После возврата связка
	// распознаётся снова автоматически — распознавание чистая функция записей.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
	public async Task TryIfDisputeExcludesLinkUntilRevert()
	{
		// Arrange: конструкция и связка кейса 15.10.2026 — покупка 0.3 фьючерса
		// и продажа 0.3 колла закрывают часть путовой ноги стреддла.
		var construction = await _constructionService.CreateAsync("XAUT стреддл 30OCT26 4400", 100m);
		var links = new List<SyntheticCloseLink> { RecognizedXautLink() };

		// Act: оспариваем связку и фильтруем распознанный прогон списком споров.
		await _disputeService.DisputeAsync(construction.Id, "case-perp", "case-call");
		var filteredWhileDisputed = SyntheticCloseDisputeFilter.ExcludeDisputed(
			links, ReadDisputes(construction.Id));

		// Assert: спорная связка скрыта из логического слоя.
		Assert.That(filteredWhileDisputed, Is.Empty, "Спорная связка исключена из распознавания");

		// Act: возвращаем связку и фильтруем снова.
		await _disputeService.RevertAsync(construction.Id, "case-perp", "case-call");
		var filteredAfterRevert = SyntheticCloseDisputeFilter.ExcludeDisputed(
			links, ReadDisputes(construction.Id));

		// Assert: связка вернулась — споров нет, распознавание видит её опять.
		Assert.That(filteredAfterRevert.Select(link => link.LinearExecId), Is.EqualTo(new[] { "case-perp" }));
	}

	[TestMethod]
	[Description("Запись оспаривания хранит пару execId и момент оспаривания поставщика времени")]
	public async Task TryIfDisputeStoredWithPairAndDisputedAt()
	{
		// Arrange: конструкция и момент фиксированных часов.
		var construction = await _constructionService.CreateAsync("XAUT стреддл 30OCT26 4400", 100m);

		// Act: оспариваем связку.
		await _disputeService.DisputeAsync(construction.Id, "case-perp", "case-call");

		// Assert: запись хранит пару связки и время фиксированного поставщика.
		var dispute = ReadDisputes(construction.Id).Single();
		Assert.That(dispute.ConstructionId, Is.EqualTo(construction.Id));
		Assert.That(dispute.LinearExecId, Is.EqualTo("case-perp"));
		Assert.That(dispute.CounterOptionExecId, Is.EqualTo("case-call"));
		Assert.That(dispute.DisputedAt, Is.EqualTo(_timeProvider.UtcNow));
	}

	[TestMethod]
	[Description("Повторное оспаривание той же связки безвредно: запись одна, момент не меняется")]
	// Спор связки один: уникальный индекс по «конструкция + пара execId» не
	// допускает двух записей, повторный вызов не переписывает момент оспаривания.
	// Traceability: change:synthetic-close-logical-layer/design#d3
	public async Task TryIfRepeatedDisputeIsIdempotent()
	{
		// Arrange: конструкция и первое оспаривание.
		var construction = await _constructionService.CreateAsync("XAUT стреддл 30OCT26 4400", 100m);
		await _disputeService.DisputeAsync(construction.Id, "case-perp", "case-call");
		var first = ReadDisputes(construction.Id).Single();

		// Act: второе оспаривание той же пары через час по фиксированным часам.
		_timeProvider.UtcNow = _timeProvider.UtcNow.AddHours(1);
		await _disputeService.DisputeAsync(construction.Id, "case-perp", "case-call");

		// Assert: запись осталась прежней — одна, с исходным моментом.
		var disputes = ReadDisputes(construction.Id);
		Assert.That(disputes, Has.Count.EqualTo(1), "Спор связки не задублирован");
		Assert.That(disputes[0].Id, Is.EqualTo(first.Id));
		Assert.That(disputes[0].DisputedAt, Is.EqualTo(first.DisputedAt), "Момент оспаривания не переписан");
	}

	[TestMethod]
	[Description("Возврат неоспоренной пары отказывает")]
	[ExpectedException(typeof(SyntheticCloseDisputeNotFoundException))]
	public async Task ThrowOnRevertUnknownPair()
	{
		// Arrange: конструкция без споров.
		var construction = await _constructionService.CreateAsync("XAUT стреддл 30OCT26 4400", 100m);

		// Act: возврат пары, которую никто не оспаривал.
		await _disputeService.RevertAsync(construction.Id, "case-perp", "case-call");
	}

	[TestMethod]
	[Description("Оспаривание с пустым execId отказывает")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnDisputeWithEmptyExecId()
	{
		// Arrange: конструкция существует.
		var construction = await _constructionService.CreateAsync("XAUT стреддл 30OCT26 4400", 100m);

		// Act: линейный execId из пробелов.
		await _disputeService.DisputeAsync(construction.Id, "   ", "case-call");
	}

	[TestMethod]
	[Description("Оспаривание с пустым встречным execId отказывает")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnDisputeWithEmptyCounterExecId()
	{
		// Arrange: конструкция существует.
		var construction = await _constructionService.CreateAsync("XAUT стреддл 30OCT26 4400", 100m);

		// Act: встречный execId из пробелов.
		await _disputeService.DisputeAsync(construction.Id, "case-perp", " ");
	}

	[TestMethod]
	[Description("Оспаривание неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnDisputeWithUnknownConstruction()
	{
		// Arrange — Act: спор в несуществующую конструкцию.
		await _disputeService.DisputeAsync(999, "case-perp", "case-call");
	}

	[TestMethod]
	[Description("Возврат с пустым execId отказывает")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnRevertWithEmptyExecId()
	{
		// Arrange — Act: возврат пары с execId из пробелов.
		await _disputeService.RevertAsync(1, "case-perp", "  ");
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new SyntheticCloseDisputeService(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>
	/// Читает записи споров конструкции напрямую из хранилища: сервис спорами
	/// только командует, наблюдение результата делается по данным базы.
	/// </summary>
	private List<SyntheticCloseDispute> ReadDisputes(long constructionId)
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.SyntheticCloseDisputes
			.AsNoTracking()
			.Where(dispute => dispute.ConstructionId == constructionId)
			.OrderBy(dispute => dispute.Id)
			.ToList();
	}

	/// <summary>Связка кейса 15.10.2026: покупка 0.3 XAUTUSDT и продажа 0.3 колла 4400.</summary>
	private static SyntheticCloseLink RecognizedXautLink() => new()
	{
		LinearExecId = "case-perp",
		LinearExecTimeMs = new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
		CounterOptionExecId = "case-call",
		CounterOptionExecTimeMs = new DateTimeOffset(2026, 10, 15, 13, 4, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
		BaseCoin = "XAUT",
		ClosedLegSymbol = XautPut4400,
		ClosedLegType = OptionType.Put,
		Strike = 4400m,
		BoardExpiryDate = new DateTime(2026, 10, 30),
		Quantity = 0.3m,
	};

	#endregion
}
