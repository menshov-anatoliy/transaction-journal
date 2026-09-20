using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain;

/// <summary>
/// Проверки use-case сервиса комментариев трёх уровней: комментарий позиции
/// хранится по ключу «конструкция × инструмент» и переживает пересчёты
/// остатка при переносах сделок, комментарии любого уровня не влияют
/// на позиции, результаты и статусы; отказы на неизвестные сделку
/// и конструкцию, пустые ключи.
/// </summary>
[TestClass]
public class CommentServiceTests
{
	private const string LinearSymbol = "BTCUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private CommentService _service = null!;
	private ConstructionService _constructionService = null!;
	private TradeBindingService _bindingService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-comment-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_service = new CommentService(CreateOptions());
		_constructionService = new ConstructionService(CreateOptions());
		_bindingService = new TradeBindingService(CreateOptions());
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
	[Description("Комментарий позиции переживает пересчёт: закрытие и переоткрытие позиции переносом сделок не трогают комментарий")]
	public async Task TryIfPositionCommentSurvivesRecompute()
	{
		// Arrange: конструкция с открытой позицией по линейному перпу —
		// две покупки дают остаток; на позицию оставлен комментарий.
		var construction = await _constructionService.CreateAsync("Спред календаря", 1000m);
		await AddLinearTradesAsync(
			("exec-hold-1", "Buy", "0.0002", ExecMs(2023, 12, 28, 10, 0)),
			("exec-hold-2", "Buy", "0.0002", ExecMs(2023, 12, 28, 10, 30)));
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-hold-1", "exec-hold-2" });
		await _service.SetPositionCommentAsync(construction.Id, LinearSymbol, "Держим до экспирации");
		var readModel = new PositionReadModel(CreateOptions());

		// Assert-предусловие: позиция открыта с накопленным остатком.
		var opened = (await readModel.ListAsync(construction.Id)).Positions.Single();
		Assert.That(opened.Residual, Is.EqualTo(0.0004m));
		Assert.That(opened.IsOpen, Is.True);

		// Act: закрываем позицию переносом обеих сделок во «Входящие», а затем
		// открываем заново повторной привязкой — позиция пересчитывается дважды.
		await _bindingService.UnbindAsync("exec-hold-1");
		await _bindingService.UnbindAsync("exec-hold-2");
		var closed = await readModel.ListAsync(construction.Id);
		Assert.That(closed.Positions, Is.Empty);
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-hold-1", "exec-hold-2" });
		var reopened = (await readModel.ListAsync(construction.Id)).Positions.Single();

		// Assert: позиция открылась заново с прежним остатком, а комментарий —
		// на месте: он хранится по ключу «конструкция × инструмент», а не в строке
		// производной позиции.
		// Требование: комментарий позиции переживает закрытие и переоткрытие
		// из-за переноса сделок.
		// Traceability: openspec:domain/constructions#scenario-position-comment-survives-recompute
		Assert.That(reopened.Residual, Is.EqualTo(0.0004m));
		Assert.That(reopened.IsOpen, Is.True);
		Assert.That(await _service.GetPositionCommentAsync(construction.Id, LinearSymbol),
			Is.EqualTo("Держим до экспирации"));
	}

	[TestMethod]
	[Description("Комментарии любого уровня не влияют на результат: позиции, статусы и капитал не изменяются")]
	public async Task TryIfCommentsDoNotAffectResult()
	{
		// Arrange: конструкция с открытой позицией (покупка и частичная продажа)
		// и исходным снимком позиций до комментариев.
		var construction = await _constructionService.CreateAsync("Комментируемый спред", 1200m);
		await AddLinearTradesAsync(
			("exec-note-1", "Buy", "0.0002", ExecMs(2023, 12, 28, 10, 0)),
			("exec-note-2", "Sell", "0.0001", ExecMs(2023, 12, 28, 11, 0)));
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-note-1", "exec-note-2" });
		var readModel = new PositionReadModel(CreateOptions());
		var before = await readModel.ListAsync(construction.Id);

		// Act: добавляем комментарии всех трёх уровней — сделки, позиции
		// и конструкции.
		await _service.SetTradeCommentAsync("exec-note-1", "Вход половиной");
		await _service.SetPositionCommentAsync(construction.Id, LinearSymbol, "Сокращать к отчёту");
		await _service.SetConstructionCommentAsync(construction.Id, "Квартальная цель");
		var afterAdding = await readModel.ListAsync(construction.Id);

		// Assert: позиции, закрывающие записи и предупреждения не изменились —
		// комментарии не входят ни в один расчёт.
		// Требование: комментарии не влияют на позиции, результаты и статусы.
		// Traceability: openspec:domain/constructions#scenario-comments-inert
		AssertPositionState(before, afterAdding);

		// Act: удаляем комментарии всех трёх уровней.
		await _service.SetTradeCommentAsync("exec-note-1", null);
		await _service.SetPositionCommentAsync(construction.Id, LinearSymbol, null);
		await _service.SetConstructionCommentAsync(construction.Id, null);
		var afterRemoving = await readModel.ListAsync(construction.Id);

		// Assert: производное состояние снова не изменилось, а статус и капитал
		// конструкции остались прежними.
		AssertPositionState(before, afterRemoving);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var stored = db.Constructions.Single();
			Assert.That(stored.Status, Is.EqualTo(ConstructionStatus.Open));
			Assert.That(stored.AllocatedCapitalUsdt, Is.EqualTo(1200m));
			Assert.That(stored.Comment, Is.Null);
		}
	}

	[TestMethod]
	[Description("Комментарий сделки задаётся и снимается по execId, не трогая привязку")]
	public async Task TryIfTradeCommentSetAndClearedWithoutTouchingBinding()
	{
		// Arrange: сделка привязана к конструкции.
		var construction = await _constructionService.CreateAsync("Комментарий сделки", 300m);
		await AddLinearTradesAsync(("exec-cmt-1", "Buy", "0.0001", ExecMs(2023, 12, 28, 10, 0)));
		await _bindingService.BindAsync(construction.Id, "exec-cmt-1");

		// Act: задаём комментарий сделки, затем снимаем пустым значением.
		await _service.SetTradeCommentAsync("exec-cmt-1", "Проверить дельту");
		var set = await _service.GetTradeCommentAsync("exec-cmt-1");
		await _service.SetTradeCommentAsync("exec-cmt-1", "  ");
		var cleared = await _service.GetTradeCommentAsync("exec-cmt-1");

		// Assert: комментарий хранится в пользовательских данных сделки и снимается
		// без удаления строки и без изменения привязки — комментарий переживает
		// любые перепривязки.
		// Требование: система поддерживает свободные текстовые комментарии на сделках.
		// Traceability: openspec:domain/constructions#requirement-entity-comments
		Assert.That(set, Is.EqualTo("Проверить дельту"));
		Assert.That(cleared, Is.Null);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var userdata = db.TradeUserdata.Single();
			Assert.That(userdata.ConstructionId, Is.EqualTo(construction.Id));
			Assert.That(userdata.Comment, Is.Null);
		}
	}

	[TestMethod]
	[Description("Комментарий конструкции задаётся и снимается, не трогая имя, статус и капитал")]
	public async Task TryIfConstructionCommentSetAndClearedPreservingAttributes()
	{
		// Arrange: конструкция с исходным комментарием создания.
		var construction = await _constructionService.CreateAsync("Цель с комментарием", 900m, "Стартовая заметка");

		// Act: заменяем комментарий, затем снимаем.
		await _service.SetConstructionCommentAsync(construction.Id, "Обновлённая заметка");
		var replaced = await _service.GetConstructionCommentAsync(construction.Id);
		await _service.SetConstructionCommentAsync(construction.Id, null);
		var cleared = await _service.GetConstructionCommentAsync(construction.Id);

		// Assert: комментарий хранится и возвращается; прочие атрибуты конструкции
		// не затронуты правками комментария.
		// Требование: система поддерживает свободные текстовые комментарии
		// на конструкциях.
		// Traceability: openspec:domain/constructions#requirement-entity-comments
		Assert.That(replaced, Is.EqualTo("Обновлённая заметка"));
		Assert.That(cleared, Is.Null);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var stored = db.Constructions.Single();
			Assert.That(stored.Name, Is.EqualTo("Цель с комментарием"));
			Assert.That(stored.Status, Is.EqualTo(ConstructionStatus.Open));
			Assert.That(stored.AllocatedCapitalUsdt, Is.EqualTo(900m));
		}
	}

	[TestMethod]
	[Description("Комментарий позиции удаляется вместе со строкой при снятии")]
	public async Task TryIfPositionCommentRemovedWithRowOnClear()
	{
		// Arrange: конструкция с комментарием позиции.
		var construction = await _constructionService.CreateAsync("Снять комментарий", 100m);
		await _service.SetPositionCommentAsync(construction.Id, LinearSymbol, "Временная пометка");

		// Act: снимаем комментарий позиции пустым текстом.
		await _service.SetPositionCommentAsync(construction.Id, LinearSymbol, null);

		// Assert: строка комментария удалена целиком — чтение возвращает null,
		// таблица комментариев пуста.
		Assert.That(await _service.GetPositionCommentAsync(construction.Id, LinearSymbol), Is.Null);
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.PositionComments.Count(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Комментарий неизвестной сделки отказывает")]
	[ExpectedException(typeof(TradeNotFoundException))]
	public async Task ThrowOnSetTradeCommentForUnknownTrade()
	{
		// Arrange — Act: комментарий сделке, которой нет в журнале.
		await _service.SetTradeCommentAsync("exec-phantom", "Заметка");
	}

	[TestMethod]
	[Description("Комментарий сделки с пустым идентификатором отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	public async Task ThrowOnSetTradeCommentWithBlankExecId(string blankExecId)
	{
		// Act: комментарий сделке с пустым идентификатором.
		await _service.SetTradeCommentAsync(blankExecId, "Заметка");
	}

	[TestMethod]
	[Description("Чтение комментария неизвестной сделки отказывает")]
	[ExpectedException(typeof(TradeNotFoundException))]
	public async Task ThrowOnGetTradeCommentForUnknownTrade()
	{
		// Act: читаем комментарий сделки, которой нет в журнале.
		await _service.GetTradeCommentAsync("exec-phantom");
	}

	[TestMethod]
	[Description("Комментарий позиции неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnSetPositionCommentForUnknownConstruction()
	{
		// Act: комментарий позиции несуществующей конструкции.
		await _service.SetPositionCommentAsync(999, LinearSymbol, "Заметка");
	}

	[TestMethod]
	[Description("Комментарий позиции с пустым инструментом отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	public async Task ThrowOnSetPositionCommentWithBlankSymbol(string blankSymbol)
	{
		// Arrange: существующая конструкция.
		var construction = await _constructionService.CreateAsync("Пустой инструмент", 100m);

		// Act: комментарий позиции с пустым инструментом.
		await _service.SetPositionCommentAsync(construction.Id, blankSymbol, "Заметка");
	}

	[TestMethod]
	[Description("Чтение комментария позиции неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnGetPositionCommentForUnknownConstruction()
	{
		// Act: читаем комментарий позиции несуществующей конструкции.
		await _service.GetPositionCommentAsync(999, LinearSymbol);
	}

	[TestMethod]
	[Description("Комментарий неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnSetConstructionCommentForUnknownConstruction()
	{
		// Act: комментарий конструкции, которой нет в журнале.
		await _service.SetConstructionCommentAsync(999, "Заметка");
	}

	[TestMethod]
	[Description("Чтение комментария неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnGetConstructionCommentForUnknownConstruction()
	{
		// Act: читаем комментарий конструкции, которой нет в журнале.
		await _service.GetConstructionCommentAsync(999);
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new CommentService(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Добавляет сырые записи исполнения линейного перпа BTCUSDT.</summary>
	private async Task AddLinearTradesAsync(params (string ExecId, string Side, string ExecQty, long ExecTimeMs)[] executions)
	{
		using var db = new JournalDbContext(CreateOptions());
		foreach (var (execId, side, execQty, execTimeMs) in executions)
		{
			db.RawExecutions.Add(new RawExecution
			{
				ExecId = execId,
				Category = "linear",
				Symbol = LinearSymbol,
				ExecTimeMs = execTimeMs,
				PayloadJson = ExecutionPayload(execId, LinearSymbol, side, "42000", execQty, execTimeMs),
				FetchedAt = FetchedAt,
			});
		}

		await db.SaveChangesAsync();
	}

	/// <summary>Запись исполнения в форме ответа execution-list: числа биржа шлёт строками.</summary>
	private static string ExecutionPayload(
		string execId,
		string symbol,
		string side,
		string execPrice,
		string execQty,
		long execTimeMs,
		bool isMaker = false)
	{
		var isMakerJson = isMaker ? "true" : "false";
		return $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"0.0001","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"BTC","isMaker":{{isMakerJson}}}""";
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Сравнивает производное состояние позиций двух чтений: остатки, закрывающие записи и предупреждения.</summary>
	private static void AssertPositionState(PositionReadResult expected, PositionReadResult actual)
	{
		Assert.That(
			actual.Positions.Select(position => new { position.ConstructionId, position.Symbol, position.Residual, position.IsOpen }).ToArray(),
			Is.EqualTo(expected.Positions.Select(position => new { position.ConstructionId, position.Symbol, position.Residual, position.IsOpen }).ToArray()));
		Assert.That(actual.ClosingEntries.Count, Is.EqualTo(expected.ClosingEntries.Count));
		Assert.That(actual.Warnings.Count, Is.EqualTo(expected.Warnings.Count));
	}

	#endregion
}
