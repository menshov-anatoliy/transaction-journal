namespace TransactionJournal.Tests.Hints.Display;

using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки read-модели отображения подсказок: живые подсказки группируются
/// по закрытому справочнику групп v1, пустые группы не показываются, панель
/// отделяет живые от терминальной истории, журнал фильтруется по статусу,
/// характеру и группе; команды «Применено»/«Отклонено» и первый показ идут
/// в хранилище единственными мутациями.
// Traceability: openspec:ui/screens#requirement-ui-hint-section-groups
// Traceability: openspec:ui/screens#requirement-ui-hint-log
/// </summary>
[TestClass]
public class HintDisplayReadModelTests
{
	[TestMethod]
	[Description("Панель группирует живые подсказки по группам справочника v1 и скрывает пустые группы")]
	public void TryIfPanelGroupsLiveHintsAndHidesEmptyGroups()
	{
		// Arrange: у конструкции живые подсказки характеров «риск-режим» и
		// «роллирование»; подсказок группы «Фьючерсная нога» нет.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "risk-mode", constructionId: 7),
				Record(2, HintStatus.New, character: "rolling", constructionId: 7),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: пользователь открывает панель подсказок конструкции.
		var panel = readModel.ReadConstructionPanelAsync(7).Result;

		// Assert: живые подсказки лежат в группах справочника v1, группа без
		// подсказок в панель не попадает.
		// Требование: панель и журнал группируют записи по справочнику v1,
		// пустые группы не показываются.
		// Traceability: openspec:ui/screens#requirement-ui-hint-section-groups
		// Traceability: openspec:ui/screens#scenario-ui-panel-groups-hints
		Assert.That(panel.LiveGroups.Select(section => section.Group.Id), Is.EqualTo(["risk-mode", "construction-management"]));
		Assert.That(panel.LiveGroups[0].Group.Title, Is.EqualTo("Риск-режим"));
		Assert.That(panel.LiveGroups[0].Hints.Select(record => record.Id), Is.EqualTo([1L]));
		Assert.That(panel.LiveGroups[1].Hints.Select(record => record.Id), Is.EqualTo([2L]));
		Assert.That(panel.History, Is.Empty);
		Assert.That(panel.LiveCount, Is.EqualTo(2));
	}

	[TestMethod]
	[Description("Терминальные записи уходят в историю панели, живые остаются наверху")]
	public void TryIfPanelSplitsLiveAndTerminalHistory()
	{
		// Arrange: одна живая подсказка и три терминальные — применённая,
		// отклонённая и погашенная агентом.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "exit", constructionId: 7),
				Record(2, HintStatus.Applied, character: "exit", constructionId: 7),
				Record(3, HintStatus.Dismissed, character: "rolling", constructionId: 7),
				Record(4, HintStatus.Expired, character: "risk-mode", constructionId: 7),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: панель конструкции с живыми и терминальными записями.
		var panel = readModel.ReadConstructionPanelAsync(7).Result;

		// Assert: живая подсказка одна и наверху, терминальная история
		// свёрнута отдельно; история отсортирована свежими сверху.
		// Требование: живые показаны сверху, терминальная история свёрнута.
		// Traceability: openspec:ui/screens#scenario-ui-panel-live-first
		Assert.That(panel.LiveCount, Is.EqualTo(1));
		Assert.That(panel.LiveGroups.Single().Hints.Single().Id, Is.EqualTo(1L));
		Assert.That(panel.History.Select(record => record.Id), Is.EqualTo([4L, 3L, 2L]));
	}

	[TestMethod]
	[Description("Панель отсекает подсказки других субъектов и сортирует по времени генерации")]
	public void TryIfPanelFiltersBySubjectAndSortsByGeneration()
	{
		// Arrange: подсказки двух конструкций и журнала; у целевой конструкции
		// две живые записи с разным временем генерации.
		var fresh = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
		var older = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "risk-mode", constructionId: 7, asOf: older),
				Record(2, HintStatus.New, character: "risk-mode", constructionId: 8),
				Record(3, HintStatus.New, character: "risk-mode", subjectJournal: true, asOf: fresh),
				Record(4, HintStatus.New, character: "risk-mode", constructionId: 7, asOf: fresh),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: панель конструкции 7.
		var panel = readModel.ReadConstructionPanelAsync(7).Result;

		// Assert: чужие субъекты не попали, сортировка по времени генерации —
		// свежая подсказка первой.
		// Требование: сортировка внутри панели — по времени генерации.
		// Traceability: openspec:ui/screens#scenario-ui-panel-live-first
		Assert.That(panel.LiveGroups.Single().Hints.Select(record => record.Id), Is.EqualTo([4L, 1L]));
	}

	[TestMethod]
	[Description("Панель журнала показывает только подсказки субъекта «журнал»")]
	public void TryIfJournalPanelShowsJournalHintsOnly()
	{
		// Arrange: портфельная подсказка и две подсказки конструкций.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "risk-mode", subjectJournal: true),
				Record(2, HintStatus.New, character: "exit", constructionId: 7),
				Record(3, HintStatus.New, character: "futures-leg", constructionId: 8),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: панель обзорного экрана.
		var panel = readModel.ReadJournalPanelAsync().Result;

		// Assert: панель несёт только подсказки субъекта «журнал».
		// Требование: обзорный экран показывает панель подсказок журнала.
		// Traceability: openspec:ui/screens#requirement-ui-portfolio-hint-panel
		Assert.That(panel.LiveCount, Is.EqualTo(1));
		Assert.That(panel.LiveGroups.Single().Hints.Single().Id, Is.EqualTo(1L));
	}

	[TestMethod]
	[Description("Характер вне справочника v1 показывается в группе «Управление конструкцией»")]
	public void TryIfUnknownCharacterFallsBackToConstructionManagement()
	{
		// Arrange: живая подсказка с характером, снятым вместе с карточкой.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync([Record(1, HintStatus.New, character: "entry", constructionId: 7)]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: панель конструкции.
		var panel = readModel.ReadConstructionPanelAsync(7).Result;

		// Assert: справочник закрыт — вне набора v1 запись видна в запасной
		// группе «Управление конструкцией», не теряется.
		Assert.That(panel.LiveGroups.Single().Group.Id, Is.EqualTo(HintSectionGroups.ConstructionManagementGroupId));
	}

	[TestMethod]
	[Description("Индикаторы списка считаются только по живым подсказкам конструкций")]
	public void TryIfLiveCountsCountOnlyLiveConstructionHints()
	{
		// Arrange: живые подсказки двух конструкций, терминальная и портфельная
		// в счётчики списка не попадают.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListLiveAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "exit", constructionId: 7),
				Record(2, HintStatus.New, character: "exit", constructionId: 7),
				Record(3, HintStatus.New, character: "futures-leg", constructionId: 9),
				Record(4, HintStatus.New, character: "risk-mode", subjectJournal: true),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: чтение индикаторов списка конструкций.
		var counts = readModel.ReadLiveCountsByConstructionAsync().Result;

		// Assert: считаются только живые подсказки конструкции; конструкция
		// без живых подсказок в словаре отсутствует.
		// Требование: индикатор — число живых подсказок конструкции.
		// Traceability: openspec:ui/screens#requirement-ui-construction-hint-badge
		Assert.That(counts.Keys, Is.EquivalentTo([7L, 9L]));
		Assert.That(counts[7], Is.EqualTo(2));
		Assert.That(counts[9], Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Журнал фильтруется по статусу и характеру")]
	public void TryIfLogFiltersByStatusAndCharacter()
	{
		// Arrange: журнал из записей разных статусов и характеров.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "risk-mode", subjectJournal: true),
				Record(2, HintStatus.Applied, character: "risk-mode", constructionId: 7),
				Record(3, HintStatus.Dismissed, character: "exit", constructionId: 7),
				Record(4, HintStatus.Expired, character: "futures-leg", constructionId: 8),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: фильтр по статусу applied и характеру risk-mode.
		var filtered = readModel.ReadLogAsync(new HintLogFilter { Status = HintStatus.Applied, Character = "risk-mode" }).Result;

		// Assert: список показывает только совпадающие записи по всем субъектам.
		// Требование: журнал фильтруется по статусу и характеру.
		// Traceability: openspec:ui/screens#scenario-ui-hint-log-filters
		Assert.That(filtered.Select(record => record.Id), Is.EqualTo([2L]));
	}

	[TestMethod]
	[Description("Фильтр группы ограничивает журнал характерами этой группы")]
	public void TryIfLogGroupFilterLimitsToGroupCharacters()
	{
		// Arrange: журнал с записями всех трёх групп справочника v1.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "risk-mode", subjectJournal: true),
				Record(2, HintStatus.New, character: "rolling", constructionId: 7),
				Record(3, HintStatus.New, character: "futures-leg", constructionId: 7),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: выбрана группа «Фьючерсная нога».
		var filtered = readModel.ReadLogAsync(new HintLogFilter { GroupId = "futures-leg" }).Result;

		// Assert: список ограничен характерами выбранной группы.
		// Требование: журнал фильтруется по группе справочника v1.
		// Traceability: openspec:ui/screens#scenario-ui-hint-log-group-filter
		Assert.That(filtered.Select(record => record.Id), Is.EqualTo([3L]));
	}

	[TestMethod]
	[Description("Журнал сортируется по времени генерации, свежие сверху")]
	public void TryIfLogSortsByGenerationDescending()
	{
		// Arrange: записи с разным временем генерации.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.ListAllAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				Record(1, HintStatus.New, character: "exit", constructionId: 7, asOf: DateTimeOffset.Parse("2026-10-01T09:00:00Z")),
				Record(2, HintStatus.New, character: "exit", constructionId: 7, asOf: DateTimeOffset.Parse("2026-10-03T09:00:00Z")),
				Record(3, HintStatus.New, character: "exit", constructionId: 7, asOf: DateTimeOffset.Parse("2026-10-02T09:00:00Z")),
			]);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: журнал без фильтров.
		var log = readModel.ReadLogAsync().Result;

		// Assert: свежие записи сверху.
		Assert.That(log.Select(record => record.Id), Is.EqualTo([2L, 3L, 1L]));
	}

	[TestMethod]
	[Description("Кнопки «Применено»/«Отклонено» переводят живую запись через хранилище")]
	public async Task TryIfApplyAndDismissDelegateHumanTransitions()
	{
		// Arrange: хранилище подтверждает оба перевода живой записи.
		var store = new Mock<IHintStore>();
		store
			.Setup(hintStore => hintStore.TryTransitionAsync(5, HintStatus.Applied, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		store
			.Setup(hintStore => hintStore.TryTransitionAsync(6, HintStatus.Dismissed, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: человек нажимает «Применено» на одной живой подсказке и
		// «Отклонено» на другой.
		var applied = await readModel.ApplyAsync(5);
		var dismissed = await readModel.DismissAsync(6);

		// Assert: обе команды дошли до хранилища человек-переходами.
		// Требование: кнопки — единственные мутации подсказок в UI.
		// Traceability: openspec:ui/screens#scenario-ui-apply-dismiss-buttons
		Assert.That(applied, Is.True);
		Assert.That(dismissed, Is.True);
	}

	[TestMethod]
	[Description("Первый показ фиксируется в хранилище отметкой момента")]
	public async Task TryIfMarkSeenDelegatesToStore()
	{
		// Arrange: хранилище принимает пометку первого показа.
		var store = new Mock<IHintStore>();
		var seenAt = DateTimeOffset.Parse("2026-10-04T12:00:00Z");
		var readModel = new HintDisplayReadModel(store.Object);

		// Act: панель показала живую подсказку впервые.
		await readModel.MarkSeenAsync(5, seenAt);

		// Assert: пометка первого показа ушла в хранилище.
		// Требование: firstSeenAt — автопометка первого показа в UI.
		// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
		store.Verify(hintStore => hintStore.MarkFirstSeenAsync(5, seenAt, It.IsAny<CancellationToken>()), Times.Once);
	}

	/// <summary>Запись подсказки-минимум для панели и журнала.</summary>
	private static HintRecord Record(
		long id,
		HintStatus status,
		string character,
		long? constructionId = null,
		bool subjectJournal = false,
		DateTimeOffset? asOf = null) => new()
	{
		Id = id,
		RuleId = $"ac-{id:00}",
		Subject = subjectJournal
			? HintSubject.ForJournal()
			: HintSubject.ForConstruction(constructionId ?? 0),
		Character = character,
		Clarity = "краткая директива",
		Sources = [],
		Text = $"Текст подсказки {id}",
		Facts = new Dictionary<string, string>(),
		AsOf = asOf ?? DateTimeOffset.Parse("2026-10-01T12:00:00Z"),
		Status = status,
	};
}
