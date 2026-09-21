using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки экрана «Входящие»: таблица непривязанных сделок с атрибутами
/// биржевой записи и чекбоксами выбора строк; переключатель «Выбрать всё»
/// работает как «все или ничего»; пустые и недоступные «Входящие» показываются
/// явно.
/// Traceability: openspec:ui/screens#requirement-inbox-screen
/// </summary>
[TestClass]
public class InboxScreenTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private Bunit.TestContext _context = null!;
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-inbox-screen-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_context = new Bunit.TestContext();

		// Read-модель «Входящих» — sealed-класс без интерфейса: тест поднимает
		// её над той же временной базой, из которой компонент читает сделки.
		_context.Services.AddSingleton(new InboxReadModel(CreateOptions()));
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();

		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
	[Description("Таблица показывает непривязанные сделки с атрибутами биржевой записи")]
	public void TryIfTableShowsUnboundTradesWithExchangeAttributes()
	{
		// Arrange: две сделки — покупка BTCUSDT и продажа ETHUSDT с rebate-комиссией.
		SeedExecution("exec-buy", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		SeedExecution("exec-sell", "ETHUSDT", "Sell", "2400.5", "0.5", "-0.01", "USDT", ExecMs(2026, 6, 20, 11, 30));

		// Act: пользователь открывает экран «Входящие».
		var cut = _context.RenderComponent<Inbox>();

		// Assert: обе строки несут время, execId, инструмент, направление, количество,
		// цену, сумму и комиссию; заголовок перечисляет столбцы биржевой записи.
		// Требование: таблица показывает непривязанные сделки с атрибутами биржевой записи.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find("thead").TextContent, Does.Contain("Время")
				.And.Contain("execId")
				.And.Contain("Инструмент")
				.And.Contain("Направление")
				.And.Contain("Количество")
				.And.Contain("Цена")
				.And.Contain("Сумма")
				.And.Contain("Комиссия")
				.And.Contain("Выбрать всё"));

			var rows = cut.FindAll("tbody tr");
			Assert.That(rows, Has.Count.EqualTo(2));
			Assert.That(rows[0].TextContent, Does.Contain("2026-06-20 10:00")
				.And.Contain("exec-buy")
				.And.Contain("BTCUSDT")
				.And.Contain("покупка")
				.And.Contain("+0.01")
				.And.Contain("45000")
				.And.Contain("450")
				.And.Contain("+0.5 USDT"));
			Assert.That(rows[1].TextContent, Does.Contain("2026-06-20 11:30")
				.And.Contain("exec-sell")
				.And.Contain("ETHUSDT")
				.And.Contain("продажа")
				.And.Contain("-0.5")
				.And.Contain("2400.5")
				.And.Contain("1200.25")
				.And.Contain("-0.01 USDT"));
		});
	}

	[TestMethod]
	[Description("«Выбрать всё» переключает все или ничего")]
	public void TryIfSelectAllTogglesAllOrNothing()
	{
		// Arrange: во «Входящих» три непривязанные сделки, ничего не выбрано.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		SeedExecution("exec-2", "BTCUSDT", "Sell", "44000", "0.02", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 5));
		SeedExecution("exec-3", "ETHUSDT", "Buy", "2400", "0.5", "0.1", "USDT", ExecMs(2026, 6, 20, 10, 10));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(3)));

		// Act: пользователь нажимает переключатель «Выбрать всё» в шапке таблицы.
		cut.Find("input.select-all").Change(true);

		// Assert: выбраны все непривязанные сделки — отмечены все чекбоксы строк
		// и сам переключатель.
		// Сценарий: переключатель выбирает все непривязанные сделки.
		// Traceability: openspec:ui/screens#scenario-inbox-select-all-or-none
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll("tbody input[type=checkbox]"), Has.Count.EqualTo(3));
			Assert.That(SelectedRowCount(cut), Is.EqualTo(3));
			Assert.That(cut.Find("input.select-all").HasAttribute("checked"), Is.True);
		});

		// Act: повторное нажатие переключателя.
		cut.Find("input.select-all").Change(false);

		// Assert: выбор полностью снят — не отмечен ни один чекбокс строки.
		// Сценарий: переключатель полностью снимает выбор.
		// Traceability: openspec:ui/screens#scenario-inbox-select-all-or-none
		cut.WaitForAssertion(() => Assert.That(SelectedRowCount(cut), Is.EqualTo(0)));
	}

	[TestMethod]
	[Description("«Выбрать всё» после частичного выбора выбирает все строки, а не переключается")]
	public void TryIfSelectAllAfterPartialSelectionSelectsEverything()
	{
		// Arrange: во «Входящих» три сделки; пользователь выбрал одну чекбоксом строки.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		SeedExecution("exec-2", "BTCUSDT", "Sell", "44000", "0.02", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 5));
		SeedExecution("exec-3", "ETHUSDT", "Buy", "2400", "0.5", "0.1", "USDT", ExecMs(2026, 6, 20, 10, 10));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(3)));
		cut.FindAll("tbody input[type=checkbox]")[0].Change(true);

		// Assert: выбрана одна строка, переключатель в шапке не отмечен.
		cut.WaitForAssertion(() =>
		{
			Assert.That(SelectedRowCount(cut), Is.EqualTo(1));
			Assert.That(cut.Find("input.select-all").HasAttribute("checked"), Is.False);
		});

		// Act: нажатие «Выбрать всё» при частичном выборе.
		cut.Find("input.select-all").Change(true);

		// Assert: выбраны все три строки — «все или ничего», а не смена состояния
		// на «снять всё» при частичном выборе.
		// Сценарий: переключатель выбирает все непривязанные сделки.
		// Traceability: openspec:ui/screens#scenario-inbox-select-all-or-none
		cut.WaitForAssertion(() => Assert.That(SelectedRowCount(cut), Is.EqualTo(3)));

		// Act: снятие выбора с одной строки её чекбоксом.
		cut.FindAll("tbody input[type=checkbox]")[0].Change(false);

		// Assert: строка снята, остальные две остаются выбранными, переключатель
		// в шапке снова не отмечен.
		// Требование: каждая строка имеет чекбокс выбора.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(SelectedRowCount(cut), Is.EqualTo(2));
			Assert.That(cut.Find("input.select-all").HasAttribute("checked"), Is.False);
		});
	}

	[TestMethod]
	[Description("Пустые «Входящие» показываются явным сообщением без таблицы")]
	public void TryIfEmptyInboxShowsExplicitMessage()
	{
		// Arrange: журнал без сырых записей — «Входящие» пусты.

		// Act: пользователь открывает экран «Входящие».
		var cut = _context.RenderComponent<Inbox>();

		// Assert: экран показывает явное сообщение об отсутствии непривязанных
		// сделок вместо пустой таблицы.
		// Требование: отсутствие данных — видимое состояние, а не пустой экран.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Входящие пусты"));
			Assert.That(cut.FindAll("table"), Has.Count.EqualTo(0));
		});
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Число отмеченных чекбоксов строк таблицы.</summary>
	private static int SelectedRowCount(IRenderedComponent<Inbox> cut) =>
		cut.FindAll("tbody input[type=checkbox]").Count(input => input.HasAttribute("checked"));

	/// <summary>Кладёт сырую запись исполнения линейного инструмента в хранилище:
	/// линейные сделки не требуют справочника инструментов для материализации.</summary>
	private void SeedExecution(
		string execId,
		string symbol,
		string side,
		string price,
		string qty,
		string fee,
		string feeCurrency,
		long execTimeMs)
	{
		var payload =
			$$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{fee}}","execId":"{{execId}}","execPrice":"{{price}}","execQty":"{{qty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""";
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "linear",
			Symbol = symbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = payload,
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
