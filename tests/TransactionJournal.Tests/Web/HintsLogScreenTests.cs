using Bunit;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using HintsLogScreen = TransactionJournal.Components.Pages.Hints;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Web;

/// <summary>
/// Проверки журнала подсказок: общий read-only список всех записей по всем
/// субъектам с фильтрами статуса, характера и группы; карточки полного состава
/// без кнопок мутации, группа сужает набор характеров фильтра.
/// Traceability: openspec:ui/screens#requirement-ui-hint-log
/// </summary>
[TestClass]
public class HintsLogScreenTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IHintDisplayReadModel> _hints = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();

		// Read-модель подсказок: по умолчанию журнал без записей — проверки
		// фильтров и состава переопределяют выдачу.
		_hints = new Mock<IHintDisplayReadModel>();
		_hints
			.Setup(model => model.ReadLogAsync(It.IsAny<HintLogFilter?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);
		_context.Services.AddSingleton(_hints.Object);
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Фильтры статуса и характера ограничивают выдачу журнала")]
	public void TryIfLogFiltersByStatusAndCharacter()
	{
		// Act: пользователь открывает журнал подсказок.
		var cut = _context.RenderComponent<HintsLogScreen>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".hint-card"), Has.Count.EqualTo(0)));

		// Assert: без фильтров журнал читается целиком, без ограничений.
		// Требование: журнал показывает все записи по субъектам.
		// Traceability: openspec:ui/screens#requirement-ui-hint-log
		_hints.Verify(model => model.ReadLogAsync(null, It.IsAny<CancellationToken>()), Times.Once);

		// Act: пользователь выбирает статус «живая».
		Selects(cut)[0].Change(((int)HintStatus.New).ToString());
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("по фильтру записей нет")));

		// Assert: журнал перечитан с фильтром статуса new.
		// Требование: фильтр статуса ограничивает выдачу журнала.
		// Traceability: openspec:ui/screens#scenario-ui-hint-log-filters
		_hints.Verify(
			model => model.ReadLogAsync(
				It.Is<HintLogFilter?>(filter => filter != null && filter.Status == HintStatus.New),
				It.IsAny<CancellationToken>()),
			Times.Once);

		// Act: пользователь выбирает характер «лимиты и режим риска».
		Selects(cut)[2].Change("risk-mode");
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("по фильтру записей нет")));

		// Assert: журнал перечитан с фильтрами статуса и характера вместе.
		// Требование: фильтры статуса и характера сочетаются.
		// Traceability: openspec:ui/screens#scenario-ui-hint-log-filters
		_hints.Verify(
			model => model.ReadLogAsync(
				It.Is<HintLogFilter?>(filter => filter != null
					&& filter.Status == HintStatus.New
					&& filter.Character == "risk-mode"),
				It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[TestMethod]
	[Description("Фильтр группы сужает набор характеров и снимает характер вне группы")]
	public void TryIfLogGroupFilterNarrowsCharacters()
	{
		// Arrange: пользователь открыл журнал и выбрал характер «лимиты и режим риска».
		var cut = _context.RenderComponent<HintsLogScreen>();
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".hint-card"), Has.Count.EqualTo(0)));
		Selects(cut)[2].Change("risk-mode");
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Not.Contain("Чтение журнала")));

		// Act: пользователь выбирает группу «Фьючерсная нога».
		Selects(cut)[1].Change("futures-leg");

		// Assert: характер вне состава группы сброшен — фильтр группы сочетается
		// только со своими характерами; выпадающий список характеров сужен.
		// Требование: группа — фильтр по набору характеров.
		// Traceability: openspec:ui/screens#scenario-ui-hint-log-group-filter
		cut.WaitForAssertion(() =>
		{
			_hints.Verify(
				model => model.ReadLogAsync(
					It.Is<HintLogFilter?>(filter => filter != null
						&& filter.GroupId == "futures-leg"
						&& filter.Character == null),
					It.IsAny<CancellationToken>()),
				Times.Once);
		});
		var characters = Selects(cut)[2].QuerySelectorAll("option").Select(option => option.GetAttribute("value")).ToList();
		Assert.That(characters, Is.EqualTo(new[] { string.Empty, "futures-leg" }));

		// Act: пользователь выбирает характер «фьючерсная нога» в составе группы.
		Selects(cut)[2].Change("futures-leg");

		// Assert: группа и характер группы сочетаются в одном фильтре.
		// Требование: фильтр группы сужает набор характеров без снятия группы.
		// Traceability: openspec:ui/screens#scenario-ui-hint-log-group-filter
		_hints.Verify(
			model => model.ReadLogAsync(
				It.Is<HintLogFilter?>(filter => filter != null
					&& filter.GroupId == "futures-leg"
					&& filter.Character == "futures-leg"),
				It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[TestMethod]
	[Description("Журнал показывает записи всех субъектов с полным составом без кнопок")]
	public void TryIfLogShowsRecordsOfAllSubjects()
	{
		// Arrange: журнал с портфельной подсказкой и подсказкой конструкции.
		var journalHint = Hint(51, HintSubject.ForJournal(), "risk-mode", "Лимит риска периода исчерпан", HintStatus.Applied);
		var constructionHint = Hint(52, HintSubject.ForConstruction(7), "exit", "Рассмотри выход по плану", HintStatus.New);
		_hints
			.Setup(model => model.ReadLogAsync(It.IsAny<HintLogFilter?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([journalHint, constructionHint]);

		// Act: пользователь открывает журнал подсказок.
		var cut = _context.RenderComponent<HintsLogScreen>();

		// Assert: карточки несут субъект словами, группу справочника и полный
		// состав; у живой записи журнала кнопок мутации нет.
		// Требование: журнал read-only, записи идут по всем субъектам.
		// Traceability: openspec:ui/screens#requirement-ui-hint-log
		cut.WaitForAssertion(() =>
		{
			var cards = cut.FindAll(".hint-card");
			Assert.That(cards, Has.Count.EqualTo(2));
			Assert.That(cards[0].TextContent, Does.Contain("журнал"));
			Assert.That(cards[1].TextContent, Does.Contain("конструкция 7"));
			Assert.That(cards[1].TextContent, Does.Contain("возможность выхода"));
			Assert.That(cards[0].TextContent, Does.Contain("применена"));
			Assert.That(cut.FindAll(".hint-actions"), Has.Count.EqualTo(0));
		});
	}

	#region Помощники

	/// <summary>Фильтры журнала в порядке разметки: статус, группа, характер.</summary>
	private static IReadOnlyList<IElement> Selects(IRenderedComponent<IComponent> cut) =>
		cut.FindAll("select.log-filter");

	/// <summary>Запись подсказки журнала с денормализованным снимком.</summary>
	private static HintRecord Hint(long id, HintSubject subject, string character, string text, HintStatus status) => new()
	{
		Id = id,
		RuleId = "rule-" + character,
		Subject = subject,
		Character = character,
		Clarity = "crisp",
		Sources = [],
		Text = text,
		Facts = new Dictionary<string, string>(StringComparer.Ordinal),
		AsOf = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
		Status = status,
		FirstSeenAt = new DateTimeOffset(2026, 9, 19, 12, 5, 0, TimeSpan.Zero),
	};

	#endregion
}
