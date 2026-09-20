using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain;

/// <summary>
/// Проверки use-case сервиса привязки сделок: привязка из «Входящих», перенос
/// между конструкциями, возврат во «Входящие» и массовая привязка. Инвариант
/// единственной принадлежности — сделка ровно в одной конструкции либо
/// во «Входящих» — проверяется во всех четырёх сценариях требования.
/// </summary>
[TestClass]
public class TradeBindingServiceTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private TradeBindingService _service = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-binding-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_service = new TradeBindingService(CreateOptions());
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
	[Description("Привязка выводит сделку из «Входящих»: у сделки появляется ровно одна конструкция")]
	public async Task TryIfBindingRemovesTradeFromInbox()
	{
		// Arrange: конструкция и сделка «Входящих» без строки пользовательских данных.
		var construction = await CreateConstructionAsync("Стреддл BTC");
		await SeedRawExecutionAsync("exec-inbox-1");

		// Act: привязываем сделку из «Входящих» к конструкции.
		await _service.BindAsync(construction.Id, "exec-inbox-1");

		// Assert: сделка принадлежит ровно одной конструкции — единственная строка
		// пользовательских данных по execId указывает на неё.
		// Требование: привязка выводит сделку из «Входящих», инвариант единственной
		// принадлежности сохраняется.
		// Traceability: openspec:domain/constructions#scenario-binding-removes-from-inbox
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var binding = db.TradeUserdata.Single();
			Assert.That(binding.ExecId, Is.EqualTo("exec-inbox-1"));
			Assert.That(binding.ConstructionId, Is.EqualTo(construction.Id));
		}

		// Assert: повторная привязка к той же конструкции не создаёт вторую строку.
		await _service.BindAsync(construction.Id, "exec-inbox-1");
		using (var dbAgain = new JournalDbContext(CreateOptions()))
		{
			Assert.That(dbAgain.TradeUserdata.Count(), Is.EqualTo(1));
		}
	}

	[TestMethod]
	[Description("Перенос сделки между конструкциями меняет принадлежность без следов прежней")]
	public async Task TryIfTransferMovesTradeBetweenConstructionsWithoutTrace()
	{
		// Arrange: две конструкции; сделка с комментарием привязана к первой.
		var first = await CreateConstructionAsync("Первая цель");
		var second = await CreateConstructionAsync("Вторая цель");
		await SeedRawExecutionAsync("exec-move-1");
		await _service.BindAsync(first.Id, "exec-move-1");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var userdata = db.TradeUserdata.Single();
			userdata.Comment = "Вход в конструкцию";
			await db.SaveChangesAsync();
		}

		// Act: переносим сделку из первой конструкции во вторую.
		await _service.BindAsync(second.Id, "exec-move-1");

		// Assert: сделка принадлежит только второй конструкции — одна строка данных
		// по execId с единственной привязкой; следов первой принадлежности нет,
		// комментарий пережил перенос.
		// Требование: перенос пересчитывает позиции обеих конструкций при чтении,
		// инвариант единственной принадлежности сохраняется.
		// Traceability: openspec:domain/constructions#scenario-rebind-recomputes-both
		using (var dbAfterMove = new JournalDbContext(CreateOptions()))
		{
			var binding = dbAfterMove.TradeUserdata.Single();
			Assert.That(binding.ExecId, Is.EqualTo("exec-move-1"));
			Assert.That(binding.ConstructionId, Is.EqualTo(second.Id));
			Assert.That(binding.Comment, Is.EqualTo("Вход в конструкцию"));
		}
	}

	[TestMethod]
	[Description("Возврат во «Входящие» снимает привязку и сохраняет комментарий сделки")]
	public async Task TryIfUnbindReturnsTradeToInboxAndKeepsComment()
	{
		// Arrange: конструкция с привязанной сделкой и комментарием.
		var construction = await CreateConstructionAsync("Хедж фьючерсом");
		await SeedRawExecutionAsync("exec-return-1");
		await _service.BindAsync(construction.Id, "exec-return-1");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var userdata = db.TradeUserdata.Single();
			userdata.Comment = "Проверить дельту";
			await db.SaveChangesAsync();
		}

		// Act: возвращаем сделку во «Входящие».
		await _service.UnbindAsync("exec-return-1");

		// Assert: привязка снята, но строка данных и комментарий остались —
		// сделка снова во «Входящих» ровно в одном экземпляре.
		// Требование: возврат во «Входящие» отменяет вклад сделки в позиции
		// прежней конструкции, инвариант единственной принадлежности сохраняется.
		// Traceability: openspec:domain/constructions#scenario-return-to-inbox
		using (var dbAfterUnbind = new JournalDbContext(CreateOptions()))
		{
			var userdata = dbAfterUnbind.TradeUserdata.Single();
			Assert.That(userdata.ExecId, Is.EqualTo("exec-return-1"));
			Assert.That(userdata.ConstructionId, Is.Null);
			Assert.That(userdata.Comment, Is.EqualTo("Проверить дельту"));
		}

		// Assert: возврат непривязанной сделки — допустимое отсутствие действия.
		await _service.UnbindAsync("exec-return-1");
		using (var dbAgain = new JournalDbContext(CreateOptions()))
		{
			Assert.That(dbAgain.TradeUserdata.Single().ConstructionId, Is.Null);
		}
	}

	[TestMethod]
	[Description("Массовая привязка помещает каждую сделку пачки ровно в целевую конструкцию")]
	public async Task TryIfBatchBindingPutsEveryTradeIntoSingleConstruction()
	{
		// Arrange: целевая конструкция, сторонняя конструкция и три сделки:
		// непривязанная «Входящих», привязанная к сторонней и свежая с комментарием.
		var target = await CreateConstructionAsync("Собрать спред");
		var other = await CreateConstructionAsync("Прежняя цель");
		await SeedRawExecutionAsync("exec-batch-1");
		await SeedRawExecutionAsync("exec-batch-2");
		await SeedRawExecutionAsync("exec-batch-3");
		await _service.BindAsync(other.Id, "exec-batch-2");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.TradeUserdata.Add(new TradeUserdata { ExecId = "exec-batch-3", Comment = "Наблюдение" });
			await db.SaveChangesAsync();
		}

		// Act: массовая привязка всех трёх сделок к целевой конструкции.
		await _service.BindBatchAsync(target.Id, new[] { "exec-batch-1", "exec-batch-2", "exec-batch-3" });

		// Assert: каждая сделка ровно в целевой конструкции — по одной строке данных
		// на execId, все привязки указывают на целевую конструкцию, комментарий
		// третьей сделки пережил привязку.
		// Требование: массовая привязка сохраняет инвариант единственной
		// принадлежности для каждой сделки пачки.
		// Traceability: openspec:domain/constructions#scenario-batch-binding
		using (var dbAfterBatch = new JournalDbContext(CreateOptions()))
		{
			var bindings = dbAfterBatch.TradeUserdata
				.OrderBy(userdata => userdata.ExecId)
				.ToList();
			Assert.That(bindings.Count, Is.EqualTo(3));
			Assert.That(bindings.Select(userdata => userdata.ExecId).ToArray(),
				Is.EqualTo(new[] { "exec-batch-1", "exec-batch-2", "exec-batch-3" }));
			Assert.That(bindings.All(userdata => userdata.ConstructionId == target.Id), Is.True);
			Assert.That(bindings.Single(userdata => userdata.ExecId == "exec-batch-3").Comment,
				Is.EqualTo("Наблюдение"));
		}
	}

	[TestMethod]
	[Description("Массовая привязка с неизвестной сделкой отказывает всю пачку без изменений")]
	public async Task ThrowOnBatchBindingWithUnknownTradeLeavesBindingsUntouched()
	{
		// Arrange: целевая конструкция и одна известная сделка.
		var target = await CreateConstructionAsync("Пачка с ошибкой");
		await SeedRawExecutionAsync("exec-batch-known");

		// Act: пытаемся привязать пачку с неизвестной сделкой.
		TradeNotFoundException? thrown = null;
		try
		{
			await _service.BindBatchAsync(target.Id, new[] { "exec-batch-known", "exec-batch-unknown" });
		}
		catch (TradeNotFoundException exception)
		{
			thrown = exception;
		}

		// Assert: отказ называет неизвестную сделку, привязки не изменились —
		// массовая привязка атомарна, частичных пачек не бывает.
		Assert.That(thrown, Is.Not.Null);
		Assert.That(thrown!.ExecId, Is.EqualTo("exec-batch-unknown"));
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.TradeUserdata.Count(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Привязка к несуществующей конструкции отклоняется")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnBindToUnknownConstruction()
	{
		// Arrange: сделка есть, конструкции нет.
		await SeedRawExecutionAsync("exec-no-target");

		// Act: привязываем сделку к несуществующей конструкции.
		await _service.BindAsync(12345, "exec-no-target");
	}

	[TestMethod]
	[Description("Привязка неизвестной сделки отклоняется")]
	[ExpectedException(typeof(TradeNotFoundException))]
	public async Task ThrowOnBindUnknownTrade()
	{
		// Arrange: конструкция есть, сделки в сыром хранилище нет.
		var construction = await CreateConstructionAsync("Без сделок");

		// Act: привязываем сделку, которой нет в журнале.
		await _service.BindAsync(construction.Id, "exec-phantom");
	}

	[TestMethod]
	[Description("Возврат во «Входящие» неизвестной сделки отклоняется")]
	[ExpectedException(typeof(TradeNotFoundException))]
	public async Task ThrowOnUnbindUnknownTrade()
	{
		// Act: возвращаем во «Входящие» сделку, которой нет в журнале.
		await _service.UnbindAsync("exec-phantom");
	}

	[TestMethod]
	[Description("Массовая привязка к несуществующей конструкции отклоняется")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnBatchBindToUnknownConstruction()
	{
		// Arrange: сделка есть, целевой конструкции нет.
		await SeedRawExecutionAsync("exec-batch-no-target");

		// Act: массово привязываем к несуществующей конструкции.
		await _service.BindBatchAsync(12345, new[] { "exec-batch-no-target" });
	}

	[TestMethod]
	[Description("Привязка с пустым идентификатором исполнения отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	public async Task ThrowOnBindWithBlankExecId(string blankExecId)
	{
		// Arrange: существующая конструкция.
		var construction = await CreateConstructionAsync("Пустой execId");

		// Act: привязываем сделку с пустым идентификатором.
		await _service.BindAsync(construction.Id, blankExecId);
	}

	[TestMethod]
	[Description("Возврат во «Входящие» с пустым идентификатором исполнения отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	public async Task ThrowOnUnbindWithBlankExecId(string blankExecId)
	{
		// Act: возвращаем во «Входящие» сделку с пустым идентификатором.
		await _service.UnbindAsync(blankExecId);
	}

	[TestMethod]
	[Description("Массовая привязка с пустым идентификатором в пачке отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnBatchBindWithBlankExecIdInBatch()
	{
		// Arrange: существующая конструкция и сделка.
		var construction = await CreateConstructionAsync("Пачка с пустым идентификатором");
		await SeedRawExecutionAsync("exec-batch-blank");

		// Act: массово привязываем пачку с пустым идентификатором.
		await _service.BindBatchAsync(construction.Id, new[] { "exec-batch-blank", " " });
	}

	[TestMethod]
	[Description("Null-пачка идентификаторов отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnBatchBindNullExecIds()
	{
		// Arrange: существующая конструкция.
		var construction = await CreateConstructionAsync("Null-пачка");

		// Act: массово привязываем null-пачку.
		await _service.BindBatchAsync(construction.Id, null!);
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new TradeBindingService(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Создаёт конструкцию через доменный сервис — как это делает пользователь.</summary>
	private async Task<Construction> CreateConstructionAsync(string name)
	{
		var service = new ConstructionService(CreateOptions());
		return await service.CreateAsync(name, 1000m);
	}

	/// <summary>Кладёт сырую запись исполнения в хранилище: привязывать можно только реальные сделки журнала.</summary>
	private async Task SeedRawExecutionAsync(string execId)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "linear",
			Symbol = "BTCUSDT",
			ExecTimeMs = 0,
			PayloadJson = $$"""{"symbol":"BTCUSDT","side":"Buy","execId":"{{execId}}","execPrice":"42000","execQty":"0.01","execFee":"0.0042","execTime":"0","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	#endregion
}
