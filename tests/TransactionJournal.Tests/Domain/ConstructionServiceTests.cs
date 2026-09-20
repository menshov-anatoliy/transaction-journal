using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain;

/// <summary>
/// Проверки use-case сервиса управления конструкциями: создание со статусом
/// «открыта», свободное переименование без побочных эффектов, ручная смена
/// статуса с архивацией, активные и полные списки чтения и удаление только
/// конструкций без сделок и корректировок.
/// </summary>
[TestClass]
public class ConstructionServiceTests
{
	private string _databasePath = null!;
	private ConstructionService _service = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-construction-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_service = new ConstructionService(CreateOptions());
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
	[Description("Новая конструкция открывается со статусом «открыта» и появляется в активном списке")]
	public async Task TryIfNewConstructionStartsOpenAndAppearsInActiveList()
	{
		// Act: создаём конструкцию с именем, выделенным капиталом и комментарием.
		var created = await _service.CreateAsync("Стреддл BTC", 1000m, "Первая конструкция");

		// Assert: стартовый статус — «открыта», атрибуты сохранены как заданы.
		// Требование: новая конструкция получает ручной статус «открыта» по умолчанию.
		// Traceability: openspec:domain/constructions#scenario-new-construction-default-open
		Assert.That(created.Name, Is.EqualTo("Стреддл BTC"));
		Assert.That(created.Status, Is.EqualTo(ConstructionStatus.Open));
		Assert.That(created.AllocatedCapitalUsdt, Is.EqualTo(1000m));
		Assert.That(created.Comment, Is.EqualTo("Первая конструкция"));

		// Assert: конструкция видна и в активном списке, и в полном списке аналитики.
		var activeIds = (await _service.ListActiveAsync()).Select(construction => construction.Id).ToList();
		Assert.That(activeIds, Does.Contain(created.Id));
		var allIds = (await _service.ListAllAsync()).Select(construction => construction.Id).ToList();
		Assert.That(allIds, Does.Contain(created.Id));
	}

	[TestMethod]
	[Description("Переименование конструкции меняет только имя: привязки, капитал, статус и комментарий не трогаются")]
	public async Task TryIfRenamePreservesBindingsCapitalStatusAndComment()
	{
		// Arrange: конструкция с капиталом, комментарием и статусом «закрыта»,
		// к которой привязана сделка со своим комментарием.
		var construction = await _service.CreateAsync("Хедж фьючерсом", 2500m, "Комментарий конструкции");
		await _service.ChangeStatusAsync(construction.Id, ConstructionStatus.Closed);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.TradeUserdata.Add(new TradeUserdata
			{
				ExecId = "exec-rename",
				ConstructionId = construction.Id,
				Comment = "Вход в конструкцию",
			});
			db.SaveChanges();
		}

		// Act: переименовываем конструкцию.
		await _service.RenameAsync(construction.Id, "Новое имя цели");

		// Assert: имя изменилось, остальные атрибуты конструкции не тронуты.
		// Требование: переименование свободно и не затрагивает прочие данные.
		// Traceability: openspec:domain/constructions#scenario-rename-preserves-everything
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var stored = db.Constructions.Single();
			Assert.That(stored.Name, Is.EqualTo("Новое имя цели"));
			Assert.That(stored.AllocatedCapitalUsdt, Is.EqualTo(2500m));
			Assert.That(stored.Status, Is.EqualTo(ConstructionStatus.Closed));
			Assert.That(stored.Comment, Is.EqualTo("Комментарий конструкции"));

			// Assert: привязка сделки и её комментарий остались на месте.
			var binding = db.TradeUserdata.Single();
			Assert.That(binding.ConstructionId, Is.EqualTo(construction.Id));
			Assert.That(binding.Comment, Is.EqualTo("Вход в конструкцию"));
		}
	}

	[TestMethod]
	[Description("Архивная конструкция скрыта из активного списка, но видна в полном списке аналитики")]
	public async Task TryIfArchivedConstructionHiddenFromActiveListButVisibleInAnalytics()
	{
		// Arrange: две открытые конструкции — одну архивируем.
		var activeConstruction = await _service.CreateAsync("Действующая", 500m);
		var oldConstruction = await _service.CreateAsync("Завершённая", 700m);

		// Act: переводим вторую конструкцию в архив.
		await _service.ArchiveAsync(oldConstruction.Id);

		// Assert: статус сохранён как «архив».
		// Требование: архивация скрывает конструкцию из активных списков,
		// сохраняя её и историю в аналитике.
		// Traceability: openspec:domain/constructions#scenario-archived-hidden-but-analyzed
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var stored = db.Constructions.Single(construction => construction.Id == oldConstruction.Id);
			Assert.That(stored.Status, Is.EqualTo(ConstructionStatus.Archived));
		}

		// Assert: в активном списке только действующая; полный список аналитики видит обе.
		var activeIds = (await _service.ListActiveAsync()).Select(construction => construction.Id).ToList();
		Assert.That(activeIds, Is.EqualTo(new[] { activeConstruction.Id }));
		var allIds = (await _service.ListAllAsync()).Select(construction => construction.Id).ToList();
		Assert.That(allIds, Is.EqualTo(new[] { activeConstruction.Id, oldConstruction.Id }));
	}

	[TestMethod]
	[Description("Смена статуса конструкции свободна: любое значение доступно из стартового статуса")]
	[DataRow(ConstructionStatus.Open)]
	[DataRow(ConstructionStatus.Closed)]
	[DataRow(ConstructionStatus.Archived)]
	public async Task TryIfStatusChangesFreely(ConstructionStatus targetStatus)
	{
		// Arrange: конструкция открывается со статусом «открыта».
		var construction = await _service.CreateAsync("Свободная смена статуса", 100m);

		// Act: переводим конструкцию в целевой статус.
		await _service.ChangeStatusAsync(construction.Id, targetStatus);

		// Assert: статус сохранён; автоматических ограничений на переходы нет.
		// Требование: статус конструкции — ручной, смена свободна.
		// Traceability: change:add-core-domain/design#d4
		using var db = new JournalDbContext(CreateOptions());
		var stored = db.Constructions.Single();
		Assert.That(stored.Status, Is.EqualTo(targetStatus));
	}

	[TestMethod]
	[Description("Удаление пустой конструкции проходит, осиротевшие записи уходят каскадом")]
	public async Task TryIfDeleteEmptyConstructionSucceeds()
	{
		// Arrange: конструкция без сделок и корректировок, но с осиротевшим
		// комментарием позиции — блокирующих записей нет, удаление допустимо.
		var construction = await _service.CreateAsync("Пустая после переноса", 100m);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.PositionComments.Add(new PositionComment
			{
				ConstructionId = construction.Id,
				Symbol = "ETHUSDT",
				Text = "Осиротевший комментарий",
			});
			db.SaveChanges();
		}

		// Act: удаляем конструкцию.
		await _service.DeleteAsync(construction.Id);

		// Assert: конструкция и осиротевший комментарий удалены.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.Constructions.Count(), Is.EqualTo(0));
			Assert.That(db.PositionComments.Count(), Is.EqualTo(0));
		}
	}

	[TestMethod]
	[Description("Удаление конструкции с привязанными сделками отказывает с объяснением и не меняет данные")]
	public async Task ThrowOnDeleteConstructionWithBoundTrades()
	{
		// Arrange: конструкция с двумя привязанными сделками.
		var construction = await _service.CreateAsync("Сделки на месте", 300m);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.TradeUserdata.Add(new TradeUserdata { ExecId = "exec-delete-1", ConstructionId = construction.Id });
			db.TradeUserdata.Add(new TradeUserdata { ExecId = "exec-delete-2", ConstructionId = construction.Id });
			db.SaveChanges();
		}

		// Act: пытаемся удалить конструкцию с привязанными сделками.
		ConstructionDeletionRefusedException? thrown = null;
		try
		{
			await _service.DeleteAsync(construction.Id);
		}
		catch (ConstructionDeletionRefusedException exception)
		{
			thrown = exception;
		}

		// Assert: отказ объясняет причину — привязанные сделки.
		// Требование: удаление возможно только без сделок и корректировок;
		// отказ сопровождается объяснением причины, данные остаются неизменными.
		// Traceability: openspec:domain/constructions#scenario-delete-only-when-empty
		Assert.That(thrown, Is.Not.Null);
		Assert.That(thrown!.BoundTradeCount, Is.EqualTo(2));
		Assert.That(thrown.AdjustmentCount, Is.EqualTo(0));
		Assert.That(thrown.Message, Does.Contain("сделок"));

		// Assert: конструкция и её сделки остались неизменными.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.Constructions.Count(), Is.EqualTo(1));
			Assert.That(db.TradeUserdata.Count(), Is.EqualTo(2));
		}
	}

	[TestMethod]
	[Description("Удаление конструкции с внешними корректировками PnL отказывает с объяснением и не меняет данные")]
	public async Task ThrowOnDeleteConstructionWithAdjustments()
	{
		// Arrange: конструкция с одной корректировкой PnL робота.
		var construction = await _service.CreateAsync("Корректировки на месте", 400m);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.PnLAdjustments.Add(new PnLAdjustment
			{
				ConstructionId = construction.Id,
				Date = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3)),
				Source = PnLAdjustmentSource.Robot,
				AmountUsdt = 25m,
			});
			db.SaveChanges();
		}

		// Act: пытаемся удалить конструкцию с корректировкой.
		ConstructionDeletionRefusedException? thrown = null;
		try
		{
			await _service.DeleteAsync(construction.Id);
		}
		catch (ConstructionDeletionRefusedException exception)
		{
			thrown = exception;
		}

		// Assert: отказ объясняет причину — внешние корректировки PnL.
		// Требование: удаление возможно только без сделок и корректировок;
		// отказ сопровождается объяснением причины, данные остаются неизменными.
		// Traceability: openspec:domain/constructions#scenario-delete-only-when-empty
		Assert.That(thrown, Is.Not.Null);
		Assert.That(thrown!.BoundTradeCount, Is.EqualTo(0));
		Assert.That(thrown.AdjustmentCount, Is.EqualTo(1));
		Assert.That(thrown.Message, Does.Contain("корректировок"));

		// Assert: конструкция и её корректировка остались неизменными.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.Constructions.Count(), Is.EqualTo(1));
			Assert.That(db.PnLAdjustments.Count(), Is.EqualTo(1));
		}
	}

	[TestMethod]
	[Description("Создание конструкции с пустым именем отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	public void ThrowOnCreateConstructionWithBlankName(string blankName)
	{
		// Act: пытаемся создать конструкцию без имени.
		_service.CreateAsync(blankName, 100m).GetAwaiter().GetResult();
	}

	[TestMethod]
	[Description("Переименование конструкции в пустое имя отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	public void ThrowOnRenameConstructionToBlankName(string blankName)
	{
		// Arrange: существующая конструкция.
		var construction = _service.CreateAsync("Имя есть", 100m).GetAwaiter().GetResult();

		// Act: пытаемся переименовать в пустое имя.
		_service.RenameAsync(construction.Id, blankName).GetAwaiter().GetResult();
	}

	[TestMethod]
	[Description("Переименование несуществующей конструкции отклоняется")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public void ThrowOnRenameUnknownConstruction()
	{
		// Act: переименовываем конструкцию, которой нет в журнале.
		_service.RenameAsync(12345, "Имя").GetAwaiter().GetResult();
	}

	[TestMethod]
	[Description("Смена статуса несуществующей конструкции отклоняется")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public void ThrowOnChangeStatusUnknownConstruction()
	{
		// Act: меняем статус конструкции, которой нет в журнале.
		_service.ChangeStatusAsync(12345, ConstructionStatus.Closed).GetAwaiter().GetResult();
	}

	[TestMethod]
	[Description("Архивация несуществующей конструкции отклоняется")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public void ThrowOnArchiveUnknownConstruction()
	{
		// Act: архивируем конструкцию, которой нет в журнале.
		_service.ArchiveAsync(12345).GetAwaiter().GetResult();
	}

	[TestMethod]
	[Description("Удаление несуществующей конструкции отклоняется")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public void ThrowOnDeleteUnknownConstruction()
	{
		// Act: удаляем конструкцию, которой нет в журнале.
		_service.DeleteAsync(12345).GetAwaiter().GetResult();
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	#endregion
}
