using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain;

/// <summary>
/// Проверки use-case сервиса внешних корректировок PnL: корректировка робота
/// входит в результат конструкции как слагаемое наравне с торговым результатом
/// (суммирование — через тестовую заглушку, сам расчёт PnL — capability
/// аналитики), атрибуты хранятся и возвращаются целиком, правка и удаление
/// свободны; отказы на неизвестную конструкцию и несуществующую корректировку.
/// </summary>
[TestClass]
public class PnLAdjustmentServiceTests
{
	private string _databasePath = null!;
	private PnLAdjustmentService _service = null!;
	private ConstructionService _constructionService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-adjustment-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_service = new PnLAdjustmentService(CreateOptions());
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
	[Description("Корректировка робота входит в результат конструкции наравне с торговым результатом")]
	public async Task TryIfRobotAdjustmentEntersConstructionResult()
	{
		// Arrange: конструкция с корректировкой PnL робота и ручной поправкой;
		// торговый результат взят заглушкой — сам расчёт принадлежит аналитике.
		var construction = await _constructionService.CreateAsync("Робот на диапазоне", 1000m);
		await _service.AddAsync(
			construction.Id,
			new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3)),
			PnLAdjustmentSource.Robot,
			25.5m);
		await _service.AddAsync(
			construction.Id,
			new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(3)),
			PnLAdjustmentSource.Manual,
			-5.5m);
		const decimal stubTradeResult = 10m;

		// Act: собираем результат конструкции заглушкой — торговая часть
		// плюс сумма корректировок, как это сделает capability аналитики.
		var adjustments = await _service.ListAsync(construction.Id);
		var result = ComputeResultStub(stubTradeResult, adjustments);

		// Assert: обе корректировки вошли в результат слагаемыми наравне
		// с торговым результатом: 10 + 25,5 − 5,5 = 30.
		// Требование: корректировка робота учитывается в результате конструкции
		// как слагаемое наравне с торговым результатом.
		// Traceability: openspec:domain/constructions#scenario-robot-adjustment-in-result
		Assert.That(result, Is.EqualTo(30m));
	}

	[TestMethod]
	[Description("Атрибуты корректировки — дата, источник, знаковая сумма и комментарий — хранятся и возвращаются целиком")]
	public async Task TryIfAdjustmentAttributesStoredAndReturnedWhole()
	{
		// Arrange: конструкция с корректировкой робота и ручной корректировкой;
		// у каждой заданы все четыре атрибута, включая отрицательную сумму.
		var construction = await _constructionService.CreateAsync("Смешанный результат", 800m);
		var robotDate = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3));
		var manualDate = new DateTimeOffset(2026, 9, 3, 18, 30, 0, TimeSpan.FromHours(3));
		await _service.AddAsync(construction.Id, robotDate, PnLAdjustmentSource.Robot, 42m, "PnL утренней сессии");
		await _service.AddAsync(construction.Id, manualDate, PnLAdjustmentSource.Manual, -7.25m, "Поправка проскальзывания");

		// Act: читаем корректировки конструкции.
		var adjustments = await _service.ListAsync(construction.Id);

		// Assert: читающий слой возвращает все четыре атрибута каждой корректировки;
		// порядок хронологический по дате.
		// Требование: атрибуты корректировки хранятся и возвращаются целиком.
		// Traceability: openspec:domain/constructions#scenario-adjustment-attributes-stored
		Assert.That(adjustments.Count, Is.EqualTo(2));

		var robot = adjustments[0];
		Assert.That(robot.ConstructionId, Is.EqualTo(construction.Id));
		Assert.That(robot.Date, Is.EqualTo(robotDate));
		Assert.That(robot.Source, Is.EqualTo(PnLAdjustmentSource.Robot));
		Assert.That(robot.AmountUsdt, Is.EqualTo(42m));
		Assert.That(robot.Comment, Is.EqualTo("PnL утренней сессии"));

		var manual = adjustments[1];
		Assert.That(manual.Date, Is.EqualTo(manualDate));
		Assert.That(manual.Source, Is.EqualTo(PnLAdjustmentSource.Manual));
		Assert.That(manual.AmountUsdt, Is.EqualTo(-7.25m));
		Assert.That(manual.Comment, Is.EqualTo("Поправка проскальзывания"));
	}

	[TestMethod]
	[Description("Правка и удаление корректировки свободны: результат пересчитывается с новым набором")]
	public async Task TryIfAdjustmentEditAndDeleteAreFree()
	{
		// Arrange: конструкция с двумя корректировками — правим одну, удаляем другую.
		var construction = await _constructionService.CreateAsync("Правки свободны", 600m);
		var kept = await _service.AddAsync(
			construction.Id,
			new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3)),
			PnLAdjustmentSource.Manual,
			10m);
		var removed = await _service.AddAsync(
			construction.Id,
			new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(3)),
			PnLAdjustmentSource.Robot,
			-3m);

		// Act: свободно правим все атрибуты оставшейся корректировки
		// и удаляем вторую.
		var editedDate = new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.FromHours(3));
		await _service.EditAsync(kept.Id, editedDate, PnLAdjustmentSource.Robot, -12.5m, "Развернули знак");
		await _service.DeleteAsync(removed.Id);

		// Assert: в конструкции осталась одна корректировка с новыми атрибутами;
		// результат заглушкой пересчитан с новым набором без ограничений.
		// Требование: правка и удаление корректировки свободны, результат
		// пересчитывается с новым набором корректировок.
		// Traceability: openspec:domain/constructions#scenario-adjustment-edit-delete-free
		var adjustments = await _service.ListAsync(construction.Id);
		Assert.That(adjustments.Count, Is.EqualTo(1));
		Assert.That(adjustments[0].Id, Is.EqualTo(kept.Id));
		Assert.That(adjustments[0].Date, Is.EqualTo(editedDate));
		Assert.That(adjustments[0].Source, Is.EqualTo(PnLAdjustmentSource.Robot));
		Assert.That(adjustments[0].AmountUsdt, Is.EqualTo(-12.5m));
		Assert.That(adjustments[0].Comment, Is.EqualTo("Развернули знак"));
		Assert.That(ComputeResultStub(5m, adjustments), Is.EqualTo(-7.5m));
	}

	[TestMethod]
	[Description("Добавление корректировки к неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnAddWithUnknownConstruction()
	{
		// Arrange — Act: корректировка в несуществующую конструкцию.
		await _service.AddAsync(999, DateTimeOffset.UtcNow, PnLAdjustmentSource.Robot, 10m);
	}

	[TestMethod]
	[Description("Правка несуществующей корректировки отказывает")]
	[ExpectedException(typeof(PnLAdjustmentNotFoundException))]
	public async Task ThrowOnEditUnknownAdjustment()
	{
		// Arrange — Act: правка корректировки, которой нет.
		await _service.EditAsync(999, DateTimeOffset.UtcNow, PnLAdjustmentSource.Manual, 10m, null);
	}

	[TestMethod]
	[Description("Удаление несуществующей корректировки отказывает")]
	[ExpectedException(typeof(PnLAdjustmentNotFoundException))]
	public async Task ThrowOnDeleteUnknownAdjustment()
	{
		// Arrange — Act: удаление корректировки, которой нет.
		await _service.DeleteAsync(999);
	}

	[TestMethod]
	[Description("Чтение корректировок неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnListUnknownConstruction()
	{
		// Arrange — Act: список корректировок несуществующей конструкции.
		await _service.ListAsync(999);
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new PnLAdjustmentService(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>
	/// Тестовая заглушка суммирования результата: торговая часть плюс сумма
	/// корректировок. Сам расчёт торгового результата — capability аналитики
	/// (тикет #10) и в этом change не реализуется.
	/// </summary>
	private static decimal ComputeResultStub(decimal tradeResult, IReadOnlyList<PnLAdjustment> adjustments) =>
		tradeResult + adjustments.Sum(adjustment => adjustment.AmountUsdt);

	#endregion
}
