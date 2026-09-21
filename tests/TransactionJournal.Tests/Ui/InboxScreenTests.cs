using AngleSharp.Dom;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки экрана «Входящие»: таблица непривязанных сделок с атрибутами
/// биржевой записи и чекбоксами выбора строк; переключатель «Выбрать всё»
/// работает как «все или ничего»; массовая привязка к существующей конструкции
/// и создание конструкции из выбранных очищают выбор и список, оповещают
/// каркас о смене числа непривязанных; команды недоступны при пустом выборе;
/// пустые и недоступные «Входящие» показываются явно.
/// Traceability: openspec:ui/screens#requirement-inbox-screen
/// </summary>
[TestClass]
public class InboxScreenTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private Bunit.TestContext _context = null!;
	private string _databasePath = null!;
	private ConstructionService _constructions = null!;
	private ITradeBindingService _bindings = null!;
	private JournalChangeSignal _changes = null!;
	private InboxReadModel _inbox = null!;
	private int _changesRaised;

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
		_inbox = new InboxReadModel(CreateOptions());
		_context.Services.AddSingleton(_inbox);

		// Разбор выбранных выполняется use-case сервисами домена: тест поднимает
		// настоящие сервисы над той же базой, чтобы проверять итоговые привязки
		// и созданные конструкции, а не только вызовы.
		_constructions = new ConstructionService(CreateOptions());
		_context.Services.AddSingleton<IConstructionService>(_constructions);
		_bindings = new TradeBindingService(CreateOptions());
		_context.Services.AddSingleton<ITradeBindingService>(_bindings);

		// Сигнал изменений журнала — то, по чему каркас уменьшает бейдж вкладки
		// «Входящих»; проверка считает его срабатывания.
		_changes = new JournalChangeSignal();
		_context.Services.AddSingleton(_changes);
		_changesRaised = 0;
		_changes.Subscribe(() =>
		{
			_changesRaised++;
			return Task.CompletedTask;
		});
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

	[TestMethod]
	[Description("Команды разбора недоступны при пустом выборе и доступны после выбора")]
	public void TryIfBindingActionsDisabledWithoutSelection()
	{
		// Arrange: одна непривязанная сделка, выбор пуст.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));

		// Assert: при пустом выборе обе команды тулбара недоступны.
		// Требование: действия разбора недоступны при пустом выборе.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		Assert.That(FindToolbarButtons(cut).All(button => button.HasAttribute("disabled")), Is.True);

		// Act: пользователь выбирает сделку чекбоксом строки.
		cut.FindAll("tbody input[type=checkbox]")[0].Change(true);

		// Assert: после выбора команды становятся доступными.
		// Требование: для выбранных сделок доступны привязка и создание.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		Assert.That(FindToolbarButtons(cut).All(button => button.HasAttribute("disabled")), Is.False);
	}

	[TestMethod]
	[Description("Массовая привязка убирает сделки из «Входящих», очищает выбор и оповещает каркас")]
	public async Task TryIfBatchBindClearsListSelectionAndRaisesSignal()
	{
		// Arrange: две непривязанные сделки и активная целевая конструкция.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		SeedExecution("exec-2", "ETHUSDT", "Sell", "2400", "0.5", "0.1", "USDT", ExecMs(2026, 6, 20, 10, 5));
		var target = await _constructions.CreateAsync("Целевая", 1000m);
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));

		// Act: пользователь выбирает все сделки и открывает форму привязки.
		cut.Find("input.select-all").Change(true);
		FindToolbarButton(cut, "Привязать к конструкции…").Click();
		cut.WaitForAssertion(() =>
		{
			var options = cut.FindAll(".action-form select option");
			Assert.That(options, Has.Count.EqualTo(1));
			Assert.That(options[0].TextContent, Is.EqualTo("Целевая"));
		});

		// Act: подтверждает привязку к предложенной цели.
		FindButton(cut, "Привязать выбранное").Click();

		// Assert: обе сделки исчезли из «Входящих» и принадлежат ровно целевой
		// конструкции; выбор строк очищается, каркас получил сигнал — бейдж
		// вкладки уменьшается.
		// Сценарий: массовая привязка очищает список и выбор.
		// Traceability: openspec:ui/screens#scenario-inbox-batch-bind-clears
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Входящие пусты"));
			Assert.That(cut.FindAll("tbody input[type=checkbox]"), Has.Count.EqualTo(0));
			Assert.That(cut.FindAll(".action-form"), Has.Count.EqualTo(0));
			Assert.That(_changesRaised, Is.GreaterThanOrEqualTo(1));
		});
		await using (var db = new JournalDbContext(CreateOptions()))
		{
			var bindings = await db.TradeUserdata
				.Where(userdata => userdata.ConstructionId == target.Id)
				.Select(userdata => userdata.ExecId)
				.ToListAsync();
			Assert.That(bindings, Is.EquivalentTo(new[] { "exec-1", "exec-2" }));
		}

		// Список «Входящих» перечитан из read-модели: непривязанных сделок нет.
		Assert.That(await ReadInboxCountAsync(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Создание конструкции из выбранных открывает её со статусом «открыта» и привязывает все сделки")]
	public async Task TryIfCreateFromSelectedOpensConstructionAndBindsAllSelected()
	{
		// Arrange: две непривязанные сделки; конструкций в журнале нет.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		SeedExecution("exec-2", "ETHUSDT", "Sell", "2400", "0.5", "0.1", "USDT", ExecMs(2026, 6, 20, 10, 5));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));

		// Act: пользователь выбирает все сделки и заполняет форму создания.
		cut.Find("input.select-all").Change(true);
		FindToolbarButton(cut, "Создать конструкцию из выбранных…").Click();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".action-form .action-input"), Has.Count.EqualTo(2)));
		cut.FindAll(".action-form .action-input")[0].Change("Спринт октябрь");
		cut.FindAll(".action-form .action-input")[1].Change("3000");

		// Act: подтверждает создание и привязку.
		FindButton(cut, "Создать и привязать").Click();

		// Assert: конструкция создана с указанными именем и капиталом, со
		// статусом «открыта», и все выбранные сделки привязаны к ней; список
		// «Входящих» пуст, выбор очищен, каркас получил сигнал о бейдже.
		// Сценарий: создание конструкции из выбранных привязывает все выбранные.
		// Traceability: openspec:ui/screens#scenario-inbox-create-construction-from-selected
		await using (var db = new JournalDbContext(CreateOptions()))
		{
			var created = await db.Constructions.SingleAsync();
			Assert.That(created.Name, Is.EqualTo("Спринт октябрь"));
			Assert.That(created.Status, Is.EqualTo(ConstructionStatus.Open));
			Assert.That(created.AllocatedCapitalUsdt, Is.EqualTo(3000m));

			var bindings = await db.TradeUserdata
				.Where(userdata => userdata.ConstructionId == created.Id)
				.Select(userdata => userdata.ExecId)
				.ToListAsync();
			Assert.That(bindings, Is.EquivalentTo(new[] { "exec-1", "exec-2" }));
		}

		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Входящие пусты"));
			Assert.That(cut.FindAll("tbody input[type=checkbox]"), Has.Count.EqualTo(0));
			Assert.That(cut.FindAll(".action-form"), Has.Count.EqualTo(0));
			Assert.That(_changesRaised, Is.GreaterThanOrEqualTo(1));
		});
		Assert.That(await ReadInboxCountAsync(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Отмена форм разбора закрывает форму без привязки и создания")]
	public void TryIfCancelClosesFormsWithoutBindingOrCreating()
	{
		// Arrange: одна сделка и целевая конструкция; обе формы открываются по очереди.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		_constructions.CreateAsync("Целевая", 1000m).GetAwaiter().GetResult();
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));
		cut.FindAll("tbody input[type=checkbox]")[0].Change(true);

		// Act: отмена формы привязки.
		FindToolbarButton(cut, "Привязать к конструкции…").Click();
		FindButton(cut, "Отмена").Click();

		// Assert: форма закрыта, привязка не запускалась.
		// Требование: без подтверждения разбор не выполняется.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".action-form"), Has.Count.EqualTo(0)));
		Assert.That(_changesRaised, Is.EqualTo(0));

		// Act: отмена формы создания.
		FindToolbarButton(cut, "Создать конструкцию из выбранных…").Click();
		FindButton(cut, "Отмена").Click();

		// Assert: форма закрыта, конструкция не создана, сделка осталась во «Входящих».
		// Требование: без подтверждения разбор не выполняется.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".action-form"), Has.Count.EqualTo(0)));
		Assert.That(_constructions.ListActiveAsync().GetAwaiter().GetResult(), Has.Count.EqualTo(1));
		Assert.That(_changesRaised, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Форма создания требует имя и числовой капитал перед запуском команды")]
	public async Task TryIfCreateFormValidatesNameAndCapital()
	{
		// Arrange: одна выбранная сделка и открытая форма создания.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", ExecMs(2026, 6, 20, 10, 0));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));
		cut.FindAll("tbody input[type=checkbox]")[0].Change(true);
		FindToolbarButton(cut, "Создать конструкцию из выбранных…").Click();

		// Act: подтверждение с пустым именем.
		FindButton(cut, "Создать и привязать").Click();

		// Assert: показана причина, команда не запускалась, форма открыта.
		// Требование: создание требует имя конструкции.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".action-form").TextContent, Does.Contain("Введите имя конструкции"));
			Assert.That(_changesRaised, Is.EqualTo(0));
		});

		// Act: имя задано, капитал не число.
		cut.FindAll(".action-form .action-input")[0].Change("Спринт октябрь");
		cut.FindAll(".action-form .action-input")[1].Change("ноль");
		FindButton(cut, "Создать и привязать").Click();

		// Assert: показана причина про капитал, конструкция не создана.
		// Требование: капитал разбирается как число в USDT.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".action-form").TextContent, Does.Contain("Введите число в USDT"));
			Assert.That(_changesRaised, Is.EqualTo(0));
		});
		Assert.That((await _inbox.ListAsync()).Count, Is.EqualTo(1));
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Число отмеченных чекбоксов строк таблицы.</summary>
	private static int SelectedRowCount(IRenderedComponent<Inbox> cut) =>
		cut.FindAll("tbody input[type=checkbox]").Count(input => input.HasAttribute("checked"));

	/// <summary>Обе команды разбора выбранных в тулбаре экрана.</summary>
	private static IReadOnlyList<IElement> FindToolbarButtons(IRenderedComponent<Inbox> cut) =>
		cut.FindAll(".toolbar button").ToList();

	/// <summary>Кнопка тулбара по её видимому тексту.</summary>
	private static IElement FindToolbarButton(IRenderedComponent<Inbox> cut, string text) =>
		FindToolbarButtons(cut).Single(button => button.TextContent.Trim() == text);

	/// <summary>Кнопка формы по её видимому тексту.</summary>
	private static IElement FindButton(IRenderedComponent<Inbox> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

	/// <summary>Число непривязанных сделок в read-модели «Входящих».</summary>
	private async Task<int> ReadInboxCountAsync() => (await _inbox.ListAsync()).Count;

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
