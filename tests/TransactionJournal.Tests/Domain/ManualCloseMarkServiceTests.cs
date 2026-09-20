using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain;

/// <summary>
/// Проверки use-case сервиса ручных пометок закрытия: добавление с ценой пользователя
/// или без неё, свободная правка атрибутов, свободное удаление и чтение списка
/// пометок конструкции; отказы на неизвестную конструкцию, пустой инструмент
/// и несуществующую пометку.
/// </summary>
[TestClass]
public class ManualCloseMarkServiceTests
{
	private string _databasePath = null!;
	private ManualCloseMarkService _markService = null!;
	private ConstructionService _constructionService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-mark-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_markService = new ManualCloseMarkService(CreateOptions());
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
	[Description("Пометка добавляется с атрибутами: цена пользователя хранится, без цены — null")]
	public async Task TryIfMarkAddedWithAttributesAndOptionalPrice()
	{
		// Arrange: конструкция с позицией без биржевых записей закрытия.
		var construction = await _constructionService.CreateAsync("Диапазон BTC", 500m);
		var markedAt = new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero);

		// Act: добавляем две пометки — с ценой пользователя и без цены.
		var withPrice = await _markService.AddAsync(construction.Id, "BTCUSDT", markedAt, 42100.55m);
		var withoutPrice = await _markService.AddAsync(construction.Id, "ETHUSDT", markedAt.AddDays(1));

		// Assert: обе пометки возвращаются конструкцией с полным набором атрибутов;
		// незаданная цена хранится null — производный слой подставит последнюю марку
		// при чтении, а не в момент добавления.
		// Требование: цена пометки задаётся пользователем, по умолчанию — последняя марка.
		// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
		var marks = await _markService.ListAsync(construction.Id);
		Assert.That(marks.Select(mark => mark.Id).ToArray(),
			Is.EqualTo(new[] { withPrice.Id, withoutPrice.Id }));
		Assert.That(marks[0].ConstructionId, Is.EqualTo(construction.Id));
		Assert.That(marks[0].Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(marks[0].MarkedAt, Is.EqualTo(markedAt));
		Assert.That(marks[0].Price, Is.EqualTo(42100.55m));
		Assert.That(marks[1].Symbol, Is.EqualTo("ETHUSDT"));
		Assert.That(marks[1].MarkedAt, Is.EqualTo(markedAt.AddDays(1)));
		Assert.That(marks[1].Price, Is.Null);
	}

	[TestMethod]
	[Description("Правка пометки свободна: атрибуты заменяются целиком")]
	public async Task TryIfMarkEditedFreely()
	{
		// Arrange: пометка с первоначальными атрибутами.
		var construction = await _constructionService.CreateAsync("Диапазон BTC", 500m);
		var mark = await _markService.AddAsync(
			construction.Id, "BTCUSDT", new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero), 42100m);

		// Act: свободно правим инструмент, время и цену.
		var editedAt = new DateTimeOffset(2023, 12, 31, 9, 30, 0, TimeSpan.Zero);
		await _markService.EditAsync(mark.Id, "ETHUSDT", editedAt, null);

		// Assert: пометка хранит новые атрибуты; правка не ограничена и не требует
		// синхронных правок производных — позиции пересчитаются при чтении.
		// Требование: ручная пометка свободно правится.
		// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
		var edited = (await _markService.ListAsync(construction.Id)).Single();
		Assert.That(edited.Id, Is.EqualTo(mark.Id));
		Assert.That(edited.Symbol, Is.EqualTo("ETHUSDT"));
		Assert.That(edited.MarkedAt, Is.EqualTo(editedAt));
		Assert.That(edited.Price, Is.Null);
	}

	[TestMethod]
	[Description("Удаление пометки свободно: список конструкции пустеет")]
	public async Task TryIfMarkDeletedFreely()
	{
		// Arrange: конструкция с одной пометкой.
		var construction = await _constructionService.CreateAsync("Диапазон BTC", 500m);
		var mark = await _markService.AddAsync(
			construction.Id, "BTCUSDT", new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero), 42100m);

		// Act: удаляем пометку.
		await _markService.DeleteAsync(mark.Id);

		// Assert: пометок у конструкции не осталось — удаление свободно, без аудита
		// и ограничений MVP.
		// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
		var marks = await _markService.ListAsync(construction.Id);
		Assert.That(marks, Is.Empty);
	}

	[TestMethod]
	[Description("Добавление пометки к неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnAddWithUnknownConstruction()
	{
		// Arrange — Act: пометка в несуществующую конструкцию.
		await _markService.AddAsync(999, "BTCUSDT", DateTimeOffset.UtcNow, 42100m);
	}

	[TestMethod]
	[Description("Добавление пометки с пустым инструментом отказывает")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnAddWithEmptySymbol()
	{
		// Arrange: конструкция существует.
		var construction = await _constructionService.CreateAsync("Диапазон BTC", 500m);

		// Act: инструмент из пробелов.
		await _markService.AddAsync(construction.Id, "   ", DateTimeOffset.UtcNow);
	}

	[TestMethod]
	[Description("Правка несуществующей пометки отказывает")]
	[ExpectedException(typeof(ManualCloseMarkNotFoundException))]
	public async Task ThrowOnEditUnknownMark()
	{
		// Arrange — Act: правка пометки, которой нет.
		await _markService.EditAsync(999, "BTCUSDT", DateTimeOffset.UtcNow, 42100m);
	}

	[TestMethod]
	[Description("Удаление несуществующей пометки отказывает")]
	[ExpectedException(typeof(ManualCloseMarkNotFoundException))]
	public async Task ThrowOnDeleteUnknownMark()
	{
		// Arrange — Act: удаление пометки, которой нет.
		await _markService.DeleteAsync(999);
	}

	[TestMethod]
	[Description("Чтение пометок неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnListUnknownConstruction()
	{
		// Arrange — Act: список пометок несуществующей конструкции.
		await _markService.ListAsync(999);
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new ManualCloseMarkService(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	#endregion
}
