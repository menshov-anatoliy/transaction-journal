namespace TransactionJournal.Tests.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Consultations;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки адаптера корпуса правил для консультаций: индекс перечисляет все
/// карточки компактно — id, название, краткое содержание из первых фраз
/// описаний и статус, — полный текст карточки рендерится только по id,
/// чужой id даёт null, пустой id отклоняется.
/// Traceability: openspec:consultations/context#scenario-context-card-index-only
/// </summary>
[TestClass]
public class RulesCorpusConsultationAdapterTests
{
	private string _corpusDir = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своим временным каталогом корпуса.
		_corpusDir = CorpusYaml.TempDir();
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Временный каталог корпуса удаляется со всем содержимым.
		CorpusYaml.DeleteDir(_corpusDir);
	}

	[TestMethod]
	[Description("Индекс перечисляет все карточки корпуса с id, названием, кратким содержанием и статусом")]
	// Проверяем компактный индекс: исполняемая карточка, карточка без машинного
	// триггера и retired-карточка входят в индекс упорядоченно по id, статус
	// передаётся строкой, краткое содержание собрано из описаний.
	// Traceability: openspec:consultations/context#scenario-context-card-index-only
	public async Task TryIfIndexListsAllCardsWithIdTitleSummaryAndStatus()
	{
		// Arrange: три карточки корпуса — с машинным триггером, без него и retired.
		CorpusYaml.Write(_corpusDir, "ac-02.yaml", CorpusYaml.Card("ac-02"));
		CorpusYaml.Write(_corpusDir, "ac-01.yaml", CorpusYaml.Card("ac-01", implementation: "weekly-loss-above-threshold"));
		CorpusYaml.Write(_corpusDir, "ac-03.yaml", CorpusYaml.Card("ac-03", status: "retired", retiredReason: "superseded"));
		var adapter = CreateAdapter();

		// Act: читаем компактный индекс корпуса.
		var index = await adapter.ListIndexAsync();

		// Assert: все три карточки упорядочены по id, статусы и краткие содержания на месте.
		Assert.That(index, Has.Count.EqualTo(3));
		Assert.That(index.Select(card => card.Id).ToArray(), Is.EqualTo(new[] { "ac-01", "ac-02", "ac-03" }));
		Assert.That(index[0].Status, Is.EqualTo("active"));
		Assert.That(index[1].Status, Is.EqualTo("active"));
		Assert.That(index[2].Status, Is.EqualTo("retired"));
		Assert.That(index[1].Title, Is.EqualTo("Правило ac-02"));
		Assert.That(index[1].Summary, Is.EqualTo("Тестовое условие правила ac-02. → Тестовое действие правила ac-02."));
		Assert.That(index[2].Summary, Is.EqualTo("Тестовое условие правила ac-03. → Тестовое действие правила ac-03."));
	}

	[TestMethod]
	[Description("Полный текст карточки рендерится по id с декларациями, описаниями, порогами и источниками")]
	// Проверяем чтение полного текста: декларативные поля, описания триггера
	// и действия, пороги, шаблон подсказки и атрибуция источников присутствуют.
	// Traceability: openspec:consultations/context#scenario-context-card-index-only
	public async Task TryIfCardFullTextRendersDeclarationsDescriptionsThresholdsAndSources()
	{
		// Arrange: активная карточка с порогом и шаблоном подсказки.
		CorpusYaml.Write(_corpusDir, "ac-02.yaml", CorpusYaml.Card(
			"ac-02",
			hintTemplate: "Убыток недели {loss} против лимита {limit}.",
			thresholds: [("weekly-loss", "1", "percent")]));
		var adapter = CreateAdapter();

		// Act: читаем полный текст карточки по id.
		var content = await adapter.ReadCardAsync("ac-02");

		// Assert: текст несёт заголовок, декларации, описания, порог, шаблон и источники.
		Assert.That(content, Is.Not.Null);
		Assert.That(content!.Id, Is.EqualTo("ac-02"));
		var text = content.Text;
		Assert.That(text, Does.Contain("# ac-02 — Правило ac-02"));
		Assert.That(text, Does.Contain("Характер действия: risk-mode"));
		Assert.That(text, Does.Contain("Чёткость: crisp — императив прямого действия"));
		Assert.That(text, Does.Contain("Область: открытые конструкции"));
		Assert.That(text, Does.Contain("Статус: active"));
		Assert.That(text, Does.Contain("Триггер: Тестовое условие правила ac-02."));
		Assert.That(text, Does.Contain("Действие: Тестовое действие правила ac-02."));
		Assert.That(text, Does.Contain("- weekly-loss = 1 percent"));
		Assert.That(text, Does.Contain("Шаблон подсказки: Убыток недели {loss} против лимита {limit}."));
		Assert.That(text, Does.Contain("Источники:"));
		Assert.That(text, Does.Contain("- ТЕСТ, Тесты/Тест.md:"));
		Assert.That(text, Does.Contain("  - «Цитата-доказательство ac-02»"));
	}

	[TestMethod]
	[Description("Чужой id карточки даёт null")]
	// Проверяем отсутствие карточки: инструмента чтения по несуществующему id
	// возвращает null, а не ошибку — отсутствие карточки штатный исход.
	// Traceability: openspec:consultations/context#scenario-context-card-index-only
	public async Task TryIfUnknownCardIdReturnsNull()
	{
		// Arrange: корпус из одной карточки.
		CorpusYaml.Write(_corpusDir, "ac-02.yaml", CorpusYaml.Card("ac-02"));
		var adapter = CreateAdapter();

		// Act: читаем карточку по чужому id.
		var content = await adapter.ReadCardAsync("nope");

		// Assert: карточки с таким id нет — результат null.
		Assert.That(content, Is.Null);
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Отсутствующий id карточки отклоняется")]
	// Проверяем негативный контракт чтения: null-id отклоняется аргумент-исключением.
	// Traceability: openspec:consultations/context#scenario-context-card-index-only
	public void ThrowOnNullCardId()
	{
		// Arrange: адаптер над пустым каталогом корпуса.
		var adapter = CreateAdapter();

		// Act / Assert: чтение с null-id поднимает ArgumentNullException.
		adapter.ReadCardAsync(null!);
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	[Description("Пустой или пробельный id карточки отклоняется")]
	// Проверяем негативный контракт чтения: пустой или пробельный id отклоняется
	// аргумент-исключением, чтение корпуса не выполняется.
	// Traceability: openspec:consultations/context#scenario-context-card-index-only
	public void ThrowOnNullOrWhitespaceCardId(string cardId)
	{
		// Arrange: адаптер над пустым каталогом корпуса.
		var adapter = CreateAdapter();

		// Act / Assert: чтение с пустым id поднимает ArgumentException.
		adapter.ReadCardAsync(cardId);
	}

	[TestMethod]
	[Description("Краткое содержание берёт только первые фразы описаний триггера и действия")]
	// Проверяем компактность краткого содержания: из многофразовых описаний
	// в индекс попадает только первая фраза каждой части, разделённые стрелкой.
	// Traceability: openspec:consultations/context#scenario-context-card-index-only
	public async Task TryIfSummaryKeepsOnlyFirstSentencesOfDescriptions()
	{
		// Arrange: карточка с двухфразовыми описаниями триггера и действия.
		CorpusYaml.Write(_corpusDir, "ac-04.yaml", CorpusYaml.Card(
			"ac-04",
			triggerDescription: "Убыток календарной недели превысил порог. Второе предложение триггера, которое не должно попасть в индекс.",
			actionDescription: "Снизить размер нового риска вдвое. Второе предложение действия, которое тоже не должно попасть в индекс."));
		var adapter = CreateAdapter();

		// Act: читаем компактный индекс корпуса.
		var index = await adapter.ListIndexAsync();

		// Assert: краткое содержание — только первые фразы, соединённые стрелкой.
		var summary = index.Single(card => card.Id == "ac-04").Summary;
		Assert.That(summary, Is.EqualTo("Убыток календарной недели превысил порог. → Снизить размер нового риска вдвое."));
		Assert.That(summary, Does.Not.Contain("Второе предложение"));
	}

	[TestMethod]
	[Description("Карточка без описаний откатывает краткое содержание к шаблону подсказки")]
	// Проверяем запасной путь краткого содержания: без описаний триггера
	// и действия индекс показывает шаблон подсказки, а не пустую строку.
	// Traceability: openspec:consultations/context#scenario-context-card-index-only
	public async Task TryIfCardWithoutDescriptionsFallsBackToHintTemplate()
	{
		// Arrange: карточка, собранная вручную, без полей description в триггере и действии.
		var yaml = string.Join(Environment.NewLine,
		[
			"id: ac-05",
			"title: Правило ac-05",
			"character: risk-mode",
			"technique: null",
			"clarity: crisp",
			"scope: portfolio",
			"status: active",
			"thresholds: []",
			"trigger:",
			"  implementation: null",
			"action:",
			"  hintTemplate: >-",
			"    Шаблон подсказки без описаний {loss}.",
			"sources:",
			"  - tag: ТЕСТ",
			"    file: \"Тесты/Тест.md\"",
			"    quotes:",
			"      - '«Цитата-доказательство ac-05»'",
			string.Empty,
		]);
		CorpusYaml.Write(_corpusDir, "ac-05.yaml", yaml);
		var adapter = CreateAdapter();

		// Act: читаем компактный индекс корпуса.
		var index = await adapter.ListIndexAsync();

		// Assert: краткое содержание откатилось к шаблону подсказки.
		var summary = index.Single(card => card.Id == "ac-05").Summary;
		Assert.That(summary, Is.EqualTo("Шаблон подсказки без описаний {loss}."));
	}

	/// <summary>Собирает адаптер корпуса над загрузчиком временного каталога.</summary>
	private RulesCorpusConsultationAdapter CreateAdapter() => new(new RulesCorpusLoader(_corpusDir));
}
