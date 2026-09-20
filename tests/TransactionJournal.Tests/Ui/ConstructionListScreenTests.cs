using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки экрана «Конструкции»: сводка журнала (итог, отметка марок, счётчик
/// конструкций) и таблица со столбцами метрик аналитики; клик по строке открывает
/// детали конструкции; пустой и недоступный журнал показываются явно.
/// Traceability: openspec:ui/screens#requirement-construction-list-screen
/// </summary>
[TestClass]
public class ConstructionListScreenTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IConstructionListReadModel> _list = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();
		_list = new Mock<IConstructionListReadModel>();
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(EmptyData());
		_context.Services.AddSingleton(_list.Object);
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Клик по строке конструкции открывает её детали")]
	public void TryIfRowClickOpensConstructionDetail()
	{
		// Arrange: в списке две конструкции — открытая и закрытая.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open), Item(8, "Контртренд ETH", ConstructionStatus.Closed)));
		var cut = _context.RenderComponent<Constructions>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll("tr.clickable"), Has.Count.EqualTo(2)));

		// Act: пользователь кликает строку первой конструкции.
		cut.Find("tr.clickable").Click();

		// Assert: открываются детали этой конструкции — адрес ведёт на её экран,
		// где каркас откроет транзитную вкладку.
		// Требование: строка открывается в детали конструкции кликом.
		// Traceability: openspec:ui/screens#scenario-list-row-opens-detail
		var navigation = _context.Services.GetRequiredService<NavigationManager>();
		Assert.That(navigation.Uri, Does.EndWith("/constructions/7"));
	}

	[TestMethod]
	[Description("Сводка показывает итог, отметку марок и счётчик конструкций с числом открытых")]
	public void TryIfSummaryShowsJournalTotalMarksAndCounts()
	{
		// Arrange: журнал с итогом, отметкой марок и четырьмя конструкциями,
		// из которых открыта одна.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(
				Item(7, "Календарь сентябрь", ConstructionStatus.Open, realized: 214.32m, unrealized: -58.2m),
				Item(8, "Контртренд ETH", ConstructionStatus.Closed),
				Item(9, "Спред NATGAS", ConstructionStatus.Closed),
				Item(10, "Разбор коллапса", ConstructionStatus.Closed))
				with
			{
				TotalPnL = 125.5m,
				MarksAsOf = new DateTimeOffset(2026, 9, 19, 12, 34, 0, TimeSpan.Zero),
				OpenCount = 1,
			});

		// Act: пользователь открывает экран «Конструкции».
		var cut = _context.RenderComponent<Constructions>();

		// Assert: сводка показывает итог журнала со знаком, отметку времени марок
		// и счётчик конструкций с числом открытых.
		// Требование: экран показывает сводку журнала — итог, марки и счётчик.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("+125.5 USDT"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("2026-09-19 12:34"));
			Assert.That(cut.Find(".kstrip").TextContent, Does.Contain("4 (1 открыта)"));
		});
	}

	[TestMethod]
	[Description("Таблица выводит столбцы метрик аналитики и статус каждой конструкции")]
	public void TryIfTableShowsMetricColumnsAndStatus()
	{
		// Arrange: открытая конструкция со всеми заполненными метриками.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateData(Item(7, "Календарь сентябрь", ConstructionStatus.Open,
				capital: 3000m, realized: 214.32m, unrealized: -58.2m, adjustments: 87.4m, total: 243.52m, percent: 8.1m)));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: строка несёт имя, статус, капитал и все метрики аналитики,
		// заголовок таблицы перечисляет столбцы метрик.
		// Требование: таблица выводит имя, статус, капитал, PnL, корректировки,
		// итог, процент и даты открытия/закрытия.
		// Traceability: openspec:ui/screens#requirement-construction-list-screen
		cut.WaitForAssertion(() =>
		{
			var row = cut.Find("tr.clickable");
			Assert.That(row.TextContent, Does.Contain("Календарь сентябрь"));
			Assert.That(row.TextContent, Does.Contain("открыта"));
			Assert.That(row.TextContent, Does.Contain("3000"));
			Assert.That(row.TextContent, Does.Contain("+214.32"));
			Assert.That(row.TextContent, Does.Contain("-58.2"));
			Assert.That(row.TextContent, Does.Contain("+87.4"));
			Assert.That(row.TextContent, Does.Contain("+243.52"));
			Assert.That(row.TextContent, Does.Contain("+8.1%"));
			var header = cut.Find("thead");
			Assert.That(header.TextContent, Does.Contain("Нереализов."));
			Assert.That(header.TextContent, Does.Contain("% капитала"));
			Assert.That(header.TextContent, Does.Contain("Закрыта"));
		});
	}

	[TestMethod]
	[Description("Пустой список показывает явное сообщение об отсутствии конструкций")]
	public void TryIfEmptyListShowsExplicitMessage()
	{
		// Arrange: журнал без конструкций.
		var cut = _context.RenderComponent<Constructions>();

		// Assert: таблица показывает сообщение об отсутствии, а не пустую разметку.
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find("td.empty").TextContent, Does.Contain("конструкций нет")));
	}

	[TestMethod]
	[Description("Недоступный журнал показывается явным состоянием, а не пустым экраном")]
	public void TryIfUnavailableJournalShowsExplicitState()
	{
		// Arrange: чтение журнала падает — сырьё повреждено или база недоступна.
		_list
			.Setup(model => model.ReadAsync(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("база недоступна"));

		var cut = _context.RenderComponent<Constructions>();

		// Assert: экран показывает явное состояние недоступности без таблицы.
		// Отсутствие данных — видимое состояние, а не пустой экран.
		// Traceability: change:add-ui-screens/design#goals-non-goals
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Журнал недоступен"));
			Assert.That(cut.FindAll("table"), Has.Count.EqualTo(0));
		});
	}

	#region Помощники

	/// <summary>Пустые данные списка: нулевые счётчики и отсутствие строк.</summary>
	private static ConstructionListData EmptyData() => new(0m, null, 0, 0, []);

	/// <summary>Данные списка со строками и счётчиком открытых по статусам строк.</summary>
	private static ConstructionListData CreateData(params ConstructionListItem[] items) => new(
		0m,
		null,
		items.Length,
		items.Count(item => item.Status == ConstructionStatus.Open),
		items);

	/// <summary>Строка конструкции с заполненными по умолчанию метриками.</summary>
	private static ConstructionListItem Item(
		long id,
		string name,
		ConstructionStatus status,
		decimal capital = 1000m,
		decimal realized = 0m,
		decimal? unrealized = 0m,
		decimal adjustments = 0m,
		decimal? total = null,
		decimal? percent = null) => new(
		id,
		name,
		status,
		capital,
		realized,
		unrealized,
		adjustments,
		total ?? realized + unrealized.GetValueOrDefault() + adjustments,
		percent,
		new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero),
		status == ConstructionStatus.Closed ? new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero) : null);

	#endregion
}
