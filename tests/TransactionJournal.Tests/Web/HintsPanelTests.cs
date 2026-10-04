using Bunit;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Components;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Web;

/// <summary>
/// Проверки панели «Подсказки» субъекта: живые подсказки сверху по группам
/// справочника v1, терминальная история свёрнута кнопкой, карточка показывает
/// полный состав подсказки, кнопки «Применено»/«Отклонено» — единственные
/// мутации подсказок в UI.
/// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
/// </summary>
[TestClass]
public class HintsPanelTests
{
	private Bunit.TestContext _context = null!;
	private Mock<IHintDisplayReadModel> _hints = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();

		// Read-модель подсказок: по умолчанию обе панели пусты — проверки
		// переопределяют нужную панель под сценарий.
		_hints = new Mock<IHintDisplayReadModel>();
		_hints
			.Setup(model => model.ReadConstructionPanelAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(EmptyPanel(HintSubject.ForConstruction(7)));
		_hints
			.Setup(model => model.ReadJournalPanelAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(EmptyPanel(HintSubject.ForJournal()));
		_context.Services.AddSingleton(_hints.Object);
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Живые подсказки показываются сверху по группам, история свёрнута кнопкой")]
	public void TryIfLiveHintsShownByGroupsAndHistoryCollapsed()
	{
		// Arrange: панель конструкции с двумя живыми группами и двумя
		// терминальными записями истории.
		var liveRisk = Hint(31, HintSubject.ForConstruction(7), "risk-mode", "Лимит риска периода исчерпан");
		var liveExit = Hint(32, HintSubject.ForConstruction(7), "exit", "Рассмотри выход по плану");
		var applied = Hint(33, HintSubject.ForConstruction(7), "profit-target", "Цель достигнута", HintStatus.Applied);
		var expired = Hint(34, HintSubject.ForConstruction(7), "rolling", "Ролл исполнен", HintStatus.Expired);
		var riskGroup = HintSectionGroups.V1.Single(group => group.Id == "risk-mode");
		var managementGroup = HintSectionGroups.V1.Single(group => group.Id == "construction-management");
		_hints
			.Setup(model => model.ReadConstructionPanelAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPanelData
			{
				Subject = HintSubject.ForConstruction(7),
				LiveGroups =
				[
					new HintSection { Group = riskGroup, Hints = [liveRisk] },
					new HintSection { Group = managementGroup, Hints = [liveExit] },
				],
				History = [applied, expired],
			});

		// Act: пользователь открывает детали конструкции с панелью подсказок.
		var cut = _context.RenderComponent<HintsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));

		// Assert: живые подсказки идут сверху по группам справочника, история
		// скрыта за кнопкой со счётчиком; раскрытие показывает терминальные.
		// Требование: живые подсказки идут сверху, история свёрнута.
		// Traceability: openspec:ui/screens#scenario-ui-panel-live-first
		cut.WaitForAssertion(() =>
		{
			var groups = cut.FindAll(".hint-group");
			Assert.That(groups, Has.Count.EqualTo(2));
			Assert.That(groups[0].QuerySelector("h3")!.TextContent, Is.EqualTo("Риск-режим"));
			Assert.That(cut.FindAll(".hint-card"), Has.Count.EqualTo(2));
			Assert.That(cut.Find(".history-toggle").TextContent, Does.Contain("История (2)"));
			Assert.That(cut.FindAll(".hint-history .hint-card"), Has.Count.EqualTo(0));
		});

		// Act: пользователь раскрывает историю кнопкой.
		cut.Find(".history-toggle").Click();

		// Assert: терминальная история показана внутри свёрнутого блока.
		// Требование: история раскрывается кнопкой, а не показывается всегда.
		// Traceability: openspec:ui/screens#scenario-ui-panel-live-first
		cut.WaitForAssertion(() =>
			Assert.That(cut.FindAll(".hint-history .hint-card"), Has.Count.EqualTo(2)));
	}

	[TestMethod]
	[Description("Карточка подсказки показывает полный состав: текст, метаданные, факты и источники")]
	public void TryIfHintCardShowsFullComposition()
	{
		// Arrange: живая подсказка с фактом триггера и источником с цитатой.
		var hint = new HintRecord
		{
			Id = 31,
			RuleId = "risk-window",
			Subject = HintSubject.ForConstruction(7),
			Character = "risk-mode",
			Clarity = "crisp",
			Sources = [new HintSourceTag
			{
				Tag = "ПИ",
				File = "vault/trading/памятка-риска.md",
				Quotes = ["Риск периода не превышает лимит недели"],
			}],
			Text = "Лимит риска недели исчерпан на 82%",
			Facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["Доля лимита"] = "82%" },
			AsOf = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
			Status = HintStatus.New,
			FirstSeenAt = new DateTimeOffset(2026, 9, 19, 12, 5, 0, TimeSpan.Zero),
		};
		_hints
			.Setup(model => model.ReadConstructionPanelAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(PanelOf(HintSubject.ForConstruction(7), "risk-mode", [hint]));

		var cut = _context.RenderComponent<HintsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));

		// Assert: карточка несёт весь снимок — текст, характер словами,
		// чёткость, правило, as-of, статус, факт и источник с цитатой.
		// Требование: карточка показывает полный состав подсказки.
		// Traceability: openspec:ui/screens#scenario-ui-hint-card-full-composition
		cut.WaitForAssertion(() =>
		{
			var card = cut.Find(".hint-card");
			Assert.That(card.QuerySelector(".hint-text")!.TextContent, Is.EqualTo("Лимит риска недели исчерпан на 82%"));
			var meta = card.QuerySelector(".hint-meta")!.TextContent;
			Assert.That(meta, Does.Contain("лимиты и режим риска"));
			Assert.That(meta, Does.Contain("crisp"));
			Assert.That(meta, Does.Contain("risk-window"));
			Assert.That(meta, Does.Contain(DisplayTime.FormatMoment(hint.AsOf)));
			Assert.That(meta, Does.Contain("живая"));
			Assert.That(card.QuerySelector(".hint-facts")!.TextContent, Does.Contain("Доля лимита").And.Contain("82%"));
			Assert.That(card.QuerySelector(".hint-source-tag")!.TextContent, Is.EqualTo("ПИ"));
			Assert.That(card.QuerySelector(".hint-source-file")!.TextContent, Does.Contain("памятка-риска.md"));
			Assert.That(card.QuerySelector(".hint-source-quotes")!.TextContent, Does.Contain("Риск периода не превышает лимит недели"));
		});
	}

	[TestMethod]
	[Description("Кнопки «Применено»/«Отклонено» переводят живую подсказку в терминальный статус")]
	public void TryIfApplyAndDismissButtons()
	{
		// Arrange: живая подсказка и применённая в раскрытой истории.
		var live = Hint(31, HintSubject.ForConstruction(7), "exit", "Рассмотри выход по плану");
		var applied = Hint(33, HintSubject.ForConstruction(7), "exit", "Выход исполнен", HintStatus.Applied);
		_hints
			.Setup(model => model.ReadConstructionPanelAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new HintPanelData
			{
				Subject = HintSubject.ForConstruction(7),
				LiveGroups = [Section("construction-management", [live])],
				History = [applied],
			});

		var cut = _context.RenderComponent<HintsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));
		cut.WaitForAssertion(() => Assert.That(cut.FindAll(".hint-card"), Has.Count.EqualTo(1)));

		// Act: пользователь раскрывает историю — терминальная запись видна без
		// кнопок мутации, переходов из applied нет.
		// Assert: кнопки есть только у живой подсказки.
		// Требование: переходы жизненного цикла ставит только человек из UI.
		// Traceability: openspec:ui/screens#scenario-ui-apply-dismiss-buttons
		cut.Find(".history-toggle").Click();
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.FindAll(".hint-history .hint-card"), Has.Count.EqualTo(1));
			Assert.That(cut.FindAll(".hint-history .hint-actions"), Has.Count.EqualTo(0));
			Assert.That(cut.FindAll(".hint-actions button"), Has.Count.EqualTo(2));
		});

		// Act: пользователь нажимает «Применено» на живой подсказке.
		// Assert: подсказка переведена в applied терминальный статус, панель перечитана.
		// Требование: переход в applied ставит только человек из UI.
		// Traceability: openspec:ui/screens#scenario-ui-apply-dismiss-buttons
		FindButton(cut, "Применено").Click();
		_hints.Verify(model => model.ApplyAsync(31, It.IsAny<CancellationToken>()), Times.Once);
		cut.WaitForAssertion(() =>
			_hints.Verify(model => model.ReadConstructionPanelAsync(7, It.IsAny<CancellationToken>()), Times.Exactly(2)));

		// Act: пользователь нажимает «Отклонено» на живой подсказке.
		// Assert: подсказка переведена в dismissed терминальный статус.
		// Требование: переход в dismissed ставит только человек из UI.
		// Traceability: openspec:ui/screens#scenario-ui-apply-dismiss-buttons
		FindButton(cut, "Отклонено").Click();
		_hints.Verify(model => model.DismissAsync(31, It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestMethod]
	[Description("Панель без параметра показывает подсказки журнала")]
	public void TryIfJournalPanelUsesJournalReader()
	{
		// Arrange: у журнала одна живая портфельная подсказка.
		var hint = Hint(41, HintSubject.ForJournal(), "risk-mode", "Лимит риска периода исчерпан");
		_hints
			.Setup(model => model.ReadJournalPanelAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(PanelOf(HintSubject.ForJournal(), "risk-mode", [hint]));

		// Act: панель рендерится без ConstructionId — субъект «журнал».
		var cut = _context.RenderComponent<HintsPanel>();

		// Assert: панель читает портфельную панель журнала и показывает её
		// подсказку; панель конструкции не запрашивается.
		// Требование: портфельные подсказки показывает субъект «журнал».
		// Traceability: openspec:ui/screens#scenario-ui-portfolio-panel-shows-journal-hints
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".hint-text").TextContent, Does.Contain("Лимит риска периода исчерпан")));
		_hints.Verify(model => model.ReadJournalPanelAsync(It.IsAny<CancellationToken>()), Times.Once);
		_hints.Verify(model => model.ReadConstructionPanelAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[TestMethod]
	[Description("Пустая панель показывает явное состояние отсутствия подсказок")]
	public void TryIfEmptyPanelShowsExplicitState()
	{
		// Arrange: панель конструкции без подсказок вовсе.
		var cut = _context.RenderComponent<HintsPanel>(parameters => parameters.Add(panel => panel.ConstructionId, 7));

		// Assert: панель показывает явное «подсказок нет», а не пустой блок.
		// Требование: отсутствие подсказок — видимое состояние панели.
		// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
		cut.WaitForAssertion(() =>
			Assert.That(cut.Find(".empty-hints").TextContent, Does.Contain("подсказок нет")));
	}

	#region Помощники

	/// <summary>Живая секция панели по идентификатору группы справочника v1.</summary>
	private static HintSection Section(string groupId, IReadOnlyList<HintRecord> hints) => new()
	{
		Group = HintSectionGroups.V1.Single(group => group.Id == groupId),
		Hints = hints,
	};

	/// <summary>Панель с одной живой группой без истории.</summary>
	private static HintPanelData PanelOf(HintSubject subject, string groupId, IReadOnlyList<HintRecord> hints) => new()
	{
		Subject = subject,
		LiveGroups = [Section(groupId, hints)],
		History = [],
	};

	/// <summary>Пустые данные панели подсказок.</summary>
	private static HintPanelData EmptyPanel(HintSubject subject) => new()
	{
		Subject = subject,
		LiveGroups = [],
		History = [],
	};

	/// <summary>Живая подсказка с денормализованным снимком момента генерации.</summary>
	private static HintRecord Hint(long id, HintSubject subject, string character, string text, HintStatus status = HintStatus.New) => new()
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

	/// <summary>Кнопка панели по видимому тексту.</summary>
	private static IElement FindButton(IRenderedComponent<IComponent> cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

	#endregion
}
