using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Data;

/// <summary>
/// Проверки хранения доменных пользовательских записей: конструкции,
/// корректировки PnL и пользовательские данные сделок, позиций и пометок.
/// Инварианты — уникальные ключи, запрет удаления непустой конструкции и
/// каскадное удаление осиротевших записей — проверяются на уровне БД.
/// </summary>
[TestClass]
public class DomainStorageTests
{
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-domain-tests-{Guid.NewGuid():N}.db");
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
	[Description("Миграция разворачивает таблицы доменных записей на пустой SQLite-базе")]
	public void TryIfMigrationCreatesCoreDomainTablesOnEmptyDatabase()
	{
		// Act: применяем миграции к пустому файлу базы.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		// Assert: таблицы домена появились в базе вместе с сырым хранилищем.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var tables = db.Database
				.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
				.ToList();

			Assert.That(tables, Does.Contain("Constructions"));
			Assert.That(tables, Does.Contain("PnLAdjustments"));
			Assert.That(tables, Does.Contain("TradeUserdata"));
			Assert.That(tables, Does.Contain("PositionComments"));
			Assert.That(tables, Does.Contain("ManualCloseMarks"));
		}
	}

	[TestMethod]
	[Description("Доменные записи хранятся и читаются целиком: атрибуты конструкций и корректировок не теряются")]
	public void TryIfCoreDomainRecordsAreStoredAndReadWithAllAttributes()
	{
		// Arrange: конструкция с корректировкой и пользовательскими записями.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();

			var construction = CreateConstruction("Стреддл BTC");
			db.Constructions.Add(construction);
			db.SaveChanges();

			db.PnLAdjustments.Add(new PnLAdjustment
			{
				ConstructionId = construction.Id,
				Date = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3)),
				Source = PnLAdjustmentSource.Robot,
				AmountUsdt = -125.50m,
				Comment = "PnL робота за август",
			});
			db.SaveChanges();
		}

		// Act: читаем записи из новой базы.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var construction = db.Constructions.Single();
			var adjustment = db.PnLAdjustments.Single();

			// Assert: все атрибуты возвращаются теми же, с какими были записаны.
			Assert.That(construction.Name, Is.EqualTo("Стреддл BTC"));
			Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Open));
			Assert.That(construction.AllocatedCapitalUsdt, Is.EqualTo(1000m));
			Assert.That(adjustment.ConstructionId, Is.EqualTo(construction.Id));
			Assert.That(adjustment.Date, Is.EqualTo(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3))));
			Assert.That(adjustment.Source, Is.EqualTo(PnLAdjustmentSource.Robot));
			Assert.That(adjustment.AmountUsdt, Is.EqualTo(-125.50m));
			Assert.That(adjustment.Comment, Is.EqualTo("PnL робота за август"));
		}
	}

	[TestMethod]
	[Description("Удаление конструкции с внешними корректировками PnL отклоняется на уровне БД")]
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnConstructionDeleteWithAdjustments()
	{
		// Удаление допустимо только без сделок и корректировок: внешний ключ
		// с запретом не даёт молча стереть слагаемые результата конструкции.
		// Traceability: openspec:domain/constructions#scenario-delete-only-when-empty
		// Arrange: конструкция с одной корректировкой PnL.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();

			var construction = CreateConstruction("Контртренд ETH");
			db.Constructions.Add(construction);
			db.SaveChanges();

			db.PnLAdjustments.Add(new PnLAdjustment
			{
				ConstructionId = construction.Id,
				Date = DateTimeOffset.UtcNow,
				Source = PnLAdjustmentSource.Manual,
				AmountUsdt = 42m,
			});
			db.SaveChanges();
		}

		// Act: удаляем конструкцию в отдельном контексте, где корректировки не загружены, —
		// отказ должен прийти от ограничения БД, а не от трекинга EF.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var construction = db.Constructions.Single();
			db.Constructions.Remove(construction);
			db.SaveChanges();
		}
	}

	[TestMethod]
	[Description("Удаление конструкции с привязанными сделками отклоняется на уровне БД")]
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnConstructionDeleteWithBoundTrades()
	{
		// Привязанная сделка — часть конструкции: пока есть привязки,
		// удаление конструкции запрещено так же, как при корректировках.
		// Traceability: openspec:domain/constructions#requirement-trade-single-binding
		// Arrange: конструкция и сделка, привязанная к ней по execId.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();

			var construction = CreateConstruction("Хедж фьючерсом");
			db.Constructions.Add(construction);
			db.SaveChanges();

			db.TradeUserdata.Add(new TradeUserdata
			{
				ExecId = "exec-bound",
				ConstructionId = construction.Id,
			});
			db.SaveChanges();
		}

		// Act: удаляем конструкцию в отдельном контексте без загруженных привязок.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var construction = db.Constructions.Single();
			db.Constructions.Remove(construction);
			db.SaveChanges();
		}
	}

	[TestMethod]
	[Description("Повторная строка пользовательских данных с тем же execId отклоняется уникальным индексом")]
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnDuplicateTradeUserdataExecId()
	{
		// execId — ключ сделки: одна строка пользовательских данных хранит
		// единственную привязку, дубликат нарушил бы инвариант принадлежности.
		// Traceability: openspec:domain/constructions#requirement-trade-single-binding
		// Arrange: база с одной строкой данных сделки.
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();

		db.TradeUserdata.Add(new TradeUserdata { ExecId = "exec-same" });
		db.SaveChanges();

		// Act: вставляем вторую строку с тем же execId.
		db.TradeUserdata.Add(new TradeUserdata { ExecId = "exec-same" });
		db.SaveChanges();
	}

	[TestMethod]
	[Description("Повторный комментарий позиции на тот же ключ «конструкция × инструмент» отклоняется")]
	[ExpectedException(typeof(DbUpdateException))]
	public void ThrowOnDuplicatePositionCommentKey()
	{
		// Комментарий позиции живёт по стабильному ключу «конструкция × инструмент»:
		// уникальный индекс не даёт завести вторую строку на тот же ключ.
		// Traceability: openspec:domain/constructions#requirement-entity-comments
		// Arrange: конструкция с комментарием позиции.
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();

		var construction = CreateConstruction("Календарный спред");
		db.Constructions.Add(construction);
		db.SaveChanges();

		db.PositionComments.Add(new PositionComment
		{
			ConstructionId = construction.Id,
			Symbol = "BTC-27DEC24-2800-C",
			Text = "Ждём распад",
		});
		db.SaveChanges();

		// Act: вставляем второй комментарий на тот же ключ.
		db.PositionComments.Add(new PositionComment
		{
			ConstructionId = construction.Id,
			Symbol = "BTC-27DEC24-2800-C",
			Text = "Дубль",
		});
		db.SaveChanges();
	}

	[TestMethod]
	[Description("Удаление пустой конструкции каскадно снимает осиротевшие пометки и комментарии позиций")]
	public void TryIfConstructionDeleteCascadesOrphanedPositionRecords()
	{
		// Осиротевшие комментарии позиций и пометки закрытия не переживают
		// конструкцию: условие удаления «без сделок и корректировок» на практике
		// исключает живые пометки, поэтому они уходят каскадом вместе с ней.
		// Traceability: change:add-core-domain/design#d1
		// Arrange: конструкция с комментарием позиции и пометкой закрытия,
		// но без корректировок и привязанных сделок; рядом — непривязанная
		// сделка «Входящих», которую конструкция не касается.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();

			var construction = CreateConstruction("Пустая после переноса");
			db.Constructions.Add(construction);
			db.SaveChanges();

			db.PositionComments.Add(new PositionComment
			{
				ConstructionId = construction.Id,
				Symbol = "ETHUSDT",
				Text = "Закрыто переносом",
			});
			db.ManualCloseMarks.Add(new ManualCloseMark
			{
				ConstructionId = construction.Id,
				Symbol = "ETHUSDT",
				Price = 3100m,
				MarkedAt = DateTimeOffset.UtcNow,
			});
			db.TradeUserdata.Add(new TradeUserdata { ExecId = "exec-inbox" });
			db.SaveChanges();
		}

		// Act: удаляем пустую конструкцию.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var construction = db.Constructions.Single();
			db.Constructions.Remove(construction);
			db.SaveChanges();
		}

		// Assert: осиротевшие записи удалены каскадом, а непривязанная
		// сделка «Входящих» осталась нетронутой.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(db.Constructions.Count(), Is.EqualTo(0));
			Assert.That(db.PositionComments.Count(), Is.EqualTo(0));
			Assert.That(db.ManualCloseMarks.Count(), Is.EqualTo(0));
			Assert.That(db.TradeUserdata.Count(), Is.EqualTo(1));
			Assert.That(db.TradeUserdata.Single().ExecId, Is.EqualTo("exec-inbox"));
		}
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	private static Construction CreateConstruction(string name) => new()
	{
		Name = name,
		Status = ConstructionStatus.Open,
		AllocatedCapitalUsdt = 1000m,
	};

	#endregion
}
