using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Components;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
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
/// пустые и недоступные «Входящие» показываются явно. Фильтры списка —
/// диапазон дат, инструменты и направление — действуют одновременно,
/// ограничивают «Выбрать всё» видимыми строками и не влияют на бейдж вкладки.
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
		// Arrange: две сделки сегодняшнего дня — покупка BTCUSDT и продажа
		// ETHUSDT с rebate-комиссией; обе внутри окна фильтров по умолчанию.
		var buyAt = TodayMs(10, 0);
		var sellAt = TodayMs(11, 30);
		SeedExecution("exec-buy", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", buyAt);
		SeedExecution("exec-sell", "ETHUSDT", "Sell", "2400.5", "0.5", "-0.01", "USDT", sellAt);

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
			Assert.That(rows[0].TextContent, Does.Contain(ExecText(buyAt))
				.And.Contain("exec-buy")
				.And.Contain("BTCUSDT")
				.And.Contain("покупка")
				.And.Contain("+0.01")
				.And.Contain("45000")
				.And.Contain("450")
				.And.Contain("+0.5 USDT"));
			Assert.That(rows[1].TextContent, Does.Contain(ExecText(sellAt))
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
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-2", "BTCUSDT", "Sell", "44000", "0.02", "0.5", "USDT", TodayMs(10, 5));
		SeedExecution("exec-3", "ETHUSDT", "Buy", "2400", "0.5", "0.1", "USDT", TodayMs(10, 10));
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
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-2", "BTCUSDT", "Sell", "44000", "0.02", "0.5", "USDT", TodayMs(10, 5));
		SeedExecution("exec-3", "ETHUSDT", "Buy", "2400", "0.5", "0.1", "USDT", TodayMs(10, 10));
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
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
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
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-2", "ETHUSDT", "Sell", "2400", "0.5", "0.1", "USDT", TodayMs(10, 5));
		var target = await _constructions.CreateAsync("Целевая", 1000m);
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));

		// Act: пользователь выбирает все сделки и открывает форму привязки.
		cut.Find("input.select-all").Change(true);
		FindToolbarButton(cut, "Привязать к конструкции…").Click();
		cut.WaitForAssertion(() =>
		{
			var options = cut.FindAll(ActionFormSelector + " select option");
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
			Assert.That(cut.FindAll(ActionFormSelector), Has.Count.EqualTo(0));
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
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-2", "ETHUSDT", "Sell", "2400", "0.5", "0.1", "USDT", TodayMs(10, 5));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));

		// Act: пользователь выбирает все сделки и заполняет форму создания.
		cut.Find("input.select-all").Change(true);
		FindToolbarButton(cut, "Создать конструкцию из выбранных…").Click();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(ActionFormSelector + " .action-input"), Has.Count.EqualTo(2)));
		cut.FindAll(ActionFormSelector + " .action-input")[0].Change("Спринт октябрь");
		cut.FindAll(ActionFormSelector + " .action-input")[1].Change("3000");

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
			Assert.That(cut.FindAll(ActionFormSelector), Has.Count.EqualTo(0));
			Assert.That(_changesRaised, Is.GreaterThanOrEqualTo(1));
		});
		Assert.That(await ReadInboxCountAsync(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Создание конструкции с пустым полем капитала создаёт её без выделенного бюджета")]
	public async Task TryIfCreateWithoutCapitalCreatesConstructionWithoutBudget()
	{
		// Arrange: две непривязанные сделки; форма создания открыта.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-2", "ETHUSDT", "Sell", "2400", "0.5", "0.1", "USDT", TodayMs(10, 5));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));
		cut.Find("input.select-all").Change(true);
		FindToolbarButton(cut, "Создать конструкцию из выбранных…").Click();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(ActionFormSelector + " .action-input"), Has.Count.EqualTo(2)));

		// Assert: подпись поля капитала выводится с заглавной буквы —
		// «Выделенный капитал, USDT».
		// Требование: форма создания несёт подпись «Выделенный капитал, USDT».
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		Assert.That(cut.Find(ActionFormSelector).TextContent, Does.Contain("Выделенный капитал, USDT"));

		// Act: имя задано, поле капитала оставлено пустым.
		cut.FindAll(ActionFormSelector + " .action-input")[0].Change("Без бюджета");
		FindButton(cut, "Создать и привязать").Click();

		// Assert: конструкция создана без капитала, все выбранные сделки
		// привязаны к ней; список «Входящих» пуст, выбор очищен.
		// Сценарий: создание без капитала проходит с пустым полем.
		// Traceability: openspec:ui/screens#scenario-inbox-create-without-capital
		await using (var db = new JournalDbContext(CreateOptions()))
		{
			var created = await db.Constructions.SingleAsync();
			Assert.That(created.Name, Is.EqualTo("Без бюджета"));
			Assert.That(created.Status, Is.EqualTo(ConstructionStatus.Open));
			Assert.That(created.AllocatedCapitalUsdt, Is.Null);

			var bindings = await db.TradeUserdata
				.Where(userdata => userdata.ConstructionId == created.Id)
				.Select(userdata => userdata.ExecId)
				.ToListAsync();
			Assert.That(bindings, Is.EquivalentTo(new[] { "exec-1", "exec-2" }));
		}

		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Входящие пусты"));
			Assert.That(cut.FindAll(ActionFormSelector), Has.Count.EqualTo(0));
		});
		Assert.That(await ReadInboxCountAsync(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Отмена форм разбора закрывает форму без привязки и создания")]
	public void TryIfCancelClosesFormsWithoutBindingOrCreating()
	{
		// Arrange: одна сделка и целевая конструкция; обе формы открываются по очереди.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
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
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(ActionFormSelector), Has.Count.EqualTo(0)));
		Assert.That(_changesRaised, Is.EqualTo(0));

		// Act: отмена формы создания.
		FindToolbarButton(cut, "Создать конструкцию из выбранных…").Click();
		FindButton(cut, "Отмена").Click();

		// Assert: форма закрыта, конструкция не создана, сделка осталась во «Входящих».
		// Требование: без подтверждения разбор не выполняется.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(ActionFormSelector), Has.Count.EqualTo(0)));
		Assert.That(_constructions.ListActiveAsync().GetAwaiter().GetResult(), Has.Count.EqualTo(1));
		Assert.That(_changesRaised, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Форма создания требует имя и числовой капитал перед запуском команды")]
	public async Task TryIfCreateFormValidatesNameAndCapital()
	{
		// Arrange: одна выбранная сделка и открытая форма создания.
		SeedExecution("exec-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
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
			Assert.That(cut.Find(ActionFormSelector).TextContent, Does.Contain("Введите имя конструкции"));
			Assert.That(_changesRaised, Is.EqualTo(0));
		});

		// Act: имя задано, капитал не число.
		cut.FindAll(ActionFormSelector + " .action-input")[0].Change("Спринт октябрь");
		cut.FindAll(ActionFormSelector + " .action-input")[1].Change("ноль");
		FindButton(cut, "Создать и привязать").Click();

		// Assert: показана причина про капитал, конструкция не создана.
		// Требование: капитал разбирается как число в USDT.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(ActionFormSelector).TextContent, Does.Contain("Введите число в USDT"));
			Assert.That(_changesRaised, Is.EqualTo(0));
		});
		Assert.That((await _inbox.ListAsync()).Count, Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Фильтры по умолчанию предзаполнены последним месяцем, всеми инструментами и обоими направлениями")]
	public void TryIfFiltersDefaultToLastMonthWithAllSymbolsAndDirections()
	{
		// Arrange: одна сделка внутри последнего месяца и одна старше окна.
		SeedExecution("exec-fresh", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-old", "ETHUSDT", "Sell", "2400", "0.5", "0.1", "USDT", MonthsAgoMs(2, 10, 0));

		// Act: пользователь открывает экран «Входящие» и раскрывает контрол
		// инструментов, чтобы увидеть список символов.
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));
		cut.Find(".symbols-toggle").Click();

		// Assert: поля дат предзаполнены «сегодня − 1 месяц … сегодня», все
		// инструменты и оба направления отмечены; таблица показывает только
		// свежую сделку — сделка старше окна по умолчанию скрыта. Область
		// фильтров обнесена рамкой с заголовком «Фильтры», направление —
		// группа в рамке с заголовком «Направление».
		// Сценарий: фильтры по умолчанию показывают последний месяц.
		// Traceability: openspec:ui/screens#scenario-inbox-filters-default-last-month
		cut.WaitForAssertion(() =>
		{
			var legends = cut.FindAll("legend");
			Assert.That(legends.Select(legend => legend.TextContent.Trim()), Is.EquivalentTo(new[] { "Фильтры", "Направление" }));

			var dateInputs = cut.FindAll(".inbox-filters input[type=date]");
			Assert.That(dateInputs, Has.Count.EqualTo(2));
			Assert.That(dateInputs[0].GetAttribute("value"), Is.EqualTo(DateText(DateOnly.FromDateTime(DateTime.Now).AddMonths(-1))));
			Assert.That(dateInputs[1].GetAttribute("value"), Is.EqualTo(DateText(DateOnly.FromDateTime(DateTime.Now))));

			var symbolLabels = cut.FindAll(".filter-symbols label");
			Assert.That(symbolLabels.Select(label => label.TextContent.Trim()), Is.EquivalentTo(new[] { "BTCUSDT", "ETHUSDT" }));
			Assert.That(CheckedCount(cut, ".filter-symbols input[type=checkbox]"), Is.EqualTo(2));
			Assert.That(cut.Find(".symbols-toggle").TextContent, Does.Contain("Инструменты: 2 из 2"));
			Assert.That(CheckedCount(cut, ".filter-direction input[type=checkbox]"), Is.EqualTo(2));

			var rows = cut.FindAll("tbody tr");
			Assert.That(rows, Has.Count.EqualTo(1));
			Assert.That(rows[0].TextContent, Does.Contain("exec-fresh"));
			Assert.That(rows[0].TextContent, Does.Not.Contain("exec-old"));
		});
	}

	[TestMethod]
	[Description("Диапазон дат, инструмент и направление скрывают строки одновременно")]
	public void TryIfDateSymbolAndDirectionFiltersCombine()
	{
		// Arrange: четыре сделки — подходящая под все фильтры и по одному
		// «нарушителю» на каждый фильтр: ранняя, чужой инструмент, продажа.
		SeedExecution("exec-ok", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-early", "BTCUSDT", "Buy", "44000", "0.02", "0.5", "USDT", DaysAgoMs(3, 10, 0));
		SeedExecution("exec-other-symbol", "ETHUSDT", "Buy", "2400", "0.5", "0.1", "USDT", TodayMs(10, 5));
		SeedExecution("exec-sell", "BTCUSDT", "Sell", "44500", "0.01", "0.1", "USDT", TodayMs(10, 10));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(4)));

		// Act: пользователь задаёт «с» = сегодня, раскрывает контрол
		// инструментов, снимает ETHUSDT и направление «продажа».
		cut.FindAll(".inbox-filters input[type=date]")[0].Change(DateText(DateOnly.FromDateTime(DateTime.Now)));
		cut.Find(".symbols-toggle").Click();
		cut.FindAll(".filter-symbols input[type=checkbox]")[1].Change(false);
		cut.FindAll(".filter-direction input[type=checkbox]")[1].Change(false);

		// Assert: видимой осталась только сделка, подходящая под все три
		// условия сразу; сделка вне диапазона, снятого инструмента и снятого
		// направления скрыты.
		// Сценарий: фильтры действуют одновременно.
		// Traceability: openspec:ui/screens#scenario-inbox-filters-combine
		cut.WaitForAssertion(() =>
		{
			var rows = cut.FindAll("tbody tr");
			Assert.That(rows, Has.Count.EqualTo(1));
			Assert.That(rows[0].TextContent, Does.Contain("exec-ok"));
		});

		// «Выбрать всё» при активных фильтрах выбирает только видимую строку:
		// скрытые сделки в выбор не попадают — выбор ограничен видимым.
		// Требование: разбор выбранных работает с видимыми строками.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.Find("input.select-all").Change(true);
		cut.WaitForAssertion(() =>
		{
			Assert.That(SelectedRowCount(cut), Is.EqualTo(1));
			Assert.That(FindToolbarButtons(cut).All(button => button.HasAttribute("disabled")), Is.False);
		});
	}

	[TestMethod]
	[Description("«Выбрать все» в контроле инструментов переключает все инструменты или ничего")]
	public void TryIfSymbolsSelectAllTogglesAllOrNone()
	{
		// Arrange: две сделки разных инструментов внутри окна фильтров —
		// набор инструментов фильтра состоит из BTCUSDT и ETHUSDT.
		SeedExecution("exec-btc", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-eth", "ETHUSDT", "Buy", "2400", "0.5", "0.1", "USDT", TodayMs(10, 5));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));

		// Act: пользователь раскрывает контрол инструментов и снимает
		// «Выбрать все» — полностью выбранный набор снимается целиком.
		cut.Find(".symbols-toggle").Click();
		cut.Find(".symbols-select-all input[type=checkbox]").Change(false);

		// Assert: инструментов не выбрано — все строки скрыты, показано
		// сообщение о пустом результате фильтров, счётчик кнопки — «0 из 2».
		// Сценарий: «Выбрать все» в контроле инструментов переключает все или ничего.
		// Traceability: openspec:ui/screens#scenario-inbox-filters-symbols-select-all
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll("tbody tr"), Is.Empty);
			Assert.That(cut.Markup, Does.Contain("По заданным фильтрам ничего не найдено"));
			Assert.That(cut.Find(".symbols-toggle").TextContent, Does.Contain("Инструменты: 0 из 2"));
		});

		// Act: пользователь возвращает один символ (частичный выбор) и
		// снова нажимает «Выбрать все».
		cut.FindAll(".filter-symbols input[type=checkbox]")[0].Change(true);
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));
		cut.Find(".symbols-select-all input[type=checkbox]").Change(true);

		// Assert: неполный набор дополнен до всех — снова видны обе сделки.
		// Сценарий: «Выбрать все» в контроле инструментов переключает все или ничего.
		// Traceability: openspec:ui/screens#scenario-inbox-filters-symbols-select-all
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2));
			Assert.That(cut.Find(".symbols-toggle").TextContent, Does.Contain("Инструменты: 2 из 2"));
		});
	}

	[TestMethod]
	[Description("«Выбрать всё» при активных фильтрах выбирает все видимые строки или снимает выбор полностью")]
	public void TryIfSelectAllWithActiveFiltersSelectsVisibleOnlyOrNothing()
	{
		// Arrange: три сделки — две видимые в окне по умолчанию, одна старше окна.
		SeedExecution("exec-visible-1", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-visible-2", "ETHUSDT", "Buy", "2400", "0.5", "0.1", "USDT", TodayMs(10, 5));
		SeedExecution("exec-hidden", "BTCUSDT", "Buy", "44000", "0.02", "0.5", "USDT", MonthsAgoMs(2, 10, 0));
		var cut = _context.RenderComponent<Inbox>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));

		// Act: «Выбрать всё» при активных фильтрах.
		cut.Find("input.select-all").Change(true);

		// Assert: выбраны обе видимые строки, скрытая в выбор не попала.
		// Сценарий: переключатель выбирает все видимые (отфильтрованные) сделки.
		// Traceability: openspec:ui/screens#scenario-inbox-select-all-or-none
		cut.WaitForAssertion(() => Assert.That(SelectedRowCount(cut), Is.EqualTo(2)));

		// Act: снятие ограничения «с» возвращает скрытую сделку в таблицу.
		cut.FindAll(".inbox-filters input[type=date]")[0].Change(string.Empty);

		// Assert: скрытая сделка появилась с неотмеченным чекбоксом —
		// «Выбрать всё» скрытые фильтрами сделки не выбирал.
		// Traceability: openspec:ui/screens#scenario-inbox-select-all-or-none
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(3));
			Assert.That(SelectedRowCount(cut), Is.EqualTo(2));
			Assert.That(cut.Find("input.select-all").HasAttribute("checked"), Is.False);
		});

		// Act: при частичном выборе переключатель выбирает все видимые строки.
		cut.Find("input.select-all").Change(true);

		// Assert: выбраны все три видимые сделки.
		cut.WaitForAssertion(() => Assert.That(SelectedRowCount(cut), Is.EqualTo(3)));

		// Act: повторное нажатие при полном выборе видимых строк.
		cut.Find("input.select-all").Change(false);

		// Assert: выбор снят полностью — «все или ничего».
		// Сценарий: переключатель полностью снимает выбор.
		// Traceability: openspec:ui/screens#scenario-inbox-select-all-or-none
		cut.WaitForAssertion(() => Assert.That(SelectedRowCount(cut), Is.EqualTo(0)));
	}

	[TestMethod]
	[Description("Бейдж вкладки показывает все непривязанные сделки при скрытых фильтрами строках")]
	public void TryIfInboxBadgeIgnoresFilters()
	{
		// Arrange: одна сделка в окне фильтров по умолчанию и одна старше окна;
		// каркас читает счётчик непривязанных сделок собственной read-моделью.
		SeedExecution("exec-fresh", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", TodayMs(10, 0));
		SeedExecution("exec-hidden", "ETHUSDT", "Sell", "2400", "0.5", "0.1", "USDT", MonthsAgoMs(2, 10, 0));
		var frame = new Mock<IFrameReadModel>();
		frame.Setup(model => model.CountInboxAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
		_context.Services.AddSingleton(frame.Object);
		_context.Services.AddSingleton(new Mock<IJournalMetricsReadModel>().Object);

		// Act: пользователь открывает «Входящие» внутри каркаса.
		var cut = _context.RenderComponent<MainLayout>(parameters => parameters.Add(layout => layout.Body, Screen<Inbox>()));

		// Assert: фильтры скрыли старую сделку — в таблице одна строка, а бейдж
		// вкладки по-прежнему показывает обе непривязанные сделки.
		// Сценарий: бейдж вкладки не зависит от фильтров.
		// Traceability: openspec:ui/screens#scenario-inbox-filters-badge-unaffected
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".tab-badge").TextContent, Is.EqualTo("2"));
			Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1));
			Assert.That(cut.FindAll("tbody tr")[0].TextContent, Does.Contain("exec-fresh"));
		});
	}

	[TestMethod]
	[Description("Фильтры, скрывшие все строки, дают отдельное сообщение вместо «Входящие пусты»")]
	public void TryIfAllRowsHiddenByFiltersShowsFilteredOutMessage()
	{
		// Arrange: единственная сделка старше последнего месяца — вне окна по умолчанию.
		SeedExecution("exec-old", "BTCUSDT", "Buy", "45000", "0.01", "0.5", "USDT", MonthsAgoMs(2, 10, 0));

		// Act: пользователь открывает экран «Входящие».
		var cut = _context.RenderComponent<Inbox>();

		// Assert: список непуст, но фильтры скрыли все строки — показано
		// сообщение о пустом результате фильтров; сообщение об отсутствии
		// непривязанных сделок не показывается, таблицы нет.
		// Требование: пустые состояния списка и фильтров различимы.
		// Traceability: openspec:ui/screens#requirement-inbox-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("По заданным фильтрам ничего не найдено"));
			Assert.That(cut.Markup, Does.Not.Contain("Входящие пусты"));
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

	/// <summary>Обе команды разбора выбранных в тулбаре экрана.</summary>
	private static IReadOnlyList<IElement> FindToolbarButtons(IRenderedComponent<Inbox> cut) =>
		cut.FindAll(".toolbar button").ToList();

	/// <summary>Кнопка тулбара по её видимому тексту.</summary>
	private static IElement FindToolbarButton(IRenderedComponent<Inbox> cut, string text) =>
		FindToolbarButtons(cut).Single(button => button.TextContent.Trim() == text);

	/// <summary>Кнопка формы по её видимому тексту.</summary>
	private static IElement FindButton(IRenderedComponent<Inbox> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

	/// <summary>Селектор форм разбора (привязка, создание): блок фильтров класса inbox-filters в него не входит.</summary>
	private const string ActionFormSelector = ".action-form:not(.inbox-filters)";

	/// <summary>Число непривязанных сделок в read-модели «Входящих».</summary>
	private async Task<int> ReadInboxCountAsync() => (await _inbox.ListAsync()).Count;

	/// <summary>Фрагмент рендера экрана как тела каркаса.</summary>
	private static RenderFragment Screen<TScreen>() where TScreen : IComponent => builder =>
	{
		builder.OpenComponent<TScreen>(0);
		builder.CloseComponent();
	};

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

	/// <summary>Число отмеченных чекбоксов по селектору.</summary>
	private static int CheckedCount(IRenderedComponent<Inbox> cut, string selector) =>
		cut.FindAll(selector).Count(input => input.HasAttribute("checked"));

	/// <summary>
	/// Момент исполнения по календарной дате и часам сервера: фильтр дат
	/// сравнивает сутки исполнения по текущим суткам сервера, поэтому и сиды
	/// задаются относительно локального «сегодня».
	/// </summary>
	private static long AtMs(DateOnly date, int hour, int minute)
	{
		var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
		return new DateTimeOffset(date, new TimeOnly(hour, minute), offset).ToUnixTimeMilliseconds();
	}

	/// <summary>Момент исполнения «сегодня» — сделка внутри окна фильтров по умолчанию.</summary>
	private static long TodayMs(int hour, int minute) =>
		AtMs(DateOnly.FromDateTime(DateTime.Now), hour, minute);

	/// <summary>Момент исполнения N суток назад — внутри месяца, но до границы «с» при её сужении.</summary>
	private static long DaysAgoMs(int daysAgo, int hour, int minute) =>
		AtMs(DateOnly.FromDateTime(DateTime.Now).AddDays(-daysAgo), hour, minute);

	/// <summary>Момент исполнения N месяцев назад — вне окна фильтров по умолчанию.</summary>
	private static long MonthsAgoMs(int monthsAgo, int hour, int minute) =>
		AtMs(DateOnly.FromDateTime(DateTime.Now).AddMonths(-monthsAgo), hour, minute);

	/// <summary>Ожидаемый текст времени сделки: экран показывает UTC-штамп локальным временем.</summary>
	// Даты сделок хранятся в UTC и рендерятся локальной стеночной частью.
	// Traceability: openspec:ui/screens#scenario-sync-dates-shown-local
	private static string ExecText(long execTimeMs) =>
		DisplayTime.FormatMoment(DateTimeOffset.FromUnixTimeMilliseconds(execTimeMs));

	/// <summary>Дата в формате input type="date" — для подстановки в поле фильтра и проверки значения.</summary>
	private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	#endregion
}
